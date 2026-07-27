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
/// Calls Replicate API for cloud-GPU surface detection using SAM 3.
///
/// Single API call replaces the old 2-call Grounding DINO → SAM 2 pipeline.
/// SAM 3 accepts a free-text concept prompt and returns surface detections with
/// polygon masks, surface type labels, and confidence scores all in one response.
///
/// Activated when engine_detection = "replicate".
/// Requires platform setting: replicate_api_key
///
/// No local GPU, drivers, CUDA, or Python version issues — everything runs on Replicate's cloud.
/// </summary>
public class ReplicateSurfaceDetectionService : ISurfaceDetectionService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<ReplicateSurfaceDetectionService> _logger;
    private readonly PostgresDbContext _db;
    private readonly HttpClient _http;

    private const string ReplicateBaseUrl = "https://api.replicate.com/v1";

    // ⚠️ Community model — not an official Meta-maintained model.
    // Model owner can update, retag, or deprecate at any time without notice.
    // Pin to an explicit version hash: owner/model:hash. Check periodically.
    // Current hash placeholder — replace with the real hash before production deploy.
    private const string Sam3ImageModel = "lucataco/sam3-image";

    // Text prompt for open-vocabulary surface detection — describes characteristics,
    // not a fixed list. SAM 3 uses this to constrain segmentation to ad-placeable regions.
    private const string SurfacePrompt =
        "a flat rectangular surface . a smooth empty area . " +
        "a visible wall or panel . a screen or display . " +
        "a large flat side of an object . a planar region . " +
        "a surface suitable for placing an image . " +
        "a blank area on a vehicle or building . " +
        "a sign or board . a poster or banner . " +
        "a tabletop or counter surface . a floor or ground plane . " +
        "a fabric panel or curtain . a door or window surface";

    public ReplicateSurfaceDetectionService(
        IPlatformSettingsService settings,
        ILogger<ReplicateSurfaceDetectionService> logger,
        PostgresDbContext db)
    {
        _settings = settings;
        _logger = logger;
        _db = db;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<List<SurfaceDetectionResult>> DetectAsync(
        string contentId, int sceneIndex, int startFrame, int endFrame,
        CancellationToken cancellationToken = default)
    {
        var apiKey = await _settings.GetAsync("replicate_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "Replicate API key is not configured. " +
                "Set Platform Setting 'replicate_api_key' in Admin Console → AI Engine → API Keys, " +
                "or switch engine_detection to 'yolo', 'grounding-dino', 'gemini', or 'google'.");
        }

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        // ── Extract key frame as base64 ──
        var videoPath = await ResolveVideoPath(contentId);
        if (string.IsNullOrEmpty(videoPath))
        {
            _logger.LogWarning("[Replicate] Cannot resolve video path for {ContentId}", contentId);
            return new List<SurfaceDetectionResult>();
        }

        var middleFrame = (startFrame + endFrame) / 2;
        var frameBase64 = await ExtractKeyFrameAsync(videoPath, middleFrame, cancellationToken);
        if (frameBase64 == null)
        {
            _logger.LogWarning("[Replicate] Failed to extract frame {Frame}", middleFrame);
            return new List<SurfaceDetectionResult>();
        }

        // ── Single call: SAM 3 (detection + segmentation in one) ──
        _logger.LogInformation(
            "[Replicate] Running SAM 3 for {ContentId} scene {Scene}",
            contentId, sceneIndex);

        var sam3Results = await RunSam3Async(frameBase64, cancellationToken);

        if (sam3Results.Count == 0)
        {
            _logger.LogInformation("[Replicate] No surfaces detected by SAM 3");
            return new List<SurfaceDetectionResult>();
        }

        // ── Assemble results ──
        var surfaces = new List<SurfaceDetectionResult>();
        foreach (var result in sam3Results)
        {
            surfaces.Add(new SurfaceDetectionResult
            {
                SurfaceType = result.Label ?? "Detected Surface",
                BoundaryCoordinatesJson = JsonSerializer.Serialize(result.Mask),
                EstimatedDepth = 5.0,
                OrientationVectorJson = "{\"yaw\":0,\"pitch\":0,\"roll\":0}",
                ConfidenceScore = result.Score,
                ViabilityScore = Math.Round(Math.Clamp(result.Score * 0.9, 0.0, 1.0), 2),
            });
        }

        _logger.LogInformation(
            "[Replicate] Complete: {Count} surfaces for {ContentId} scene {Scene}",
            surfaces.Count, contentId, sceneIndex);

        return surfaces;
    }

    // ── SAM 3 API call (replaces Grounding DINO + SAM 2) ──

    private async Task<List<Sam3DetectionResult>> RunSam3Async(string imageBase64, CancellationToken ct)
    {
        try
        {
            var modelSlug = await _settings.GetAsync("replicate_sam3_model", Sam3ImageModel);

            // If a version hash is configured, append it to pin the model.
            // Without a hash, Replicate uses the latest version — which can change silently.
            var versionHash = await _settings.GetAsync("replicate_sam3_version", "");
            if (!string.IsNullOrEmpty(versionHash) && !modelSlug.Contains(':'))
            {
                modelSlug = $"{modelSlug}:{versionHash}";
            }

            var payload = new
            {
                // version is deprecated by Replicate when model hash is in the slug
                input = new
                {
                    image = $"data:image/jpeg;base64,{imageBase64}",
                    prompt = SurfacePrompt,
                    box_threshold = await _settings.GetDoubleAsync("replicate_box_threshold", 0.25),
                    text_threshold = await _settings.GetDoubleAsync("replicate_text_threshold", 0.20),
                },
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(
                $"{ReplicateBaseUrl}/models/{modelSlug}/predictions", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("[Replicate] SAM 3 HTTP {Status}: {Body}", (int)response.StatusCode, errBody);
                return new List<Sam3DetectionResult>();
            }

            var prediction = await PollForCompletion(response, ct);
            return ParseSam3Output(prediction);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Replicate] SAM 3 failed");
            return new List<Sam3DetectionResult>();
        }
    }

    /// <summary>
    /// Polls a Replicate prediction until it completes or fails.
    /// Replicate predictions are async — we submit, then poll.
    /// </summary>
    private async Task<JsonElement> PollForCompletion(HttpResponseMessage initialResponse, CancellationToken ct)
    {
        var responseJson = await initialResponse.Content.ReadAsStringAsync();
        var prediction = JsonSerializer.Deserialize<JsonElement>(responseJson);

        var predictionId = prediction.GetProperty("id").GetString();
        var pollUrl = $"{ReplicateBaseUrl}/predictions/{predictionId}";

        // Poll up to 60 times (2 minutes at 2s intervals)
        for (int i = 0; i < 60; i++)
        {
            ct.ThrowIfCancellationRequested();

            var pollResponse = await _http.GetAsync(pollUrl, ct);
            var pollJson = await pollResponse.Content.ReadAsStringAsync();
            var pollData = JsonSerializer.Deserialize<JsonElement>(pollJson);

            var status = pollData.GetProperty("status").GetString();

            if (status == "succeeded")
                return pollData;

            if (status == "failed" || status == "canceled")
            {
                var error = "unknown";
                if (pollData.TryGetProperty("error", out var err))
                    error = err.GetString() ?? "unknown";
                throw new InvalidOperationException($"Replicate prediction failed: {error}");
            }

            // Still processing — wait and retry
            await Task.Delay(2000, ct);
        }

        throw new TimeoutException("Replicate prediction timed out after 2 minutes.");
    }

    // ── SAM 3 Output Parsing ──

    private static List<Sam3DetectionResult> ParseSam3Output(JsonElement prediction)
    {
        var results = new List<Sam3DetectionResult>();

        try
        {
            // SAM 3 output: { "output": [{ "label": "...", "mask": [[x,y],...], "score": 0.95 }, ...] }
            // (format varies by model version — handle common patterns)

            JsonElement output;
            if (!prediction.TryGetProperty("output", out output))
                return results;

            // Handle both array and string-wrapped-JSON outputs
            var outputArray = output;
            if (output.ValueKind == JsonValueKind.String)
            {
                outputArray = JsonSerializer.Deserialize<JsonElement>(output.GetString()!);
            }

            if (outputArray.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in outputArray.EnumerateArray())
            {
                var result = ParseSam3Item(item);
                if (result != null) results.Add(result);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Replicate] Failed to parse SAM 3 output: {ex.Message}");
        }

        return results;
    }

    private static Sam3DetectionResult? ParseSam3Item(JsonElement item)
    {
        try
        {
            double score = 0.5;
            string? label = null;
            var mask = new List<Coord>();

            if (item.TryGetProperty("score", out var s))
                score = s.GetDouble();
            else if (item.TryGetProperty("confidence", out var c))
                score = c.GetDouble();

            if (item.TryGetProperty("label", out var l))
                label = l.GetString();
            else if (item.TryGetProperty("text", out var t))
                label = t.GetString();
            else if (item.TryGetProperty("surface_type", out var st))
                label = st.GetString();

            // Parse mask/polygon
            JsonElement maskElement = item;
            if (item.TryGetProperty("mask", out var m)) maskElement = m;
            else if (item.TryGetProperty("polygon", out var p)) maskElement = p;
            else if (item.TryGetProperty("boundary", out var b)) maskElement = b;

            if (maskElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var point in maskElement.EnumerateArray())
                {
                    if (point.ValueKind == JsonValueKind.Array)
                    {
                        var pts = new List<double>();
                        foreach (var coord in point.EnumerateArray())
                            pts.Add(coord.GetDouble());
                        if (pts.Count >= 2)
                            mask.Add(new Coord { X = (int)pts[0], Y = (int)pts[1] });
                    }
                    else if (point.ValueKind == JsonValueKind.Object)
                    {
                        int x = 0, y = 0;
                        if (point.TryGetProperty("x", out var xElem)) x = xElem.GetInt32();
                        if (point.TryGetProperty("y", out var yElem)) y = yElem.GetInt32();
                        mask.Add(new Coord { X = x, Y = y });
                    }
                }
            }

            // Minimum viable quadrilateral check
            if (mask.Count < 4)
            {
                // Try extracting a bounding box as fallback
                if (item.TryGetProperty("box", out var boxArr) && boxArr.ValueKind == JsonValueKind.Array)
                {
                    var coords = new List<double>();
                    foreach (var c in boxArr.EnumerateArray()) coords.Add(c.GetDouble());
                    if (coords.Count >= 4)
                    {
                        mask = new List<Coord>
                        {
                            new() { X = (int)coords[0], Y = (int)coords[1] },
                            new() { X = (int)coords[2], Y = (int)coords[1] },
                            new() { X = (int)coords[2], Y = (int)coords[3] },
                            new() { X = (int)coords[0], Y = (int)coords[3] },
                        };
                    }
                }
            }

            return new Sam3DetectionResult { Label = label, Mask = mask, Score = score };
        }
        catch
        {
            return null;
        }
    }

    // ── Data Models ──

    private class Sam3DetectionResult
    {
        public string? Label { get; set; }
        public List<Coord> Mask { get; set; } = new();
        public double Score { get; set; }
    }

    // ── Helpers ──

    private async Task<string?> ResolveVideoPath(string contentId)
    {
        try
        {
            var content = await _db.ContentItems.FindAsync(contentId);
            if (content == null || string.IsNullOrEmpty(content.StorageKey)) return null;
            var fileName = content.StorageKey.Replace("/api/content/file/", "");
            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
            var filePath = Path.Combine(uploadsDir, fileName);
            if (File.Exists(filePath)) return filePath;
            var proxyPath = Path.Combine(uploadsDir, "proxies", fileName);
            return File.Exists(proxyPath) ? proxyPath : null;
        }
        catch { return null; }
    }

    private async Task<string?> ExtractKeyFrameAsync(string videoPath, int frameNumber, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"bit-rep-{Guid.NewGuid():N}.jpg");
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -i \"{videoPath}\" -vf \"select=eq(n\\,{frameNumber})\" -vframes 1 -q:v 2 \"{tempFile}\"",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                },
            };
            process.Start();
            await process.WaitForExitAsync(ct);

            if (!File.Exists(tempFile) || new FileInfo(tempFile).Length < 100) return null;
            var bytes = await File.ReadAllBytesAsync(tempFile, ct);
            return Convert.ToBase64String(bytes);
        }
        catch { return null; }
        finally { try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { } }
    }
}
