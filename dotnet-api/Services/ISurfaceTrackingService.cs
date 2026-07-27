using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Tracks a surface boundary through every frame of its scene.
///
/// Takes an operator-adjusted seed boundary and propagates it through
/// frames 1..N using SAM 3 video mode, returning per-frame polygon data
/// with drift confidence estimates.
///
/// Activated by the engine_tracking platform setting.
/// </summary>
public interface ISurfaceTrackingService
{
    /// <summary>
    /// Track a surface boundary across a frame range.
    /// </summary>
    /// <param name="surfaceId">The surface being tracked.</param>
    /// <param name="videoPath">Absolute path to the source video file.</param>
    /// <param name="startFrame">First frame to track (typically scene start).</param>
    /// <param name="endFrame">Last frame to track (typically scene end).</param>
    /// <param name="seedBoundaryJson">Operator-adjusted boundary polygon as JSON [{x,y},...].</param>
    /// <param name="promptFrame">Frame number where the seed boundary was detected (used as SAM3 prompt frame).</param>
    /// <param name="sam3Prompt">Gemini-generated visual description for SAM3 segmentation. Null if unavailable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Per-frame boundary data. Empty list if tracking fails or is unsupported.</returns>
    Task<List<FrameBoundary>> TrackAsync(
        string surfaceId,
        string videoPath,
        int startFrame,
        int endFrame,
        string seedBoundaryJson,
        int promptFrame = -1,
        string? sam3Prompt = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Boundary data for a single frame within a tracked surface.
/// </summary>
public class FrameBoundary
{
    /// <summary>Absolute frame number.</summary>
    public int Frame { get; set; }

    /// <summary>Boundary polygon as JSON [{x,y}, ...].</summary>
    public string BoundaryCoordinatesJson { get; set; } = "[]";

    /// <summary>
    /// Confidence that the boundary hasn't drifted from the seed (0.0–1.0).
    /// Low values indicate the tracker lost the surface or it left the frame.
    /// </summary>
    public double DriftConfidence { get; set; }
}
