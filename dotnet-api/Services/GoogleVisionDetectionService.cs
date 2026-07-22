using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls Google Cloud Vision API for object localization and surface detection.
/// Activated when engine_detection = "google".
/// </summary>
public class GoogleVisionDetectionService : ISurfaceDetectionService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<GoogleVisionDetectionService> _logger;
    private readonly HttpClient _http;

    public GoogleVisionDetectionService(IPlatformSettingsService settings, ILogger<GoogleVisionDetectionService> logger)
    {
        _settings = settings;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };
    }

    public async Task<List<SurfaceDetectionResult>> DetectAsync(string contentId, int sceneIndex, int startFrame, int endFrame)
    {
        var apiKey = await _settings.GetAsync("google_vision_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Google Vision API key not configured — falling back to basic detection");
            return await new BasicSurfaceDetectionService().DetectAsync(contentId, sceneIndex, startFrame, endFrame);
        }

        try
        {
            // TODO: Extract representative frame, send to Vision API for object localization
            // var frameBase64 = ExtractKeyFrame(contentId, startFrame);
            // var response = await _http.PostAsync($"https://vision.googleapis.com/v1/images:annotate?key={apiKey}", ...);
            // Parse response into SurfaceDetectionResult list

            _logger.LogInformation("[GoogleVision] Would call Vision API for content {ContentId}, frames {Start}-{End}",
                contentId, startFrame, endFrame);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Vision API call failed — falling back to basic detection");
        }

        return await new BasicSurfaceDetectionService().DetectAsync(contentId, sceneIndex, startFrame, endFrame);
    }
}
