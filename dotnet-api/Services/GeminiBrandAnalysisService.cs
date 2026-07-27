using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls Gemini 3 Flash Vision API for multimodal brand, logo, and text analysis.
/// Activated when engine_brand_analysis = "gemini".
/// </summary>
public class GeminiBrandAnalysisService : IBrandAnalysisService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<GeminiBrandAnalysisService> _logger;
    private readonly HttpClient _http;

    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string DefaultModel = "gemini-3-flash";

    private const string AnalysisPrompt = @"You are a competitive brand analysis system. Analyze this image region and identify:

1. BRANDS: Any brand names, logos, or trademarks visible. For each, state the brand name and product category.
2. LOGOS: Any logo symbols or marks. Describe the logo shape and what brand it represents.
3. TEXT: Any readable text. Transcribe it exactly.
4. COMPETITIVE CONFLICT: Determine if any detected brand competes with common advertising categories (beverages, automotive, telecom, financial services, retail, etc.).

Return ONLY valid JSON in this exact format — no markdown, no code fences, no explanation:
{
  ""brands"": [
    { ""name"": ""Brand Name"", ""category"": ""Beverages"" }
  ],
  ""logos"": [
    { ""description"": ""Red swoosh"", ""brand"": ""Coca-Cola"" }
  ],
  ""text"": [""Drink Fresh"", ""Est. 1886""],
  ""has_conflict"": true,
  ""conflict_description"": ""Detected Coca-Cola branding which conflicts with PepsiCo campaigns"",
  ""confidence"": 0.95
}

If no brands, logos, or text are found, return:
{ ""brands"": [], ""logos"": [], ""text"": [], ""has_conflict"": false, ""conflict_description"": null, ""confidence"": 0.0 }";

    public GeminiBrandAnalysisService(IPlatformSettingsService settings, ILogger<GeminiBrandAnalysisService> logger)
    {
        _settings = settings;
        _logger = logger;
        // Timeout configurable via gemini_timeout_seconds (default 90s)
        var timeoutSec = settings.GetAsync("gemini_timeout_seconds", "90").GetAwaiter().GetResult();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(int.TryParse(timeoutSec, out var t) ? t : 90) };
    }

    public async Task<BrandAnalysisResult> AnalyzeAsync(string contentId, string surfaceType, string frameRegionBase64)
    {
        var apiKey = await _settings.GetAsync("gemini_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[Gemini BA] API key not configured. Skipping brand analysis.");
            return new BrandAnalysisResult();
        }

        try
        {
            var model = await _settings.GetAsync("gemini_model", DefaultModel);
            var url = $"{GeminiBaseUrl}/models/{model}:generateContent?key={apiKey}";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = AnalysisPrompt },
                            new { inline_data = new { mime_type = "image/jpeg", data = frameRegionBase64 } },
                        },
                    },
                },
                generation_config = new
                {
                    temperature = 0.1,
                    top_p = 0.95,
                    max_output_tokens = 2048,
                    response_mime_type = "application/json",
                },
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("[Gemini BA] Analyzing surface '{SurfaceType}' for content {ContentId}",
                surfaceType, contentId);

            var response = await _http.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GeminiResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            var text = result?.Candidates?[0]?.Content?.Parts?[0]?.Text;
            if (string.IsNullOrEmpty(text))
                return new BrandAnalysisResult();

            // Strip markdown code fences if present (fallback for non-JSON-mode responses)
            text = text.Trim();
            if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) text = text[7..];
            else if (text.StartsWith("```", StringComparison.Ordinal)) text = text[3..];
            if (text.EndsWith("```", StringComparison.Ordinal)) text = text[..^3];
            text = text.Trim();

            var analysisResult = JsonSerializer.Deserialize<GeminiBrandAnalysisOutput>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (analysisResult == null)
                return new BrandAnalysisResult();

            var brandNames = analysisResult.Brands?.Select(b => b.Name ?? b.Brand ?? "Unknown").ToList() ?? new List<string>();
            var logoDescs = analysisResult.Logos?.Select(l => $"{l.Brand ?? "Unknown"}: {l.Description ?? "logo detected"}").ToList() ?? new List<string>();
            var detectedText = analysisResult.Text ?? new List<string>();

            _logger.LogInformation(
                "[Gemini BA] Found {BrandCount} brands, {LogoCount} logos, {TextCount} text items for {ContentId}",
                brandNames.Count, logoDescs.Count, detectedText.Count, contentId);

            return new BrandAnalysisResult
            {
                DetectedBrands = brandNames,
                DetectedLogos = logoDescs,
                DetectedText = detectedText,
                HasCompetitiveConflict = analysisResult.HasConflict,
                ConflictDescription = analysisResult.ConflictDescription,
                ConfidenceScore = analysisResult.Confidence,
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Gemini BA] HTTP error for {ContentId}", contentId);
            return new BrandAnalysisResult();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[Gemini BA] Failed to parse response for {ContentId}", contentId);
            return new BrandAnalysisResult();
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[Gemini BA] Timed out for {ContentId}", contentId);
            return new BrandAnalysisResult();
        }
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

    private class GeminiBrandAnalysisOutput
    {
        [JsonPropertyName("brands")]
        public List<BrandEntry>? Brands { get; set; }

        [JsonPropertyName("logos")]
        public List<LogoEntry>? Logos { get; set; }

        [JsonPropertyName("text")]
        public List<string>? Text { get; set; }

        [JsonPropertyName("has_conflict")]
        public bool HasConflict { get; set; }

        [JsonPropertyName("conflict_description")]
        public string? ConflictDescription { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
    }

    private class BrandEntry
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("brand")]
        public string? Brand { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }
    }

    private class LogoEntry
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("brand")]
        public string? Brand { get; set; }
    }
}
