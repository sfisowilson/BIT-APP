using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

public class EngineFactory : IEngineFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EngineFactory> _logger;

    // Cached engine keys — loaded once from DB, refreshed on explicit Set calls
    private static readonly Dictionary<string, string> CachedKeys = new();
    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static bool _keysLoaded;

    public EngineFactory(IServiceProvider serviceProvider, ILogger<EngineFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private string GetCachedKey(string settingKey, string fallback)
    {
        if (CachedKeys.TryGetValue(settingKey, out var val)) return val;

        // First call — load all engine keys from DB synchronously (safe: called once per process)
        CacheLock.Wait();
        try
        {
            if (!_keysLoaded)
            {
                using var scope = _serviceProvider.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<IPlatformSettingsService>();
                foreach (var sk in new[] { "engine_detection", "engine_brand_analysis", "engine_compositing", "engine_tracking" })
                {
                    CachedKeys[sk] = settings.GetAsync(sk).GetAwaiter().GetResult() ?? fallback;
                }
                _keysLoaded = true;
                _logger.LogInformation("[EngineFactory] Engine keys cached from DB");
            }
        }
        finally { CacheLock.Release(); }

        return CachedKeys.TryGetValue(settingKey, out val) ? val : fallback;
    }

    // ── Synchronous resolvers (used by DI, cached keys — no DB I/O per request) ──

    public ISurfaceDetectionService GetSurfaceDetectionEngine()
    {
        var key = GetCachedKey("engine_detection", "replicate");
        _logger.LogInformation("[EngineFactory] Resolving surface detection engine: {EngineKey}", key);
        return key.ToLowerInvariant() switch
        {
            "replicate"      => _serviceProvider.GetRequiredService<ReplicateSurfaceDetectionService>(),
            "gemini"         => _serviceProvider.GetRequiredService<GeminiDetectionService>(),
            "google"         => _serviceProvider.GetRequiredService<GoogleVisionDetectionService>(),
            "yolo"           => _serviceProvider.GetRequiredService<YoloSurfaceDetectionService>(),
            "grounding-dino" => _serviceProvider.GetRequiredService<GroundingDinoDetectionService>(),
            _ => throw new InvalidOperationException(
                $"No valid surface detection engine configured (engine_detection='{key}'). " +
                "Set the 'engine_detection' Platform Setting to one of: replicate, gemini, google, yolo, grounding-dino."),
        };
    }

    public IBrandAnalysisService GetBrandAnalysisEngine()
    {
        var key = GetCachedKey("engine_brand_analysis", "gemini");
        _logger.LogInformation("[EngineFactory] Resolving brand analysis engine: {EngineKey}", key);
        return key.ToLowerInvariant() switch
        {
            "google" => _serviceProvider.GetRequiredService<GoogleVisionBrandAnalysisService>(),
            "gemini" => _serviceProvider.GetRequiredService<GeminiBrandAnalysisService>(),
            _ => throw new InvalidOperationException(
                $"No valid brand analysis engine configured (engine_brand_analysis='{key}'). " +
                "Set the 'engine_brand_analysis' Platform Setting to one of: google, gemini."),
        };
    }

    public ICompositingService GetCompositingEngine()
    {
        var key = GetCachedKey("engine_compositing", "opencv");
        _logger.LogInformation("[EngineFactory] Resolving compositing engine: {EngineKey}", key);
        return key.ToLowerInvariant() switch
        {
            "opencv" => _serviceProvider.GetRequiredService<OpenCvCompositingService>(),
            "pikaswaps" => _serviceProvider.GetRequiredService<PikaswapsCompositingService>(),
            "planar-warp" => _serviceProvider.GetRequiredService<PlanarWarpCompositingService>(),
            _ => throw new InvalidOperationException(
                $"No valid compositing engine configured (engine_compositing='{key}'). " +
                "Set the 'engine_compositing' Platform Setting to one of: opencv, pikaswaps, planar-warp."),
        };
    }

    public ISurfaceTrackingService GetTrackingEngine()
    {
        var key = GetCachedKey("engine_tracking", "sam3");
        _logger.LogInformation("[EngineFactory] Resolving tracking engine: {EngineKey}", key);
        return key.ToLowerInvariant() switch
        {
            "sam3" => _serviceProvider.GetRequiredService<Sam3TrackingService>(),
            _ => throw new InvalidOperationException(
                $"No valid tracking engine configured (engine_tracking='{key}'). " +
                "Set the 'engine_tracking' Platform Setting to: sam3."),
        };
    }

    // ── Async resolvers (used by Hangfire jobs — creates own scope) ──

    public async Task<ISurfaceDetectionService> GetSurfaceDetectionEngineAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPlatformSettingsService>();
        var engineKey = await settings.GetAsync("engine_detection") ?? "replicate";

        _logger.LogInformation("[EngineFactory] Resolving surface detection engine: {EngineKey}", engineKey);

        return engineKey.ToLowerInvariant() switch
        {
            "replicate"      => scope.ServiceProvider.GetRequiredService<ReplicateSurfaceDetectionService>(),
            "gemini"         => scope.ServiceProvider.GetRequiredService<GeminiDetectionService>(),
            "google"         => scope.ServiceProvider.GetRequiredService<GoogleVisionDetectionService>(),
            "yolo"           => scope.ServiceProvider.GetRequiredService<YoloSurfaceDetectionService>(),
            "grounding-dino" => scope.ServiceProvider.GetRequiredService<GroundingDinoDetectionService>(),
            _ => throw new InvalidOperationException(
                $"No valid surface detection engine configured (engine_detection='{engineKey}'). " +
                "Set the 'engine_detection' Platform Setting to one of: replicate, gemini, google, yolo, grounding-dino."),
        };
    }

    public async Task<IBrandAnalysisService> GetBrandAnalysisEngineAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPlatformSettingsService>();
        var engineKey = await settings.GetAsync("engine_brand_analysis") ?? "gemini";

        _logger.LogInformation("[EngineFactory] Resolving brand analysis engine: {EngineKey}", engineKey);

        return engineKey.ToLowerInvariant() switch
        {
            "google" => scope.ServiceProvider.GetRequiredService<GoogleVisionBrandAnalysisService>(),
            "gemini" => scope.ServiceProvider.GetRequiredService<GeminiBrandAnalysisService>(),
            _ => throw new InvalidOperationException(
                $"No valid brand analysis engine configured (engine_brand_analysis='{engineKey}'). " +
                "Set the 'engine_brand_analysis' Platform Setting to one of: google, gemini."),
        };
    }

    public async Task<ICompositingService> GetCompositingEngineAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPlatformSettingsService>();
        var engineKey = await settings.GetAsync("engine_compositing") ?? "opencv";

        _logger.LogInformation("[EngineFactory] Resolving compositing engine: {EngineKey}", engineKey);

        return engineKey.ToLowerInvariant() switch
        {
            "opencv" => scope.ServiceProvider.GetRequiredService<OpenCvCompositingService>(),
            "pikaswaps" => scope.ServiceProvider.GetRequiredService<PikaswapsCompositingService>(),
            "planar-warp" => scope.ServiceProvider.GetRequiredService<PlanarWarpCompositingService>(),
            _ => throw new InvalidOperationException(
                $"No valid compositing engine configured (engine_compositing='{engineKey}'). " +
                "Set the 'engine_compositing' Platform Setting to one of: opencv, pikaswaps, planar-warp."),
        };
    }

    public async Task<ISurfaceTrackingService> GetTrackingEngineAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPlatformSettingsService>();
        var engineKey = await settings.GetAsync("engine_tracking") ?? "sam3";

        _logger.LogInformation("[EngineFactory] Resolving tracking engine: {EngineKey}", engineKey);

        return engineKey.ToLowerInvariant() switch
        {
            "sam3" => scope.ServiceProvider.GetRequiredService<Sam3TrackingService>(),
            _ => throw new InvalidOperationException(
                $"No valid tracking engine configured (engine_tracking='{engineKey}'). " +
                "Set the 'engine_tracking' Platform Setting to: sam3."),
        };
    }
}
