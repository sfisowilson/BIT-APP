using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls Gemini (vision) to rewrite a user's rough Kontext placement idea into a precise
/// instruction grounded in the actual scene frame and asset image — not just the text alone.
/// Read-only: never dispatches a render, just returns a suggestion for the user to accept or
/// ignore in the Kontext→Kling panel. Requires platform setting: gemini_api_key.
/// </summary>
public class GeminiKontextPromptService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<GeminiKontextPromptService> _logger;
    private readonly PostgresDbContext _db;
    private readonly IEventLogService _eventLog;
    private readonly HttpClient _http;

    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string DefaultModel = "gemini-3.6-flash";

    // FLUX Kontext's most common failure mode (seen repeatedly in practice) is treating "place
    // the product" too literally — inserting the asset photo as a physical 3D object in the
    // scene instead of compositing it as flat artwork onto an existing surface. This instructs
    // Gemini to actively steer away from that, and to ground the instruction in what it actually
    // sees (naming the specific sign/surface) rather than a generic description.
    private const string InstructionTemplate = @"
You are helping refine a user's rough idea into a precise instruction for FLUX.1 Kontext, an
AI image-editing model. You are given the video frame to be edited, the brand asset image to be
placed into it, and the user's rough placement idea.

FLUX Kontext's most common failure: when told to ""place"" or ""put up"" a product/ad, it often
inserts the asset as a literal 3D object sitting in the scene, instead of compositing it as flat
2D artwork onto an existing surface (a sign, screen, wall, billboard, etc). Your rewritten
instruction must avoid triggering that failure.

USER'S ROUGH IDEA: {0}

Write ONE improved instruction that:
1. Names the SPECIFIC surface/sign/object visible in the frame to use (by its actual appearance —
   color, position, or label — not a generic description), if the user's idea implies using an
   existing surface.
2. Explicitly says the asset should appear as flat 2D artwork/graphic ON that surface — not as a
   physical object placed in the scene — unless the user's idea clearly calls for a real 3D
   object (e.g. a genuine product-placement shot).
3. Mentions matching the scene's lighting and perspective.
4. Stays concise — one to two sentences, no preamble.

Return ONLY valid JSON, no markdown, no code fences:
{{
  ""suggestedPrompt"": ""the rewritten instruction""
}}
";

    public GeminiKontextPromptService(
        IPlatformSettingsService settings,
        ILogger<GeminiKontextPromptService> logger,
        PostgresDbContext db,
        IEventLogService eventLog)
    {
        _settings = settings;
        _logger = logger;
        _db = db;
        _eventLog = eventLog;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<SuggestKontextPromptResponseDto> SuggestPromptAsync(SuggestKontextPromptDto dto, CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync("gemini_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key is not configured. Set Platform Setting 'gemini_api_key' in Admin Console → AI Engine → API Keys.");
        }

        var content = await _db.ContentItems.FindAsync(new object[] { dto.ContentId }, ct);
        if (content == null || string.IsNullOrEmpty(content.StorageKey))
            throw new ArgumentException("Content not found or has no video file.");

        var asset = await _db.CreativeAssets.FindAsync(new object[] { dto.AssetId }, ct);
        if (asset == null)
            throw new ArgumentException("Asset not found.");

        string? surfaceDescription = null;
        if (!string.IsNullOrEmpty(dto.SurfaceId))
        {
            var surface = await _db.SurfaceItems.FindAsync(new object[] { dto.SurfaceId }, ct);
            surfaceDescription = surface?.SurfaceType;
        }

        var videoPath = ResolveVideoPath(content.StorageKey);
        if (videoPath == null)
            throw new InvalidOperationException("Source video file not found.");

        var fps = content.FrameRate > 0 && content.FrameRate <= 240 ? content.FrameRate : 30;
        var frameBase64 = await ExtractFrameAsync(videoPath, dto.FrameNumber, fps, ct);
        if (frameBase64 == null)
            throw new InvalidOperationException("Failed to extract the chosen frame from the video.");

        var assetPath = ResolveAssetPath(asset.StorageKey);
        if (assetPath == null || !File.Exists(assetPath))
            throw new InvalidOperationException("Asset file not found.");
        var assetBytes = await File.ReadAllBytesAsync(assetPath, ct);
        var assetBase64 = Convert.ToBase64String(assetBytes);
        var assetMimeType = Path.GetExtension(assetPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };

        var roughPrompt = dto.RoughPrompt.Trim();
        if (!string.IsNullOrEmpty(surfaceDescription))
            roughPrompt += $" (detected surface: {surfaceDescription})";

        var model = await _settings.GetAsync("gemini_model", DefaultModel);
        var url = $"{GeminiBaseUrl}/models/{model}:generateContent?key={apiKey}";

        var instruction = string.Format(InstructionTemplate, roughPrompt);

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = instruction },
                        new { inline_data = new { mime_type = "image/jpeg", data = frameBase64 } },
                        new { inline_data = new { mime_type = assetMimeType, data = assetBase64 } },
                    },
                },
            },
            generation_config = new
            {
                temperature = 0.3,
                max_output_tokens = 2048,
                response_mime_type = "application/json",
            },
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("[GeminiKontextPrompt] Calling {Model} for content {ContentId} frame {Frame}",
            model, dto.ContentId, dto.FrameNumber);

        var response = await _http.PostAsync(url, httpContent, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            await _eventLog.LogEventAsync("GeminiKontextPrompt", "SUGGEST_HTTP_ERROR", "Error",
                $"HTTP {(int)response.StatusCode}: {Truncate(responseJson, 500)}");
            throw new InvalidOperationException(
                $"Gemini rejected the request (HTTP {(int)response.StatusCode}): {Truncate(responseJson, 400)}");
        }

        var result = JsonSerializer.Deserialize<GeminiResponse>(responseJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        var candidate = result?.Candidates?[0];
        var text = candidate?.Content?.Parts?[0]?.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException("Gemini returned an empty response.");

        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) text = text[7..];
        else if (text.StartsWith("```", StringComparison.Ordinal)) text = text[3..];
        if (text.EndsWith("```", StringComparison.Ordinal)) text = text[..^3];
        text = text.Trim();

        GeminiSuggestionOutput? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<GeminiSuggestionOutput>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException ex)
        {
            // finishReason "MAX_TOKENS" confirms truncation (the most likely cause of a cut-off
            // JSON string); logging the raw text either way means the next failure is diagnosable
            // instead of just "JsonException" with no visibility into what Gemini actually said.
            // Logged to EventLogs (DB), not just ILogger — ILogger output only reaches whichever
            // console happens to be running the process, which isn't reliably inspectable later.
            _logger.LogWarning(ex, "[GeminiKontextPrompt] Failed to parse Gemini output. finishReason={FinishReason}, raw text: {Text}",
                candidate?.FinishReason ?? "(none)", text);
            await _eventLog.LogEventAsync("GeminiKontextPrompt", "SUGGEST_PARSE_ERROR", "Error",
                $"finishReason={candidate?.FinishReason ?? "(none)"}, raw text: {Truncate(text, 800)}");
            throw new InvalidOperationException(
                candidate?.FinishReason == "MAX_TOKENS"
                    ? "Gemini's response was cut off before finishing (hit the output token limit). Try again."
                    : $"Gemini returned malformed output: {Truncate(text, 300)}");
        }

        if (string.IsNullOrWhiteSpace(parsed?.SuggestedPrompt))
            throw new InvalidOperationException("Gemini did not return a suggested prompt.");

        return new SuggestKontextPromptResponseDto
        {
            SuggestedPrompt = parsed.SuggestedPrompt.Trim(),
            ModelUsed = model,
        };
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static string? ResolveVideoPath(string storageKey)
    {
        var fileName = storageKey.Replace("/api/content/file/", "");
        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
        var filePath = Path.Combine(uploadsDir, fileName);
        if (File.Exists(filePath)) return filePath;
        var proxyPath = Path.Combine(uploadsDir, "proxies", fileName);
        return File.Exists(proxyPath) ? proxyPath : null;
    }

    private static string? ResolveAssetPath(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey)) return null;
        var fileName = storageKey.Replace("/api/assets/file/", "");
        return Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "assets", fileName);
    }

    /// <summary>
    /// Extracts a single frame via a fast pre-seek (jumps near the target via -ss before -i,
    /// keyframe-granularity) plus a small fine seek after -i for accuracy — unlike a
    /// "select=eq(n,...)" filter, which must decode from frame 0 up to the target and gets
    /// disproportionately slow for frames deep into a long video. Frame-exact precision isn't
    /// needed here (Gemini is doing general scene understanding, not pixel-perfect compositing),
    /// so this tradeoff is safe for this use case specifically.
    /// </summary>
    private async Task<string?> ExtractFrameAsync(string videoPath, int frameNumber, double fps, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"bit-gem-prompt-{Guid.NewGuid():N}.jpg");
        var timeSec = frameNumber / (fps > 0 ? fps : 30.0);
        var preSeek = Math.Max(0, timeSec - 2);
        var postSeek = timeSec - preSeek;
        var preStr = preSeek.ToString(CultureInfo.InvariantCulture);
        var postStr = postSeek.ToString(CultureInfo.InvariantCulture);

        var timedOut = false;
        string stderrText = "";
        int exitCode = -1;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -ss {preStr} -noaccurate_seek -i \"{videoPath}\" -ss {postStr} -vframes 1 -q:v 2 \"{tempFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();

            // Mandatory: drain both streams asynchronously — ffmpeg's verbose stderr output can
            // fill the OS pipe buffer and deadlock a synchronous wait if left unread.
            var readStdout = process.StandardOutput.ReadToEndAsync(ct);
            var readStderr = process.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                timedOut = true;
                if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch { } }
            }

            stderrText = await readStderr;
            await readStdout;
            if (process.HasExited) exitCode = process.ExitCode;

            if (!File.Exists(tempFile) || new FileInfo(tempFile).Length < 100)
            {
                _logger.LogWarning("[GeminiKontextPrompt] ffmpeg frame extraction failed for frame {Frame}. timedOut={TimedOut}, exitCode={ExitCode}, stderr={Stderr}",
                    frameNumber, timedOut, exitCode, stderrText);
                await _eventLog.LogEventAsync("GeminiKontextPrompt", "FRAME_EXTRACT_FAILED", "Error",
                    $"frame={frameNumber}, timedOut={timedOut}, exitCode={exitCode}, stderr={Truncate(stderrText, 500)}");
                return null;
            }
            var bytes = await File.ReadAllBytesAsync(tempFile, ct);
            return Convert.ToBase64String(bytes);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GeminiKontextPrompt] Failed to extract frame {Frame}", frameNumber);
            await _eventLog.LogEventAsync("GeminiKontextPrompt", "FRAME_EXTRACT_EXCEPTION", "Error",
                $"frame={frameNumber}: {ex.GetType().Name} — {ex.Message}");
            return null;
        }
        finally { try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { } }
    }

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public System.Collections.Generic.List<GeminiCandidate>? Candidates { get; set; }
    }

    private class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }

        [JsonPropertyName("finishReason")]
        public string? FinishReason { get; set; }
    }

    private class GeminiContent
    {
        [JsonPropertyName("parts")]
        public System.Collections.Generic.List<GeminiPart>? Parts { get; set; }
    }

    private class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private class GeminiSuggestionOutput
    {
        [JsonPropertyName("suggestedPrompt")]
        public string? SuggestedPrompt { get; set; }
    }
}
