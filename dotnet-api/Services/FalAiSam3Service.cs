using System;
using System.Collections.Generic;
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
/// Calls Fal.ai SAM 3 API for pixel-perfect polygon mask generation.
///
/// SAM 3 accepts combined box + text prompts for higher accuracy:
///   - Bounding boxes from Gemini detection (same as SAM 2)
///   - Surface type text string from Gemini (new in SAM 3 — constrains segmentation to the described concept)
///
/// Endpoint: POST https://fal.run/fal-ai/sam-3/image
/// Cost: ~$0.001 per mask call (billed per image, not per box)
/// </summary>
public class FalAiSam3Service
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<FalAiSam3Service> _logger;
    private readonly HttpClient _http;

    private const string DefaultEndpoint = "https://fal.run/fal-ai/sam-3/image";

    public FalAiSam3Service(IPlatformSettingsService settings, ILogger<FalAiSam3Service> logger)
    {
        _settings = settings;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    /// <summary>
    /// Generate polygon masks for a list of bounding boxes on a given image,
    /// optionally guided by surface type text descriptions.
    /// </summary>
    /// <param name="imageBase64">Base64-encoded JPEG frame.</param>
    /// <param name="boxes">List of [x1, y1, x2, y2] bounding boxes.</param>
    /// <param name="surfaceTypes">Optional list of surface type labels, one per box (e.g. "brick wall", "LED scoreboard").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of polygon point arrays, one per input box. Empty list if a box couldn't be segmented.</returns>
    public async Task<List<List<Coord>>> GenerateMasksAsync(
        string imageBase64,
        List<List<double>> boxes,
        List<string>? surfaceTypes = null,
        CancellationToken ct = default)
    {
        if (boxes.Count == 0) return new List<List<Coord>>();

        var apiKey = await _settings.GetAsync("falai_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[Fal.ai SAM3] API key not configured. Set 'falai_api_key' platform setting. Skipping masks.");
            // Return bbox-based quads as fallback
            return boxes.Select(b => BoxToQuad(b)).ToList();
        }

        var endpoint = await _settings.GetAsync("falai_sam3_endpoint", DefaultEndpoint);

        try
        {
            // Build prompts: combine box + surface type text for SAM 3's combined-prompt mode
            var prompts = new List<object>();
            for (int i = 0; i < boxes.Count; i++)
            {
                var prompt = new Dictionary<string, object>
                {
                    ["box"] = boxes[i],
                };
                if (surfaceTypes != null && i < surfaceTypes.Count && !string.IsNullOrEmpty(surfaceTypes[i]))
                {
                    prompt["text"] = surfaceTypes[i];
                }
                prompts.Add(prompt);
            }

            var payload = new
            {
                image_url = $"data:image/jpeg;base64,{imageBase64}",
                prompts = prompts,
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Key {apiKey}");

            _logger.LogInformation("[Fal.ai SAM3] Requesting masks for {Count} boxes{TextHint}",
                boxes.Count,
                surfaceTypes != null && surfaceTypes.Any(t => !string.IsNullOrEmpty(t)) ? " (with text prompts)" : "");

            var response = await _http.PostAsync(endpoint, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("[Fal.ai SAM3] HTTP {Status}: {Body}", (int)response.StatusCode, Truncate(errBody, 500));
                return boxes.Select(b => BoxToQuad(b)).ToList();
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<FalAiSam3Response>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            var masks = ParseSam3Masks(result, boxes.Count);

            _logger.LogInformation("[Fal.ai SAM3] Got {Count} masks from {BoxCount} boxes",
                masks.Count(m => m.Count >= 4), boxes.Count);

            return masks;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[Fal.ai SAM3] Timed out — falling back to bounding boxes");
            return boxes.Select(b => BoxToQuad(b)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Fal.ai SAM3] Failed — falling back to bounding boxes");
            return boxes.Select(b => BoxToQuad(b)).ToList();
        }
    }

    // ── SAM 3 Response Parsing ──

    /// <summary>
    /// SAM 3 returns multiple mask candidates per box with per-instance confidence scores.
    /// We take the top-scored mask for each box.
    /// </summary>
    private static List<List<Coord>> ParseSam3Masks(FalAiSam3Response? result, int expectedCount)
    {
        var masks = new List<List<Coord>>();

        if (result?.Masks == null && result?.Output == null)
        {
            // Try the result wrapper — may be a JsonElement containing an array
            if (result?.Result.HasValue == true)
            {
                var resultValue = result.Result.Value;
                if (resultValue.ValueKind == JsonValueKind.Array)
                    return ParseMaskList(resultValue.EnumerateArray().ToList(), expectedCount);
            }
            return Enumerable.Range(0, expectedCount).Select(_ => new List<Coord>()).ToList();
        }

        // SAM 3 may return masks directly or nested in output
        var maskList = result.Masks ?? new List<JsonElement>();
        if (maskList.Count == 0 && result.Output.HasValue)
        {
            maskList = ParseOutputField(result.Output.Value);
        }

        return ParseMaskList(maskList, expectedCount);
    }

    private static List<List<Coord>> ParseMaskList(List<JsonElement> maskList, int expectedCount)
    {
        var masks = new List<List<Coord>>();

        foreach (var maskElement in maskList)
        {
            var bestMask = new List<Coord>();
            double bestScore = -1;

            // SAM 3 returns multiple candidates with per-instance confidence
            if (maskElement.ValueKind == JsonValueKind.Object &&
                maskElement.TryGetProperty("candidates", out var candidates))
            {
                foreach (var candidate in candidates.EnumerateArray())
                {
                    var score = 0.0;
                    if (candidate.TryGetProperty("score", out var scoreElem))
                        score = scoreElem.GetDouble();
                    if (candidate.TryGetProperty("confidence", out var confElem))
                        score = Math.Max(score, confElem.GetDouble());

                    var polygon = ParsePolygon(candidate);
                    if (polygon.Count >= 4 && score > bestScore)
                    {
                        bestScore = score;
                        bestMask = polygon;
                    }
                }

                // If no scored candidates, take the first valid one
                if (bestScore < 0)
                {
                    foreach (var candidate in candidates.EnumerateArray())
                    {
                        var polygon = ParsePolygon(candidate);
                        if (polygon.Count >= 4) { bestMask = polygon; break; }
                    }
                }
            }
            else
            {
                // Flat format: mask is the element itself or has a "polygon" / "mask" field
                bestMask = ParsePolygon(maskElement);
            }

            masks.Add(bestMask.Count >= 4 ? bestMask : new List<Coord>());
        }

        // Pad to expected count
        while (masks.Count < expectedCount)
            masks.Add(new List<Coord>());

        return masks;
    }

    private static List<Coord> ParsePolygon(JsonElement element)
    {
        var polygon = new List<Coord>();

        // Try different JSON shapes: direct array, {polygon: [...]}, {mask: [...]}, {points: [...]}
        JsonElement pointsArray = element;
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("polygon", out var poly)) pointsArray = poly;
            else if (element.TryGetProperty("mask", out var mask)) pointsArray = mask;
            else if (element.TryGetProperty("points", out var pts)) pointsArray = pts;
            else if (element.TryGetProperty("coordinates", out var coords)) pointsArray = coords;
        }

        if (pointsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var point in pointsArray.EnumerateArray())
            {
                if (point.ValueKind == JsonValueKind.Array)
                {
                    var coords = new List<double>();
                    foreach (var c in point.EnumerateArray())
                        coords.Add(c.GetDouble());
                    if (coords.Count >= 2)
                        polygon.Add(new Coord { X = (int)coords[0], Y = (int)coords[1] });
                }
                else if (point.ValueKind == JsonValueKind.Object)
                {
                    int x = 0, y = 0;
                    if (point.TryGetProperty("x", out var xElem)) x = xElem.GetInt32();
                    if (point.TryGetProperty("y", out var yElem)) y = yElem.GetInt32();
                    polygon.Add(new Coord { X = x, Y = y });
                }
            }
        }

        return polygon;
    }

    private static List<JsonElement> ParseOutputField(JsonElement output)
    {
        if (output.ValueKind == JsonValueKind.Array)
            return output.EnumerateArray().ToList();
        return new List<JsonElement>();
    }

    private static List<Coord> BoxToQuad(List<double> box)
    {
        if (box.Count < 4) return new List<Coord>();
        return new List<Coord>
        {
            new() { X = (int)box[0], Y = (int)box[1] },
            new() { X = (int)box[2], Y = (int)box[1] },
            new() { X = (int)box[2], Y = (int)box[3] },
            new() { X = (int)box[0], Y = (int)box[3] },
        };
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    // ── JSON models ──

    private class FalAiSam3Response
    {
        [JsonPropertyName("masks")]
        public List<JsonElement>? Masks { get; set; }

        [JsonPropertyName("output")]
        public JsonElement? Output { get; set; }

        [JsonPropertyName("result")]
        public JsonElement? Result { get; set; }
    }
}

/// <summary>
/// Simple coordinate pair used across the pipeline.
/// </summary>
public class Coord
{
    [System.Text.Json.Serialization.JsonPropertyName("x")]
    public int X { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("y")]
    public int Y { get; set; }
}
