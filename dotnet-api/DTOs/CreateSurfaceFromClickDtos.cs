namespace Afrobotics.Bit.Api.DTOs;

/// <summary>
/// Request to persist a SurfaceItem from an interactive "Insert Product" click
/// (SAM3 preview-segment mask). Used to obtain a real SurfaceId before dispatching
/// a Generative interactive render.
/// </summary>
public class CreateSurfaceFromClickRequest
{
    public string ContentId { get; set; } = string.Empty;

    /// <summary>0-based frame index where the user clicked. Used to resolve the owning scene.</summary>
    public int FrameIndex { get; set; }

    /// <summary>Polygon points as [{x, y}, ...] JSON — from SegmentPreviewResponse.MaskPolygonJson.</summary>
    public string MaskPolygonJson { get; set; } = "[]";

    public string SurfaceType { get; set; } = "Product Surface";
}

/// <summary>
/// Request to persist a SurfaceItem from an interactive "Place Signage" 4-corner quad.
/// Used to obtain a real SurfaceId before dispatching a Planar interactive render.
/// </summary>
public class CreateSurfaceFromQuadRequest
{
    public string ContentId { get; set; } = string.Empty;

    /// <summary>0-based frame index where the quad was drawn. Used to resolve the owning scene.</summary>
    public int FrameIndex { get; set; }

    /// <summary>4 corner points as [{x, y}, ...] JSON, in native video pixel space.</summary>
    public string QuadCornersJson { get; set; } = "[]";

    public string SurfaceType { get; set; } = "Signage Surface";
}

public class CreateSurfaceResponse
{
    public string SurfaceId { get; set; } = string.Empty;
    public string SceneId { get; set; } = string.Empty;
}
