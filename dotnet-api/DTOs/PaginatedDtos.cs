using System;
using System.Collections.Generic;

namespace Afrobotics.Bit.Api.DTOs
{
    // ─── Generic Paginated Response ───────────────────────────────────────

    /// <summary>Standard paginated response wrapper for all list endpoints.</summary>
    public class PaginatedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage { get; set; }
        public bool HasNextPage { get; set; }
    }

    // ─── Base Pagination Parameters ───────────────────────────────────────

    /// <summary>
    /// Base query parameters for paginated list endpoints.
    /// Default sort is descending by the entity's primary timestamp (newest first).
    /// </summary>
    public class PaginationParams
    {
        private int _page = 1;
        private int _pageSize = 20;

        /// <summary>1-based page number. Defaults to 1.</summary>
        public int Page
        {
            get => _page;
            set => _page = value < 1 ? 1 : value;
        }

        /// <summary>Items per page. Clamped to 1–100. Defaults to 20.</summary>
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value < 1 ? 1 : (value > 100 ? 100 : value);
        }

        /// <summary>Optional sort column name. Falls back to entity default timestamp when null.</summary>
        public string? SortBy { get; set; }

        /// <summary>True = descending (newest first), false = ascending. Defaults to true.</summary>
        public bool SortDescending { get; set; } = true;
    }

    // ─── Entity-Specific Filter Parameters ────────────────────────────────

    /// <summary>Filter + pagination for GET /api/content</summary>
    public class ContentFilterParams : PaginationParams
    {
        public string? IngestionStatus { get; set; }
        public string? CampaignId { get; set; }
        public string? Search { get; set; }
    }

    /// <summary>Filter + pagination for GET /api/campaigns</summary>
    public class CampaignFilterParams : PaginationParams
    {
        public string? Status { get; set; }
        public string? Search { get; set; }
    }

    /// <summary>Filter + pagination for GET /api/assets</summary>
    public class AssetFilterParams : PaginationParams
    {
        public string? Type { get; set; }
        public string? BrandCategory { get; set; }
        public string? CampaignId { get; set; }
        /// <summary>When true and CampaignId is not set, returns only assets with no campaign (CampaignId == null).</summary>
        public bool? Unassigned { get; set; }
        public string? Search { get; set; }
    }

    /// <summary>Filter + pagination for GET /api/renders</summary>
    public class RenderFilterParams : PaginationParams
    {
        public string? RenderStatus { get; set; }
        public string? CampaignId { get; set; }
    }

    /// <summary>Filter + pagination for GET /api/logs</summary>
    public class LogFilterParams : PaginationParams
    {
        public string? Severity { get; set; }
        public string? Module { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? Search { get; set; }
    }

    /// <summary>Filter + pagination for GET /api/alarms</summary>
    public class AlarmFilterParams : PaginationParams
    {
        public string? Severity { get; set; }
        public bool? IsActive { get; set; }
    }

    /// <summary>Filter + pagination for GET /api/users</summary>
    public class UserFilterParams : PaginationParams
    {
        public string? Role { get; set; }
        public string? AccountStatus { get; set; }
        public string? Search { get; set; }
    }

    // ─── Entity-Specific Response DTOs ────────────────────────────────────

    /// <summary>
    /// GET /api/renders item shape — the raw RenderItem entity plus display fields joined from
    /// ContentItem/SceneItem/SurfaceItem/CreativeAsset, so the Render Queue can show what a
    /// render actually targeted without the frontend making N follow-up requests per row.
    ///
    /// SceneId is always resolved here (unlike the raw entity, where it's only set for
    /// RenderMode "PromptEdit" — Interactive renders derive it via SurfaceId → SurfaceItem.SceneId).
    /// See RenderService.GetRendersAsync.
    /// </summary>
    public class RenderItemResponse
    {
        public string Id { get; set; } = string.Empty;
        public string ContentId { get; set; } = string.Empty;
        public string? SurfaceId { get; set; }
        public string CampaignId { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;
        public string ExportPreset { get; set; } = string.Empty;
        public string StorageKey { get; set; } = string.Empty;
        public string RenderStatus { get; set; } = string.Empty;
        public string? SceneId { get; set; }
        public string? PromptText { get; set; }
        /// <summary>The original FLUX Kontext placement instruction, preserved separately from
        /// PromptText (which "Redo Kling" overwrites with a Kling-only propagation prompt) —
        /// null for non-KontextStep renders.</summary>
        public string? KontextPromptText { get; set; }
        public string? PreviewStorageKey { get; set; }
        public string? KontextFrameStorageKey { get; set; }
        public string? RenderMode { get; set; }
        public int Progress { get; set; }
        public int ProcessingDurationMs { get; set; }
        public string? LastErrorMessage { get; set; }
        public string CompositingEngine { get; set; } = string.Empty;
        public string QualityTier { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsQueuedForFinal { get; set; }

        /// <summary>ContentItem.Title — null if the source content has since been deleted.</summary>
        public string? ContentTitle { get; set; }
        /// <summary>SceneItem.SceneIndex for the resolved SceneId — null if unresolvable.</summary>
        public int? SceneIndex { get; set; }
        /// <summary>SurfaceItem.SurfaceType — null for PromptEdit renders (no surface) or a deleted surface.</summary>
        public string? SurfaceType { get; set; }
        /// <summary>CreativeAsset.Name — null if the asset has since been deleted.</summary>
        public string? AssetName { get; set; }
    }
}
