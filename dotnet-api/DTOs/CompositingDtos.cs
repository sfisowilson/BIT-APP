namespace Afrobotics.Bit.Api.DTOs
{
    public class CompositingRequest
    {
        public string SurfaceId { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;
        public string ContentId { get; set; } = string.Empty;
        public int FrameNumber { get; set; } = 0;
        public string BoundaryCoordinatesJson { get; set; } = "[]";
    }

    public class CompositedFrame
    {
        public string ImageBase64 { get; set; } = string.Empty;
        public string ContentType { get; set; } = "image/png";
        public string EngineUsed { get; set; } = "BasicCompositor";
        public long ProcessingMs { get; set; }
    }

    /// <summary>
    /// Request to dispatch an interactive placement render.
    /// Routes to generative (pikaswaps) or planar (homography warp) based on AssetType.
    /// </summary>
    public class CreateInteractiveRenderDto
    {
        public string ContentId { get; set; } = string.Empty;
        public string SurfaceId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;
        public string AssetType { get; set; } = "Generative"; // "Generative" or "Planar"
        public string ExportPreset { get; set; } = "Web-Ready MP4";
    }
}
