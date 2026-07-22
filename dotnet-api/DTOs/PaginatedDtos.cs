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
}
