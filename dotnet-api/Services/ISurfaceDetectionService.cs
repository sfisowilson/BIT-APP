using System.Collections.Generic;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Detects candidate advertising surfaces in video frames.
/// Implementations: Basic (random), Replicate (SAM 2), Google (Cloud Vision).
/// Activated by the engine_detection platform setting.
/// </summary>
public interface ISurfaceDetectionService
{
    Task<List<SurfaceDetectionResult>> DetectAsync(string contentId, int sceneIndex, int startFrame, int endFrame);
}

public class SurfaceDetectionResult
{
    public string SurfaceType { get; set; } = string.Empty;
    public string BoundaryCoordinatesJson { get; set; } = "[]";
    public double EstimatedDepth { get; set; }
    public string OrientationVectorJson { get; set; } = "{}";
    public double ConfidenceScore { get; set; }
    public double ViabilityScore { get; set; }
    public string? ExclusionReason { get; set; }
}
