using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Afrobotics.Bit.Api.DTOs;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls Gemini to intelligently match assets to surfaces based on natural language instructions.
/// Replaces the client-side string-matching heuristic with real AI reasoning.
/// </summary>
public class GeminiPlacementService : IAiPlacementService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<GeminiPlacementService> _logger;
    private readonly HttpClient _http;

    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    public GeminiPlacementService(IPlatformSettingsService settings, ILogger<GeminiPlacementService> logger)
    {
        _settings = settings;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<AiPlacementResponse> SuggestPlacementsAsync(AiPlacementRequest request, CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync("gemini_api_key");
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("Gemini API key not configured.");

        var model = await _settings.GetAsync("gemini_model", "gemini-3.6-flash");

        var surfacesJson = JsonSerializer.Serialize(request.Surfaces.Select(s => new { id = s.Id, type = s.SurfaceType, confidence = s.ConfidenceScore }));
        var assetsJson = JsonSerializer.Serialize(request.Assets.Select(a => new { id = a.Id, name = a.Name, category = a.BrandCategory }));

        var prompt = $@"You are an advertising placement assistant. Given a user's natural language instruction,
a list of detected surfaces, and available brand assets, determine which asset should be placed on which surface.

USER INSTRUCTION: {request.Prompt}

DETECTED SURFACES (JSON):
{surfacesJson}

AVAILABLE ASSETS (JSON):
{assetsJson}

Return ONLY valid JSON in this exact format — no markdown, no code fences, no explanation outside the JSON:
{{
  ""placements"": [
    {{
      ""surfaceId"": ""id_of_surface"",
      ""assetId"": ""id_of_asset"",
      ""reasoning"": ""brief explanation of why this pairing""
    }}
  ],
  ""explanation"": ""one-sentence summary of your overall strategy""
}}

RULES:
- Match assets to surfaces based on the user's instruction and common sense
- An asset can only be placed on ONE surface
- If the user says 'place on all available surfaces' or similar, distribute assets across surfaces
- If the user names a specific asset and surface type, match them exactly
- If no clear match, leave placements empty
- Only use surfaces and assets from the provided lists — do not invent any";

        var url = $"{GeminiBaseUrl}/models/{model}:generateContent?key={apiKey}";
        var payload = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generation_config = new
            {
                temperature = 0.2,
                max_output_tokens = 2048,
                response_mime_type = "application/json",
            },
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("[GeminiPlacement] Calling {Model} for placement suggestions", model);

        HttpResponseMessage? response = null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            response = await _http.PostAsync(url, httpContent, ct);
            if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests) break;
            await Task.Delay(TimeSpan.FromSeconds(3 * Math.Pow(2, attempt)), ct);
        }
        response!.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<GeminiResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var text = result?.Candidates?[0]?.Content?.Parts?[0]?.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return new AiPlacementResponse();

        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) text = text[7..];
        else if (text.StartsWith("```")) text = text[3..];
        if (text.EndsWith("```")) text = text[..^3];
        text = text.Trim();

        var parsed = JsonSerializer.Deserialize<AiPlacementResponse>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        parsed!.ModelUsed = model;

        _logger.LogInformation("[GeminiPlacement] {Count} placements suggested", parsed.Placements.Count);
        return parsed;
    }

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
}
