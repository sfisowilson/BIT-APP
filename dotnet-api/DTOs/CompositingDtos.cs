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

    /// <summary>
    /// Request to dispatch a surface-anchored render — the "Anchor &amp; Generate" flow.
    /// Anchors placement on a real detected surface (its exact DetectedAtFrame + Gemini SurfaceType)
    /// and allows the user to add a free-text placement prompt for fine-grained control.
    /// Two-step pipeline: FLUX.1 Kontext composites the asset into the detected-at frame →
    /// Kling O1 Edit propagates the edit across the full scene using that frame as reference.
    /// Scene must fall within Kling O1's allowed duration window.
    /// </summary>
    public class CreateSurfaceAnchorRenderDto
    {
        public string ContentId { get; set; } = string.Empty;
        public string SceneId { get; set; } = string.Empty;
        public string SurfaceId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;
        public string PromptText { get; set; } = string.Empty;
        public string ExportPreset { get; set; } = "Web-Ready MP4";
    }

    /// <summary>
    /// Request to generate just the FLUX.1 Kontext composited frame (Step 1 of the interactive
    /// Kontext→Kling workflow). Extracts the video frame at the user-chosen frameNumber,
    /// composites the asset into it, and returns the frame for review — without calling Kling.
    /// The user can redo this step before proceeding to Kling propagation.
    /// </summary>
    public class CreateKontextFrameDto
    {
        public string ContentId { get; set; } = string.Empty;
        public string SceneId { get; set; } = string.Empty;
        /// <summary>Optional — when provided, the surface's SurfaceType description improves Kontext placement accuracy. When absent, placement relies on the prompt alone.</summary>
        public string? SurfaceId { get; set; }
        public string CampaignId { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;
        /// <summary>The exact frame number the user paused on to anchor the placement.</summary>
        public int FrameNumber { get; set; }
        /// <summary>Placement prompt describing where/how to place the asset on the frame.</summary>
        public string PromptText { get; set; } = string.Empty;
        public string ExportPreset { get; set; } = "Web-Ready MP4";
        /// <summary>Which image model composites the frame: "flux-kontext" (default) or "nano-banana-pro".
        /// Nano Banana Pro (Gemini 3 Pro Image) tends to integrate lighting/shadows/depth more
        /// convincingly; FLUX Kontext is comparatively stronger at identity preservation.</summary>
        public string? Provider { get; set; }
    }

    /// <summary>
    /// Alternative to CreateKontextFrameDto (Step 1): the user already has a reference frame
    /// (e.g. from a prior attempt or an external tool) and wants to skip FLUX.1 Kontext generation
    /// entirely. The uploaded image is stored directly as the render's composited frame and the
    /// render is created straight into "KontextReady" status, ready for Kling propagation.
    /// </summary>
    public class UploadKontextFrameDto
    {
        public string ContentId { get; set; } = string.Empty;
        public string SceneId { get; set; } = string.Empty;
        public string? SurfaceId { get; set; }
        public string CampaignId { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;
        /// <summary>The frame number this reference image corresponds to (for record-keeping).</summary>
        public int FrameNumber { get; set; }
        /// <summary>Optional — becomes the initial Kling propagation prompt. Falls back to a generic default.</summary>
        public string? PromptText { get; set; }
    }

    /// <summary>
    /// Request to propagate a previously-generated Kontext frame through Kling O1 Edit
    /// (Step 2 of the interactive Kontext→Kling workflow). Reads the stored KontextFrameStorageKey
    /// from the RenderItem. The user can update the prompt before re-triggering this step.
    /// </summary>
    public class PropagateKlingDto
    {
        /// <summary>Updated placement prompt. If empty, reuses the original PromptText from the render.</summary>
        public string? PromptText { get; set; }
    }

    /// <summary>
    /// Request Gemini's suggested rewrite of a rough Kontext placement prompt — grounded in the
    /// actual scene frame and asset image, not just the text. Read-only: doesn't create a render
    /// or persist anything, just returns a suggestion the user can accept or ignore.
    /// </summary>
    public class SuggestKontextPromptDto
    {
        public string ContentId { get; set; } = string.Empty;
        /// <summary>The exact frame number the user paused on, matching what Kontext will actually anchor on.</summary>
        public int FrameNumber { get; set; }
        public string AssetId { get; set; } = string.Empty;
        /// <summary>Optional — when provided, the surface's SurfaceType description is passed to Gemini too.</summary>
        public string? SurfaceId { get; set; }
        /// <summary>The user's rough, unpolished placement idea.</summary>
        public string RoughPrompt { get; set; } = string.Empty;
    }

    public class SuggestKontextPromptResponseDto
    {
        public string SuggestedPrompt { get; set; } = string.Empty;
        public string ModelUsed { get; set; } = string.Empty;
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
