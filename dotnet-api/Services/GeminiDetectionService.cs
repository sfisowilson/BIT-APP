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
/// Calls Google Gemini 3 Flash for multimodal surface detection + brand safety.
///
/// One API call replaces four separate models:
///   - Zero-shot surface detection (like Grounding DINO)
///   - Boundary polygon estimation (like SAM)
///   - Brand-safety classification (like CLIP)
///   - Surface type classification
///
/// Activated when engine_detection = "gemini".
/// Requires platform setting: gemini_api_key
///
/// Extremely cheap (~$0.0001/image) and no local GPU needed.
/// </summary>
public class GeminiDetectionService : ISurfaceDetectionService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<GeminiDetectionService> _logger;
    private readonly PostgresDbContext _db;
    private readonly HttpClient _http;

    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string DefaultModel = "gemini-3.6-flash";

    // Structured prompt that tells Gemini to find EVERY possible surface — no category limits.
    // Core principle: ANY flat or semi-flat visible surface is an advertising candidate.
    private const string DetectionPromptTemplate = @"
You are an advertising surface detection system. Your job is to find EVERY surface in this
video frame that could potentially hold an advertisement, brand logo, or visual insert.

CRITICAL RULE — NO CATEGORY LIMITS:
Any visible surface is a candidate. Do NOT limit yourself to billboards and screens.
Consider ALL of these and more:
- Walls, ceilings, floors, pavements, roads
- Vehicle exteriors (cars, buses, trucks, trains, boats, planes)
- Windows, doors, glass panels
- Tables, desks, counters, shelves
- Clothing, fabric, curtains, flags, banners
- Packaging, boxes, containers, crates
- Natural surfaces: calm water, flat ground, sand, grass
- Electronic displays of any kind (TVs, phones, tablets, LED boards)
- Signs, posters, boards of any size
- Furniture surfaces (any flat side)
- Any flat or semi-flat region on any object

Return ONLY valid JSON in this exact format — no markdown, no code fences, no explanation:
{
  ""surfaces"": [
    {
      ""type"": ""string describing what the surface is"",
      ""boundary"": [[x1,y1],[x2,y2],[x3,y3],[x4,y4]],
      ""confidence"": 0.0_to_1.0,
      ""viability"": 0.0_to_1.0,
      ""unsafe"": false,
      ""unsafe_reason"": ""if unsafe, brief reason, otherwise null"",
      ""sam3_prompt"": ""A detailed visual description of this specific surface for SAM3 segmentation. Include shape, material, color, texture, lighting, edges, and any distinctive visual features that would help a segmentation model precisely isolate this surface across video frames. Focus on visual appearance only — no viability or brand-safety commentary. Example: 'white textured plaster wall, rectangular shape, smooth flat surface, warm indoor lighting, distinct dark edges against background'""
    }
  ]
}

RULES:
- boundary: 4 corner points forming a quadrilateral in pixel coordinates
  (use the image dimensions — the image is {0}x{1} pixels. All boundary coordinates MUST be within 0-{0} for x and 0-{1} for y)
- Viability score (0-1): how good an ad placement this surface would be.
  Consider: size (5-40% of frame = optimal), visibility, contrast with background,
  surface texture (smooth > rough), lighting, angle (front-facing > angled > edge-on),
  and whether the surface is stationary or likely to stay in frame.
- Confidence (0-1): how certain you are this is actually a surface.
- type: a short descriptive label (e.g. ""brick wall"", ""white van side"",
  ""wooden tabletop"", ""glass window"", ""concrete floor"", ""LED scoreboard"",
  ""green curtain"", ""cardboard box"").

BRAND SAFETY — PERMANENTLY EXCLUDE these (set unsafe=true):
- Human faces, heads, or bodies (people are NEVER ad surfaces)
- Children or minors
- Emergency vehicles (ambulance, fire truck, police car)
- Military vehicles, weapons, or soldiers
- Religious symbols, places of worship, or sacred objects
- Government buildings, official insignia, flags of state
- Alcohol branding, tobacco products, or drug paraphernalia
- Gore, blood, violence, or explicit content
- Any surface that would cause public offense or brand damage

OBJECT SEPARATION — CRITICAL:
If there are multiple similar adjacent objects (e.g. 5 photo frames on a wall, 3 windows
side by side, a grid of tiles), detect EACH ONE as a SEPARATE surface with its own unique
boundary coordinates. Do NOT merge adjacent objects into a single large surface. Each
individual item gets its own entry in the surfaces array with its own tight boundary.
This is essential for precise ad placement targeting.

Return at most 20 surfaces, sorted by viability descending.
If no surfaces found, return { ""surfaces"": [] }.
";

    public GeminiDetectionService(
        IPlatformSettingsService settings,
        ILogger<GeminiDetectionService> logger,
        PostgresDbContext db)
    {
        _settings = settings;
        _logger = logger;
        _db = db;
        // Timeout configurable via gemini_timeout_seconds (default 90s — generous for rate-limit backoff, large frames, and network jitter)
        var timeoutSec = settings.GetAsync("gemini_timeout_seconds", "90").GetAwaiter().GetResult();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(int.TryParse(timeoutSec, out var t) ? t : 90) };
    }

    public async Task<List<SurfaceDetectionResult>> DetectAsync(
        string contentId, int sceneIndex, int startFrame, int endFrame,
        CancellationToken cancellationToken = default)
    {
        var apiKey = await _settings.GetAsync("gemini_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key is not configured. " +
                "Set Platform Setting 'gemini_api_key' in Admin Console → AI Engine → API Keys, " +
                "or switch engine_detection to 'yolo', 'grounding-dino', 'replicate', or 'google'.");
        }

        // ── Extract key frame as base64 ──
        var videoPath = await ResolveVideoPath(contentId);
        if (string.IsNullOrEmpty(videoPath))
        {
            _logger.LogWarning("[Gemini] Cannot resolve video path for {ContentId}", contentId);
            return new List<SurfaceDetectionResult>();
        }

        var middleFrame = (startFrame + endFrame) / 2;
        var frameBase64 = await ExtractKeyFrameAsync(videoPath, middleFrame, cancellationToken);
        if (frameBase64 == null)
        {
            _logger.LogWarning("[Gemini] Failed to extract frame {Frame}", middleFrame);
            return new List<SurfaceDetectionResult>();
        }

        // Get actual video dimensions via ffprobe — always reads the real file
        var (videoW, videoH) = await GetVideoDimensionsAsync(videoPath);
        if (videoW <= 0) videoW = 1920;
        if (videoH <= 0) videoH = 1080;

        // ── Call Gemini 2.0 Flash ──
        try
        {
            var model = await _settings.GetAsync("gemini_model", DefaultModel);
            var url = $"{GeminiBaseUrl}/models/{model}:generateContent?key={apiKey}";

            var prompt = BuildPrompt(videoW, videoH);

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new { inline_data = new { mime_type = "image/jpeg", data = frameBase64 } },
                        },
                    },
                },
                generation_config = new
                {
                    temperature = 0.1,        // low temp for consistent structured output
                    top_p = 0.95,
                    max_output_tokens = 4096,
                    response_mime_type = "application/json",  // Gemini 3 native JSON mode
                },
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation(
                "[Gemini] Calling {Model} for {ContentId} scene {Scene} frame {Frame}",
                model, contentId, sceneIndex, middleFrame);

            var response = await _http.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GeminiResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            // ── Parse Gemini's JSON output ──
            var text = result?.Candidates?[0]?.Content?.Parts?[0]?.Text;
            if (string.IsNullOrEmpty(text))
            {
                // Try top-level parse — Gemini may return JSON directly when response_mime_type is set
                _logger.LogWarning("[Gemini] No candidates[0].content.parts[0].text — trying top-level parse. Response: {Response}",
                    responseJson.Length <= 500 ? responseJson : responseJson[..500] + "...");
                text = responseJson;
            }

            // Strip markdown code fences if present (fallback for non-JSON-mode responses)
            text = text.Trim();
            if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) text = text[7..];
            else if (text.StartsWith("```", StringComparison.Ordinal)) text = text[3..];
            if (text.EndsWith("```", StringComparison.Ordinal)) text = text[..^3];
            text = text.Trim();

            var detectionResult = JsonSerializer.Deserialize<GeminiDetectionOutput>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (detectionResult?.Surfaces == null || detectionResult.Surfaces.Count == 0)
            {
                _logger.LogInformation("[Gemini] No surfaces detected for {ContentId}", contentId);
                return new List<SurfaceDetectionResult>();
            }

            // ── Map to BIT format — coords already in video pixel space ──
            var surfaces = new List<SurfaceDetectionResult>();
            foreach (var s in detectionResult.Surfaces)
            {
                var boundary = s.Boundary ?? new List<List<double>>();
                var boundaryCoords = new List<Coord>();
                foreach (var pt in boundary)
                {
                    if (pt.Count >= 2)
                        boundaryCoords.Add(new Coord { X = (int)pt[0], Y = (int)pt[1] });
                }

                surfaces.Add(new SurfaceDetectionResult
                {
                    SurfaceType = s.Type ?? "Detected Surface",
                    BoundaryCoordinatesJson = JsonSerializer.Serialize(boundaryCoords),
                    EstimatedDepth = 5.0,
                    OrientationVectorJson = "{\"yaw\":0,\"pitch\":0,\"roll\":0}",
                    ConfidenceScore = s.Confidence,
                    ViabilityScore = s.Unsafe ? Math.Min(s.Viability, 0.15) : s.Viability,
                    ExclusionReason = s.Unsafe ? s.UnsafeReason : null,
                    Sam3Prompt = s.Sam3Prompt,
                });
            }

            _logger.LogInformation(
                "[Gemini] Found {Count} surfaces for {ContentId} ({Unsafe} excluded for safety)",
                surfaces.Count, contentId,
                surfaces.Count(s => s.ExclusionReason != null));

            return surfaces;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Gemini] HTTP error for {ContentId}", contentId);
            return new List<SurfaceDetectionResult>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[Gemini] Failed to parse response for {ContentId}", contentId);
            return new List<SurfaceDetectionResult>();
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[Gemini] Timed out for {ContentId}", contentId);
            return new List<SurfaceDetectionResult>();
        }
    }

    // ── Base64 direct detection (used by SurfaceDetectionPipeline) ──

    /// <summary>
    /// Detect surfaces directly from a base64-encoded frame.
    /// Called by SurfaceDetectionPipeline which handles frame extraction.
    /// </summary>
    public async Task<List<SurfaceDetectionResult>> DetectFromBase64Async(
        string contentId,
        int sceneIndex,
        string frameBase64,
        int frameNumber,
        int endFrame,
        int scaledWidth,
        int scaledHeight,
        int origWidth,
        int origHeight,
        CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync("gemini_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key is not configured. " +
                "Set Platform Setting 'gemini_api_key' in Admin Console → AI Engine → API Keys, " +
                "or switch engine_detection to 'yolo', 'grounding-dino', 'replicate', or 'google'.");
        }

        return await CallGeminiApi(contentId, sceneIndex, frameBase64, frameNumber,
            scaledWidth, scaledHeight, origWidth, origHeight, apiKey, ct);
    }

    // ── Core Gemini API call ──

    private async Task<List<SurfaceDetectionResult>> CallGeminiApi(
        string contentId,
        int sceneIndex,
        string frameBase64,
        int frameNumber,
        int scaledWidth,
        int scaledHeight,
        int origWidth,
        int origHeight,
        string apiKey,
        CancellationToken ct)
    {
        try
        {
            var model = await _settings.GetAsync("gemini_model", DefaultModel);
            var url = $"{GeminiBaseUrl}/models/{model}:generateContent?key={apiKey}";

            var prompt = BuildPrompt(scaledWidth, scaledHeight);

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new { inline_data = new { mime_type = "image/jpeg", data = frameBase64 } },
                        },
                    },
                },
                generation_config = new
                {
                    temperature = 0.1,
                    top_p = 0.95,
                    max_output_tokens = 4096,
                    response_mime_type = "application/json",  // Gemini 3 native JSON mode
                },
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("[Gemini] Calling {Model} frame {Frame} scene {Scene}",
                model, frameNumber, sceneIndex);

            // Retry with exponential backoff for 429 rate limiting
            HttpResponseMessage? response = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                response = await _http.PostAsync(url, httpContent, ct);
                if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
                    break;

                // Capture rate-limit headers for diagnostics
                var retryAfter = response.Headers.Contains("Retry-After")
                    ? response.Headers.GetValues("Retry-After").FirstOrDefault() : "?";
                var rlRemaining = response.Headers.Contains("x-ratelimit-remaining")
                    ? response.Headers.GetValues("x-ratelimit-remaining").FirstOrDefault() : "?";
                var rlLimit = response.Headers.Contains("x-ratelimit-limit")
                    ? response.Headers.GetValues("x-ratelimit-limit").FirstOrDefault() : "?";

                // Longer backoff: 3s, 6s, 12s, 24s, 48s — free tier quota resets every 60s
                var delaySeconds = 3 * Math.Pow(2, attempt);
                _logger.LogWarning("[Gemini] Rate limited (429). Retry-After={RetryAfter}s, Remaining={Remaining}/{Limit}. Backoff {Delay}s (attempt {Attempt}/5)...",
                    retryAfter, rlRemaining, rlLimit, delaySeconds, attempt + 1);

                // Store quota info for admin panel
                await StoreQuotaInfo(rlLimit, rlRemaining, retryAfter);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }

            response!.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<GeminiResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            var text = result?.Candidates?[0]?.Content?.Parts?[0]?.Text;
            if (string.IsNullOrEmpty(text)) return new List<SurfaceDetectionResult>();

            // Strip markdown code fences if present (fallback for non-JSON-mode responses)
            text = text.Trim();
            if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) text = text[7..];
            else if (text.StartsWith("```", StringComparison.Ordinal)) text = text[3..];
            if (text.EndsWith("```", StringComparison.Ordinal)) text = text[..^3];
            text = text.Trim();

            var detectionResult = JsonSerializer.Deserialize<GeminiDetectionOutput>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (detectionResult?.Surfaces == null || detectionResult.Surfaces.Count == 0)
                return new List<SurfaceDetectionResult>();

            // Scale coordinates from scaled frame space → original video space
            var scaleX = origWidth > 0 && scaledWidth > 0 ? (double)origWidth / scaledWidth : 1.0;
            var scaleY = origHeight > 0 && scaledHeight > 0 ? (double)origHeight / scaledHeight : 1.0;

            var surfaces = new List<SurfaceDetectionResult>();
            foreach (var s in detectionResult.Surfaces)
            {
                var boundary = s.Boundary ?? new List<List<double>>();
                var boundaryCoords = new List<Coord>();
                foreach (var pt in boundary)
                {
                    if (pt.Count >= 2)
                        boundaryCoords.Add(new Coord { X = (int)(pt[0] * scaleX), Y = (int)(pt[1] * scaleY) });
                }

                surfaces.Add(new SurfaceDetectionResult
                {
                    SurfaceType = s.Type ?? "Candidate Surface",
                    BoundaryCoordinatesJson = JsonSerializer.Serialize(boundaryCoords),
                    EstimatedDepth = 5.0,
                    OrientationVectorJson = "{\"yaw\":0,\"pitch\":0,\"roll\":0}",
                    ConfidenceScore = s.Confidence,
                    ViabilityScore = s.Unsafe ? Math.Min(s.Viability, 0.15) : s.Viability,
                    ExclusionReason = s.Unsafe ? s.UnsafeReason : null,
                    Sam3Prompt = s.Sam3Prompt,
                });
            }

            _logger.LogInformation("[Gemini] {Count} surfaces, {Unsafe} excluded",
                surfaces.Count, surfaces.Count(s => s.ExclusionReason != null));

            return surfaces;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Gemini] HTTP error");
            return new List<SurfaceDetectionResult>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[Gemini] JSON parse error");
            return new List<SurfaceDetectionResult>();
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[Gemini] Timed out");
            return new List<SurfaceDetectionResult>();
        }
    }

    /// <summary>Build the detection prompt with actual image dimensions injected.</summary>
    private static string BuildPrompt(int width, int height)
        => DetectionPromptTemplate.Replace("{0}", width.ToString()).Replace("{1}", height.ToString());

    /// <summary>Get actual video dimensions via ffprobe — works for any video format.</summary>
    private static async Task<(int width, int height)> GetVideoDimensionsAsync(string videoPath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{videoPath}\"",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                },
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var parts = output.Trim().Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                return (w, h);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gemini] ffprobe dimensions failed: {ex.Message}");
        }
        return (0, 0);
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
        var tempFile = Path.Combine(Path.GetTempPath(), $"bit-gem-{Guid.NewGuid():N}.jpg");
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

            // Timeout after 30 s to avoid hanging the pipeline
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            await process.WaitForExitAsync(linkedCts.Token);

            if (!File.Exists(tempFile) || new FileInfo(tempFile).Length < 100)
            {
                if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch { } }
                return null;
            }
            var bytes = await File.ReadAllBytesAsync(tempFile, ct);
            return Convert.ToBase64String(bytes);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Gemini] Failed to extract keyframe at frame {Frame}", frameNumber);
            return null;
        }
        finally { try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { } }
    }

    // ── JSON models ──

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart>? Parts { get; set; }
    }

    private class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private class GeminiDetectionOutput
    {
        [JsonPropertyName("surfaces")]
        public List<GeminiSurface>? Surfaces { get; set; }
    }

    private class GeminiSurface
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("boundary")]
        public List<List<double>>? Boundary { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("viability")]
        public double Viability { get; set; }

        [JsonPropertyName("unsafe")]
        public bool Unsafe { get; set; }

        [JsonPropertyName("unsafe_reason")]
        public string? UnsafeReason { get; set; }

        [JsonPropertyName("sam3_prompt")]
        public string? Sam3Prompt { get; set; }
    }

    /// <summary>Store Gemini quota info in PlatformSettings for admin panel display.</summary>
    private async Task StoreQuotaInfo(string? limit, string? remaining, string? retryAfter)
    {
        try
        {
            var now = DateTime.UtcNow.ToString("o");
            var info = System.Text.Json.JsonSerializer.Serialize(new
            {
                limit = limit ?? "?",
                remaining = remaining ?? "?",
                retryAfterSeconds = retryAfter ?? "?",
                checkedAt = now,
            });
            var settings = _db.Set<Afrobotics.Bit.Api.Models.PlatformSetting>();
            var existing = await settings.FindAsync("gemini_quota_status");
            if (existing != null)
            {
                existing.Value = info;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                settings.Add(new Afrobotics.Bit.Api.Models.PlatformSetting
                {
                    Key = "gemini_quota_status",
                    Value = info,
                    Description = "Gemini API rate-limit status (auto-updated on 429)",
                    UpdatedAt = DateTime.UtcNow,
                });
            }
            await _db.SaveChangesAsync();
        }
        catch { /* non-critical */ }
    }

    /// <summary>
    /// Generate the modify_region and prompt strings for pikaswaps compositing.
    /// Called during render dispatch to prepare the pikaswaps API payload.
    /// </summary>
    /// <param name="surfaceType">The detected surface type (e.g. "Stadium Perimeter LED Board").</param>
    /// <param name="assetName">The brand asset name (e.g. "Coca-Cola Logo").</param>
    /// <returns>Tuple of (modify_region, prompt). Both null if generation fails.</returns>
    public async Task<(string? modifyRegion, string? prompt)> GeneratePikaswapsPromptAsync(
        string surfaceType, string assetName)
    {
        try
        {
            var apiKey = await _settings.GetAsync("gemini_api_key");
            if (string.IsNullOrEmpty(apiKey)) return (null, null);

            var model = await _settings.GetAsync("gemini_model", DefaultModel);
            var url = $"{GeminiBaseUrl}/models/{model}:generateContent?key={apiKey}";

            var promptText = $@"You are a video compositing assistant. Generate two short text strings for the pikaswaps AI video editing API.

The user wants to replace ""{surfaceType}"" with a ""{assetName}"" advertisement in a video.

Return ONLY valid JSON — no markdown, no code fences:
{{
  ""modify_region"": ""<10 words describing the object/region to replace in the video>"",
  ""prompt"": ""<10 words describing the desired result — how the new asset should look, with lighting and perspective>""
}}

Rules:
- modify_region: describe the EXISTING object to be replaced (e.g. ""the LED perimeter board on the soccer field"")
- prompt: describe the DESIRED result (e.g. ""replace with a Coca-Cola logo, photorealistic, matching stadium lighting"")
- Both strings should be short (under 15 words), precise, and optimized for pikaswaps' text-driven region detection
- prompt must NEVER instruct changing the brand asset's text, wording, logo, or colors — only describe lighting, shading, perspective, and placement adjustments that keep the asset's content exactly as provided";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = promptText }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 100,
                }
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) return (null, null);

            using var doc = JsonDocument.Parse(responseBody);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrWhiteSpace(text)) return (null, null);

            // Parse the JSON response from Gemini
            using var resultDoc = JsonDocument.Parse(text.Trim());
            var root = resultDoc.RootElement;
            var modifyRegion = root.TryGetProperty("modify_region", out var mr) ? mr.GetString() : null;
            var prompt = root.TryGetProperty("prompt", out var p) ? p.GetString() : null;

            _logger.LogInformation(
                "[Gemini] Generated pikaswaps prompt: modify_region='{ModifyRegion}', prompt='{Prompt}'",
                modifyRegion, prompt);

            return (modifyRegion, prompt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Gemini] Failed to generate pikaswaps prompt for {SurfaceType} + {AssetName}",
                surfaceType, assetName);
            return (null, null);
        }
    }

    /// <summary>
    /// Generate a short visual description of a surface, used as a SAM3 video-rle text
    /// prompt to re-anchor tracking in a shot after a hard cut — the pixel location from the
    /// previous shot is meaningless in the new camera angle, but a semantic description
    /// ("the LED perimeter board on the field") still identifies the same real-world surface.
    /// </summary>
    /// <param name="surfaceType">The detected surface type (e.g. "Stadium Perimeter LED Board").</param>
    /// <param name="assetName">The brand asset being placed, for context. Not required to match visually.</param>
    /// <returns>A short description, or null if generation fails (caller should fall back to surfaceType itself).</returns>
    public async Task<string?> GenerateSurfaceDescriptionAsync(string surfaceType, string assetName)
    {
        try
        {
            var apiKey = await _settings.GetAsync("gemini_api_key");
            if (string.IsNullOrEmpty(apiKey)) return null;

            var model = await _settings.GetAsync("gemini_model", DefaultModel);
            var url = $"{GeminiBaseUrl}/models/{model}:generateContent?key={apiKey}";

            var promptText = $@"You are a video segmentation assistant. A surface of type ""{surfaceType}"" " +
                $@"(currently displaying a ""{assetName}"" brand placement) needs to be re-located in a different " +
                $@"camera shot of the same video scene, where its screen position has changed.

Return ONLY a short visual description (under 12 words) of the surface itself, suitable as a text
prompt for an object-segmentation model — e.g. ""the LED perimeter board along the field edge"".
Describe the physical surface, not the brand content on it. No markdown, no quotes, just the phrase.";

            var payload = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = promptText } } }
                },
                generationConfig = new { temperature = 0.2, maxOutputTokens = 40 }
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(responseBody);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            var description = text?.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(description)) return null;

            _logger.LogInformation("[Gemini] Generated re-anchor description for {SurfaceType}: '{Description}'",
                surfaceType, description);
            return description;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Gemini] Failed to generate surface description for {SurfaceType}", surfaceType);
            return null;
        }
    }
}
