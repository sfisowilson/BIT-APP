using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls Gemini Vision API for multimodal brand, logo, and text analysis.
/// Activated when engine_brand_analysis = "gemini".
/// </summary>
public class GeminiBrandAnalysisService : IBrandAnalysisService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<GeminiBrandAnalysisService> _logger;
    private readonly HttpClient _http;

    public GeminiBrandAnalysisService(IPlatformSettingsService settings, ILogger<GeminiBrandAnalysisService> logger)
    {
        _settings = settings;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<BrandAnalysisResult> AnalyzeAsync(string contentId, string surfaceType, string frameRegionBase64)
    {
        var apiKey = await _settings.GetAsync("gemini_api_key");
        if (string.IsNullOrEmpty(apiKey))
            return new BrandAnalysisResult();

        try
        {
            // TODO: Send frame region to Gemini Vision API
            // POST https://generativelanguage.googleapis.com/v1/models/gemini-pro-vision:generateContent?key={apiKey}
            // Prompt: "List any brands, logos, or advertising visible in this image. For each, state the brand name and category."
            // Parse response into BrandAnalysisResult

            _logger.LogInformation("[Gemini] Would analyze frame for content {ContentId}, surface {SurfaceType}",
                contentId, surfaceType);

            return new BrandAnalysisResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini brand analysis failed");
            return new BrandAnalysisResult();
        }
    }
}
