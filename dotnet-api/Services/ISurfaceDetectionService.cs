using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Detects candidate advertising surfaces in video frames.
/// Implementations: Basic (random), Replicate (SAM 2), Google (Cloud Vision), YOLO.
/// Activated by the engine_detection platform setting.
/// </summary>
public interface ISurfaceDetectionService
{
    Task<List<SurfaceDetectionResult>> DetectAsync(string contentId, int sceneIndex, int startFrame, int endFrame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch detection: process all scenes for a content item in a single call.
    /// Default implementation falls back to per-scene DetectAsync.
    /// Engines that support batching (YOLO) override for better performance.
    /// </summary>
    Task<List<SceneDetectionBatchResult>> DetectBatchAsync(string contentId, string videoPath,
        List<SceneCut> scenes, CancellationToken cancellationToken = default)
    {
        // Default: sequential per-scene calls
        return DetectBatchFallbackAsync(contentId, scenes, cancellationToken);
    }

    /// <summary>Fallback: calls DetectAsync per scene. Used by engines that don't support batching.</summary>
    protected async Task<List<SceneDetectionBatchResult>> DetectBatchFallbackAsync(
        string contentId, List<SceneCut> scenes, CancellationToken ct)
    {
        var results = new List<SceneDetectionBatchResult>();
        foreach (var scene in scenes)
        {
            ct.ThrowIfCancellationRequested();
            var surfaces = await DetectAsync(contentId, scene.SceneIndex, scene.StartFrame, scene.EndFrame, ct);
            results.Add(new SceneDetectionBatchResult
            {
                SceneIndex = scene.SceneIndex,
                Surfaces = surfaces,
                Succeeded = true,
            });
        }
        return results;
    }
}

/// <summary>Result for a single scene within a batch detection call.</summary>
public class SceneDetectionBatchResult
{
    public int SceneIndex { get; set; }
    public List<SurfaceDetectionResult> Surfaces { get; set; } = new();
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>Simple scene cut info for batch requests.</summary>
public class SceneCut
{
    public int SceneIndex { get; set; }
    public int StartFrame { get; set; }
    public int EndFrame { get; set; }
    public double DurationSeconds { get; set; }
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

    /// <summary>Gemini-generated visual description optimized for SAM3 segmentation.</summary>
    public string? Sam3Prompt { get; set; }
}
