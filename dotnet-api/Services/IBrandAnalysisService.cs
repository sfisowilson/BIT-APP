using System.Collections.Generic;
using System.Threading.Tasks;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Analyzes frames for existing brands, logos, text, and competitive separation.
/// Implementations: Basic (no-op), Google (Cloud Vision), Gemini (Vision).
/// Activated by the engine_brand_analysis platform setting.
/// </summary>
public interface IBrandAnalysisService
{
    Task<BrandAnalysisResult> AnalyzeAsync(string contentId, string surfaceType, string frameRegionBase64);
}

public class BrandAnalysisResult
{
    public List<string> DetectedBrands { get; set; } = new();
    public List<string> DetectedLogos { get; set; } = new();
    public List<string> DetectedText { get; set; } = new();
    public bool HasCompetitiveConflict { get; set; }
    public string? ConflictDescription { get; set; }
    public double ConfidenceScore { get; set; }
}
