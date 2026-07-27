using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls Google Cloud Vision API for logo detection, text extraction, and label detection.
/// Activated when engine_brand_analysis = "google".
/// </summary>
public class GoogleVisionBrandAnalysisService : IBrandAnalysisService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<GoogleVisionBrandAnalysisService> _logger;
    private readonly HttpClient _http;

    public GoogleVisionBrandAnalysisService(IPlatformSettingsService settings, ILogger<GoogleVisionBrandAnalysisService> logger)
    {
        _settings = settings;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<BrandAnalysisResult> AnalyzeAsync(string contentId, string surfaceType, string frameRegionBase64)
    {
        var apiKey = await _settings.GetAsync("google_vision_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[GoogleVision BA] API key not configured. Skipping brand analysis.");
            return new BrandAnalysisResult();
        }

        try
        {
            var requestBody = new
            {
                requests = new[]
                {
                    new
                    {
                        image = new { content = frameRegionBase64 },
                        features = new[]
                        {
                            new { type = "LOGO_DETECTION", maxResults = 20 },
                            new { type = "TEXT_DETECTION", maxResults = 50 },
                            new { type = "LABEL_DETECTION", maxResults = 30 },
                        },
                    },
                },
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"https://vision.googleapis.com/v1/images:annotate?key={apiKey}";
            _logger.LogInformation("[GoogleVision BA] Analyzing surface '{SurfaceType}' for content {ContentId}",
                surfaceType, contentId);

            var response = await _http.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            var result = new BrandAnalysisResult();

            if (root.TryGetProperty("responses", out var responses) && responses.GetArrayLength() > 0)
            {
                var firstResponse = responses[0];

                // ── Logo detection ──
                if (firstResponse.TryGetProperty("logoAnnotations", out var logos))
                {
                    foreach (var logo in logos.EnumerateArray())
                    {
                        var desc = GetStringProperty(logo, "description") ?? "Unknown logo";
                        result.DetectedLogos.Add(desc);
                    }
                }

                // ── Text detection ──
                if (firstResponse.TryGetProperty("textAnnotations", out var texts))
                {
                    foreach (var text in texts.EnumerateArray().Take(20))  // limit to avoid excessive data
                    {
                        var desc = GetStringProperty(text, "description");
                        if (!string.IsNullOrEmpty(desc))
                            result.DetectedText.Add(desc);
                    }
                }

                // ── Label detection → brand names ──
                if (firstResponse.TryGetProperty("labelAnnotations", out var labels))
                {
                    foreach (var label in labels.EnumerateArray())
                    {
                        var desc = GetStringProperty(label, "description");
                        var score = GetDoubleProperty(label, "score");
                        if (!string.IsNullOrEmpty(desc) && score > 0.60)
                            result.DetectedBrands.Add(desc);
                    }
                }
            }

            result.ConfidenceScore = result.DetectedBrands.Count > 0 || result.DetectedLogos.Count > 0 ? 0.85 : 0.0;

            _logger.LogInformation(
                "[GoogleVision BA] Found {BrandCount} labels, {LogoCount} logos, {TextCount} text items for {ContentId}",
                result.DetectedBrands.Count, result.DetectedLogos.Count, result.DetectedText.Count, contentId);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[GoogleVision BA] HTTP error for {ContentId}", contentId);
            return new BrandAnalysisResult();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[GoogleVision BA] Failed to parse response for {ContentId}", contentId);
            return new BrandAnalysisResult();
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[GoogleVision BA] Timed out for {ContentId}", contentId);
            return new BrandAnalysisResult();
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static double GetDoubleProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetDouble();
        return 0;
    }
}
