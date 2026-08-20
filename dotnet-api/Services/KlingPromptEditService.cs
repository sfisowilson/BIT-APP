using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls fal.ai Kling O3 Pro Edit Video for prompt-driven AI video generation — the "AI Placement
/// Assistant → Generate New" flow. Unlike the click/quad-based Insert Product and Place Signage
/// flows, this needs no detected/drawn surface at all: the model infers placement purely from a
/// free-text instruction plus a reference asset image.
///
/// Flow: POST submit → poll status → GET result → download generated video.
/// Input: source scene clip URL + brand asset image URL + free-text placement prompt.
/// Output: edited video with the asset placed as described, motion structure preserved.
///
/// Endpoint: https://queue.fal.run/fal-ai/kling-video/o3/pro/video-to-video/edit
/// Real model constraints (not configurable): 3.0–10.05s input duration, 720–2160px resolution.
/// </summary>
public class KlingPromptEditService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<KlingPromptEditService> _logger;
    private readonly IEventLogService _eventLog;
    private readonly HttpClient _http;

    private const string DefaultEndpoint = "https://queue.fal.run/fal-ai/kling-video/o3/pro/video-to-video/edit";
    private static readonly TimeSpan MaxPollTime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    /// <summary>Kling O3 Pro's real, hard input-duration constraints — a scene outside this window
    /// cannot be sent to this endpoint at all. Referenced by RenderService/RenderJobService's
    /// "allowed length" gate rather than duplicated as separate literals.</summary>
    public const double MinPromptEditDurationSeconds = 3.0;
    public const double MaxPromptEditDurationSeconds = 10.05;

    /// <summary>Kling O3 Pro's hard max resolution on either axis (confirmed via a live 422:
    /// "Video dimensions are too large. Maximum width is 2160 pixels."). The source scene clip
    /// must be downscaled to fit before submission — see ProcessPromptPreviewJob.</summary>
    public const int MaxPromptEditResolutionPx = 2160;

    /// <summary>Appended to every user-authored prompt before it reaches the model — the free-text
    /// box is the one place a user can type arbitrary instructions, so this is the one enforcement
    /// point that actually constrains what gets sent. Deliberately kept to one short clause: an
    /// earlier, longer version (itemizing "text/wording/logo/colors/keyframes/regions/frames") was
    /// observed live to make the model overcorrect — editing unrelated parts of the scene (a
    /// speaker's face, replacing an unrelated prop, in one case replacing the whole scene) instead
    /// of just placing the asset as asked. Video-edit models are sensitive to prompt length/
    /// complexity; a terse instruction stays closer to the user's actual, narrow request.</summary>
    public const string BrandIntegrityRules =
        "Only change what's needed to place the asset as described — keep the asset's appearance " +
        "and the rest of the scene and clip exactly as given.";

    public KlingPromptEditService(
        IPlatformSettingsService settings,
        ILogger<KlingPromptEditService> logger,
        IEventLogService eventLog)
    {
        _settings = settings;
        _logger = logger;
        _eventLog = eventLog;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
    }

    /// <summary>
    /// Full Kling O1 edit call. Returns the local path to the downloaded generated video, or
    /// null on failure. Composes the final @Image1-annotated prompt here so the caller's stored
    /// RenderItem.PromptText stays the user's raw, unmodified text.
    /// </summary>
    /// <param name="videoUrl">Publicly accessible URL of the source scene clip (3.0–10.05s).</param>
    /// <param name="assetImageUrl">Publicly accessible URL of the brand asset reference image.</param>
    /// <param name="userPromptText">The user's raw free-text placement instruction.</param>
    /// <param name="renderId">Render ID for logging and the download file name.</param>
    /// <param name="keepAudio">Whether to keep the source clip's original audio.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Local file path to the downloaded generated video, or null.</returns>
    public async Task<string?> EditWithPromptAsync(
        string videoUrl,
        string assetImageUrl,
        string userPromptText,
        string renderId,
        bool keepAudio = true,
        CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync("falai_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            await _eventLog.LogEventAsync("KlingPromptEdit", "NO_API_KEY", "Error", "falai_api_key not configured.");
            return null;
        }

        var endpoint = await _settings.GetAsync("kling_edit_endpoint", DefaultEndpoint);
        var prompt = $"{userPromptText}, using @Image1 as the brand asset to place. {BrandIntegrityRules}";

        try
        {
            await _eventLog.LogEventAsync("KlingPromptEdit", "EDIT_START", "Info",
                $"Render {renderId}: video={TruncateUrl(videoUrl)}, image={TruncateUrl(assetImageUrl)}, prompt='{prompt}'");

            var payload = new
            {
                video_url = videoUrl,
                image_urls = new[] { assetImageUrl },
                prompt,
                keep_audio = keepAudio,
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Key {apiKey}");

            _logger.LogInformation("[KlingPromptEdit] Render {RenderId} prompt='{Prompt}'", renderId, prompt);

            // ── Submit ──
            var submitResponse = await _http.PostAsync(endpoint, content, ct);
            var submitJson = await submitResponse.Content.ReadAsStringAsync(ct);

            if (!submitResponse.IsSuccessStatusCode)
            {
                await _eventLog.LogEventAsync("KlingPromptEdit", "SUBMIT_ERROR", "Error",
                    $"HTTP {(int)submitResponse.StatusCode}: {Truncate(submitJson, 500)}");
                return null;
            }

            await _eventLog.LogEventAsync("KlingPromptEdit", "SUBMITTED", "Info",
                $"Submit response: {Truncate(submitJson, 500)}");

            // ── Extract request_id + the queue's own status/result URLs ──
            // fal.ai apps with sub-paths (like this one) root their queue routes at the app's
            // top-level namespace, not the full submit path — e.g. status_url here is
            // "https://queue.fal.run/fal-ai/kling-video/requests/{id}/status", NOT
            // "https://queue.fal.run/fal-ai/kling-video/o1/video-to-video/edit/requests/{id}/status".
            // Always use the URLs fal.ai hands back rather than reconstructing them.
            string? requestId = null;
            string? statusUrl = null;
            string? resultUrl = null;
            using (var submitDoc = JsonDocument.Parse(submitJson))
            {
                var root = submitDoc.RootElement;
                if (root.TryGetProperty("request_id", out var rid))
                    requestId = rid.GetString();
                if (root.TryGetProperty("status_url", out var su))
                    statusUrl = su.GetString();
                if (root.TryGetProperty("response_url", out var ru))
                    resultUrl = ru.GetString();
            }

            if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(statusUrl) || string.IsNullOrEmpty(resultUrl))
            {
                await _eventLog.LogEventAsync("KlingPromptEdit", "NO_REQUEST_ID", "Error",
                    $"Missing request_id/status_url/response_url in submit response: {Truncate(submitJson, 300)}");
                return null;
            }

            // ── Poll for result ──
            var videoPath = await PollForResultAsync(statusUrl, resultUrl, requestId, renderId, ct);

            if (videoPath == null)
            {
                await _eventLog.LogEventAsync("KlingPromptEdit", "NO_VIDEO", "Error",
                    $"Kling O1 did not return a video. request_id={requestId}");
                return null;
            }

            await _eventLog.LogEventAsync("KlingPromptEdit", "EDIT_COMPLETE", "Info",
                $"Render {renderId}: generated video saved to {videoPath}");

            return videoPath;
        }
        catch (OperationCanceledException)
        {
            await _eventLog.LogEventAsync("KlingPromptEdit", "CANCELLED", "Warning", "Edit cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            await _eventLog.LogEventAsync("KlingPromptEdit", "EXCEPTION", "Error",
                $"Kling edit failed: {ex.GetType().Name} — {ex.Message}");
            _logger.LogError(ex, "[KlingPromptEdit] Render {RenderId} FAILED", renderId);
            return null;
        }
    }

    // ── Polling ──

    private async Task<string?> PollForResultAsync(string statusUrl, string resultUrl, string requestId, string renderId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.Add(MaxPollTime);

        await _eventLog.LogEventAsync("KlingPromptEdit", "POLLING_START", "Info",
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
                    await _eventLog.LogEventAsync("KlingPromptEdit", "POLL_ERROR", "Warning",
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
                    await _eventLog.LogEventAsync("KlingPromptEdit", "POLLING_COMPLETE", "Info",
                        $"Request {requestId} completed. Fetching result.");
                    break;
                }

                if (status == "FAILED")
                {
                    var error = "Unknown error";
                    if (root.TryGetProperty("error", out var err))
                        error = err.GetString() ?? error;
                    await _eventLog.LogEventAsync("KlingPromptEdit", "REQUEST_FAILED", "Error",
                        $"Request {requestId} failed: {error}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("KlingPromptEdit", "POLL_EXCEPTION", "Warning",
                    $"Poll error: {ex.Message}. Retrying...");
            }

            await Task.Delay(PollInterval, ct);
        }

        // ── Fetch result ──
        var resultResp = await _http.GetAsync(resultUrl, ct);
        var resultJson = await resultResp.Content.ReadAsStringAsync(ct);

        if (!resultResp.IsSuccessStatusCode)
        {
            await _eventLog.LogEventAsync("KlingPromptEdit", "RESULT_ERROR", "Error",
                $"Result fetch HTTP {(int)resultResp.StatusCode}: {Truncate(resultJson, 300)}");
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<KlingResultResponse>(resultJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            var videoUrl = result?.Video?.Url;
            if (string.IsNullOrEmpty(videoUrl)) return null;

            // Download the generated video
            var videoBytes = await _http.GetByteArrayAsync(videoUrl, ct);
            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "tmp-renders", renderId);
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, $"preview_{requestId[..8]}.mp4");
            await File.WriteAllBytesAsync(outputPath, videoBytes, ct);

            return outputPath;
        }
        catch (Exception ex)
        {
            await _eventLog.LogEventAsync("KlingPromptEdit", "PARSE_ERROR", "Error",
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

    private class KlingResultResponse
    {
        [JsonPropertyName("video")]
        public KlingFile? Video { get; set; }
    }

    private class KlingFile
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
