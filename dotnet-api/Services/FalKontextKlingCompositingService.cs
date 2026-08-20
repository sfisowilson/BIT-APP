using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Two-step fal.ai compositing engine: FLUX.1 Kontext (frame-level surgical asset placement) →
/// Kling O3 Pro Edit (video-level propagation of the composited frame across the whole scene).
///
/// Step 1 — FLUX.1 Kontext [max] multi: accepts the video frame at the surface's detected-at
/// position + brand asset image + placement prompt. Returns a composited frame with the asset
/// surgically placed, everything else unchanged.
///
/// Step 2 — Kling O3 Pro Edit video-to-video: accepts the original scene clip + composited frame
/// as a visual reference + prompt. Propagates the edit across the full scene while preserving
/// camera motion, people, and everything else.
///
/// Activated when engine_compositing = "fal-kontext-kling".
/// Endpoints: https://queue.fal.run/fal-ai/flux-pro/kontext/max/multi
///            https://queue.fal.run/fal-ai/kling-video/o3/pro/video-to-video/edit
/// </summary>
public class FalKontextKlingCompositingService : ICompositingService
{
    private readonly PostgresDbContext _context;
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<FalKontextKlingCompositingService> _logger;
    private readonly IEventLogService _eventLog;
    private readonly HttpClient _http;

    private const string DefaultKontextEndpoint = "https://queue.fal.run/fal-ai/flux-pro/kontext/max/multi";
    private const string DefaultKlingEndpoint = "https://queue.fal.run/fal-ai/kling-video/o3/pro/video-to-video/edit";
    private const string DefaultNanoBananaProEndpoint = "https://queue.fal.run/fal-ai/nano-banana-pro/edit";
    private static readonly TimeSpan MaxPollTime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    public FalKontextKlingCompositingService(
        PostgresDbContext context,
        IPlatformSettingsService settings,
        ILogger<FalKontextKlingCompositingService> logger,
        IEventLogService eventLog)
    {
        _context = context;
        _settings = settings;
        _logger = logger;
        _eventLog = eventLog;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
    }

    /// <summary>
    /// Standard ICompositingService entry point. Looks up the surface to get its SurfaceType
    /// description and DetectedAtFrame, extracts that frame, calls FLUX Kontext to composite
    /// the asset into it, and returns the composited frame as a base64 preview image.
    /// </summary>
    public async Task<CompositedFrame> CompositeAsync(CompositingRequest request)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var apiKey = await _settings.GetAsync("falai_api_key");
            if (string.IsNullOrEmpty(apiKey))
            {
                await _eventLog.LogEventAsync("FalKontextKling", "NO_API_KEY", "Error", "falai_api_key not configured.");
                return new CompositedFrame
                {
                    ImageBase64 = string.Empty,
                    ContentType = "text/plain",
                    EngineUsed = "FalKontextKling",
                    ProcessingMs = sw.ElapsedMilliseconds
                };
            }

            // 1. Look up the surface to get its description and detected-at frame
            var surface = await _context.SurfaceItems.FindAsync(request.SurfaceId);
            if (surface == null)
                throw new ArgumentException($"Surface {request.SurfaceId} not found.");

            var surfaceDescription = !string.IsNullOrEmpty(surface.SurfaceType)
                ? surface.SurfaceType
                : "the surface";

            var captureFrame = surface.DetectedAtFrame > 0
                ? surface.DetectedAtFrame.Value
                : request.FrameNumber;

            // 2. Look up asset
            var asset = await _context.CreativeAssets.FindAsync(request.AssetId);
            if (asset == null)
                throw new ArgumentException($"Asset {request.AssetId} not found.");

            var assetPath = ResolveAssetPath(asset.StorageKey);
            if (!File.Exists(assetPath))
                throw new InvalidOperationException($"Asset file not found: {assetPath}");

            // 3. Look up video file
            var content = await _context.ContentItems.FindAsync(request.ContentId);
            if (content == null)
                throw new ArgumentException($"Content {request.ContentId} not found.");

            var videoPath = ResolveVideoPath(content.StorageKey);
            if (!File.Exists(videoPath))
                throw new InvalidOperationException($"Video file not found: {videoPath}");

            // 4. Extract the frame
            var fps = content.FrameRate > 0 ? content.FrameRate : 25;
            var framePath = await ExtractFrameAsync(videoPath, captureFrame, fps,
                $"{request.ContentId}_{captureFrame}");
            if (framePath == null)
            {
                await _eventLog.LogEventAsync("FalKontextKling", "FRAME_EXTRACT_FAILED", "Error",
                    $"Failed to extract frame {captureFrame} from content {request.ContentId}");
                return new CompositedFrame
                {
                    ImageBase64 = string.Empty,
                    ContentType = "text/plain",
                    EngineUsed = "FalKontextKling",
                    ProcessingMs = sw.ElapsedMilliseconds
                };
            }

            // 5. Build public URLs for frame and asset
            var videoBaseUrl = await _settings.GetAsync("sam3_video_base_url", "http://localhost:57220");
            var frameFileName = Path.GetFileName(framePath);
            var frameUrl = $"{videoBaseUrl}/api/content/file/tmp-renders/frames/{frameFileName}";

            var assetFileName = Path.GetFileName(assetPath);
            var assetUrl = $"{videoBaseUrl}/api/assets/file/{assetFileName}";

            // 6. Call FLUX Kontext
            var (frameWidth, frameHeight) = VideoProbe.GetDimensions(videoPath);
            var compositedUrl = await CompositeFrameWithKontextAsync(
                frameUrl, assetUrl, surfaceDescription, request.BoundaryCoordinatesJson /* user prompt */,
                request.SurfaceId, frameWidth, frameHeight, ct: CancellationToken.None);

            if (compositedUrl == null)
                return new CompositedFrame
                {
                    ImageBase64 = string.Empty,
                    ContentType = "text/plain",
                    EngineUsed = "FalKontextKling",
                    ProcessingMs = sw.ElapsedMilliseconds
                };

            // 7. Download the composited frame and return as base64
            var imageBytes = await _http.GetByteArrayAsync(compositedUrl);
            var base64 = Convert.ToBase64String(imageBytes);
            var contentType = "image/png";

            try { File.Delete(framePath); } catch { }

            return new CompositedFrame
            {
                ImageBase64 = base64,
                ContentType = contentType,
                EngineUsed = "FalKontextKling",
                ProcessingMs = sw.ElapsedMilliseconds
            };
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FalKontextKling] CompositeAsync failed");
            await _eventLog.LogEventAsync("FalKontextKling", "COMPOSITE_ERROR", "Error", ex.Message);
            return new CompositedFrame
            {
                ImageBase64 = string.Empty,
                ContentType = "text/plain",
                EngineUsed = "FalKontextKling",
                ProcessingMs = sw.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Step 1: Call FLUX.1 Kontext [max] multi to composite the asset image onto the scene frame.
    /// Returns the URL of the composited output image.
    /// </summary>
    /// <param name="frameUrl">Publicly accessible URL of the scene frame image (the surface's detected-at frame).</param>
    /// <param name="assetUrl">Publicly accessible URL of the brand asset image.</param>
    /// <param name="surfaceDescription">Gemini-generated surface description (SurfaceType) for placement context.</param>
    /// <param name="userPlacementPrompt">User's placement instruction (where/how to place the asset).</param>
    /// <param name="correlationId">Surface or render ID for logging.</param>
    /// <param name="frameWidth">The source frame's actual pixel width — used to pick the closest
    /// aspect_ratio this endpoint supports. Without this, the API silently falls back to its own
    /// default aspect ratio regardless of the real frame shape, producing a composited image whose
    /// proportions don't match the source video — visible as stretching once it's later scaled
    /// back into the actual frame dimensions.</param>
    /// <param name="frameHeight">The source frame's actual pixel height. See frameWidth.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>URL of the composited frame image, or null.</returns>
    public async Task<string?> CompositeFrameWithKontextAsync(
        string frameUrl,
        string assetUrl,
        string surfaceDescription,
        string userPlacementPrompt,
        string correlationId,
        int frameWidth,
        int frameHeight,
        CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync("falai_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            await _eventLog.LogEventAsync("FalKontextKling", "NO_API_KEY", "Error", "falai_api_key not configured.");
            return null;
        }

        var endpoint = await _settings.GetAsync("kontext_endpoint", DefaultKontextEndpoint);
        var aspectRatio = ClosestSupportedAspectRatio(frameWidth, frameHeight);

        // Only mention a surface description when it's an actual detected SurfaceType — "the
        // surface" is just the generic fallback used when no surface was detected, and stating
        // it literally ("on the the surface") is both a grammar bug and adds nothing. The user's
        // own instruction (userPlacementPrompt) — whether hand-written or Gemini-suggested via
        // the prompt-suggestion feature — already carries the actual placement/lighting/
        // perspective guidance; this wrapper used to restate a generic version of that ahead of
        // it, which diluted and sometimes conflicted with a well-crafted instruction. Keep the
        // wrapper to only what Kontext structurally needs: which image is which, the user's
        // instruction verbatim, and the "don't touch anything else" safety clause.
        var surfaceHint = !string.IsNullOrWhiteSpace(surfaceDescription) &&
            !surfaceDescription.Equals("the surface", StringComparison.OrdinalIgnoreCase)
                ? $" The target surface has been identified as: {surfaceDescription}."
                : "";

        var prompt = $"The first image is the scene to edit; the second image is the brand asset to composite into it. " +
                     $"{userPlacementPrompt}{surfaceHint} " +
                     "Do not change anything else in the scene — keep all people, objects, text, and background exactly as they are.";

        await _eventLog.LogEventAsync("FalKontextKling", "KONTEXT_START", "Info",
            $"Correlation {correlationId}: frame={TruncateUrl(frameUrl)}, asset={TruncateUrl(assetUrl)}, " +
            $"surface='{surfaceDescription}', aspectRatio={aspectRatio} ({frameWidth}x{frameHeight}), prompt='{userPlacementPrompt}'");

        var payload = new { prompt, image_urls = new[] { frameUrl, assetUrl }, aspect_ratio = aspectRatio };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("Authorization", $"Key {apiKey}");

        try
        {
            var submitResponse = await _http.PostAsync(endpoint, content, ct);
            var submitJson = await submitResponse.Content.ReadAsStringAsync(ct);

            if (!submitResponse.IsSuccessStatusCode)
            {
                await _eventLog.LogEventAsync("FalKontextKling", "KONTEXT_SUBMIT_ERROR", "Error",
                    $"HTTP {(int)submitResponse.StatusCode}: {Truncate(submitJson, 500)}");
                return null;
            }

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
                await _eventLog.LogEventAsync("FalKontextKling", "KONTEXT_NO_REQUEST_ID", "Error",
                    $"Missing request_id/status_url/response_url: {Truncate(submitJson, 300)}");
                return null;
            }

            // Poll for result
            var imageUrl = await PollKontextResultAsync(statusUrl, resultUrl, requestId, correlationId, ct);
            return imageUrl;
        }
        catch (OperationCanceledException)
        {
            await _eventLog.LogEventAsync("FalKontextKling", "KONTEXT_CANCELLED", "Warning", "Kontext compositing cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            await _eventLog.LogEventAsync("FalKontextKling", "KONTEXT_EXCEPTION", "Error",
                $"Kontext failed: {ex.GetType().Name} — {ex.Message}");
            _logger.LogError(ex, "[FalKontextKling] Correlation {Id} FAILED", correlationId);
            return null;
        }
    }

    /// <summary>
    /// Alternative to Step 1: composite via Google's Nano Banana Pro (Gemini 3 Pro Image) instead
    /// of FLUX.1 Kontext. Same frame + asset → composited-frame contract as
    /// <see cref="CompositeFrameWithKontextAsync"/> and shares its polling/result-parsing logic
    /// (fal.ai's queue contract — request_id/status_url/response_url, images[0].url — is uniform
    /// across their catalog) — offered as a user-selectable option because Nano Banana Pro's
    /// stronger scene-understanding priors tend to integrate lighting/shadows/depth more
    /// convincingly than FLUX Kontext, which is comparatively stronger at identity preservation.
    /// </summary>
    public async Task<string?> CompositeFrameWithNanoBananaAsync(
        string frameUrl,
        string assetUrl,
        string surfaceDescription,
        string userPlacementPrompt,
        string correlationId,
        int frameWidth,
        int frameHeight,
        CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync("falai_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            await _eventLog.LogEventAsync("FalKontextKling", "NO_API_KEY", "Error", "falai_api_key not configured.");
            return null;
        }

        var endpoint = await _settings.GetAsync("nano_banana_pro_endpoint", DefaultNanoBananaProEndpoint);

        // "auto" sounded like it should infer the source frame's own ratio, but in practice it
        // does not reliably preserve it — Nano Banana Pro would return a differently-proportioned
        // image, breaking downstream alignment with the source video. Nano Banana Pro's enum is
        // a near-superset of FLUX Kontext's (missing only "9:21"), so the same closest-match
        // helper applies with a fallback for that one label.
        var aspectRatio = ClosestSupportedAspectRatio(frameWidth, frameHeight);
        if (aspectRatio == "9:21") aspectRatio = "9:16";

        var surfaceHint = !string.IsNullOrWhiteSpace(surfaceDescription) &&
            !surfaceDescription.Equals("the surface", StringComparison.OrdinalIgnoreCase)
                ? $" The target surface has been identified as: {surfaceDescription}."
                : "";

        var prompt = $"The first image is the scene to edit; the second image is the brand asset to composite into it. " +
                     $"{userPlacementPrompt}{surfaceHint} " +
                     "Match the scene's existing lighting direction, color temperature, shadows, and depth of field when placing the asset. " +
                     "Do not change anything else in the scene — keep all people, objects, text, and background exactly as they are.";

        await _eventLog.LogEventAsync("FalKontextKling", "NANO_BANANA_START", "Info",
            $"Correlation {correlationId}: frame={TruncateUrl(frameUrl)}, asset={TruncateUrl(assetUrl)}, " +
            $"surface='{surfaceDescription}', aspectRatio={aspectRatio} ({frameWidth}x{frameHeight}), prompt='{userPlacementPrompt}'");

        var payload = new { prompt, image_urls = new[] { frameUrl, assetUrl }, aspect_ratio = aspectRatio, num_images = 1 };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("Authorization", $"Key {apiKey}");

        try
        {
            var submitResponse = await _http.PostAsync(endpoint, content, ct);
            var submitJson = await submitResponse.Content.ReadAsStringAsync(ct);

            if (!submitResponse.IsSuccessStatusCode)
            {
                await _eventLog.LogEventAsync("FalKontextKling", "NANO_BANANA_SUBMIT_ERROR", "Error",
                    $"HTTP {(int)submitResponse.StatusCode}: {Truncate(submitJson, 500)}");
                return null;
            }

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
                await _eventLog.LogEventAsync("FalKontextKling", "NANO_BANANA_NO_REQUEST_ID", "Error",
                    $"Missing request_id/status_url/response_url: {Truncate(submitJson, 300)}");
                return null;
            }

            return await PollKontextResultAsync(statusUrl, resultUrl, requestId, correlationId, ct);
        }
        catch (OperationCanceledException)
        {
            await _eventLog.LogEventAsync("FalKontextKling", "NANO_BANANA_CANCELLED", "Warning", "Nano Banana Pro compositing cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            await _eventLog.LogEventAsync("FalKontextKling", "NANO_BANANA_EXCEPTION", "Error",
                $"Nano Banana Pro failed: {ex.GetType().Name} — {ex.Message}");
            _logger.LogError(ex, "[FalKontextKling] Nano Banana correlation {Id} FAILED", correlationId);
            return null;
        }
    }

    /// <summary>
    /// Step 2: Call Kling O3 Pro Edit video-to-video to propagate the composited frame across the
    /// whole scene video. Uses the composited frame as a visual reference anchor so the model
    /// knows exactly what to place and where.
    /// </summary>
    /// <param name="videoUrl">Publicly accessible URL of the source scene clip.</param>
    /// <param name="compositedFrameUrl">Publicly accessible URL of the FLUX Kontext composited frame.</param>
    /// <param name="userPlacementPrompt">User's placement instruction.</param>
    /// <param name="renderId">Render ID for logging and download path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Local file path to the downloaded generated video, or null.</returns>
    public async Task<string?> PropagateWithKlingAsync(
        string videoUrl,
        string compositedFrameUrl,
        string userPlacementPrompt,
        string renderId,
        CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync("falai_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            await _eventLog.LogEventAsync("FalKontextKling", "NO_API_KEY", "Error", "falai_api_key not configured.");
            return null;
        }

        var endpoint = await _settings.GetAsync("kling_edit_endpoint", DefaultKlingEndpoint);

        var prompt = $"Add the product/ad exactly as shown in the reference image. {userPlacementPrompt}. " +
                     "Keep everyone and everything else in the scene completely unchanged — same people, motion, camera, and background.";

        await _eventLog.LogEventAsync("FalKontextKling", "KLING_START", "Info",
            $"Render {renderId}: video={TruncateUrl(videoUrl)}, reference={TruncateUrl(compositedFrameUrl)}, " +
            $"prompt='{userPlacementPrompt}'");

        var payload = new
        {
            video_url = videoUrl,
            image_urls = new[] { compositedFrameUrl },
            prompt,
            keep_audio = true,
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("Authorization", $"Key {apiKey}");

        _logger.LogInformation("[FalKontextKling] Render {RenderId} prompt='{Prompt}'", renderId, prompt);

        try
        {
            // Submit
            var submitResponse = await _http.PostAsync(endpoint, content, ct);
            var submitJson = await submitResponse.Content.ReadAsStringAsync(ct);

            if (!submitResponse.IsSuccessStatusCode)
            {
                await _eventLog.LogEventAsync("FalKontextKling", "KLING_SUBMIT_ERROR", "Error",
                    $"HTTP {(int)submitResponse.StatusCode}: {Truncate(submitJson, 500)}");
                return null;
            }

            await _eventLog.LogEventAsync("FalKontextKling", "KLING_SUBMITTED", "Info",
                $"Submit response: {Truncate(submitJson, 500)}");

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
                await _eventLog.LogEventAsync("FalKontextKling", "KLING_NO_REQUEST_ID", "Error",
                    $"Missing request_id/status_url/response_url: {Truncate(submitJson, 300)}");
                return null;
            }

            // Poll for result
            var videoPath = await PollKlingResultAsync(statusUrl, resultUrl, requestId, renderId, ct);

            if (videoPath == null)
            {
                await _eventLog.LogEventAsync("FalKontextKling", "KLING_NO_VIDEO", "Error",
                    $"Kling O1 did not return a video. request_id={requestId}");
                return null;
            }

            await _eventLog.LogEventAsync("FalKontextKling", "KLING_COMPLETE", "Info",
                $"Render {renderId}: generated video saved to {videoPath}");

            return videoPath;
        }
        catch (OperationCanceledException)
        {
            await _eventLog.LogEventAsync("FalKontextKling", "KLING_CANCELLED", "Warning", "Kling edit cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            await _eventLog.LogEventAsync("FalKontextKling", "KLING_EXCEPTION", "Error",
                $"Kling edit failed: {ex.GetType().Name} — {ex.Message}");
            _logger.LogError(ex, "[FalKontextKling] Render {RenderId} FAILED", renderId);
            return null;
        }
    }

    // ── Frame extraction ──

    private static async Task<string?> ExtractFrameAsync(string videoPath, int frameNumber, double fps, string nameHint)
    {
        var framesDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "tmp-renders", "frames");
        Directory.CreateDirectory(framesDir);
        var outputPath = Path.Combine(framesDir, $"frame_{nameHint}.png");

        // Try frame-accurate extraction first
        var args = $"-y -hide_banner -loglevel error -i \"{videoPath.Replace("\\", "/")}\" " +
                   $"-vf \"select=eq(n\\,{frameNumber})\" -vframes 1 \"{outputPath.Replace("\\", "/")}\"";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length >= 100)
            return outputPath;

        // Fall back to time-based seek
        var seekTime = frameNumber / fps;
        args = $"-y -hide_banner -loglevel error -ss {seekTime:F3} -i \"{videoPath.Replace("\\", "/")}\" " +
               $"-vframes 1 \"{outputPath.Replace("\\", "/")}\"";
        process.Start();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length >= 100
            ? outputPath
            : null;
    }

    // ── Path resolution (mirrors RenderJobService.ResolveAssetPath/ResolveVideoPath) ──

    private static string ResolveAssetPath(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey)) throw new ArgumentException("Invalid asset storage key");
        var fileName = storageKey.Replace("/api/assets/file/", "");
        return Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "assets", fileName);
    }

    private static string? ResolveVideoPath(string? storageKey)
    {
        if (string.IsNullOrEmpty(storageKey)) return null;
        var fileName = storageKey.Replace("/api/content/file/", "");
        var path = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", fileName);
        if (File.Exists(path)) return path;
        var proxyPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "proxy",
            fileName.Replace(".mov", "_proxy.mp4").Replace(".avi", "_proxy.mp4"));
        return File.Exists(proxyPath) ? proxyPath : null;
    }

    // ── Polling for FLUX Kontext (image result) ──

    private async Task<string?> PollKontextResultAsync(string statusUrl, string resultUrl, string requestId, string correlationId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.Add(MaxPollTime);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var statusResp = await _http.GetAsync(statusUrl, ct);
                var statusJson = await statusResp.Content.ReadAsStringAsync(ct);

                if (!statusResp.IsSuccessStatusCode)
                {
                    await Task.Delay(PollInterval, ct);
                    continue;
                }

                using var doc = JsonDocument.Parse(statusJson);
                var root = doc.RootElement;
                var status = "UNKNOWN";
                if (root.TryGetProperty("status", out var s))
                    status = s.GetString() ?? "UNKNOWN";

                if (status == "COMPLETED") break;
                if (status == "FAILED")
                {
                    // The status payload usually carries the actual rejection reason (bad input
                    // image, content moderation, etc.) — without logging it, every failure looked
                    // identical ("did not return a composited frame") regardless of cause.
                    await _eventLog.LogEventAsync("FalKontextKling", "KONTEXT_JOB_FAILED", "Error",
                        $"Correlation {correlationId}: fal.ai job {requestId} reported status=FAILED. Response: {Truncate(statusJson, 1000)}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FalKontextKling] Kontext poll error for {RequestId}", requestId);
            }

            await Task.Delay(PollInterval, ct);
        }

        // Fetch result — FLUX returns images array
        var resultResp = await _http.GetAsync(resultUrl, ct);
        var resultJson = await resultResp.Content.ReadAsStringAsync(ct);

        if (!resultResp.IsSuccessStatusCode)
        {
            await _eventLog.LogEventAsync("FalKontextKling", "KONTEXT_RESULT_FETCH_ERROR", "Error",
                $"Correlation {correlationId}: fetching result for {requestId} returned HTTP {(int)resultResp.StatusCode}: {Truncate(resultJson, 1000)}");
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
            {
                var url = images[0].GetProperty("url").GetString();
                return url;
            }
            await _eventLog.LogEventAsync("FalKontextKling", "KONTEXT_NO_IMAGES", "Error",
                $"Correlation {correlationId}: result for {requestId} had no images array. Response: {Truncate(resultJson, 1000)}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FalKontextKling] Failed to parse Kontext result");
            return null;
        }
    }

    // ── Polling for Kling O1 (video result) ──

    private async Task<string?> PollKlingResultAsync(string statusUrl, string resultUrl, string requestId, string renderId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.Add(MaxPollTime);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var statusResp = await _http.GetAsync(statusUrl, ct);
                var statusJson = await statusResp.Content.ReadAsStringAsync(ct);

                if (!statusResp.IsSuccessStatusCode)
                {
                    await Task.Delay(PollInterval, ct);
                    continue;
                }

                using var doc = JsonDocument.Parse(statusJson);
                var root = doc.RootElement;
                var status = "UNKNOWN";
                if (root.TryGetProperty("status", out var s))
                    status = s.GetString() ?? "UNKNOWN";

                if (status == "COMPLETED") break;
                if (status == "FAILED") return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FalKontextKling] Kling poll error for {RequestId}", requestId);
            }

            await Task.Delay(PollInterval, ct);
        }

        // Fetch result
        var resultResp = await _http.GetAsync(resultUrl, ct);
        var resultJson = await resultResp.Content.ReadAsStringAsync(ct);

        if (!resultResp.IsSuccessStatusCode) return null;

        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("video", out var video) && video.TryGetProperty("url", out var url))
            {
                var videoUrl = url.GetString();
                if (string.IsNullOrEmpty(videoUrl)) return null;

                var videoBytes = await _http.GetByteArrayAsync(videoUrl, ct);
                var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "tmp-renders", renderId);
                Directory.CreateDirectory(outputDir);
                var outputPath = Path.Combine(outputDir, $"preview_{requestId[..8]}.mp4");
                await File.WriteAllBytesAsync(outputPath, videoBytes, ct);

                return outputPath;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FalKontextKling] Failed to parse Kling result");
            return null;
        }
    }

    // ── Helpers ──

    /// <summary>fal.ai's flux-pro/kontext/max/multi endpoint only accepts a fixed enum of aspect
    /// ratios (not arbitrary width/height) — this picks whichever one is closest to the source
    /// frame's actual ratio, so the composited output's proportions match the real video instead
    /// of silently defaulting to whatever the API's own default happens to be.</summary>
    private static readonly (string Label, double Ratio)[] SupportedAspectRatios =
    {
        ("21:9", 21.0 / 9), ("16:9", 16.0 / 9), ("4:3", 4.0 / 3), ("3:2", 3.0 / 2), ("1:1", 1.0),
        ("2:3", 2.0 / 3), ("3:4", 3.0 / 4), ("9:16", 9.0 / 16), ("9:21", 9.0 / 21),
    };

    internal static string ClosestSupportedAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0) return "16:9";
        var actual = (double)width / height;
        return SupportedAspectRatios
            .OrderBy(ar => Math.Abs(ar.Ratio - actual))
            .First().Label;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static string TruncateUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "(empty)";
        return url.Length <= 50 ? url : "..." + url[^40..];
    }
}
