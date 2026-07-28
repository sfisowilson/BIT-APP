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

        /// <summary>
        /// Per-frame tracking data: JSON array of [{frame, boundary: [{x,y},...], driftConfidence}, ...].
        /// Populated by the surface tracking engine (SAM 3 video mode) after operator boundary adjustment.
        /// </summary>
        [MaxLength(100000)]
        public string? TrackedBoundariesJson { get; set; }

        /// <summary>Gemini-generated visual description optimized for SAM3 segmentation.</summary>
        [MaxLength(500)]
        public string? Sam3Prompt { get; set; }
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
        
        [Required]
        public string SurfaceId { get; set; } = string.Empty;
        
        [Required]
        public string CampaignId { get; set; } = string.Empty;
        
        [Required]
        public string AssetId { get; set; } = string.Empty;
        
        [Required]
        public string ExportPreset { get; set; } = "Web-Ready MP4";
        
        [Required]
        public string StorageKey { get; set; } = string.Empty;
        
        [Required]
        public string RenderStatus { get; set; } = "Queued"; // Queued, Processing, Finished, Failed
        
        [Required]
        public int Progress { get; set; } = 0;
        
        [Required]
        public int ProcessingDurationMs { get; set; } = 0;

        /// <summary>Error details when render fails. Visible to Admin users for diagnostics.</summary>
        [MaxLength(2000)]
        public string? LastErrorMessage { get; set; }

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
