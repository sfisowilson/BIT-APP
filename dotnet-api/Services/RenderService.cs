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
        Task<PaginatedResult<RenderItem>> GetRendersAsync(RenderFilterParams filter);
        Task<RenderItem> DispatchRenderAsync(CreateRenderDto dto);
        Task<RenderItem> RetryRenderAsync(string renderId);
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

        public async Task<PaginatedResult<RenderItem>> GetRendersAsync(RenderFilterParams filter)
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

            return await query.ToPaginatedResultAsync(filter.Page, filter.PageSize);
        }

        public async Task<RenderItem> DispatchRenderAsync(CreateRenderDto dto)
        {
            if (string.IsNullOrEmpty(dto.ContentId) || string.IsNullOrEmpty(dto.SurfaceId) || 
                string.IsNullOrEmpty(dto.CampaignId) || string.IsNullOrEmpty(dto.AssetId))
            {
                throw new ArgumentException("Missing mandatory compositing target parameters.");
            }

            // MReq 11: Enforce approval gate — render only approved placements
            var surface = await _context.SurfaceItems.FindAsync(dto.SurfaceId);
            if (surface == null)
                throw new ArgumentException("Surface not found.");
            if (surface.Status != "Approved")
                throw new InvalidOperationException(
                    $"Placement not approved. Surface '{surface.SurfaceType}' is '{surface.Status}'. " +
                    "Only Approved surfaces can be rendered.");

            await _eventLog.LogEventAsync("RenderEngine", "RENDER_QUEUED", "Info",
                $"Render queued for surface '{surface.SurfaceType}' (campaign {dto.CampaignId}).");

            var renderId = "r-" + Guid.NewGuid().ToString().Substring(0, 4);
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
                CreatedAt = DateTime.UtcNow
            };

            await _renderRepository.AddAsync(render);
            await _renderRepository.SaveChangesAsync();

            // Enqueue render processing as a Hangfire background job (survives restarts, retries on failure)
            BackgroundJob.Enqueue<RenderJobService>(s => s.ProcessRenderJob(render.Id, default));

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

            // Re-validate the surface is still approved
            var surface = await _context.SurfaceItems.FindAsync(render.SurfaceId);
            if (surface == null)
                throw new InvalidOperationException(
                    "The surface for this render no longer exists. Please re-submit from the Editor tab.");
            if (surface.Status != "Approved")
                throw new InvalidOperationException(
                    $"Surface '{surface.SurfaceType}' is no longer Approved (current status: '{surface.Status}'). " +
                    "Please re-approve the surface before retrying.");

            await _eventLog.LogEventAsync("RenderEngine", "RENDER_RETRY_QUEUED", "Info",
                $"Render '{render.Id}' retry queued (surface '{surface.SurfaceType}', campaign {render.CampaignId}).");

            // Reset render state and re-enqueue
            render.RenderStatus = "Queued";
            render.Progress = 0;
            render.ProcessingDurationMs = 0;
            render.LastErrorMessage = null;
            await _renderRepository.SaveChangesAsync();

            BackgroundJob.Enqueue<RenderJobService>(s => s.ProcessRenderJob(render.Id, default));

            return render;
        }
    }
}
