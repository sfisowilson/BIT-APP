using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls Fal.ai SAM 2 API for pixel-perfect polygon mask generation.
///
/// Takes bounding boxes from Gemini detection and returns precise polygon
/// boundaries that follow the actual surface edges — essential for realistic
/// compositing (requirement R14).
///
/// Endpoint: POST https://fal.run/fal-ai/sam2
/// Cost: ~$0.001 per mask call (billed per image, not per box)
/// </summary>
public class FalAiSam2Service
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<FalAiSam2Service> _logger;
    private readonly HttpClient _http;

    private const string DefaultEndpoint = "https://fal.run/fal-ai/sam2";

    public FalAiSam2Service(IPlatformSettingsService settings, ILogger<FalAiSam2Service> logger)
    {
        _settings = settings;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    /// <summary>
    /// Generate polygon masks for a list of bounding boxes on a given image.
    /// </summary>
    /// <param name="imageBase64">Base64-encoded JPEG frame.</param>
    /// <param name="boxes">List of [x1, y1, x2, y2] bounding boxes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of polygon point arrays, one per input box. Empty list if a box couldn't be segmented.</returns>
    public async Task<List<List<Coord>>> GenerateMasksAsync(
        string imageBase64,
        List<List<double>> boxes,
        CancellationToken ct = default)
    {
        if (boxes.Count == 0) return new List<List<Coord>>();

        var apiKey = await _settings.GetAsync("falai_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[Fal.ai SAM2] API key not configured. Set 'falai_api_key' platform setting. Skipping masks.");
            // Return bbox-based quads as fallback
            return boxes.Select(b => BoxToQuad(b)).ToList();
        }

        var endpoint = await _settings.GetAsync("falai_sam2_endpoint", DefaultEndpoint);

        try
        {
            var payload = new
            {
                image_url = $"data:image/jpeg;base64,{imageBase64}",
                boxes = boxes,
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Key {apiKey}");

            _logger.LogInformation("[Fal.ai SAM2] Requesting masks for {Count} boxes", boxes.Count);

            var response = await _http.PostAsync(endpoint, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("[Fal.ai SAM2] HTTP {Status}: {Body}", (int)response.StatusCode, Truncate(errBody, 500));
                return boxes.Select(b => BoxToQuad(b)).ToList();
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<FalAiSam2Response>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            var masks = ParseMasks(result, boxes.Count);

            _logger.LogInformation("[Fal.ai SAM2] Got {Count} masks from {BoxCount} boxes",
                masks.Count(m => m.Count >= 4), boxes.Count);

            return masks;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[Fal.ai SAM2] Timed out — falling back to bounding boxes");
            return boxes.Select(b => BoxToQuad(b)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Fal.ai SAM2] Failed — falling back to bounding boxes");
            return boxes.Select(b => BoxToQuad(b)).ToList();
        }
    }

    // ── Parsing ──

    private static List<List<Coord>> ParseMasks(FalAiSam2Response? result, int expectedCount)
    {
        var masks = new List<List<Coord>>();

        if (result?.Masks == null)
        {
            // Try alternative output formats
            return Enumerable.Range(0, expectedCount).Select(_ => new List<Coord>()).ToList();
        }

        foreach (var maskData in result.Masks)
        {
            var polygon = new List<Coord>();

            // Fal.ai SAM2 returns masks as list of [x, y] points
            if (maskData is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var point in element.EnumerateArray())
                    {
                        if (point.ValueKind == JsonValueKind.Array)
                        {
                            var coords = new List<double>();
                            foreach (var c in point.EnumerateArray())
                                coords.Add(c.GetDouble());
                            if (coords.Count >= 2)
                                polygon.Add(new Coord { X = (int)coords[0], Y = (int)coords[1] });
                        }
                    }
                }
            }

            masks.Add(polygon.Count >= 4 ? polygon : new List<Coord>());
        }

        // Pad to expected count
        while (masks.Count < expectedCount)
            masks.Add(new List<Coord>());

        return masks;
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

    private class FalAiSam2Response
    {
        [JsonPropertyName("masks")]
        public List<JsonElement>? Masks { get; set; }

        // Some API versions use different field names
        [JsonPropertyName("output")]
        public JsonElement? Output { get; set; }

        [JsonPropertyName("result")]
        public JsonElement? Result { get; set; }
    }
}
