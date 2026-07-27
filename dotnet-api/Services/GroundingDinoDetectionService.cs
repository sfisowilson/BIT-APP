using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls the BIT v2 Detection Service (Python FastAPI) for open-vocabulary
/// surface detection using Grounding DINO + SAM + Depth Anything V2 + CLIP.
///
/// Activated when engine_detection = "grounding-dino".
///
/// Unlike YOLO (80 fixed COCO classes), this engine accepts text prompts
/// and detects ANY surface: empty walls, bus sides, stadium LED boards,
/// billboards, posters, window signage, building facades, etc.
/// </summary>
public class GroundingDinoDetectionService : ISurfaceDetectionService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<GroundingDinoDetectionService> _logger;
    private readonly PostgresDbContext _db;
    private readonly HttpClient _http;

    private const string DefaultServiceUrl = "http://localhost:8001";

    public GroundingDinoDetectionService(
        IPlatformSettingsService settings,
        ILogger<GroundingDinoDetectionService> logger,
        PostgresDbContext db)
    {
        _settings = settings;
        _logger = logger;
        _db = db;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) }; // v2 models are slower (CPU or first-run GPU init)
    }

    public async Task<List<SurfaceDetectionResult>> DetectAsync(
        string contentId, int sceneIndex, int startFrame, int endFrame,
        CancellationToken cancellationToken = default)
    {
        var serviceUrl = await _settings.GetAsync("yolo_service_url", DefaultServiceUrl);
        // Note: v2 reuses the same Python service URL (port 8001)

        // Health check with retry
        if (!await IsServiceHealthy(serviceUrl, cancellationToken))
        {
            throw new InvalidOperationException(
                $"v2 detection service at {serviceUrl} is not reachable. Ensure the Python detection service is running.");
        }

        var videoPath = await ResolveVideoPath(contentId);
        if (string.IsNullOrEmpty(videoPath))
        {
            throw new InvalidOperationException(
                $"Could not resolve video file path for content '{contentId}'.");
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
                gd_model_variant = await _settings.GetAsync("gd_model_variant", "base"),
                gd_box_threshold = await _settings.GetDoubleAsync("gd_box_threshold", 0.25),
                gd_text_threshold = await _settings.GetDoubleAsync("gd_text_threshold", 0.20),
                enable_sam = await _settings.GetBoolAsync("gd_enable_sam", true),
                enable_depth = await _settings.GetBoolAsync("gd_enable_depth", true),
                enable_brand_safety = await _settings.GetBoolAsync("gd_enable_brand_safety", true),
                enable_tracking = await _settings.GetBoolAsync("gd_enable_tracking", true),
                adaptive_frame_skip = await _settings.GetBoolAsync("gd_adaptive_frame_skip", true),
                detection_interval = await _settings.GetIntAsync("gd_detection_interval", 10),
                flow_motion_threshold = await _settings.GetDoubleAsync("gd_flow_motion_threshold", 2.5),
                track_min_detection_frames = await _settings.GetIntAsync("gd_track_min_frames", 3),
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation(
                "[GroundingDINO] Sending v2 detection: content={ContentId} scene={Scene} frames={Start}-{End}",
                contentId, sceneIndex, startFrame, endFrame);

            var response = await _http.PostAsync(
                $"{serviceUrl.TrimEnd('/')}/detect-v2", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<DetectionResponseV2>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (result?.Surfaces == null || result.Surfaces.Count == 0)
            {
                _logger.LogInformation(
                    "[GroundingDINO] No surfaces detected for {ContentId} scene {Scene}", contentId, sceneIndex);
                return new List<SurfaceDetectionResult>();
            }

            var surfaces = MapToResults(result.Surfaces);

            _logger.LogInformation(
                "[GroundingDINO] v2 detection complete: {Count} surfaces in {TimeMs}ms for {ContentId}",
                surfaces.Count, result.ProcessingTimeMs, contentId);

            return surfaces;
        }
        catch (HttpRequestException ex)
        {
            // v2 engine may return 501 if dependencies aren't installed
            if (ex.StatusCode == System.Net.HttpStatusCode.NotImplemented)
            {
                throw new InvalidOperationException(
                    "[GroundingDINO] v2 engine dependencies not installed on the Python service. " +
                    "Run: pip install transformers segment-anything torch pillow", ex);
            }
            throw new InvalidOperationException(
                $"[GroundingDINO] HTTP error: {ex.Message}", ex);
        }
        catch (TaskCanceledException)
        {
            throw new TimeoutException(
                $"[GroundingDINO] v2 detection timed out after {_http.Timeout.TotalSeconds}s. " +
                "v2 models are GPU-intensive — consider using a GPU or switching to 'yolo' engine.");
        }
    }

    /// <summary>
    /// v2 batch detection: processes all scenes via the /detect-batch-v2 endpoint.
    /// </summary>
    public async Task<List<SceneDetectionBatchResult>> DetectBatchAsync(
        string contentId, string videoPath, List<SceneCut> scenes,
        CancellationToken cancellationToken = default)
    {
        var serviceUrl = await _settings.GetAsync("yolo_service_url", DefaultServiceUrl);

        if (!await IsServiceHealthy(serviceUrl, cancellationToken))
        {
            // Fall back to per-scene sequential
            _logger.LogWarning("[GroundingDINO] Service not healthy — falling back to sequential per-scene calls");
            return await FallbackBatchAsync(contentId, scenes, cancellationToken);
        }

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
                gd_model_variant = await _settings.GetAsync("gd_model_variant", "base"),
                gd_box_threshold = await _settings.GetDoubleAsync("gd_box_threshold", 0.25),
                gd_text_threshold = await _settings.GetDoubleAsync("gd_text_threshold", 0.20),
                enable_sam = await _settings.GetBoolAsync("gd_enable_sam", true),
                enable_depth = await _settings.GetBoolAsync("gd_enable_depth", true),
                enable_brand_safety = await _settings.GetBoolAsync("gd_enable_brand_safety", true),
                enable_tracking = await _settings.GetBoolAsync("gd_enable_tracking", true),
                adaptive_frame_skip = await _settings.GetBoolAsync("gd_adaptive_frame_skip", true),
                detection_interval = await _settings.GetIntAsync("gd_detection_interval", 10),
                flow_motion_threshold = await _settings.GetDoubleAsync("gd_flow_motion_threshold", 2.5),
                track_min_detection_frames = await _settings.GetIntAsync("gd_track_min_frames", 3),
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation(
                "[GroundingDINO] Sending v2 BATCH detection: content={ContentId} scenes={Count}",
                contentId, scenes.Count);

            var response = await _http.PostAsync(
                $"{serviceUrl.TrimEnd('/')}/detect-batch-v2", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var batchResults = JsonSerializer.Deserialize<List<DetectionResponseV2>>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            var results = new List<SceneDetectionBatchResult>();
            if (batchResults != null)
            {
                foreach (var r in batchResults)
                {
                    results.Add(new SceneDetectionBatchResult
                    {
                        SceneIndex = r.SceneIndex,
                        Surfaces = MapToResults(r.Surfaces ?? new List<SurfaceResultV2>()),
                        Succeeded = string.IsNullOrEmpty(r.Error),
                        ErrorMessage = r.Error,
                    });
                }
            }

            return results;
        }
        catch (Exception ex) when (ex is NotSupportedException || ex.Message.Contains("batch"))
        {
            _logger.LogWarning("[GroundingDINO] Batch not supported — falling back to sequential");
            return await FallbackBatchAsync(contentId, scenes, cancellationToken);
        }
    }

    // ── Helpers ──

    /// <summary>Fallback: sequential per-scene DetectAsync calls.</summary>
    private async Task<List<SceneDetectionBatchResult>> FallbackBatchAsync(
        string contentId, List<SceneCut> scenes, CancellationToken ct)
    {
        var results = new List<SceneDetectionBatchResult>();
        foreach (var scene in scenes)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var surfaces = await DetectAsync(contentId, scene.SceneIndex, scene.StartFrame, scene.EndFrame, ct);
                results.Add(new SceneDetectionBatchResult
                {
                    SceneIndex = scene.SceneIndex,
                    Surfaces = surfaces,
                    Succeeded = true,
                });
            }
            catch (Exception ex)
            {
                results.Add(new SceneDetectionBatchResult
                {
                    SceneIndex = scene.SceneIndex,
                    Succeeded = false,
                    ErrorMessage = ex.Message,
                });
            }
        }
        return results;
    }

    private async Task<bool> IsServiceHealthy(string serviceUrl, CancellationToken ct)
    {
        try
        {
            var response = await _http.GetAsync($"{serviceUrl.TrimEnd('/')}/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> ResolveVideoPath(string contentId)
    {
        try
        {
            var content = await _db.ContentItems.FindAsync(contentId);
            if (content == null || string.IsNullOrEmpty(content.StorageKey))
                return null;

            var fileName = content.StorageKey.Replace("/api/content/file/", "");
            var uploadsDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Uploads");
            var filePath = System.IO.Path.Combine(uploadsDir, fileName);
            if (System.IO.File.Exists(filePath))
                return filePath;

            var proxyPath = System.IO.Path.Combine(uploadsDir, "proxies", fileName);
            if (System.IO.File.Exists(proxyPath))
                return proxyPath;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static List<SurfaceDetectionResult> MapToResults(List<SurfaceResultV2> v2Surfaces)
    {
        var surfaces = new List<SurfaceDetectionResult>();
        foreach (var s in v2Surfaces)
        {
            surfaces.Add(new SurfaceDetectionResult
            {
                SurfaceType = s.SurfaceType ?? "Detected Surface",
                BoundaryCoordinatesJson = JsonSerializer.Serialize(s.BoundaryCoordinates ?? new List<CoordV2>()),
                EstimatedDepth = s.EstimatedDepth,
                OrientationVectorJson = JsonSerializer.Serialize(s.OrientationVector ?? new OrientationV2()),
                ConfidenceScore = s.ConfidenceScore,
                ViabilityScore = s.ViabilityScore,
                ExclusionReason = s.ExclusionReason,
            });
        }
        return surfaces;
    }

    // ── JSON models for v2 API ──

    private class DetectionResponseV2
    {
        [JsonPropertyName("content_id")]
        public string ContentId { get; set; } = "";

        [JsonPropertyName("scene_index")]
        public int SceneIndex { get; set; }

        [JsonPropertyName("surfaces")]
        public List<SurfaceResultV2>? Surfaces { get; set; }

        [JsonPropertyName("frames_processed")]
        public int FramesProcessed { get; set; }

        [JsonPropertyName("model_used")]
        public string ModelUsed { get; set; } = "";

        [JsonPropertyName("processing_time_ms")]
        public double ProcessingTimeMs { get; set; }

        [JsonPropertyName("engine_version")]
        public string EngineVersion { get; set; } = "v2";

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private class SurfaceResultV2
    {
        [JsonPropertyName("surface_type")]
        public string? SurfaceType { get; set; }

        [JsonPropertyName("boundary_coordinates")]
        public List<CoordV2>? BoundaryCoordinates { get; set; }

        [JsonPropertyName("estimated_depth")]
        public double EstimatedDepth { get; set; }

        [JsonPropertyName("orientation_vector")]
        public OrientationV2? OrientationVector { get; set; }

        [JsonPropertyName("confidence_score")]
        public double ConfidenceScore { get; set; }

        [JsonPropertyName("viability_score")]
        public double ViabilityScore { get; set; }

        [JsonPropertyName("exclusion_reason")]
        public string? ExclusionReason { get; set; }

        [JsonPropertyName("track_id")]
        public int? TrackId { get; set; }
    }

    private class CoordV2
    {
        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }
    }

    private class OrientationV2
    {
        [JsonPropertyName("yaw")]
        public double Yaw { get; set; }

        [JsonPropertyName("pitch")]
        public double Pitch { get; set; }

        [JsonPropertyName("roll")]
        public double Roll { get; set; }
    }
}
