using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    /// <summary>
    /// Valid pipeline stages in order.
    /// </summary>
    public static class PipelineStages
    {
        public const string Staging = "Staging";
        public const string Transcoding = "Transcoding";
        public const string SceneDetecting = "SceneDetecting";
        public const string Completed = "Completed";
        public const string Failed = "Failed";

        /// <summary>All valid stages.</summary>
        public static readonly string[] All = { Staging, Transcoding, SceneDetecting, Completed, Failed };

        /// <summary>
        /// Valid forward transitions. Each stage can only move to the listed next stages.
        /// </summary>
        private static readonly Dictionary<string, string[]> ValidTransitions = new()
        {
            { Staging,        new[] { Transcoding, Failed } },
            { Transcoding,    new[] { SceneDetecting, Failed } },
            { SceneDetecting, new[] { Completed, Failed } },
            { Failed,         new[] { Staging } },                     // retry from beginning
            { Completed,      new[] { SceneDetecting } },              // re-detect only
        };

        /// <summary>
        /// Returns true if transitioning from currentStage to targetStage is allowed.
        /// </summary>
        public static bool IsValidTransition(string currentStage, string targetStage)
        {
            return ValidTransitions.TryGetValue(currentStage, out var allowed) &&
                   allowed.Contains(targetStage, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the list of valid next stages from the given stage.
        /// </summary>
        public static string[] GetAllowedTransitions(string currentStage)
        {
            return ValidTransitions.TryGetValue(currentStage, out var allowed)
                ? allowed
                : Array.Empty<string>();
        }
    }

    public interface IContentService
    {
        Task<PaginatedResult<ContentItem>> GetContentAsync(ContentFilterParams filter);
        Task<ContentItem> IngestVideoAsync(IngestVideoDto dto);
        Task<IEnumerable<SceneItem>> GetScenesAsync(string contentId);
        Task<bool> DeleteContentAsync(string id);
        Task<ContentItem?> GetContentByIdAsync(string id);
        Task<ContentItem> TransitionStageAsync(string contentId, string targetStage, string? errorMessage = null);
    }

    public class ContentService : IContentService
    {
        private static readonly Regex DurationRegex = new(@"^(\d{2}):([0-5]\d):([0-5]\d)$", RegexOptions.Compiled);
        private const int MinFrameRate = 1;
        private const int MaxFrameRate = 960;

        private readonly IContentRepository _contentRepository;
        private readonly IEventLogService _eventLog;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;
        private readonly PostgresDbContext _db;

        public ContentService(IContentRepository contentRepository, IEventLogService eventLog, IEmailService email, IConfiguration config, PostgresDbContext db)
        {
            _contentRepository = contentRepository;
            _eventLog = eventLog;
            _email = email;
            _config = config;
            _db = db;
        }

        public async Task<PaginatedResult<ContentItem>> GetContentAsync(ContentFilterParams filter)
        {
            var query = _contentRepository.GetAllQueryable();

            if (!string.IsNullOrEmpty(filter.IngestionStatus))
                query = query.Where(c => c.IngestionStatus == filter.IngestionStatus);
            if (!string.IsNullOrEmpty(filter.CampaignId))
                query = query.Where(c => c.CampaignId == filter.CampaignId);
            if (!string.IsNullOrEmpty(filter.Search))
                query = query.Where(c => c.Title.Contains(filter.Search));

            if (!string.IsNullOrEmpty(filter.SortBy))
                query = query.ApplySort(filter.SortBy, filter.SortDescending);
            else
                query = query.OrderByDescending(c => c.CreatedAt);

            return await query.ToPaginatedResultAsync(filter.Page, filter.PageSize);
        }

        public async Task<ContentItem> IngestVideoAsync(IngestVideoDto dto)
        {
            // MReq 1: Validate title
            if (string.IsNullOrWhiteSpace(dto.Title))
                throw new ArgumentException("Title is required.");

            // MReq 1: Validate duration as perfect HH:MM:SS
            if (!DurationRegex.IsMatch(dto.Duration))
                throw new ArgumentException("Duration must be in HH:MM:SS format (e.g. 00:05:00).");

            // MReq 1: Validate frame rate — auto-correct clearly wrong values (e.g. 3840 is a resolution, not FPS)
            var frameRate = dto.FrameRate;
            if (frameRate < MinFrameRate || frameRate > MaxFrameRate)
            {
                // If it looks like a resolution (>= 480) or impossibly high, default to 25 FPS
                if (frameRate >= 480)
                    frameRate = 25;
                else
                    throw new ArgumentException($"Frame rate must be between {MinFrameRate}–{MaxFrameRate} FPS. Received: {dto.FrameRate}.");
            }

            // Generate storage key from title if not provided
            var storageKey = !string.IsNullOrWhiteSpace(dto.StorageKey)
                ? dto.StorageKey
                : $"s3://afrobotics-raw-ingest/{dto.Title.Replace(" ", "_").ToLower()}_{DateTime.UtcNow:yyyyMMdd}.mov";

            var content = new ContentItem
            {
                Id = "v-" + Guid.NewGuid().ToString().Substring(0, 4),
                Title = dto.Title.Trim(),
                Duration = dto.Duration,
                Resolution = dto.Resolution,
                Width = dto.Width,
                Height = dto.Height,
                FrameRate = frameRate,
                SourceChannel = dto.SourceChannel,
                StorageKey = storageKey,
                IngestionStatus = "Staging",
                CampaignId = dto.CampaignId,
                CreatedAt = DateTime.UtcNow
            };

            await _contentRepository.AddAsync(content);
            await _contentRepository.SaveChangesAsync();

            return content;
        }

        public async Task<IEnumerable<SceneItem>> GetScenesAsync(string contentId)
        {
            return await _contentRepository.GetScenesByContentIdAsync(contentId);
        }

        public async Task<bool> DeleteContentAsync(string id)
        {
            var content = await _contentRepository.GetByIdAsync(id);
            if (content == null) return false;

            // ── Clean up child entities before deleting parent (belt-and-suspenders with EF cascade) ──
            var ct = CancellationToken.None;

            // Delete RenderItems for this content
            var renders = await _db.Renders
                .Where(r => r.ContentId == id)
                .ToListAsync(ct);
            if (renders.Count > 0)
                _db.Renders.RemoveRange(renders);

            // Delete scenes + their child surfaces/ad-slots/approvals
            await SceneDetectionJobService.DeleteExistingScenes(_db, id, ct);

            // Now delete the content item itself
            await _contentRepository.DeleteAsync(content);
            await _contentRepository.SaveChangesAsync();

            await _eventLog.LogEventAsync("ContentManagement", "CONTENT_DELETED",
                "Info", $"Content '{content.Title}' ({id}) deleted with all child entities.");
            return true;
        }

        public async Task<ContentItem?> GetContentByIdAsync(string id)
        {
            return await _contentRepository.GetByIdAsync(id);
        }

        /// <summary>
        /// Transition a content item to a new pipeline stage with validation and timestamp tracking.
        /// Throws InvalidOperationException if the transition is not allowed.
        /// </summary>
        public async Task<ContentItem> TransitionStageAsync(string contentId, string targetStage, string? errorMessage = null)
        {
            var content = await _contentRepository.GetByIdAsync(contentId);
            if (content == null)
                throw new ArgumentException($"Content with id '{contentId}' not found.");

            var currentStage = content.IngestionStatus;

            // Validate the transition
            if (!PipelineStages.IsValidTransition(currentStage, targetStage))
            {
                var allowed = PipelineStages.GetAllowedTransitions(currentStage);
                var allowedList = allowed.Length > 0 ? string.Join(", ", allowed) : "none";
                throw new InvalidOperationException(
                    $"Invalid pipeline transition: '{currentStage}' → '{targetStage}'. " +
                    $"Allowed transitions from '{currentStage}': {allowedList}.");
            }

            var now = DateTime.UtcNow;

            // ── Set entry/exit timestamps based on the transition ──
            switch (targetStage)
            {
                case PipelineStages.Transcoding:
                    content.StagingCompletedAt = now;
                    content.TranscodingStartedAt = now;
                    break;

                case PipelineStages.SceneDetecting:
                    content.TranscodingCompletedAt = now;
                    content.SceneDetectingStartedAt = now;
                    break;

                case PipelineStages.Completed:
                    content.SceneDetectingCompletedAt = now;
                    break;

                case PipelineStages.Failed:
                    content.LastErrorAt = now;
                    content.LastErrorMessage = errorMessage;
                    break;

                case PipelineStages.Staging:
                    // Reset all pipeline timestamps when resetting to Staging
                    content.StagingCompletedAt = null;
                    content.TranscodingStartedAt = null;
                    content.TranscodingCompletedAt = null;
                    content.SceneDetectingStartedAt = null;
                    content.SceneDetectingCompletedAt = null;
                    content.LastErrorMessage = null;
                    content.LastErrorAt = null;
                    break;
            }

            content.IngestionStatus = targetStage;
            await _contentRepository.SaveChangesAsync();

            // MReq 20: Emit event on pipeline stage transition
            var severity = targetStage == PipelineStages.Failed ? "Warning" : "Info";
            var desc = targetStage == PipelineStages.Failed
                ? $"Pipeline failed for '{content.Title}': {errorMessage ?? "Unknown error."}"
                : $"Pipeline stage transition for '{content.Title}': {currentStage} → {targetStage}";
            await _eventLog.LogEventAsync("IngestionPipeline", $"PIPELINE_{targetStage.ToUpperInvariant()}", severity, desc);

            // Notify on completion or failure
            if (targetStage == PipelineStages.Completed)
            {
                _email.Enqueue(_config["Smtp:FromEmail"] ?? "noreply@afrobotics.co.za",
                    $"Ingestion Complete — {content.Title}",
                    $"Content '{content.Title}' has completed ingestion and is ready for QA.\n\nDuration: {content.Duration}\nResolution: {content.Resolution}",
                    "IngestionCompleted");
            }
            else if (targetStage == PipelineStages.Failed)
            {
                _email.Enqueue(_config["Smtp:FromEmail"] ?? "noreply@afrobotics.co.za",
                    $"Ingestion Failed — {content.Title}",
                    $"Content '{content.Title}' ingestion failed.\n\nError: {errorMessage ?? "Unknown error."}\nStorage: {content.StorageKey}",
                    "IngestionFailed");
            }

            return content;
        }
    }
}
