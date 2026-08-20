using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// Calls Fal.ai SAM 3 for surface segmentation and tracking: single-frame click preview
/// (<see cref="PreviewSegmentAsync"/>) and per-shot video-rle segmentation
/// (<see cref="SegmentVideoRleAsync"/>), the foundation for <see cref="ShotAwareTrackingService"/>.
/// Activated when engine_tracking = "sam3".
/// </summary>
public class Sam3TrackingService : ISurfaceTrackingService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<Sam3TrackingService> _logger;
    private readonly IEventLogService _eventLog;
    private readonly HttpClient _http;

    private const string DefaultRleEndpoint = "https://fal.run/fal-ai/sam-3/video-rle";
    private const string DefaultRleQueueBase = "https://queue.fal.run/fal-ai/sam-3/video-rle/requests";

    public Sam3TrackingService(IPlatformSettingsService settings, ILogger<Sam3TrackingService> logger, IEventLogService eventLog)
    {
        _settings = settings;
        _logger = logger;
        _eventLog = eventLog;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    /// <summary>
    /// Preview-segment a clicked point on a single video frame using SAM3 video-rle.
    /// Seeds a single-frame window (frameIndex..frameIndex) with a point_prompt and decodes
    /// the returned RLE mask into a polygon for SVG overlay rendering.
    /// </summary>
    public async Task<SegmentPreviewResult?> PreviewSegmentAsync(
        string contentId, string videoPath, int frameIndex, int x, int y, CancellationToken cancellationToken = default)
    {
        await _eventLog.LogEventAsync("SAM3", "PREVIEW_START", "Info",
            $"Preview segment: content={contentId}, frame={frameIndex}, point=({x},{y})");

        var frames = await SegmentVideoRleAsync(
            videoPath, frameIndex, frameIndex,
            seedPoint: (x, y),
            promptFrame: frameIndex,
            cancellationToken: cancellationToken);

        var frame = frames.FirstOrDefault(f => f.FrameIndex == frameIndex) ?? frames.FirstOrDefault();
        var obj = frame?.Objects.OrderByDescending(o => o.Confidence).FirstOrDefault();
        if (obj == null || string.IsNullOrEmpty(obj.Rle))
        {
            await _eventLog.LogEventAsync("SAM3", "PREVIEW_NO_MASK", "Warning",
                $"SAM3 video-rle returned no mask for point ({x},{y}) at frame {frameIndex}");
            return null;
        }

        // Mask dimensions are the source video's native pixel size.
        var (videoWidth, videoHeight) = VideoProbe.GetDimensions(videoPath);
        var mask = RleDecoder.Decode(obj.Rle, videoWidth, videoHeight);
        var polygon = RleDecoder.MaskToPolygon(mask);
        if (polygon.Count < 3) return null;

        var bounds = RleDecoder.PolygonBounds(polygon);
        await _eventLog.LogEventAsync("SAM3", "PREVIEW_COMPLETE", "Info",
            $"Preview result: polygonPoints={polygon.Count}, trackId={obj.TrackId}, " +
            $"bounds=({bounds.xMin},{bounds.yMin})-({bounds.xMax},{bounds.yMax})");

        return new SegmentPreviewResult
        {
            MaskPolygon = polygon,
            Confidence = obj.Confidence,
            TrackId = obj.TrackId,
            SurfaceType = string.Empty,
            FrameIndex = frameIndex,
            Bounds = bounds,
        };
    }

    /// <summary>
    /// Segment a frame range via fal-ai/sam-3/video-rle. See <see cref="ISurfaceTrackingService.SegmentVideoRleAsync"/>.
    /// </summary>
    public async Task<List<RleFrameResult>> SegmentVideoRleAsync(
        string videoPath,
        int startFrame,
        int endFrame,
        (int xMin, int yMin, int xMax, int yMax)? seedBox = null,
        (int x, int y)? seedPoint = null,
        string? textPrompt = null,
        int promptFrame = -1,
        double detectionThreshold = 0.5,
        CancellationToken cancellationToken = default)
    {
        var apiKey = await _settings.GetAsync("falai_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            await _eventLog.LogEventAsync("SAM3", "NO_API_KEY", "Error", "falai_api_key not configured.");
            return new List<RleFrameResult>();
        }

        var endpoint = await _settings.GetAsync("falai_sam3_rle_endpoint", DefaultRleEndpoint);
        var queueBase = await _settings.GetAsync("falai_sam3_rle_queue_base_url", DefaultRleQueueBase);
        string? clipPath = null;

        try
        {
            var pf = promptFrame >= 0 ? promptFrame : startFrame;

            // Trim to just the requested frame range before submitting — passing the whole
            // source video (which can be tens of minutes long) makes fal.ai process far more
            // than needed and routinely times out the poll loop for what should be a short call.
            var fps = VideoProbe.GetFrameRate(videoPath);
            var clipDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "tmp-sam3");
            Directory.CreateDirectory(clipDir);
            var clipFileName = $"{Guid.NewGuid():N}.mp4";
            clipPath = Path.Combine(clipDir, clipFileName);

            var startSec = startFrame / fps;
            var durationSec = (endFrame - startFrame + 1) / fps;
            await RunFfmpegTrimAsync(videoPath, clipPath, startSec, durationSec, cancellationToken);

            // Prompts must be re-based to the trimmed clip's own frame numbering (it starts at 0).
            var clipPf = Math.Max(0, pf - startFrame);

            var videoBaseUrl = await _settings.GetAsync("sam3_video_base_url", "http://localhost:57220");
            var videoUrl = $"{videoBaseUrl}/api/content/file/tmp-sam3/{clipFileName}";

            var boxPrompts = seedBox.HasValue
                ? new object[] { new { frame_index = clipPf, x_min = seedBox.Value.xMin, y_min = seedBox.Value.yMin, x_max = seedBox.Value.xMax, y_max = seedBox.Value.yMax, object_id = 0 } }
                : Array.Empty<object>();

            var pointPrompts = seedPoint.HasValue
                ? new object[] { new { x = seedPoint.Value.x, y = seedPoint.Value.y, label = 1, object_id = 0, frame_index = clipPf } }
                : Array.Empty<object>();

            await _eventLog.LogEventAsync("SAM3", "RLE_SEGMENT_START", "Info",
                $"video={videoUrl}, frames={startFrame}-{endFrame} (clip {durationSec:F2}s), promptFrame={pf} (clip-relative {clipPf}), " +
                $"hasBox={seedBox.HasValue}, hasPoint={seedPoint.HasValue}, hasText={!string.IsNullOrEmpty(textPrompt)}, " +
                $"threshold={detectionThreshold}");

            var payload = new
            {
                video_url = videoUrl,
                prompt = textPrompt ?? string.Empty,
                point_prompts = pointPrompts,
                box_prompts = boxPrompts,
                apply_mask = false, // we need raw per-frame RLE data, not a baked video
                detection_threshold = detectionThreshold,
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await _eventLog.LogEventAsync("SAM3", "RLE_REQUEST_PAYLOAD", "Info", Truncate(json, 1500));

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Key {apiKey}");

            var submitResponse = await _http.PostAsync(endpoint, content, cancellationToken);
            var submitJson = await submitResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!submitResponse.IsSuccessStatusCode)
            {
                await _eventLog.LogEventAsync("SAM3", "RLE_HTTP_ERROR", "Error",
                    $"HTTP {(int)submitResponse.StatusCode} from submit: {Truncate(submitJson, 500)}");
                return new List<RleFrameResult>();
            }

            // `endpoint` (falai_sam3_rle_endpoint, default fal.run/...) is fal's SYNCHRONOUS host —
            // for calls that finish within its timeout it returns the completed result body directly,
            // not a request_id. Try parsing submitJson as a result first (covers both the nested
            // frames[] shape and the flat rle[]/metadata[] shape); only fall back to the async
            // queue.fal.run poll/fetch flow if that yields nothing and a request_id is present
            // (queueBase, e.g. when the sync host redirects a slow call into the queue).
            try
            {
                using var diagDoc = JsonDocument.Parse(submitJson);
                var diagRoot = diagDoc.RootElement.Clone();
                var sb = new StringBuilder();
                sb.Append('{');
                bool first = true;
                foreach (var prop in diagRoot.EnumerateObject())
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append($"\"{prop.Name}\":");
                    if (prop.Name == "rle" && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        // The RLE counts strings are huge (thousands of chars) and drown out
                        // everything else in a truncated log — summarize instead of dumping them.
                        sb.Append($"[<{prop.Value.GetArrayLength()} rle strings, lengths: ");
                        sb.Append(string.Join(",", prop.Value.EnumerateArray().Select(e => e.GetString()?.Length ?? 0)));
                        sb.Append(">]");
                    }
                    else
                    {
                        sb.Append(prop.Value.GetRawText());
                    }
                }
                sb.Append('}');
                await _eventLog.LogEventAsync("SAM3", "RLE_RESULT_RAW", "Info", Truncate(sb.ToString(), 3000));
            }
            catch (Exception diagEx)
            {
                await _eventLog.LogEventAsync("SAM3", "RLE_RESULT_RAW", "Warning", $"Diagnostic parse failed: {diagEx.Message}");
            }
            var frames = ParseFramesFromResultJson(submitJson);

            if (frames == null || frames.Count == 0)
            {
                string? requestId = null;
                using (var submitDoc = JsonDocument.Parse(submitJson))
                {
                    if (submitDoc.RootElement.TryGetProperty("request_id", out var rid))
                        requestId = rid.GetString();
                }

                if (!string.IsNullOrEmpty(requestId))
                {
                    await PollForRleResultAsync(queueBase, requestId, cancellationToken);
                    frames = await FetchRleFramesAsync(queueBase, requestId, cancellationToken);
                }
            }

            if (frames == null || frames.Count == 0)
            {
                await _eventLog.LogEventAsync("SAM3", "RLE_NO_FRAMES", "Warning",
                    $"SAM3 video-rle returned no frame data for {videoUrl} [{startFrame}-{endFrame}]. " +
                    $"Raw response: {Truncate(submitJson, 800)}");
                return new List<RleFrameResult>();
            }

            // frame_index in the response is relative to the trimmed clip — re-base to absolute
            // frame numbers so callers never need to know a clip was involved.
            var result = frames.Select(f => new RleFrameResult
            {
                FrameIndex = f.FrameIndex + startFrame,
                Objects = (f.Objects ?? new List<Sam3RleObjectMask>())
                    .Select(o => new RleObjectResult { TrackId = o.TrackId, Rle = o.Rle ?? string.Empty, Confidence = o.Confidence })
                    .ToList(),
            }).ToList();

            await _eventLog.LogEventAsync("SAM3", "RLE_SEGMENT_COMPLETE", "Info",
                $"Segmented {result.Count} frames, {result.Sum(f => f.Objects.Count)} total object-masks.");

            return result;
        }
        catch (OperationCanceledException)
        {
            await _eventLog.LogEventAsync("SAM3", "RLE_CANCELLED", "Warning", "Segmentation cancelled.");
            return new List<RleFrameResult>();
        }
        catch (Exception ex)
        {
            await _eventLog.LogEventAsync("SAM3", "RLE_EXCEPTION", "Error",
                $"SAM3 video-rle failed: {ex.GetType().Name} — {ex.Message}");
            return new List<RleFrameResult>();
        }
        finally
        {
            if (clipPath != null)
            {
                try { if (File.Exists(clipPath)) File.Delete(clipPath); } catch { }
            }
        }
    }

    private static async Task RunFfmpegTrimAsync(string sourcePath, string outputPath, double startSeconds, double durationSeconds, CancellationToken ct)
    {
        // Two-pass seek for frame accuracy: a single -ss before -i is a fast, keyframe-snapped
        // seek that can land seconds away from the requested timestamp — catastrophic for the
        // single-frame click-preview case (a ~0.03s clip), and a silent content-offset bug for
        // longer clips too, since callers re-base prompt frame numbers assuming the clip's
        // frame 0 is exactly startSeconds. Coarse pre-seek before -i for speed, small accurate
        // seek after -i for precision — matches the pattern already used elsewhere (e.g.
        // SurfaceDetectionPipeline.ExtractKeyFrameAsync).
        var preSeek = Math.Max(0, startSeconds - 2);
        var postSeek = startSeconds - preSeek;
        // A -t of exactly one frame's duration (e.g. 1/30s) sits right at the encoder's
        // rounding boundary — libx264 can silently emit zero frames (a valid-looking, empty
        // container) instead of one, which fal.ai then rejects with "could not decode frames".
        // Reproduced directly: a 0.033s request produced a 262-byte file with no video stream.
        // A few extra frames of headroom costs nothing (callers only care about clip-relative
        // frame 0) and reliably avoids the boundary.
        var duration = Math.Max(durationSeconds, 4.0 / 30);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -hide_banner -loglevel error " +
                    $"-ss {preSeek:F3} -noaccurate_seek -i \"{sourcePath.Replace("\\", "/")}\" " +
                    $"-ss {postSeek:F3} -t {duration:F3} -c:v libx264 -preset ultrafast -pix_fmt yuv420p -an \"{outputPath.Replace("\\", "/")}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.Start();

        var readStdout = process.StandardOutput.ReadToEndAsync(ct);
        var readStderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        await Task.WhenAll(readStdout, readStderr);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException($"Failed to trim video clip for SAM3 (exit {process.ExitCode}): {Truncate(await readStderr, 300)}");
        }
    }

    private async Task<Sam3RleVideoFile?> PollForRleResultAsync(string queueBase, string requestId, CancellationToken ct)
    {
        var statusUrl = $"{queueBase}/{requestId}/status";
        var resultUrl = $"{queueBase}/{requestId}";
        var deadline = DateTime.UtcNow.Add(TimeSpan.FromMinutes(5));

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var statusResp = await _http.GetAsync(statusUrl, ct);
                var statusJson = await statusResp.Content.ReadAsStringAsync(ct);

                if (!statusResp.IsSuccessStatusCode)
                {
                    await _eventLog.LogEventAsync("SAM3", "RLE_POLL_ERROR", "Warning",
                        $"Status HTTP {(int)statusResp.StatusCode}: {Truncate(statusJson, 200)}");
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    continue;
                }

                string status;
                try
                {
                    using var doc = JsonDocument.Parse(statusJson);
                    status = doc.RootElement.TryGetProperty("status", out var s)
                        ? (s.GetString() ?? "UNKNOWN") : "UNKNOWN";
                }
                catch (JsonException jex)
                {
                    await _eventLog.LogEventAsync("SAM3", "RLE_POLL_PARSE", "Error",
                        $"Failed to parse status JSON: {jex.Message}. Body: {Truncate(statusJson, 300)}");
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    continue;
                }

                if (status == "COMPLETED") break;
                if (status == "FAILED")
                {
                    await _eventLog.LogEventAsync("SAM3", "RLE_REQUEST_FAILED", "Error",
                        $"RLE request {requestId} failed.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("SAM3", "RLE_POLL_EXCEPTION", "Warning",
                    $"Poll error for {requestId}: {ex.Message}. Retrying...");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }

        // ── Fetch result ──
        try
        {
            var resultResp = await _http.GetAsync(resultUrl, ct);
            var resultJson = await resultResp.Content.ReadAsStringAsync(ct);

            if (!resultResp.IsSuccessStatusCode)
            {
                await _eventLog.LogEventAsync("SAM3", "RLE_RESULT_ERROR", "Error",
                    $"Result HTTP {(int)resultResp.StatusCode}: {Truncate(resultJson, 300)}");
                return null;
            }

            var result = JsonSerializer.Deserialize<Sam3RleResultResponse>(resultJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return result?.Video;
        }
        catch (JsonException jex)
        {
            await _eventLog.LogEventAsync("SAM3", "RLE_RESULT_PARSE", "Error",
                $"Failed to parse RLE result for {requestId}: {jex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            await _eventLog.LogEventAsync("SAM3", "RLE_RESULT_EXCEPTION", "Error",
                $"Fetch result failed for {requestId}: {ex.GetType().Name} — {ex.Message}");
            return null;
        }
    }

    private async Task<List<Sam3RleFrameData>?> FetchRleFramesAsync(string queueBase, string requestId, CancellationToken ct)
    {
        var resultUrl = $"{queueBase}/{requestId}";
        try
        {
            var resultResp = await _http.GetAsync(resultUrl, ct);
            var resultJson = await resultResp.Content.ReadAsStringAsync(ct);

            if (!resultResp.IsSuccessStatusCode)
            {
                await _eventLog.LogEventAsync("SAM3", "RLE_FRAMES_ERROR", "Error",
                    $"Frames HTTP {(int)resultResp.StatusCode}: {Truncate(resultJson, 300)}");
                return null;
            }

            return ParseFramesFromResultJson(resultJson);
        }
        catch (JsonException jex)
        {
            await _eventLog.LogEventAsync("SAM3", "RLE_FRAMES_PARSE", "Error",
                $"Failed to parse RLE frames for {requestId}: {jex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            await _eventLog.LogEventAsync("SAM3", "RLE_FRAMES_EXCEPTION", "Error",
                $"Fetch frames failed for {requestId}: {ex.GetType().Name} — {ex.Message}");
            return null;
        }
    }

    /// <summary>Parses a fal.ai SAM3 video-rle result body (from either the sync fal.run host
    /// or the async queue.fal.run result endpoint) into per-frame/per-object data, handling
    /// both response shapes seen in practice: nested frames[].objects[] (multi-object) and
    /// flat rle[]/metadata[] (our single object_id: 0 requests).</summary>
    private List<Sam3RleFrameData>? ParseFramesFromResultJson(string resultJson)
    {
        Sam3RleResultResponse? result;
        try
        {
            result = JsonSerializer.Deserialize<Sam3RleResultResponse>(resultJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException)
        {
            return null;
        }

        if (result?.Frames != null && result.Frames.Count > 0)
            return result.Frames;

        // Single-object requests (we always use object_id: 0) come back in a flat shape —
        // parallel top-level "rle"/"metadata" arrays, one entry per frame — rather than the
        // nested frames[].objects[] shape used for multi-object tracking. Confirmed against
        // a real fal.ai dashboard result: {"rle": ["", ...], "metadata": [{"index":0,"score":null,"box":null}, ...]}.
        if (result?.Rle != null && result.Rle.Count > 0)
        {
            var scoreSample = result.Metadata?.Take(5).Select(m => m.Score?.ToString("F2") ?? "null");
            _logger.LogInformation(
                "[SAM3] Flat-shape result: {Count} frames, {NonEmpty} with a mask, sample scores=[{Scores}]",
                result.Rle.Count, result.Rle.Count(r => !string.IsNullOrEmpty(r)),
                scoreSample != null ? string.Join(",", scoreSample) : "n/a");
            return FramesFromFlatRle(result.Rle, result.Metadata);
        }

        return null;
    }

    /// <summary>Converts the flat single-object {rle[], metadata[]} shape into the same
    /// per-frame/per-object structure the nested shape produces, so callers never need to
    /// know which shape fal.ai returned. Frame index is taken from metadata[i].Index when
    /// present (falls back to the array position), matching the nested shape's frame_index
    /// semantics (0-based, relative to the trimmed clip).</summary>
    private static List<Sam3RleFrameData> FramesFromFlatRle(List<string> rle, List<Sam3MaskMetadata>? metadata)
    {
        var frames = new List<Sam3RleFrameData>();
        for (int i = 0; i < rle.Count; i++)
        {
            var meta = metadata != null && i < metadata.Count ? metadata[i] : null;
            var frameIndex = meta?.Index ?? i;
            var objects = string.IsNullOrEmpty(rle[i])
                ? new List<Sam3RleObjectMask>()
                : new List<Sam3RleObjectMask> { new() { TrackId = 0, Rle = rle[i], Confidence = meta?.Score ?? 0 } };

            frames.Add(new Sam3RleFrameData { FrameIndex = frameIndex, Objects = objects });
        }
        return frames;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    // ── SAM3 video-rle JSON models ──

    private class Sam3RleResultResponse
    {
        [JsonPropertyName("video")]
        public Sam3RleVideoFile? Video { get; set; }

        [JsonPropertyName("frames")]
        public List<Sam3RleFrameData>? Frames { get; set; }

        // ── Flat single-object shape (object_id: 0 requests) — see FramesFromFlatRle ──
        [JsonPropertyName("rle")]
        public List<string>? Rle { get; set; }

        [JsonPropertyName("metadata")]
        public List<Sam3MaskMetadata>? Metadata { get; set; }
    }

    private class Sam3MaskMetadata
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("score")]
        public double? Score { get; set; }

        [JsonPropertyName("box")]
        public List<double>? Box { get; set; }
    }

    private class Sam3RleVideoFile
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }
    }

    private class Sam3RleFrameData
    {
        [JsonPropertyName("frame_index")]
        public int FrameIndex { get; set; }

        [JsonPropertyName("objects")]
        public List<Sam3RleObjectMask>? Objects { get; set; }
    }

    private class Sam3RleObjectMask
    {
        [JsonPropertyName("track_id")]
        public int TrackId { get; set; }

        [JsonPropertyName("rle")]
        public string? Rle { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
    }
}
