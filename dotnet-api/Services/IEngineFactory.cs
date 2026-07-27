using System.Threading.Tasks;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Factory for dynamically resolving configured AI engines without blocking DI container initialization.
/// Synchronous methods use cached engine keys (refreshed via async on first call). No per-request DB I/O.
/// </summary>
public interface IEngineFactory
{
    Task<ISurfaceDetectionService> GetSurfaceDetectionEngineAsync();
    Task<IBrandAnalysisService> GetBrandAnalysisEngineAsync();
    Task<ICompositingService> GetCompositingEngineAsync();
    Task<ISurfaceTrackingService> GetTrackingEngineAsync();

    // Synchronous variants for DI — keys are cached in memory after first async load
    ISurfaceDetectionService GetSurfaceDetectionEngine();
    IBrandAnalysisService GetBrandAnalysisEngine();
    ICompositingService GetCompositingEngine();
    ISurfaceTrackingService GetTrackingEngine();
}
