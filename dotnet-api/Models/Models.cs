using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Afrobotics.Bit.Api.Models
{
    public class User
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;
        
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
        
        [Required]
        public string PasswordHash { get; set; } = string.Empty;
        
        [Required]
        public string Role { get; set; } = "Editor"; // Admin, Editor, Advertiser
        
        [Required]
        public string AccountStatus { get; set; } = "Active";
        
        public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

        /// <summary>JSON array of notification types the user has muted (e.g. ["RenderCompleted","CampaignCreated"]).</summary>
        [MaxLength(1000)]
        public string MutedNotifications { get; set; } = "[]";
    }

    /// <summary>MReq 9: Tracks role elevation requests pending admin approval.</summary>
    public class RoleRequest
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string RequestedRole { get; set; } = string.Empty; // Admin, Editor, Advertiser

        [MaxLength(500)]
        public string? Reason { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected

        public string? ReviewedBy { get; set; }

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReviewedAt { get; set; }
    }

    public class ContentItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;
        
        [Required]
        public string Duration { get; set; } = string.Empty; // Perfect HH:MM:SS format (MReq 1)
        
        [Required]
        public string Resolution { get; set; } = string.Empty; // e.g. "1440x2560" — actual dimensions from ffprobe
        
        [Required]
        public int Width { get; set; }  // Actual video width in pixels
        
        [Required]
        public int Height { get; set; } // Actual video height in pixels
        
        [Required]
        public int FrameRate { get; set; } // e.g. 50 or 60
        
        [Required]
        public string SourceChannel { get; set; } = string.Empty;
        
        [Required]
        public string StorageKey { get; set; } = string.Empty; // Object storage URI
        
        [Required]
        public string IngestionStatus { get; set; } = "Staging"; // Staging, Transcoding, SceneDetecting, Completed, Failed
        
        public string? CampaignId { get; set; }  // MReq 10: video ingested for a specific campaign
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ── Pipeline stage timestamps ──
        public DateTime? StagingCompletedAt { get; set; }
        public DateTime? TranscodingStartedAt { get; set; }
        public DateTime? TranscodingCompletedAt { get; set; }
        public DateTime? SceneDetectingStartedAt { get; set; }
        public DateTime? SceneDetectingCompletedAt { get; set; }

        // ── Error tracking ──
        [MaxLength(500)]
        public string? LastErrorMessage { get; set; }
        public DateTime? LastErrorAt { get; set; }

        // ── Background job tracking ──
        public int DetectionProgress { get; set; } // 0-100, updated by Hangfire job
        [MaxLength(100)]
        public string? DetectionJobId { get; set; } // Hangfire job ID for status polling
        public bool IsDetectionPaused { get; set; } = false;
        [MaxLength(50)]
        public string? JobState { get; set; } // Enqueued, Processing, Paused, Completed, Cancelled, Failed
    }

    public class SceneItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string ContentId { get; set; } = string.Empty;
        
        [Required]
        public int StartFrame { get; set; }
        
        [Required]
        public int EndFrame { get; set; }
        
        [Required]
        public int SceneIndex { get; set; }
        
        [Required]
        public double DurationSeconds { get; set; }
        
        [Required]
        public string QaStatus { get; set; } = "Unchecked"; // Unchecked, Approved, Flagged
        
        // MReq 2: AI scene modification metadata
        public string? AiPrompt { get; set; }
        public string? AiStatus { get; set; }
        public string? AiOutputDescription { get; set; }
        public string? AiModelUsed { get; set; }

        /// <summary>Relative path to scene thumbnail JPEG (e.g. "thumbnails/scene-abc123.jpg"). Served via /api/content/file/.</summary>
        [MaxLength(500)]
        public string? ThumbnailPath { get; set; }

        /// <summary>Per-scene surface detection status: Pending | Detecting | Completed | Failed.</summary>
        [MaxLength(20)]
        public string SurfaceStatus { get; set; } = "Pending";
    }

    /// <summary>
    /// A single shot detected within a video. Shots are clustered into scenes
    /// via visual embedding similarity. SceneId is the single source of truth
    /// for shot→scene membership — query by FK to derive shot lists per scene.
    /// </summary>
    public class ShotItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string ContentId { get; set; } = string.Empty;

        /// <summary>FK to SceneItem. The single source of truth for shot→scene membership.
        /// NULL means unassigned (pending clustering).</summary>
        public string? SceneId { get; set; }

        /// <summary>0-based sequential index of this shot within the content video.</summary>
        [Required]
        public int ShotIndex { get; set; }

        [Required]
        public int StartFrame { get; set; }

        [Required]
        public int EndFrame { get; set; }

        /// <summary>Timestamp of the keyframe extracted for this shot (seconds from start).</summary>
        public double KeyframeTimestamp { get; set; }

        /// <summary>Relative path to the keyframe JPEG (served via /api/content/file/).</summary>
        [MaxLength(500)]
        public string? KeyframePath { get; set; }

        /// <summary>
        /// SAM3 image embedding for this shot's keyframe. Stored as JSON float[] — SAM3's
        /// embedding dimensionality serializes well past 20,000 characters, so this is
        /// intentionally unbounded (Postgres text) rather than a fixed varchar length.
        /// TODO: migrate to pgvector vector column for indexed similarity search.
        /// </summary>
        public string? KeyframeEmbeddingJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class SurfaceItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string SceneId { get; set; } = string.Empty;
        
        [Required]
        public string SurfaceType { get; set; } = string.Empty; // e.g. Stadium Perimeter LED Board
        
        [Required]
        public string BoundaryCoordinatesJson { get; set; } = string.Empty; // Serialized JSON coordinate array
        
        [Required]
        public double EstimatedDepth { get; set; }
        
        [Required]
        public string OrientationVectorJson { get; set; } = string.Empty; // Serialized yaw/pitch/roll
        
        [Required]
        public double ConfidenceScore { get; set; }
        
        [Required]
        public double ViabilityScore { get; set; }
        
        [Required]
        public string Status { get; set; } = "Candidate"; // Candidate, Approved, Excluded, Pending
        
        public string? ExclusionReason { get; set; }
        
        public string? PlacementImageUrl { get; set; }

        /// <summary>Frame number where this surface was detected (0-based). Used for video seek.</summary>
        public int? DetectedAtFrame { get; set; }

        /// <summary>Gemini-generated visual description optimized for SAM3 segmentation.</summary>
        [MaxLength(500)]
        public string? Sam3Prompt { get; set; }

        /// <summary>Asset category: "Generative" (3D product, uses pikaswaps) or "Planar" (flat signage, uses homography warp).</summary>
        [Required]
        [MaxLength(50)]
        public string AssetType { get; set; } = "Generative";

        /// <summary>How this surface was created: "AI" (auto-detected) or "Manual" (user click/draw).</summary>
        [Required]
        [MaxLength(50)]
        public string Source { get; set; } = "AI";

        /// <summary>
        /// Per-frame data specific to the AssetType, segmented by shot: JSON shape
        /// { shotSegments: [{ shotId, shotIndex, startFrame, endFrame, status, trackId,
        /// confidence, frames: [...] }, ...] }. Each segment's frames are — Generative:
        /// {frame, rle, trackId}; Planar: {frame, corners: [{x,y}x4]}. A segment with
        /// status "Skipped" has an empty frames array (source video passes through
        /// unmodified for that shot). Falls back to a flat frames array (no shotSegments
        /// wrapper) for surfaces created before shot-aware tracking existed.
        /// </summary>
        public string? TrackingDataJson { get; set; }

        /// <summary>
        /// Lightweight per-frame centroid derived from TrackingDataJson: a flat, frame-ordered
        /// JSON array `[{frame,x,y}, ...]` across every shot segment (quad-corner average for
        /// Planar, decoded-mask-pixel average for Generative). Lets the Placement Workbench draw
        /// a single moving point tracking the surface during scene playback without needing to
        /// understand the shot-segmented structure or decode RLE client-side. Null until a render
        /// has actually run for this surface (tracking only happens as part of a render job).
        /// </summary>
        public string? TrackingPointsJson { get; set; }

        /// <summary>
        /// Summary of shot-aware tracking coverage across the surface's scene: NotTracked
        /// (never run) | Tracked (every shot tracked/re-anchored) | PartialCoverage (some
        /// shots skipped — source video passes through for those frames) | LockLost (the
        /// seed shot itself failed, or every shot was skipped — nothing to render).
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string TrackingStatus { get; set; } = "NotTracked";
    }

    public class CampaignItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        public string NamingStructureCode { get; set; } = string.Empty; // MReq 10 Regex match validation
        
        [Required]
        public DateTime ScheduleStart { get; set; }
        
        [Required]
        public DateTime ScheduleEnd { get; set; }
        
        [Required]
        public string TargetRegion { get; set; } = string.Empty;
        
        [Required]
        public decimal TotalBudget { get; set; }
        
        [Required]
        public string Status { get; set; } = "Draft"; // Draft, Active, Completed, Paused
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CreativeAsset
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        public string Type { get; set; } = "Image"; // Image, Logo, Video
        
        [Required]
        public string StorageKey { get; set; } = string.Empty;
        
        [Required]
        public string FileSize { get; set; } = string.Empty;
        
        [Required]
        public string Dimensions { get; set; } = string.Empty;
        
        [Required]
        public string BrandCategory { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // MReq 10: Assets belong to a campaign
        public string? CampaignId { get; set; }
        
        [ForeignKey(nameof(CampaignId))]
        public CampaignItem? Campaign { get; set; }

        /// <summary>Computed thumbnail URL for uploaded asset files. Not persisted to DB.</summary>
        [NotMapped]
        [System.Text.Json.Serialization.JsonInclude]
        public string? ThumbnailUrl =>
            !string.IsNullOrEmpty(StorageKey) && StorageKey.StartsWith("/api/") ? StorageKey : null;
    }

    public class AdSlotItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string SurfaceId { get; set; } = string.Empty;
        
        [Required]
        public string MarketRegion { get; set; } = string.Empty;
        
        [Required]
        public decimal PricingValue { get; set; }
        
        [Required]
        public string SlotStatus { get; set; } = "Available"; // Available, Reserved, Rendering, Completed
        
        [Required]
        public string Dimensions { get; set; } = string.Empty;
        
        public string? CampaignId { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ApprovalItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string AdSlotId { get; set; } = string.Empty;
        
        [Required]
        public string CampaignId { get; set; } = string.Empty;
        
        [Required]
        public string ApproverUserId { get; set; } = string.Empty;
        
        [Required]
        [EmailAddress]
        public string ApproverEmail { get; set; } = string.Empty;
        
        [Required]
        public string Decision { get; set; } = "Approved"; // Approved, Rejected
        
        public string? RejectionReason { get; set; }
        
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class RenderItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string ContentId { get; set; } = string.Empty;
        
        /// <summary>Null for "PromptEdit" renders (RenderMode) — those target a SceneId directly with no detected/drawn boundary.</summary>
        public string? SurfaceId { get; set; }

        [Required]
        public string CampaignId { get; set; } = string.Empty;

        [Required]
        public string AssetId { get; set; } = string.Empty;

        [Required]
        public string ExportPreset { get; set; } = "Web-Ready MP4";

        [Required]
        public string StorageKey { get; set; } = string.Empty;

        [Required]
        public string RenderStatus { get; set; } = "Queued"; // Queued, Processing, Finished, Failed, NeedsReview, PreviewReady, Rejected

        /// <summary>Scene this render targets. Always set for "PromptEdit" renders (RenderMode); Interactive renders derive their scene via SurfaceId → SurfaceItem.SceneId instead.</summary>
        public string? SceneId { get; set; }

        /// <summary>User's free-text placement instruction for a "PromptEdit" render (RenderMode). Null for Interactive renders.</summary>
        [MaxLength(1000)]
        public string? PromptText { get; set; }

        /// <summary>Download path for the not-yet-approved AI-generated preview clip, set once ProcessPromptPreviewJob reaches RenderStatus "PreviewReady". Null until then / for Interactive renders.</summary>
        public string? PreviewStorageKey { get; set; }

        /// <summary>Null or "Interactive" (click/quad-based placement, the original two flows) vs "PromptEdit" (free-text AI video generation, no surface). Existing rows are all null/"Interactive".</summary>
        [MaxLength(20)]
        public string? RenderMode { get; set; }
        
        [Required]
        public int Progress { get; set; } = 0;
        
        [Required]
        public int ProcessingDurationMs { get; set; } = 0;

        /// <summary>Error details when render fails. Visible to Admin users for diagnostics.</summary>
        [MaxLength(2000)]
        public string? LastErrorMessage { get; set; }

        /// <summary>Exact compositing engine that produced this render: "pikaswaps", "PlanarWarp", "ffmpeg-luma", or "ffmpeg-perspective".</summary>
        [MaxLength(50)]
        public string CompositingEngine { get; set; } = string.Empty;

        /// <summary>Quality classification: "AI" (pikaswaps), "Exact" (planar warp), or "Standard" (ffmpeg fallback).</summary>
        [MaxLength(20)]
        public string QualityTier { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class EventLog
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        [Required]
        public string EventCode { get; set; } = string.Empty;
        
        [Required]
        public string Severity { get; set; } = "Info"; // Info, Warning, Major, Critical
        
        [Required]
        public string Module { get; set; } = string.Empty;
        
        [Required]
        public string User { get; set; } = "System";
        
        [Required]
        public string Description { get; set; } = string.Empty;
    }

    public class AlarmItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        [Required]
        public string Severity { get; set; } = "Minor"; // Minor, Major, Critical
        
        [Required]
        public string Source { get; set; } = string.Empty;
        
        [Required]
        public string Description { get; set; } = string.Empty;
        
        [Required]
        public bool IsActive { get; set; } = true;
    }

    /// <summary>MReq 22: Tracks every authenticated API request for usage auditing and billing.</summary>
    public class UsageRecord
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [MaxLength(100)]
        public string? UserId { get; set; }

        [MaxLength(200)]
        public string? UserEmail { get; set; }

        [Required]
        [MaxLength(500)]
        public string RequestPath { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        public string HttpMethod { get; set; } = string.Empty;

        [Required]
        public int StatusCode { get; set; }

        [Required]
        public long DurationMs { get; set; }

        [MaxLength(50)]
        public string? IpAddress { get; set; }
    }

    /// <summary>MReq 12, 15: Records every notification (email/SMS) sent by the platform.</summary>
    public class NotificationItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(200)]
        public string RecipientEmail { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = string.Empty; // RenderReady, CampaignEvent, ApprovalRequest, CampaignAssignment

        [MaxLength(300)]
        public string? Subject { get; set; }

        public string? Body { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Sent"; // Sent, Failed
    }

    /// <summary>MReq 18: Admin-configurable platform settings (key-value store).</summary>
    public class PlatformSetting
    {
        [Key]
        [MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Value { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Description { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>MReq 4: Permanent brand-safety exclusion categories. Add-only — never silently removed.</summary>
    public class BrandSafetyRule
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(200)]
        public string Category { get; set; } = string.Empty; // e.g. "Human Faces", "Religious Symbols"

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Time-limited password reset tokens sent via email.</summary>
    public class PasswordResetToken
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string Token { get; set; } = string.Empty;

        [Required]
        public DateTime ExpiresAt { get; set; }

        public bool Used { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
