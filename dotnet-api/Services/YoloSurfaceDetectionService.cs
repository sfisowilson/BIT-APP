using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls the BIT YOLO Detection Service (Python FastAPI) for real object-detection-based
/// surface discovery and ByteTrack frame-to-frame tracking.
/// Activated when engine_detection = "yolo".
///
/// The Python service runs YOLOv11 with ByteTrack, detecting billboards, TV screens,
/// digital signage, and other ad-placeable rectangular surfaces with stable track IDs
/// across frames.
/// </summary>
public class YoloSurfaceDetectionService : ISurfaceDetectionService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<YoloSurfaceDetectionService> _logger;
    private readonly PostgresDbContext _db;
    private readonly HttpClient _http;

    // Default YOLO service URL — override via platform setting "yolo_service_url"
    private const string DefaultServiceUrl = "http://localhost:8001";

    public YoloSurfaceDetectionService(
        IPlatformSettingsService settings,
        ILogger<YoloSurfaceDetectionService> logger,
        PostgresDbContext db)
    {
        _settings = settings;
        _logger = logger;
        _db = db;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) }; // video processing can take time (CPU-bound YOLO on long scenes)
    }

    public async Task<List<SurfaceDetectionResult>> DetectAsync(
        string contentId, int sceneIndex, int startFrame, int endFrame,
        CancellationToken cancellationToken = default)
    {
        var serviceUrl = await _settings.GetAsync("yolo_service_url", DefaultServiceUrl);

        // Health check with retry — YOLO service may be briefly restarting or under load.
        // Retry 3 times with 2s backoff before failing hard.
        var healthy = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (await IsServiceHealthy(serviceUrl))
            {
                healthy = true;
                break;
            }
            if (attempt < 2)
            {
                _logger.LogWarning(
                    "[YOLO] Health check attempt {Attempt}/3 failed for {Url} — retrying in 2s",
                    attempt + 1, serviceUrl);
                await Task.Delay(2000, cancellationToken);
            }
        }

        if (!healthy)
        {
            throw new InvalidOperationException(
                $"YOLO detection service at {serviceUrl} is not reachable after 3 attempts. Ensure the Python detection service is running.");
        }

        // Resolve the video path — look up content storage path
        var videoPath = await ResolveVideoPath(contentId);
        if (string.IsNullOrEmpty(videoPath))
        {
            throw new InvalidOperationException(
                $"Could not resolve video file path for content '{contentId}'. Ensure the video has been uploaded and transcoded.");
        }

        try
        {
            var payload = new
            {
                content_id = contentId,
                scene_index = sceneIndex,
                start_frame = startFrame,
                end_frame = endFrame,
                video_path = videoPath,
                model_size = await _settings.GetAsync("yolo_model_size", "large"),
                confidence_threshold = await _settings.GetDoubleAsync("yolo_confidence", 0.35),
                iou_threshold = await _settings.GetDoubleAsync("yolo_iou", 0.45),
                tracked = true,
                frame_skip = await _settings.GetIntAsync("yolo_frame_skip", 1),
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation(
                "[YOLO] Sending detection request for content={ContentId} scene={Scene} frames={Start}-{End}",
                contentId, sceneIndex, startFrame, endFrame);

            var response = await _http.PostAsync($"{serviceUrl.TrimEnd('/')}/detect", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<YoloDetectionResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (result?.Surfaces == null || result.Surfaces.Count == 0)
            {
                _logger.LogInformation("[YOLO] No surfaces detected for content={ContentId} scene={Scene}", contentId, sceneIndex);
                return new List<SurfaceDetectionResult>();
            }

            var surfaces = new List<SurfaceDetectionResult>();
            foreach (var s in result.Surfaces)
            {
                surfaces.Add(new SurfaceDetectionResult
                {
                    SurfaceType = s.SurfaceType ?? "Detected Surface",
                    BoundaryCoordinatesJson = JsonSerializer.Serialize(s.BoundaryCoordinates ?? new List<Coord>()),
                    EstimatedDepth = s.EstimatedDepth,
                    OrientationVectorJson = JsonSerializer.Serialize(s.OrientationVector ?? new Orientation()),
                    ConfidenceScore = s.ConfidenceScore,
                    ViabilityScore = s.ViabilityScore,
                    ExclusionReason = s.ExclusionReason,
                });
            }

            _logger.LogInformation(
                "[YOLO] Detection complete: {Count} surfaces found in {Frames} frames ({TimeMs}ms) for {ContentId}",
                surfaces.Count, result.FramesProcessed, result.ProcessingTimeMs, contentId);

            return surfaces;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"[YOLO] HTTP error communicating with detection service at {serviceUrl}: {ex.Message}", ex);
        }
        catch (TaskCanceledException)
        {
            throw new TimeoutException(
                $"[YOLO] Detection service at {serviceUrl} timed out after {_http.Timeout.TotalSeconds}s.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"[YOLO] Failed to parse detection response from {serviceUrl}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Pings the YOLO service health endpoint to verify it's running.
    /// </summary>
    private async Task<bool> IsServiceHealthy(string serviceUrl)
    {
        try
        {
            var response = await _http.GetAsync($"{serviceUrl.TrimEnd('/')}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves the local filesystem path to the video file for the given content ID.
    /// Looks up the content record to get the actual storage filename, then finds it on disk.
    /// </summary>
    private async Task<string> ResolveVideoPath(string contentId)
    {
        try
        {
            // Look up the content record to get the storage key (which contains the real filename)
            var content = await _db.ContentItems.FindAsync(contentId);
            if (content != null && !string.IsNullOrEmpty(content.StorageKey))
            {
                // StorageKey format: /api/content/file/{filename}
                var fileName = content.StorageKey.Replace("/api/content/file/", "");
                var uploadsDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Uploads");
                var filePath = System.IO.Path.Combine(uploadsDir, fileName);
                if (System.IO.File.Exists(filePath))
                {
                    _logger.LogInformation("[YOLO] Resolved video path for {ContentId}: {Path}", contentId, filePath);
                    return filePath;
                }

                // Try proxy version
                var proxyPath = System.IO.Path.Combine(uploadsDir, "proxies", fileName);
                if (System.IO.File.Exists(proxyPath))
                {
                    _logger.LogInformation("[YOLO] Using proxy video for {ContentId}: {Path}", contentId, proxyPath);
                    return proxyPath;
                }

                // Try with _proxy suffix
                var nameNoExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
                var ext = System.IO.Path.GetExtension(fileName);
                var proxyName = $"{nameNoExt}_proxy{ext}";
                var proxyAltPath = System.IO.Path.Combine(uploadsDir, proxyName);
                if (System.IO.File.Exists(proxyAltPath))
                {
                    _logger.LogInformation("[YOLO] Using proxy video for {ContentId}: {Path}", contentId, proxyAltPath);
                    return proxyAltPath;
                }

                _logger.LogWarning("[YOLO] Storage key resolved to {FileName} but file not found on disk for {ContentId}", fileName, contentId);
            }
            else
            {
                _logger.LogWarning("[YOLO] No content record or storage key found for {ContentId}", contentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[YOLO] Error resolving video path for {ContentId}", contentId);
        }

        return string.Empty;
    }

    /// <summary>
    /// Batch detection: sends all scenes for a content item in a single HTTP call.
    /// The Python service opens the video once and processes all scene ranges sequentially,
    /// eliminating per-scene video I/O overhead (major speed improvement for multi-scene videos).
    /// </summary>
    public async Task<List<SceneDetectionBatchResult>> DetectBatchAsync(
        string contentId, string videoPath, List<SceneCut> scenes,
        CancellationToken cancellationToken = default)
    {
        var serviceUrl = await _settings.GetAsync("yolo_service_url", DefaultServiceUrl);

        // Health check with retry
        var healthy = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (await IsServiceHealthy(serviceUrl)) { healthy = true; break; }
            if (attempt < 2)
            {
                _logger.LogWarning("[YOLO] Batch health check attempt {Attempt}/3 failed — retrying in 2s", attempt + 1);
                await Task.Delay(2000, cancellationToken);
            }
        }
        if (!healthy)
            throw new InvalidOperationException($"YOLO service at {serviceUrl} is not reachable after 3 attempts.");

        var frameSkip = await _settings.GetIntAsync("yolo_frame_skip", 1);
        if (frameSkip < 1) frameSkip = 1;
        if (frameSkip > 30) frameSkip = 30;

        try
        {
            var payload = new
            {
                content_id = contentId,
                video_path = videoPath,
                scenes = scenes.Select(s => new
                {
                    scene_index = s.SceneIndex,
                    start_frame = s.StartFrame,
                    end_frame = s.EndFrame,
                }).ToList(),
                model_size = await _settings.GetAsync("yolo_model_size", "large"),
                confidence_threshold = await _settings.GetDoubleAsync("yolo_confidence", 0.35),
                iou_threshold = await _settings.GetDoubleAsync("yolo_iou", 0.45),
                tracked = true,
                frame_skip = frameSkip,
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation(
                "[YOLO] Sending BATCH detection: content={ContentId} scenes={Count} frameSkip={Skip}",
                contentId, scenes.Count, frameSkip);

            var response = await _http.PostAsync($"{serviceUrl.TrimEnd('/')}/detect-batch", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var batchResults = JsonSerializer.Deserialize<List<YoloDetectionResponse>>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            var results = new List<SceneDetectionBatchResult>();
            if (batchResults != null)
            {
                foreach (var r in batchResults)
                {
                    var sceneSurfaces = new List<SurfaceDetectionResult>();
                    if (r.Surfaces != null)
                    {
                        foreach (var s in r.Surfaces)
                        {
                            sceneSurfaces.Add(new SurfaceDetectionResult
                            {
                                SurfaceType = s.SurfaceType ?? "Detected Surface",
                                BoundaryCoordinatesJson = JsonSerializer.Serialize(s.BoundaryCoordinates ?? new List<Coord>()),
                                EstimatedDepth = s.EstimatedDepth,
                                OrientationVectorJson = JsonSerializer.Serialize(s.OrientationVector ?? new Orientation()),
                                ConfidenceScore = s.ConfidenceScore,
                                ViabilityScore = s.ViabilityScore,
                                ExclusionReason = s.ExclusionReason,
                            });
                        }
                    }
                    results.Add(new SceneDetectionBatchResult
                    {
                        SceneIndex = r.SceneIndex,
                        Surfaces = sceneSurfaces,
                        Succeeded = true,
                    });

                    _logger.LogInformation(
                        "[YOLO] Batch scene {Scene}: {Count} surfaces in {Frames} frames ({TimeMs}ms)",
                        r.SceneIndex, sceneSurfaces.Count, r.FramesProcessed, r.ProcessingTimeMs);
                }
            }

            _logger.LogInformation(
                "[YOLO] Batch complete: {Count} scenes processed for {ContentId}",
                results.Count, contentId);

            return results;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"[YOLO] HTTP error in batch detection at {serviceUrl}: {ex.Message}", ex);
        }
        catch (TaskCanceledException)
        {
            throw new TimeoutException(
                $"[YOLO] Batch detection at {serviceUrl} timed out after {_http.Timeout.TotalSeconds}s.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"[YOLO] Failed to parse batch detection response: {ex.Message}", ex);
        }
    }
}

// ── JSON deserialization models for the YOLO service response ──

public class YoloDetectionResponse
{
    [JsonPropertyName("content_id")]
    public string? ContentId { get; set; }

    [JsonPropertyName("scene_index")]
    public int SceneIndex { get; set; }

    [JsonPropertyName("surfaces")]
    public List<YoloSurfaceResult>? Surfaces { get; set; }

    [JsonPropertyName("frames_processed")]
    public int FramesProcessed { get; set; }

    [JsonPropertyName("model_used")]
    public string? ModelUsed { get; set; }

    [JsonPropertyName("processing_time_ms")]
    public double ProcessingTimeMs { get; set; }
}

public class YoloSurfaceResult
{
    [JsonPropertyName("surface_type")]
    public string? SurfaceType { get; set; }

    [JsonPropertyName("boundary_coordinates")]
    public List<Coord>? BoundaryCoordinates { get; set; }

    [JsonPropertyName("estimated_depth")]
    public double EstimatedDepth { get; set; }

    [JsonPropertyName("orientation_vector")]
    public Orientation? OrientationVector { get; set; }

    [JsonPropertyName("confidence_score")]
    public double ConfidenceScore { get; set; }

    [JsonPropertyName("viability_score")]
    public double ViabilityScore { get; set; }

    [JsonPropertyName("exclusion_reason")]
    public string? ExclusionReason { get; set; }

    [JsonPropertyName("track_id")]
    public int? TrackId { get; set; }
}

public class Orientation
{
    [JsonPropertyName("yaw")]
    public double Yaw { get; set; }

    [JsonPropertyName("pitch")]
    public double Pitch { get; set; }

    [JsonPropertyName("roll")]
    public double Roll { get; set; }
}
