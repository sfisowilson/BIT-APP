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
        public string Resolution { get; set; } = string.Empty; // e.g. 1920x1080 (1080p)
        
        [Required]
        public int FrameRate { get; set; } // e.g. 50 or 60
        
        [Required]
        public string SourceChannel { get; set; } = string.Empty;
        
        [Required]
        public string StorageKey { get; set; } = string.Empty; // Object storage URI
        
        [Required]
        public string IngestionStatus { get; set; } = "Staging"; // Staging, Transcoding, SceneDetecting, Completed, Failed
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
        
        // MReq 10: Assets belong to a campaign
        public string? CampaignId { get; set; }
        
        [ForeignKey(nameof(CampaignId))]
        public CampaignItem? Campaign { get; set; }
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
}
