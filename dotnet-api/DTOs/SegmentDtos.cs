namespace Afrobotics.Bit.Api.DTOs;

/// <summary>
/// Request to preview-segment a clicked point on a video frame using SAM3 video-rle.
/// </summary>
public class SegmentPreviewRequest
{
    /// <summary>Content item ID containing the video.</summary>
    public string ContentId { get; set; } = string.Empty;

    /// <summary>0-based frame index where the user clicked.</summary>
    public int FrameIndex { get; set; }

    /// <summary>X coordinate of the click in native video pixel space.</summary>
    public int X { get; set; }

    /// <summary>Y coordinate of the click in native video pixel space.</summary>
    public int Y { get; set; }
}

/// <summary>
/// Response from SAM3 video-rle preview segmentation. Contains the decoded polygon for UI overlay.
/// </summary>
public class SegmentPreviewResponse
{
    /// <summary>Polygon points as [{x, y}, ...] JSON string for SVG overlay rendering.</summary>
    public string MaskPolygonJson { get; set; } = "[]";

    /// <summary>Confidence score from SAM3 (0.0-1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Stable track ID from SAM3 for drift-check comparison.</summary>
    public int TrackId { get; set; }

    /// <summary>Detected surface type (e.g. \"billboard\", \"screen\"). Empty if unknown.</summary>
    public string SurfaceType { get; set; } = string.Empty;

    /// <summary>Frame index the mask corresponds to.</summary>
    public int FrameIndex { get; set; }

    /// <summary>Bounding box of the mask: {xMin, yMin, xMax, yMax}.</summary>
    public int BoundsXMin { get; set; }
    public int BoundsYMin { get; set; }
    public int BoundsXMax { get; set; }
    public int BoundsYMax { get; set; }
}
