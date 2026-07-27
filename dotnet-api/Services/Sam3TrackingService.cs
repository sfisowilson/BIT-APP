using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls Fal.ai SAM 3 Video mode for full-scene surface tracking.
///
/// Flow: POST submit → poll status → GET result → download segmented video.
/// The segmented video is then composited with the brand asset by RenderJobService.
///
/// Endpoint: https://fal.run/fal-ai/sam-3/video
/// Queue:    https://queue.fal.run/fal-ai/sam-3/video/requests/{id}
/// Activated when engine_tracking = "sam3".
/// </summary>
public class Sam3TrackingService : ISurfaceTrackingService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<Sam3TrackingService> _logger;
    private readonly IEventLogService _eventLog;
    private readonly HttpClient _http;

    private const string DefaultEndpoint = "https://fal.run/fal-ai/sam-3/video";
    private const string DefaultQueueBase = "https://queue.fal.run/fal-ai/sam-3/video/requests";
    private static readonly TimeSpan MaxPollTime = TimeSpan.FromMinutes(25);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    public Sam3TrackingService(IPlatformSettingsService settings, ILogger<Sam3TrackingService> logger, IEventLogService eventLog)
    {
        _settings = settings;
        _logger = logger;
        _eventLog = eventLog;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    public async Task<List<FrameBoundary>> TrackAsync(
        string surfaceId,
        string videoPath,
        int startFrame,
        int endFrame,
        string seedBoundaryJson,
        int promptFrame = -1,
        string? sam3Prompt = null,
        CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync("falai_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            await _eventLog.LogEventAsync("SAM3", "NO_API_KEY", "Error", "falai_api_key not configured.");
            return new List<FrameBoundary>();
        }

        var endpoint = await _settings.GetAsync("sam3_tracking_endpoint", DefaultEndpoint);
        var queueBase = await _settings.GetAsync("sam3_queue_base_url", DefaultQueueBase);

        try
        {
            var seedPoints = ParseSeedBoundary(seedBoundaryJson);
            if (seedPoints == null)
            {
                await _eventLog.LogEventAsync("SAM3", "INVALID_SEED", "Error",
                    $"Seed boundary parse failed. JSON: {Truncate(seedBoundaryJson, 200)}");
                return new List<FrameBoundary>();
            }

            var videoFileName = Path.GetFileName(videoPath);
            var videoBaseUrl = await _settings.GetAsync("sam3_video_base_url", "http://localhost:57220");
            var videoUrl = $"{videoBaseUrl}/api/content/file/{videoFileName}";

            var xs = seedPoints.Select(p => p[0]).ToList();
            var ys = seedPoints.Select(p => p[1]).ToList();
            var pf = promptFrame >= 0 ? promptFrame : startFrame;

            // Use box_prompts only — SAM3 rejects mixed point+box on same frame
            var boxPrompts = new[]
            {
                new
                {
                    frame_index = pf,
                    x_min = (int)xs.Min(),
                    y_min = (int)ys.Min(),
                    x_max = (int)xs.Max(),
                    y_max = (int)ys.Max(),
                    object_id = 0
                }
            };

            await _eventLog.LogEventAsync("SAM3", "TRACKING_START", "Info",
                $"Surface {surfaceId}: video={videoUrl}, frames={startFrame}-{endFrame}, promptFrame={pf}, " +
                $"1box, hasPrompt={sam3Prompt != null}, threshold=0.3");

            var payload = new
            {
                video_url = videoUrl,
                prompt = sam3Prompt,        // Gemini-generated description (null → excluded)
                point_prompts = Array.Empty<object>(),  // Empty — not compatible with box_prompts on same frame
                box_prompts = boxPrompts,    // Bounding box — stronger spatial hint than points
                apply_mask = true,           // Use masked video as luma mask for compositing
                video_output_type = "X264 (.mp4)",
                detection_threshold = 0.3,   // Lowered for flat surfaces
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Key {apiKey}");

            _logger.LogInformation(
                "[SAM3 Track] Surface {SurfaceId} frames {Start}-{End} promptFrame={PromptFrame} hasPrompt={HasPrompt}",
                surfaceId, startFrame, endFrame, pf, sam3Prompt != null);

            // ── Step 1: Submit ──
            var submitResponse = await _http.PostAsync(endpoint, content, ct);
            var submitJson = await submitResponse.Content.ReadAsStringAsync(ct);

            if (!submitResponse.IsSuccessStatusCode)
            {
                await _eventLog.LogEventAsync("SAM3", "HTTP_ERROR", "Error",
                    $"HTTP {(int)submitResponse.StatusCode} from submit: {Truncate(submitJson, 500)}");
                return new List<FrameBoundary>();
            }

            await _eventLog.LogEventAsync("SAM3", "SUBMITTED", "Info",
                $"Submit response: {Truncate(submitJson, 500)}");

            // ── Step 2: Check for sync response (video returned immediately) ──
            Sam3File? videoFile = null;
            string? requestId = null;

            using var submitDoc = JsonDocument.Parse(submitJson);
            var submitRoot = submitDoc.RootElement;

            // Try to get request_id (async mode)
            if (submitRoot.TryGetProperty("request_id", out var rid))
                requestId = rid.GetString();

            // Try to get video directly (sync mode)
            if (submitRoot.TryGetProperty("video", out var vid))
            {
                videoFile = JsonSerializer.Deserialize<Sam3File>(vid.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            // ── Step 3: Poll if async ──
            if (videoFile == null && !string.IsNullOrEmpty(requestId))
            {
                videoFile = await PollForResultAsync(queueBase, requestId, surfaceId, ct);
            }

            // ── Step 4: Download and save SAM3 segmented video ──
            if (videoFile?.Url == null)
            {
                await _eventLog.LogEventAsync("SAM3", "NO_VIDEO", "Error",
                    $"SAM3 did not return a video URL. request_id={requestId}, submit response: {Truncate(submitJson, 300)}");
                return new List<FrameBoundary>();
            }

            await _eventLog.LogEventAsync("SAM3", "DOWNLOADING_VIDEO", "Info",
                $"Downloading SAM3 segmented video: {videoFile.Url}");
            var videoBytes = await _http.GetByteArrayAsync(videoFile.Url, ct);
            var outputDir = Path.Combine(Path.GetTempPath(), "bit-sam3");
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, $"sam3_{surfaceId}.mp4");
            await File.WriteAllBytesAsync(outputPath, videoBytes, ct);

            await _eventLog.LogEventAsync("SAM3", "TRACKING_COMPLETE", "Info",
                $"Surface {surfaceId}: SAM3 video saved to {outputPath} ({videoBytes.Length} bytes)");

            return new List<FrameBoundary>
            {
                new FrameBoundary
                {
                    Frame = startFrame,
                    BoundaryCoordinatesJson = JsonSerializer.Serialize(new { sam3_video = outputPath }),
                    DriftConfidence = 1.0,
                }
            };
        }
        catch (TaskCanceledException)
        {
            await _eventLog.LogEventAsync("SAM3", "TIMEOUT", "Error",
                $"SAM3 timed out after 30 min for surface {surfaceId}.");
            return new List<FrameBoundary>();
        }
        catch (Exception ex)
        {
            await _eventLog.LogEventAsync("SAM3", "EXCEPTION", "Error",
                $"SAM3 failed: {ex.GetType().Name} — {ex.Message}");
            return new List<FrameBoundary>();
        }
    }

    // ── Polling ──

    private async Task<Sam3File?> PollForResultAsync(string queueBase, string requestId, string surfaceId, CancellationToken ct)
    {
        var statusUrl = $"{queueBase}/{requestId}/status";
        var resultUrl = $"{queueBase}/{requestId}";
        var deadline = DateTime.UtcNow.Add(MaxPollTime);

        await _eventLog.LogEventAsync("SAM3", "POLLING_START", "Info",
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
                    await _eventLog.LogEventAsync("SAM3", "POLL_ERROR", "Warning",
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
                    await _eventLog.LogEventAsync("SAM3", "POLLING_COMPLETE", "Info",
                        $"Request {requestId} completed. Fetching result.");
                    break;
                }

                if (status == "FAILED")
                {
                    var error = "Unknown error";
                    if (root.TryGetProperty("error", out var err))
                        error = err.GetString() ?? error;
                    await _eventLog.LogEventAsync("SAM3", "REQUEST_FAILED", "Error",
                        $"Request {requestId} failed: {error}");
                    return null;
                }

                await _eventLog.LogEventAsync("SAM3", "POLL_STATUS", "Info",
                    $"Request {requestId}: {status}");
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("SAM3", "POLL_EXCEPTION", "Warning",
                    $"Poll error: {ex.Message}. Retrying...");
            }

            await Task.Delay(PollInterval, ct);
        }

        // ── Fetch result ──
        var resultResp = await _http.GetAsync(resultUrl, ct);
        var resultJson = await resultResp.Content.ReadAsStringAsync(ct);

        if (!resultResp.IsSuccessStatusCode)
        {
            await _eventLog.LogEventAsync("SAM3", "RESULT_ERROR", "Error",
                $"Result fetch HTTP {(int)resultResp.StatusCode}: {Truncate(resultJson, 300)}");
            return null;
        }

        await _eventLog.LogEventAsync("SAM3", "RESULT_RECEIVED", "Info",
            $"Result: {Truncate(resultJson, 500)}");

        try
        {
            var result = JsonSerializer.Deserialize<Sam3ResultResponse>(resultJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return result?.Video;
        }
        catch (Exception ex)
        {
            await _eventLog.LogEventAsync("SAM3", "PARSE_ERROR", "Error",
                $"Failed to parse result: {ex.Message}");
            return null;
        }
    }

    // ── Seed boundary parsing ──

    private static List<List<double>>? ParseSeedBoundary(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return null;

            var result = new List<List<double>>();
            foreach (var pt in root.EnumerateArray())
            {
                double x = 0, y = 0;
                if (pt.TryGetProperty("x", out var lx) && pt.TryGetProperty("y", out var ly))
                    { x = lx.GetDouble(); y = ly.GetDouble(); }
                else if (pt.TryGetProperty("X", out var ux) && pt.TryGetProperty("Y", out var uy))
                    { x = ux.GetDouble(); y = uy.GetDouble(); }
                else if (pt.ValueKind == JsonValueKind.Array && pt.GetArrayLength() >= 2)
                    { x = pt[0].GetDouble(); y = pt[1].GetDouble(); }
                else continue;
                result.Add(new List<double> { x, y });
            }
            return result.Count >= 4 ? result : null;
        }
        catch { return null; }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    // ── JSON models (matching official fal.ai SAM3 schema) ──

    /// <summary>Top-level response from GET /requests/{id} after completion.</summary>
    private class Sam3ResultResponse
    {
        [JsonPropertyName("video")]
        public Sam3File? Video { get; set; }

        [JsonPropertyName("boundingbox_frames_zip")]
        public Sam3File? BoundingboxFramesZip { get; set; }
    }

    private class Sam3File
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
