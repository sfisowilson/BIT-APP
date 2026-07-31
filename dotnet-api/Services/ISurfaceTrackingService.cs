using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Segments and tracks a surface within a video via SAM3: single-frame click preview and
/// per-shot video-rle segmentation (the foundation for ShotAwareTrackingService's cross-cut
/// tracking). Activated by the engine_tracking platform setting.
/// </summary>
public interface ISurfaceTrackingService
{
    /// <summary>
    /// Preview segment a clicked point on a single video frame using SAM3 video-rle.
    /// Returns the decoded polygon for UI overlay. Null if unsupported or no mask found.
    /// </summary>
    Task<SegmentPreviewResult?> PreviewSegmentAsync(
        string contentId,
        string videoPath,
        int frameIndex,
        int x,
        int y,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Segment a frame range via fal-ai/sam-3/video-rle, seeded by a point (single-frame preview
    /// click), a box (continuous tracking from a known pixel location — used within the shot
    /// containing the seed click/quad), or a text prompt (re-anchoring after a hard cut, where the
    /// previous pixel location is meaningless in the new camera angle but the surface's semantic
    /// description still identifies it). Returns per-frame RLE masks keyed by a stable track_id.
    /// Empty list if nothing clears <paramref name="detectionThreshold"/> in this frame range.
    /// </summary>
    Task<List<RleFrameResult>> SegmentVideoRleAsync(
        string videoPath,
        int startFrame,
        int endFrame,
        (int xMin, int yMin, int xMax, int yMax)? seedBox = null,
        (int x, int y)? seedPoint = null,
        string? textPrompt = null,
        int promptFrame = -1,
        double detectionThreshold = 0.5,
        CancellationToken cancellationToken = default);
}

/// <summary>Per-frame RLE mask data returned by SAM3 video-rle segmentation.</summary>
public class RleFrameResult
{
    /// <summary>0-based absolute frame index in the source video.</summary>
    public int FrameIndex { get; set; }

    public List<RleObjectResult> Objects { get; set; } = new();
}

/// <summary>A single tracked object's mask within one frame.</summary>
public class RleObjectResult
{
    /// <summary>Stable track id — the same object keeps the same id across frames within one call.</summary>
    public int TrackId { get; set; }

    /// <summary>Kaggle/COCO-order run-length-encoded mask.</summary>
    public string Rle { get; set; } = string.Empty;

    public double Confidence { get; set; }
}

/// <summary>
/// Result from SAM3 video-rle preview segmentation of a single clicked point.
/// </summary>
public class SegmentPreviewResult
{
    /// <summary>Decoded polygon points in pixel coordinates.</summary>
    public List<(int x, int y)> MaskPolygon { get; set; } = new();

    /// <summary>Confidence score from SAM3 (0.0-1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Stable track ID from SAM3 for drift-check comparison.</summary>
    public int TrackId { get; set; }

    /// <summary>Detected surface type if known.</summary>
    public string SurfaceType { get; set; } = string.Empty;

    /// <summary>Frame index the mask corresponds to.</summary>
    public int FrameIndex { get; set; }

    /// <summary>Bounding box of the mask.</summary>
    public (int xMin, int yMin, int xMax, int yMax) Bounds { get; set; }
}
