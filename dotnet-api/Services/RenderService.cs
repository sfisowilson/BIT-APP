using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface IRenderService
    {
        Task<PaginatedResult<RenderItemResponse>> GetRendersAsync(RenderFilterParams filter);
        Task<RenderItem> DispatchInteractiveRenderAsync(CreateInteractiveRenderDto dto);
        Task<RenderItem> RetryRenderAsync(string renderId);
        Task<RenderItem> DispatchPromptPreviewRenderAsync(CreatePromptRenderDto dto);
        Task<RenderItem> ApproveSpliceAsync(string renderId);
        Task RejectPromptRenderAsync(string renderId, string? reason);
    }

    public class RenderService : IRenderService
    {
        private readonly IRenderRepository _renderRepository;
        private readonly PostgresDbContext _context;
        private readonly IEventLogService _eventLog;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;

        public RenderService(IRenderRepository renderRepository, PostgresDbContext context, IEventLogService eventLog, IEmailService email, IConfiguration config)
        {
            _renderRepository = renderRepository;
            _context = context;
            _eventLog = eventLog;
            _email = email;
            _config = config;
        }

        public async Task<PaginatedResult<RenderItemResponse>> GetRendersAsync(RenderFilterParams filter)
        {
            var query = _renderRepository.GetAllQueryable();

            if (!string.IsNullOrEmpty(filter.RenderStatus))
                query = query.Where(r => r.RenderStatus == filter.RenderStatus);
            if (!string.IsNullOrEmpty(filter.CampaignId))
                query = query.Where(r => r.CampaignId == filter.CampaignId);

            if (!string.IsNullOrEmpty(filter.SortBy))
                query = query.ApplySort(filter.SortBy, filter.SortDescending);
            else
                query = query.OrderByDescending(r => r.CreatedAt);

            var page = await query.ToPaginatedResultAsync(filter.Page, filter.PageSize);
            var items = await EnrichWithDisplayFieldsAsync(page.Items);

            return new PaginatedResult<RenderItemResponse>
            {
                Items = items,
                TotalCount = page.TotalCount,
                Page = page.Page,
                PageSize = page.PageSize,
                TotalPages = page.TotalPages,
                HasPreviousPage = page.HasPreviousPage,
                HasNextPage = page.HasNextPage,
            };
        }

        /// <summary>
        /// Joins each render to the content/scene/surface/asset it targeted, for just the page
        /// of renders being returned — not the whole table. A render's effective scene is
        /// SceneId when set directly (RenderMode "PromptEdit"), otherwise derived via
        /// SurfaceId → SurfaceItem.SceneId (Interactive renders never set SceneId themselves).
        /// </summary>
        private async Task<List<RenderItemResponse>> EnrichWithDisplayFieldsAsync(List<RenderItem> renders)
        {
            var surfaceIds = renders.Where(r => !string.IsNullOrEmpty(r.SurfaceId)).Select(r => r.SurfaceId!).Distinct().ToList();
            var surfaces = surfaceIds.Count == 0
                ? new List<SurfaceItem>()
                : await _context.SurfaceItems.Where(s => surfaceIds.Contains(s.Id)).ToListAsync();
            var surfaceById = surfaces.ToDictionary(s => s.Id);

            var sceneIds = renders
                .Select(r => r.SceneId ?? (r.SurfaceId != null && surfaceById.TryGetValue(r.SurfaceId, out var sf) ? sf.SceneId : null))
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .Distinct()
                .ToList();
            var scenes = sceneIds.Count == 0
                ? new List<SceneItem>()
                : await _context.SceneItems.Where(s => sceneIds.Contains(s.Id)).ToListAsync();
            var sceneById = scenes.ToDictionary(s => s.Id);

            var contentIds = renders.Select(r => r.ContentId).Distinct().ToList();
            var contents = await _context.ContentItems.Where(c => contentIds.Contains(c.Id)).ToListAsync();
            var contentById = contents.ToDictionary(c => c.Id);

            var assetIds = renders.Select(r => r.AssetId).Distinct().ToList();
            var assets = await _context.CreativeAssets.Where(a => assetIds.Contains(a.Id)).ToListAsync();
            var assetById = assets.ToDictionary(a => a.Id);

            return renders.Select(r =>
            {
                var resolvedSceneId = r.SceneId ?? (r.SurfaceId != null && surfaceById.TryGetValue(r.SurfaceId, out var sf) ? sf.SceneId : null);
                var scene = resolvedSceneId != null && sceneById.TryGetValue(resolvedSceneId, out var sc) ? sc : null;

                return new RenderItemResponse
                {
                    Id = r.Id,
                    ContentId = r.ContentId,
                    SurfaceId = r.SurfaceId,
                    CampaignId = r.CampaignId,
                    AssetId = r.AssetId,
                    ExportPreset = r.ExportPreset,
                    StorageKey = r.StorageKey,
                    RenderStatus = r.RenderStatus,
                    SceneId = resolvedSceneId,
                    PromptText = r.PromptText,
                    PreviewStorageKey = r.PreviewStorageKey,
                    RenderMode = r.RenderMode,
                    Progress = r.Progress,
                    ProcessingDurationMs = r.ProcessingDurationMs,
                    LastErrorMessage = r.LastErrorMessage,
                    CompositingEngine = r.CompositingEngine,
                    QualityTier = r.QualityTier,
                    CreatedAt = r.CreatedAt,
                    ContentTitle = contentById.TryGetValue(r.ContentId, out var content) ? content.Title : null,
                    SceneIndex = scene?.SceneIndex,
                    SurfaceType = r.SurfaceId != null && surfaceById.TryGetValue(r.SurfaceId, out var surface) ? surface.SurfaceType : null,
                    AssetName = assetById.TryGetValue(r.AssetId, out var asset) ? asset.Name : null,
                };
            }).ToList();
        }

        public async Task<RenderItem> DispatchInteractiveRenderAsync(CreateInteractiveRenderDto dto)
        {
            if (string.IsNullOrEmpty(dto.ContentId) || string.IsNullOrEmpty(dto.SurfaceId) ||
                string.IsNullOrEmpty(dto.CampaignId) || string.IsNullOrEmpty(dto.AssetId))
                throw new ArgumentException("Missing mandatory parameters.");

            var surface = await _context.SurfaceItems.FindAsync(dto.SurfaceId);
            if (surface == null) throw new ArgumentException("Surface not found.");

            var renderId = "r-" + Guid.NewGuid();
            var render = new RenderItem
            {
                Id = renderId,
                ContentId = dto.ContentId,
                SurfaceId = dto.SurfaceId,
                CampaignId = dto.CampaignId,
                AssetId = dto.AssetId,
                ExportPreset = dto.ExportPreset ?? "Web-Ready MP4",
                StorageKey = $"s3://afrobotics-finished-renders/render_job_{renderId}.mp4",
                RenderStatus = "Queued",
                Progress = 0,
                ProcessingDurationMs = 0,
                CompositingEngine = dto.AssetType == "Planar" ? "PlanarWarp" : "pikaswaps",
                CreatedAt = DateTime.UtcNow
            };

            await _renderRepository.AddAsync(render);
            await _renderRepository.SaveChangesAsync();

            // Route to correct Hangfire job based on AssetType
            if (dto.AssetType == "Planar")
                BackgroundJob.Enqueue<RenderJobService>(s => s.ProcessPlanarRenderJob(render.Id, default));
            else
                BackgroundJob.Enqueue<RenderJobService>(s => s.ProcessGenerativeRenderJob(render.Id, default));

            await _eventLog.LogEventAsync("RenderEngine", "INTERACTIVE_RENDER_QUEUED", "Info",
                $"Interactive render {renderId}: assetType={dto.AssetType}, surface={surface.SurfaceType}");

            return render;
        }

        public async Task<RenderItem> RetryRenderAsync(string renderId)
        {
            var render = await _renderRepository.GetByIdAsync(renderId);
            if (render == null)
                throw new ArgumentException($"Render '{renderId}' not found.");

            if (render.RenderStatus != "Failed")
                throw new InvalidOperationException(
                    $"Only Failed renders can be retried. Render '{renderId}' is currently '{render.RenderStatus}'.");

            if (render.RenderMode == "PromptEdit")
            {
                await _eventLog.LogEventAsync("RenderEngine", "RENDER_RETRY_QUEUED", "Info",
                    $"Render '{render.Id}' retry queued (PromptEdit, scene {render.SceneId}, campaign {render.CampaignId}).");

                render.RenderStatus = "Queued";
                render.Progress = 0;
                render.ProcessingDurationMs = 0;
                render.LastErrorMessage = null;
                await _renderRepository.SaveChangesAsync();

                BackgroundJob.Enqueue<RenderJobService>(s => s.ProcessPromptPreviewJob(render.Id, default));
                return render;
            }

            if (string.IsNullOrEmpty(render.SurfaceId))
                throw new InvalidOperationException(
                    "This render has no associated surface or scene and cannot be retried.");

            var surface = await _context.SurfaceItems.FindAsync(render.SurfaceId);
            if (surface == null)
                throw new InvalidOperationException(
                    "The surface for this render no longer exists. Please re-submit from the Editor tab.");

            await _eventLog.LogEventAsync("RenderEngine", "RENDER_RETRY_QUEUED", "Info",
                $"Render '{render.Id}' retry queued (surface '{surface.SurfaceType}', campaign {render.CampaignId}, engine {render.CompositingEngine}).");

            // Reset render state and re-enqueue on the same engine it was originally dispatched to
            render.RenderStatus = "Queued";
            render.Progress = 0;
            render.ProcessingDurationMs = 0;
            render.LastErrorMessage = null;
            await _renderRepository.SaveChangesAsync();

            if (render.CompositingEngine == "PlanarWarp")
                BackgroundJob.Enqueue<RenderJobService>(s => s.ProcessPlanarRenderJob(render.Id, default));
            else
                BackgroundJob.Enqueue<RenderJobService>(s => s.ProcessGenerativeRenderJob(render.Id, default));

            return render;
        }

        public async Task<RenderItem> DispatchPromptPreviewRenderAsync(CreatePromptRenderDto dto)
        {
            if (string.IsNullOrEmpty(dto.ContentId) || string.IsNullOrEmpty(dto.SceneId) ||
                string.IsNullOrEmpty(dto.CampaignId) || string.IsNullOrEmpty(dto.AssetId) ||
                string.IsNullOrWhiteSpace(dto.PromptText))
                throw new ArgumentException("Missing mandatory parameters.");

            var scene = await _context.SceneItems.FindAsync(dto.SceneId);
            if (scene == null) throw new ArgumentException("Scene not found.");

            // Authoritative duration gate — checked before any Hangfire job is enqueued, so an
            // invalid request never spends fal.ai budget. Re-checked again inside the job itself.
            if (scene.DurationSeconds < KlingPromptEditService.MinPromptEditDurationSeconds ||
                scene.DurationSeconds > KlingPromptEditService.MaxPromptEditDurationSeconds)
                throw new ArgumentException(
                    $"Scene duration {scene.DurationSeconds:F1}s is outside the allowed " +
                    $"{KlingPromptEditService.MinPromptEditDurationSeconds}-{KlingPromptEditService.MaxPromptEditDurationSeconds}s window for AI-generated placement.");

            var renderId = "r-" + Guid.NewGuid();
            var render = new RenderItem
            {
                Id = renderId,
                ContentId = dto.ContentId,
                SurfaceId = null,
                SceneId = dto.SceneId,
                CampaignId = dto.CampaignId,
                AssetId = dto.AssetId,
                PromptText = dto.PromptText.Trim(),
                ExportPreset = dto.ExportPreset ?? "Web-Ready MP4",
                StorageKey = $"s3://afrobotics-finished-renders/render_job_{renderId}.mp4",
                RenderStatus = "Queued",
                Progress = 0,
                ProcessingDurationMs = 0,
                RenderMode = "PromptEdit",
                CreatedAt = DateTime.UtcNow
            };

            await _renderRepository.AddAsync(render);
            await _renderRepository.SaveChangesAsync();

            BackgroundJob.Enqueue<RenderJobService>(s => s.ProcessPromptPreviewJob(render.Id, default));

            await _eventLog.LogEventAsync("RenderEngine", "PROMPT_PREVIEW_QUEUED", "Info",
                $"Prompt placement render {renderId}: scene {dto.SceneId}, campaign {dto.CampaignId}.");

            return render;
        }

        public async Task<RenderItem> ApproveSpliceAsync(string renderId)
        {
            var render = await _renderRepository.GetByIdAsync(renderId);
            if (render == null)
                throw new ArgumentException($"Render '{renderId}' not found.");

            if (render.RenderStatus != "PreviewReady")
                throw new InvalidOperationException(
                    $"Render '{renderId}' is not awaiting approval (status: '{render.RenderStatus}').");

            // Flip status before enqueueing so a rapid double-click can't enqueue the splice
            // job twice — the job itself re-checks this too, but this closes the race window.
            render.RenderStatus = "Processing";
            await _renderRepository.SaveChangesAsync();

            BackgroundJob.Enqueue<RenderJobService>(s => s.ProcessPromptSpliceJob(render.Id, default));

            await _eventLog.LogEventAsync("RenderEngine", "PROMPT_SPLICE_QUEUED", "Info",
                $"Render '{render.Id}' approved — splice queued.");

            return render;
        }

        public async Task RejectPromptRenderAsync(string renderId, string? reason)
        {
            var render = await _renderRepository.GetByIdAsync(renderId);
            if (render == null)
                throw new ArgumentException($"Render '{renderId}' not found.");

            if (render.RenderStatus != "PreviewReady")
                throw new InvalidOperationException(
                    $"Render '{renderId}' is not awaiting approval (status: '{render.RenderStatus}').");

            render.RenderStatus = "Rejected";
            render.LastErrorMessage = string.IsNullOrWhiteSpace(reason) ? "Rejected by user after preview." : reason;
            await _renderRepository.SaveChangesAsync();

            await _eventLog.LogEventAsync("RenderEngine", "PROMPT_REJECTED", "Info",
                $"Render '{render.Id}' rejected by user.");
        }
    }
}
