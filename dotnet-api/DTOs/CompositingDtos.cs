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

    /// <summary>
    /// Request to dispatch a prompt-based AI placement preview — the "AI Placement Assistant →
    /// Generate New" flow. No pre-existing SurfaceItem; the AI model infers placement purely
    /// from PromptText plus the asset image. Scene must fall within Kling O1's allowed duration
    /// window (KlingPromptEditService.MinPromptEditDurationSeconds/MaxPromptEditDurationSeconds).
    /// </summary>
    public class CreatePromptRenderDto
    {
        public string ContentId { get; set; } = string.Empty;
        public string SceneId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;
        public string PromptText { get; set; } = string.Empty;
        public string ExportPreset { get; set; } = "Web-Ready MP4";
    }

    public class RejectPromptRenderDto
    {
        public string? Reason { get; set; }
    }

    public class SetQueuedForFinalDto
    {
        public bool Queued { get; set; }
    }
}
