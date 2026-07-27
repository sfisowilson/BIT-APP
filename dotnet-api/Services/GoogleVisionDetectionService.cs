using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
/// Calls Google Cloud Vision API for object localization and surface detection.
/// Extracts a key frame from the video, sends to Vision API, maps results to surface candidates.
/// Activated when engine_detection = "google".
/// Requires platform setting: google_vision_api_key
/// </summary>
public class GoogleVisionDetectionService : ISurfaceDetectionService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<GoogleVisionDetectionService> _logger;
    private readonly PostgresDbContext _db;
    private readonly HttpClient _http;

    // Google Vision object-localization taxonomy labels that map to ad-placeable surfaces
    private static readonly HashSet<string> SurfaceLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "billboard", "signage", "sign", "poster", "banner",
        "television", "television set", "display device", "screen", "monitor",
        "computer monitor", "flat panel display", "led display",
        "electronic signage", "digital signage", "video wall",
        "advertisement", "brand", "logo",
        "wall", "building", "window", "bus", "vehicle", "truck",
        "scoreboard", "stadium", "arena",
    };

    public GoogleVisionDetectionService(
        IPlatformSettingsService settings,
        ILogger<GoogleVisionDetectionService> logger,
        PostgresDbContext db)
    {
        _settings = settings;
        _logger = logger;
        _db = db;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    }

    public async Task<List<SurfaceDetectionResult>> DetectAsync(
        string contentId, int sceneIndex, int startFrame, int endFrame,
        CancellationToken cancellationToken = default)
    {
        var apiKey = await _settings.GetAsync("google_vision_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "Google Vision API key is not configured. " +
                "Set Platform Setting 'google_vision_api_key' in Admin Console → AI Engine → API Keys, " +
                "or switch engine_detection to 'yolo', 'grounding-dino', 'gemini', or 'replicate'.");
        }

        // ── Resolve video path ──
        var videoPath = await ResolveVideoPath(contentId);
        if (string.IsNullOrEmpty(videoPath))
        {
            _logger.LogWarning("[GoogleVision] Cannot resolve video path for {ContentId} — skipping", contentId);
            return new List<SurfaceDetectionResult>();
        }

        // ── Extract a representative key frame (middle frame of the scene) ──
        var middleFrame = (startFrame + endFrame) / 2;
        var frameBase64 = await ExtractKeyFrameAsync(videoPath, middleFrame, cancellationToken);
        if (frameBase64 == null)
        {
            _logger.LogWarning("[GoogleVision] Failed to extract frame {Frame} from {Video}", middleFrame, videoPath);
            return new List<SurfaceDetectionResult>();
        }

        // ── Call Google Vision API ──
        try
        {
            var requestBody = new
            {
                requests = new[]
                {
                    new
                    {
                        image = new { content = frameBase64 },
                        features = new[]
                        {
                            new { type = "OBJECT_LOCALIZATION", maxResults = 50 },
                        },
                    },
                },
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"https://vision.googleapis.com/v1/images:annotate?key={apiKey}";
            _logger.LogInformation(
                "[GoogleVision] Calling Vision API for content={ContentId} scene={Scene} frame={Frame}",
                contentId, sceneIndex, middleFrame);

            var response = await _http.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<VisionApiResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            // ── Parse Vision API results into surface candidates ──
            var surfaces = new List<SurfaceDetectionResult>();
            if (result?.Responses == null || result.Responses.Count == 0)
            {
                _logger.LogInformation("[GoogleVision] No objects detected for {ContentId} scene {Scene}", contentId, sceneIndex);
                return surfaces;
            }

            foreach (var annotation in result.Responses[0].LocalizedObjectAnnotations ?? Array.Empty<VisionAnnotation>())
            {
                // Filter: only keep objects whose labels match surface categories
                if (!SurfaceLabels.Contains(annotation.Name ?? ""))
                    continue;

                // Skip low-confidence detections
                if (annotation.Score < 0.4)
                    continue;

                var boundaryJson = "[]";
                if (annotation.BoundingPoly?.NormalizedVertices != null)
                {
                    boundaryJson = JsonSerializer.Serialize(annotation.BoundingPoly.NormalizedVertices);
                }

                surfaces.Add(new SurfaceDetectionResult
                {
                    SurfaceType = MapLabelToSurfaceType(annotation.Name ?? "Detected Surface"),
                    BoundaryCoordinatesJson = boundaryJson,
                    EstimatedDepth = 5.0, // Vision API doesn't provide depth — use a mid-range default
                    OrientationVectorJson = "{\"yaw\":0,\"pitch\":0,\"roll\":0}",
                    ConfidenceScore = annotation.Score,
                    ViabilityScore = ComputeViability(annotation.Score, annotation.BoundingPoly),
                });
            }

            _logger.LogInformation(
                "[GoogleVision] Found {Count} candidate surfaces for {ContentId} scene {Scene}",
                surfaces.Count, contentId, sceneIndex);

            return surfaces;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[GoogleVision] HTTP error calling Vision API for {ContentId}", contentId);
            return new List<SurfaceDetectionResult>();
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[GoogleVision] Vision API call timed out for {ContentId}", contentId);
            return new List<SurfaceDetectionResult>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[GoogleVision] Failed to parse Vision API response for {ContentId}", contentId);
            return new List<SurfaceDetectionResult>();
        }
    }

    // ── Helpers ──

    /// <summary>
    /// Extracts a single frame as a base64-encoded JPEG using ffmpeg.
    /// </summary>
    private async Task<string?> ExtractKeyFrameAsync(string videoPath, int frameNumber, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"bit-gv-{Guid.NewGuid():N}.jpg");
        try
        {
            var args = $"-y -i \"{videoPath}\" -vf \"select=eq(n\\,{frameNumber})\" -vframes 1 -q:v 2 \"{tempFile}\"";

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
            await process.WaitForExitAsync(ct);

            if (!File.Exists(tempFile) || new FileInfo(tempFile).Length < 100)
            {
                _logger.LogWarning("[GoogleVision] ffmpeg frame extraction produced no output at {Path}", tempFile);
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(tempFile, ct);
            return Convert.ToBase64String(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleVision] ffmpeg frame extraction failed for {Video} frame {Frame}", videoPath, frameNumber);
            return null;
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Resolves the local filesystem path to the video file for the given content ID.
    /// </summary>
    private async Task<string?> ResolveVideoPath(string contentId)
    {
        try
        {
            var content = await _db.ContentItems.FindAsync(contentId);
            if (content == null || string.IsNullOrEmpty(content.StorageKey))
                return null;

            var fileName = content.StorageKey.Replace("/api/content/file/", "");
            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
            var filePath = Path.Combine(uploadsDir, fileName);
            if (File.Exists(filePath))
                return filePath;

            var proxyPath = Path.Combine(uploadsDir, "proxies", fileName);
            if (File.Exists(proxyPath))
                return proxyPath;

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a Google Vision label to a BIT surface type.
    /// </summary>
    private static string MapLabelToSurfaceType(string label)
    {
        var lower = label.ToLowerInvariant();
        return lower switch
        {
            "billboard" => "Billboard",
            "signage" or "sign" => "Signage",
            "poster" => "Poster / Print Ad",
            "banner" => "Wall Banner",
            "television" or "television set" => "TV Screen",
            "display device" or "screen" or "monitor" or "computer monitor"
                or "flat panel display" or "led display" => "Digital Screen",
            "electronic signage" or "digital signage" or "video wall" => "Digital Signage",
            "wall" => "Wall Surface",
            "building" => "Building Facade",
            "window" => "Window Signage",
            "bus" or "vehicle" or "truck" => "Transit Ad Space",
            "scoreboard" => "Stadium Scoreboard",
            "stadium" or "arena" => "Stadium Surface",
            "advertisement" or "brand" or "logo" => "Branded Surface",
            _ => "Detected Surface",
        };
    }

    /// <summary>
    /// Computes a viability score from the Vision API confidence and bounding polygon geometry.
    /// </summary>
    private static double ComputeViability(double confidence, VisionBoundingPoly? poly)
    {
        if (poly?.NormalizedVertices == null || poly.NormalizedVertices.Count < 4)
            return Math.Round(confidence * 0.6, 2);

        // Estimate area coverage from normalized vertices
        var minX = double.MaxValue; var maxX = double.MinValue;
        var minY = double.MaxValue; var maxY = double.MinValue;
        foreach (var v in poly.NormalizedVertices)
        {
            if (v.X.HasValue) { minX = Math.Min(minX, v.X.Value); maxX = Math.Max(maxX, v.X.Value); }
            if (v.Y.HasValue) { minY = Math.Min(minY, v.Y.Value); maxY = Math.Max(maxY, v.Y.Value); }
        }

        var width = maxX - minX;
        var height = maxY - minY;
        var areaRatio = width * height;
        var aspect = height > 0 ? width / height : 1.0;

        double sizeScore = areaRatio switch
        {
            >= 0.05 and <= 0.40 => 1.0,
            < 0.05 => areaRatio / 0.05,
            _ => Math.Max(0.0, 1.0 - (areaRatio - 0.40) / 0.30),
        };

        double aspectScore = aspect switch
        {
            >= 1.3 and <= 3.0 => 1.0,
            < 1.3 => Math.Max(0.3, aspect / 1.3),
            _ => Math.Max(0.3, 3.0 / aspect),
        };

        return Math.Round(Math.Clamp(confidence * 0.4 + sizeScore * 0.35 + aspectScore * 0.25, 0.0, 1.0), 2);
    }

    // ── JSON models for Google Vision API ──

    private class VisionApiResponse
    {
        [JsonPropertyName("responses")]
        public List<VisionAnnotateResponse>? Responses { get; set; }
    }

    private class VisionAnnotateResponse
    {
        [JsonPropertyName("localizedObjectAnnotations")]
        public VisionAnnotation[]? LocalizedObjectAnnotations { get; set; }
    }

    private class VisionAnnotation
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("boundingPoly")]
        public VisionBoundingPoly? BoundingPoly { get; set; }
    }

    private class VisionBoundingPoly
    {
        [JsonPropertyName("normalizedVertices")]
        public List<VisionVertex>? NormalizedVertices { get; set; }
    }

    private class VisionVertex
    {
        [JsonPropertyName("x")]
        public double? X { get; set; }

        [JsonPropertyName("y")]
        public double? Y { get; set; }
    }
}
