using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface IRenderService
    {
        Task<IEnumerable<RenderItem>> GetRendersAsync();
        Task<RenderItem> DispatchRenderAsync(CreateRenderDto dto);
    }

    public class RenderService : IRenderService
    {
        private readonly IRenderRepository _renderRepository;

        public RenderService(IRenderRepository renderRepository)
        {
            _renderRepository = renderRepository;
        }

        public async Task<IEnumerable<RenderItem>> GetRendersAsync()
        {
            return await _renderRepository.GetAllAsync();
        }

        public async Task<RenderItem> DispatchRenderAsync(CreateRenderDto dto)
        {
            if (string.IsNullOrEmpty(dto.ContentId) || string.IsNullOrEmpty(dto.SurfaceId) || 
                string.IsNullOrEmpty(dto.CampaignId) || string.IsNullOrEmpty(dto.AssetId))
            {
                throw new ArgumentException("Missing mandatory compositing target parameters.");
            }

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

            // Background worker dispatch emulation
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3000);
                    render.Progress = 100;
                    render.RenderStatus = "Finished";
                    render.ProcessingDurationMs = 45000;
                    await _renderRepository.UpdateAsync(render);
                    await _renderRepository.SaveChangesAsync();
                }
                catch
                {
                    // Swallowing exception inside background task for prototype resilience
                }
            });

            return render;
        }
    }
}
