using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls fal.ai Pika v2 Pikaswaps for AI-driven video compositing.
///
/// Flow: POST submit → poll status → GET result → download inpainted video.
/// Input: source video URL + brand asset image URL + modify_region (text) + prompt (text).
/// Output: inpainted video with the asset seamlessly blended into the described region.
///
/// Activated when engine_compositing = "pikaswaps".
/// Endpoint: https://queue.fal.run/fal-ai/pika/v2/pikaswaps
/// </summary>
public class PikaswapsCompositingService : ICompositingService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<PikaswapsCompositingService> _logger;
    private readonly IEventLogService _eventLog;
    private readonly HttpClient _http;

    private const string DefaultEndpoint = "https://queue.fal.run/fal-ai/pika/v2/pikaswaps";
    private static readonly TimeSpan MaxPollTime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    public PikaswapsCompositingService(
        IPlatformSettingsService settings,
        ILogger<PikaswapsCompositingService> logger,
        IEventLogService eventLog)
    {
        _settings = settings;
        _logger = logger;
        _eventLog = eventLog;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
    }

    /// <summary>
    /// Standard ICompositingService entry point. For pikaswaps, the CompositingRequest
    /// is extended with modify_region and prompt via BoundaryCoordinatesJson.
    /// </summary>
    public async Task<CompositedFrame> CompositeAsync(CompositingRequest request)
    {
        // pikaswaps needs more context than the basic CompositingRequest provides.
        // The render job should use CompositeWithPromptAsync instead.
        return new CompositedFrame
        {
            ImageBase64 = string.Empty,
            ContentType = "text/plain",
            EngineUsed = "Pikaswaps",
            ProcessingMs = 0
        };
    }

    /// <summary>
    /// Full pikaswaps compositing call with all required parameters.
    /// Returns the path to the downloaded inpainted video, or null on failure.
    /// </summary>
    /// <param name="videoUrl">Publicly accessible URL of the source video chunk.</param>
    /// <param name="imageUrl">Publicly accessible URL of the brand asset image.</param>
    /// <param name="modifyRegion">Text describing the region to replace (e.g. "the LED board on the field").</param>
    /// <param name="prompt">Text describing the desired result (e.g. "replace with a Coca-Cola ad, photorealistic").</param>
    /// <param name="surfaceId">Surface ID for logging.</param>
    /// <param name="negativePrompt">Optional negative prompt. Defaults to quality guard.</param>
    /// <param name="seed">Optional seed for reproducibility.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Local file path to the downloaded inpainted video, or null.</returns>
    public async Task<string?> CompositeWithPromptAsync(
        string videoUrl,
        string imageUrl,
        string modifyRegion,
        string prompt,
        string surfaceId,
        string? negativePrompt = null,
        int? seed = null,
        CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync("falai_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            await _eventLog.LogEventAsync("Pikaswaps", "NO_API_KEY", "Error", "falai_api_key not configured.");
            return null;
        }

        var endpoint = await _settings.GetAsync("pikaswaps_endpoint", DefaultEndpoint);

        try
        {
            await _eventLog.LogEventAsync("Pikaswaps", "COMPOSITE_START", "Info",
                $"Surface {surfaceId}: video={TruncateUrl(videoUrl)}, image={TruncateUrl(imageUrl)}, " +
                $"modify_region='{modifyRegion}', prompt='{prompt}'");

            var payload = new
            {
                video_url = videoUrl,
                image_url = imageUrl,
                modify_region = modifyRegion,
                prompt,
                negative_prompt = negativePrompt ?? "blurry, distorted, unrealistic, watermark, low quality, artifacts, altered text, changed wording, modified logo, different branding, distorted brand colors",
                seed = seed ?? new Random().Next(),
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Key {apiKey}");

            _logger.LogInformation(
                "[Pikaswaps] Surface {SurfaceId} modify_region='{ModifyRegion}'",
                surfaceId, modifyRegion);

            // ── Submit ──
            var submitResponse = await _http.PostAsync(endpoint, content, ct);
            var submitJson = await submitResponse.Content.ReadAsStringAsync(ct);

            if (!submitResponse.IsSuccessStatusCode)
            {
                await _eventLog.LogEventAsync("Pikaswaps", "SUBMIT_ERROR", "Error",
                    $"HTTP {(int)submitResponse.StatusCode}: {Truncate(submitJson, 500)}");
                return null;
            }

            await _eventLog.LogEventAsync("Pikaswaps", "SUBMITTED", "Info",
                $"Submit response: {Truncate(submitJson, 500)}");

            // ── Extract request_id ──
            string? requestId = null;
            using (var submitDoc = JsonDocument.Parse(submitJson))
            {
                if (submitDoc.RootElement.TryGetProperty("request_id", out var rid))
                    requestId = rid.GetString();
            }

            if (string.IsNullOrEmpty(requestId))
            {
                await _eventLog.LogEventAsync("Pikaswaps", "NO_REQUEST_ID", "Error", "No request_id in response.");
                return null;
            }

            // ── Poll for result ──
            var queueBase = "https://queue.fal.run/fal-ai/pika/v2/pikaswaps/requests";
            var videoPath = await PollForResultAsync(queueBase, requestId, surfaceId, ct);

            if (videoPath == null)
            {
                await _eventLog.LogEventAsync("Pikaswaps", "NO_VIDEO", "Error",
                    $"Pikaswaps did not return a video. request_id={requestId}");
                return null;
            }

            await _eventLog.LogEventAsync("Pikaswaps", "COMPOSITE_COMPLETE", "Info",
                $"Surface {surfaceId}: inpainted video saved to {videoPath}");

            return videoPath;
        }
        catch (OperationCanceledException)
        {
            await _eventLog.LogEventAsync("Pikaswaps", "CANCELLED", "Warning", "Compositing cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            await _eventLog.LogEventAsync("Pikaswaps", "EXCEPTION", "Error",
                $"Pikaswaps failed: {ex.GetType().Name} — {ex.Message}");
            _logger.LogError(ex, "[Pikaswaps] Surface {SurfaceId} FAILED", surfaceId);
            return null;
        }
    }

    // ── Polling ──

    private async Task<string?> PollForResultAsync(string queueBase, string requestId, string surfaceId, CancellationToken ct)
    {
        var statusUrl = $"{queueBase}/{requestId}/status";
        var resultUrl = $"{queueBase}/{requestId}";
        var deadline = DateTime.UtcNow.Add(MaxPollTime);

        await _eventLog.LogEventAsync("Pikaswaps", "POLLING_START", "Info",
            $"Polling {statusUrl} for request {requestId}");

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var statusResp = await _http.GetAsync(statusUrl, ct);
                var statusJson = await statusResp.Content.ReadAsStringAsync(ct);

                if (!statusResp.IsSuccessStatusCode)
                {
                    await _eventLog.LogEventAsync("Pikaswaps", "POLL_ERROR", "Warning",
                        $"Status check HTTP {(int)statusResp.StatusCode}: {Truncate(statusJson, 200)}");
                    await Task.Delay(PollInterval, ct);
                    continue;
                }

                using var doc = JsonDocument.Parse(statusJson);
                var root = doc.RootElement;
                var status = "UNKNOWN";
                if (root.TryGetProperty("status", out var s))
                    status = s.GetString() ?? "UNKNOWN";

                if (status == "COMPLETED")
                {
                    await _eventLog.LogEventAsync("Pikaswaps", "POLLING_COMPLETE", "Info",
                        $"Request {requestId} completed. Fetching result.");
                    break;
                }

                if (status == "FAILED")
                {
                    var error = "Unknown error";
                    if (root.TryGetProperty("error", out var err))
                        error = err.GetString() ?? error;
                    await _eventLog.LogEventAsync("Pikaswaps", "REQUEST_FAILED", "Error",
                        $"Request {requestId} failed: {error}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Pikaswaps", "POLL_EXCEPTION", "Warning",
                    $"Poll error: {ex.Message}. Retrying...");
            }

            await Task.Delay(PollInterval, ct);
        }

        // ── Fetch result ──
        var resultResp = await _http.GetAsync(resultUrl, ct);
        var resultJson = await resultResp.Content.ReadAsStringAsync(ct);

        if (!resultResp.IsSuccessStatusCode)
        {
            await _eventLog.LogEventAsync("Pikaswaps", "RESULT_ERROR", "Error",
                $"Result fetch HTTP {(int)resultResp.StatusCode}: {Truncate(resultJson, 300)}");
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<PikaswapsResultResponse>(resultJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            var videoUrl = result?.Video?.Url;
            if (string.IsNullOrEmpty(videoUrl)) return null;

            // Download the inpainted video
            var videoBytes = await _http.GetByteArrayAsync(videoUrl, ct);
            var outputDir = Path.Combine(Path.GetTempPath(), "bit-pikaswaps");
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, $"pikaswaps_{surfaceId}_{requestId[..8]}.mp4");
            await File.WriteAllBytesAsync(outputPath, videoBytes, ct);

            return outputPath;
        }
        catch (Exception ex)
        {
            await _eventLog.LogEventAsync("Pikaswaps", "PARSE_ERROR", "Error",
                $"Failed to parse result: {ex.Message}");
            return null;
        }
    }

    // ── Helpers ──

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static string TruncateUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "(empty)";
        // Show last 40 chars of URL (the interesting part)
        return url.Length <= 50 ? url : "..." + url[^40..];
    }

    // ── JSON models ──

    private class PikaswapsResultResponse
    {
        [JsonPropertyName("video")]
        public PikaswapsFile? Video { get; set; }
    }

    private class PikaswapsFile
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }

        [JsonPropertyName("file_size")]
        public long FileSize { get; set; }
    }
}
