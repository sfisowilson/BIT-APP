using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.Http;
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
        Task<RenderItem> DispatchSurfaceAnchorRenderAsync(CreateSurfaceAnchorRenderDto dto);
        /// <summary>Step 1 of interactive Kontext→Kling: generate the Kontext composited frame only (no Kling).</summary>
        Task<RenderItem> DispatchKontextFrameAsync(CreateKontextFrameDto dto);
        /// <summary>Alternative to step 1: use a reference frame the user already has instead of generating one with FLUX.1 Kontext.</summary>
        Task<RenderItem> UploadKontextFrameAsync(UploadKontextFrameDto dto, IFormFile file);
        /// <summary>Step 2 of interactive Kontext→Kling: propagate the stored Kontext frame through Kling O1.</summary>
        Task<RenderItem> DispatchKlingPropagationAsync(string renderId, PropagateKlingDto dto);
        Task<RenderItem> ApproveSpliceAsync(string renderId);
        Task RejectPromptRenderAsync(string renderId, string? reason);
        /// <summary>Resolves the scene a render targets — directly via SceneId (PromptEdit), or via SurfaceId → SurfaceItem.SceneId (Interactive).</summary>
        Task<string?> ResolveSceneIdAsync(RenderItem render);
        Task<RenderItem> SetQueuedForFinalAsync(string renderId, bool queued);
        Task DeleteRenderAsync(string renderId);
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
                    KontextPromptText = r.KontextPromptText,
                    PreviewStorageKey = r.PreviewStorageKey,
                    KontextFrameStorageKey = r.KontextFrameStorageKey,
                    RenderMode = r.RenderMode,
                    Progress = r.Progress,
                    ProcessingDurationMs = r.ProcessingDurationMs,
                    LastErrorMessage = r.LastErrorMessage,
                    CompositingEngine = r.CompositingEngine,
                    QualityTier = r.QualityTier,
                    CreatedAt = r.CreatedAt,
                    IsQueuedForFinal = r.IsQueuedForFinal,
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

        public async Task<RenderItem> DispatchSurfaceAnchorRenderAsync(CreateSurfaceAnchorRenderDto dto)
        {
            if (string.IsNullOrEmpty(dto.ContentId) || string.IsNullOrEmpty(dto.SceneId) ||
                string.IsNullOrEmpty(dto.SurfaceId) || string.IsNullOrEmpty(dto.CampaignId) ||
                string.IsNullOrEmpty(dto.AssetId) || string.IsNullOrWhiteSpace(dto.PromptText))
                throw new ArgumentException("Missing mandatory parameters.");

            var scene = await _context.SceneItems.FindAsync(dto.SceneId);
            if (scene == null) throw new ArgumentException("Scene not found.");

            var surface = await _context.SurfaceItems.FindAsync(dto.SurfaceId);
            if (surface == null) throw new ArgumentException("Surface not found.");

            if (surface.SceneId != dto.SceneId)
                throw new ArgumentException("Surface does not belong to the specified scene.");

            // Same duration gate as PromptEdit — Kling O1 is still the video model
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
                SurfaceId = dto.SurfaceId,
                SceneId = dto.SceneId,
                CampaignId = dto.CampaignId,
                AssetId = dto.AssetId,
                PromptText = dto.PromptText.Trim(),
                ExportPreset = dto.ExportPreset ?? "Web-Ready MP4",
                StorageKey = $"s3://afrobotics-finished-renders/render_job_{renderId}.mp4",
                RenderStatus = "Queued",
                Progress = 0,
                ProcessingDurationMs = 0,
                RenderMode = "SurfaceAnchor",
                CreatedAt = DateTime.UtcNow
            };

            await _renderRepository.AddAsync(render);
            await _renderRepository.SaveChangesAsync();

            BackgroundJob.Enqueue<RenderJobService>(s => s.ProcessSurfaceAnchorJob(render.Id, default));

            await _eventLog.LogEventAsync("RenderEngine", "SURFACE_ANCHOR_QUEUED", "Info",
                $"Surface anchor render {renderId}: surface {dto.SurfaceId} ({surface.SurfaceType}), scene {dto.SceneId}, campaign {dto.CampaignId}.");

            return render;
        }

        public async Task<RenderItem> DispatchKontextFrameAsync(CreateKontextFrameDto dto)
        {
            if (string.IsNullOrEmpty(dto.ContentId) || string.IsNullOrEmpty(dto.SceneId) ||
                string.IsNullOrEmpty(dto.CampaignId) ||
                string.IsNullOrEmpty(dto.AssetId) || string.IsNullOrWhiteSpace(dto.PromptText) ||
                dto.FrameNumber <= 0)
                throw new ArgumentException("Missing mandatory parameters (contentId, sceneId, campaignId, assetId, frameNumber, promptText).");

            var scene = await _context.SceneItems.FindAsync(dto.SceneId);
            if (scene == null) throw new ArgumentException("Scene not found.");

            // surfaceId is optional for KontextStep — the user may pause at a frame
            // without a pre-detected surface; Kontext uses the prompt description.
            SurfaceItem? surface = null;
            if (!string.IsNullOrEmpty(dto.SurfaceId))
            {
                surface = await _context.SurfaceItems.FindAsync(dto.SurfaceId);
                if (surface != null && surface.SceneId != dto.SceneId)
                    throw new ArgumentException("Surface does not belong to the specified scene.");
            }

            var renderId = "r-" + Guid.NewGuid();
            // Store frameNumber + prompt + provider as JSON in PromptText so the job can parse it
            var metaJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                frameNumber = dto.FrameNumber,
                prompt = dto.PromptText.Trim(),
                provider = string.IsNullOrWhiteSpace(dto.Provider) ? "flux-kontext" : dto.Provider,
            });

            var render = new RenderItem
            {
                Id = renderId,
                ContentId = dto.ContentId,
                SurfaceId = dto.SurfaceId,
                SceneId = dto.SceneId,
                CampaignId = dto.CampaignId,
                AssetId = dto.AssetId,
                PromptText = metaJson,
                KontextPromptText = dto.PromptText.Trim(),
                ExportPreset = dto.ExportPreset ?? "Web-Ready MP4",
                StorageKey = $"s3://afrobotics-finished-renders/render_job_{renderId}.mp4",
                RenderStatus = "Queued",
                Progress = 0,
                ProcessingDurationMs = 0,
                RenderMode = "KontextStep",
                CreatedAt = DateTime.UtcNow
            };

            await _renderRepository.AddAsync(render);
            await _renderRepository.SaveChangesAsync();

            BackgroundJob.Enqueue<RenderJobService>(s => s.ProcessKontextFrameJob(render.Id, default));

            await _eventLog.LogEventAsync("RenderEngine", "KONTEXT_FRAME_QUEUED", "Info",
                $"Kontext frame render {renderId}: surface {dto.SurfaceId ?? "(none)"} ({surface?.SurfaceType ?? "user-chosen frame"}), frame {dto.FrameNumber}, campaign {dto.CampaignId}.");

            return render;
        }

        public async Task<RenderItem> UploadKontextFrameAsync(UploadKontextFrameDto dto, IFormFile file)
        {
            if (string.IsNullOrEmpty(dto.ContentId) || string.IsNullOrEmpty(dto.SceneId) ||
                string.IsNullOrEmpty(dto.CampaignId) || string.IsNullOrEmpty(dto.AssetId))
                throw new ArgumentException("Missing mandatory parameters (contentId, sceneId, campaignId, assetId).");
            if (file == null || file.Length == 0)
                throw new ArgumentException("No reference frame file was uploaded.");

            var scene = await _context.SceneItems.FindAsync(dto.SceneId);
            if (scene == null) throw new ArgumentException("Scene not found.");

            if (!string.IsNullOrEmpty(dto.SurfaceId))
            {
                var surface = await _context.SurfaceItems.FindAsync(dto.SurfaceId);
                if (surface != null && surface.SceneId != dto.SceneId)
                    throw new ArgumentException("Surface does not belong to the specified scene.");
            }

            var renderId = "r-" + Guid.NewGuid();
            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
            var kontextDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "kontext-frames");
            Directory.CreateDirectory(kontextDir);
            var kontextFileName = $"kontext_{renderId}{ext}";
            var kontextFilePath = Path.Combine(kontextDir, kontextFileName);
            using (var stream = new FileStream(kontextFilePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var klingPrompt = string.IsNullOrWhiteSpace(dto.PromptText)
                ? "Place the product as shown in the reference frame."
                : dto.PromptText.Trim();

            var render = new RenderItem
            {
                Id = renderId,
                ContentId = dto.ContentId,
                SurfaceId = dto.SurfaceId,
                SceneId = dto.SceneId,
                CampaignId = dto.CampaignId,
                AssetId = dto.AssetId,
                PromptText = klingPrompt,
                KontextPromptText = string.IsNullOrWhiteSpace(dto.PromptText) ? null : dto.PromptText.Trim(),
                KontextFrameStorageKey = $"/api/content/file/kontext-frames/{kontextFileName}",
                ExportPreset = "Web-Ready MP4",
                StorageKey = $"s3://afrobotics-finished-renders/render_job_{renderId}.mp4",
                RenderStatus = "KontextReady",
                Progress = 50,
                ProcessingDurationMs = 0,
                RenderMode = "KontextStep",
                CreatedAt = DateTime.UtcNow
            };

            await _renderRepository.AddAsync(render);
            await _renderRepository.SaveChangesAsync();

            await _eventLog.LogEventAsync("RenderEngine", "KONTEXT_FRAME_UPLOADED", "Info",
                $"Kontext frame uploaded for render {renderId}: scene {dto.SceneId}, surface {dto.SurfaceId ?? "(none)"}, campaign {dto.CampaignId}.");

            return render;
        }

        public async Task<RenderItem> DispatchKlingPropagationAsync(string renderId, PropagateKlingDto dto)
        {
            var render = await _renderRepository.GetByIdAsync(renderId);
            if (render == null) throw new ArgumentException($"Render {renderId} not found.");

            if (string.IsNullOrEmpty(render.KontextFrameStorageKey))
                throw new InvalidOperationException("No Kontext composited frame stored on this render. Generate the Kontext frame first.");

            // KontextReady = first send; PreviewReady = "Redo Kling" after a prior successful run;
            // Failed (with a frame already stored) = retry after a later step failed — the frame
            // itself is still good, no need to regenerate it.
            if (render.RenderStatus != "KontextReady" && render.RenderStatus != "PreviewReady" && render.RenderStatus != "Failed")
                throw new InvalidOperationException(
                    $"Render {renderId} is not ready for Kling propagation (current status: {render.RenderStatus}).");

            // Update prompt if the user provided a new one
            if (!string.IsNullOrWhiteSpace(dto.PromptText))
            {
                render.PromptText = dto.PromptText.Trim();
            }

            render.RenderStatus = "Queued";
            render.Progress = 0;
            await _renderRepository.SaveChangesAsync();

            BackgroundJob.Enqueue<RenderJobService>(s => s.ProcessKlingPropagationJob(render.Id, default));

            await _eventLog.LogEventAsync("RenderEngine", "KLING_PROPAGATION_QUEUED", "Info",
                $"Kling propagation render {renderId}: scene {render.SceneId}, prompt updated={!string.IsNullOrWhiteSpace(dto.PromptText)}.");

            return render;
        }

        public async Task<RenderItem> ApproveSpliceAsync(string renderId)
        {
            var render = await _renderRepository.GetByIdAsync(renderId);
            if (render == null)
                throw new ArgumentException($"Render '{renderId}' not found.");

            // PreviewReady = normal approval. Failed (with a preview already stored) = retry after
            // a later splice attempt failed — the Kling preview itself is still good, no need to
            // regenerate it via Kling again.
            if (render.RenderStatus != "PreviewReady" &&
                !(render.RenderStatus == "Failed" && !string.IsNullOrEmpty(render.PreviewStorageKey)))
                throw new InvalidOperationException(
                    $"Render '{renderId}' is not awaiting approval (status: '{render.RenderStatus}').");

            // Atomically flip -> Processing so a rapid double-click (or duplicate request) can only
            // ever enqueue the splice job once. A plain read-then-write here left a race window:
            // two near-simultaneous approvals could both read the same eligible status and both
            // enqueue ProcessPromptSpliceJob — the second run's generic guard-clause error ("not
            // awaiting approval") would then silently overwrite the first run's real failure
            // reason, hiding the actual cause and discarding an already-generated Kling preview
            // that was otherwise fine.
            var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Renders\" SET \"RenderStatus\" = 'Processing' WHERE \"Id\" = {renderId} AND (\"RenderStatus\" = 'PreviewReady' OR (\"RenderStatus\" = 'Failed' AND \"PreviewStorageKey\" IS NOT NULL))");
            if (rowsAffected == 0)
                throw new InvalidOperationException($"Render '{renderId}' is not awaiting approval (already being processed).");
            render.RenderStatus = "Processing";

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

        public async Task<string?> ResolveSceneIdAsync(RenderItem render)
        {
            if (!string.IsNullOrEmpty(render.SceneId)) return render.SceneId;
            if (string.IsNullOrEmpty(render.SurfaceId)) return null;

            var surface = await _context.SurfaceItems.FindAsync(render.SurfaceId);
            return surface?.SceneId;
        }

        public async Task<RenderItem> SetQueuedForFinalAsync(string renderId, bool queued)
        {
            var render = await _renderRepository.GetByIdAsync(renderId);
            if (render == null)
                throw new ArgumentException($"Render '{renderId}' not found.");

            if (queued && render.RenderStatus != "Finished" && render.RenderStatus != "NeedsReview")
                throw new InvalidOperationException(
                    $"Only a Finished or NeedsReview render can be queued for final assembly (status: '{render.RenderStatus}').");
            if (queued && string.IsNullOrEmpty(render.SceneClipStorageKey))
                throw new InvalidOperationException("This render has no scene clip to splice — cannot be queued for final assembly.");

            if (queued)
            {
                var sceneId = await ResolveSceneIdAsync(render);
                if (string.IsNullOrEmpty(sceneId))
                    throw new InvalidOperationException("Could not resolve which scene this render targets.");

                // At most one queued render per scene — un-queue whichever was queued before this one.
                var previouslyQueued = await _context.Renders
                    .Where(r => r.Id != renderId && r.IsQueuedForFinal)
                    .ToListAsync();
                foreach (var other in previouslyQueued)
                {
                    if (await ResolveSceneIdAsync(other) == sceneId)
                        other.IsQueuedForFinal = false;
                }
            }

            render.IsQueuedForFinal = queued;
            await _renderRepository.SaveChangesAsync();

            await _eventLog.LogEventAsync("RenderEngine", queued ? "RENDER_QUEUED_FOR_FINAL" : "RENDER_UNQUEUED_FOR_FINAL", "Info",
                $"Render '{render.Id}' {(queued ? "queued for" : "removed from")} final assembly.");

            return render;
        }

        public async Task DeleteRenderAsync(string renderId)
        {
            var render = await _renderRepository.GetByIdAsync(renderId);
            if (render == null)
                throw new ArgumentException($"Render '{renderId}' not found.");

            var rendersDir = Path.Combine(Directory.GetCurrentDirectory(), "renders");
            foreach (var fileName in new[] { $"BIT_Render_{renderId}.mp4", $"BIT_Preview_{renderId}.mp4", $"BIT_SceneClip_{renderId}.mp4" })
            {
                var path = Path.Combine(rendersDir, fileName);
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort cleanup */ }
            }

            await _renderRepository.DeleteAsync(render);
            await _renderRepository.SaveChangesAsync();

            await _eventLog.LogEventAsync("RenderEngine", "RENDER_DELETED", "Info", $"Render '{renderId}' deleted.");
        }
    }
}
