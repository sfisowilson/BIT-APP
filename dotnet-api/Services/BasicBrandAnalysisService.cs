using System.Threading.Tasks;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Default: no brand analysis. Returns empty results.
/// Used when no external AI engine is configured (engine_brand_analysis = "basic").
/// </summary>
public class BasicBrandAnalysisService : IBrandAnalysisService
{
    public Task<BrandAnalysisResult> AnalyzeAsync(string contentId, string surfaceType, string frameRegionBase64)
    {
        return Task.FromResult(new BrandAnalysisResult());
    }
}
