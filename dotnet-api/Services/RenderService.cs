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

            // MReq 7, 14: Simulate GPU render pipeline with realistic incremental progress
            _ = Task.Run(async () =>
            {
                try
                {
                    // Phase 1: Preprocessing (0 → 30%)
                    for (int p = 5; p <= 30; p += 5)
                    {
                        await Task.Delay(400);
                        render.Progress = p;
                        await _renderRepository.UpdateAsync(render);
                        await _renderRepository.SaveChangesAsync();
                    }

                    // Phase 2: GPU Compositing (30 → 75%)
                    render.RenderStatus = "Processing";
                    await _renderRepository.UpdateAsync(render);
                    await _renderRepository.SaveChangesAsync();
                    for (int p = 35; p <= 75; p += 5)
                    {
                        await Task.Delay(350);
                        render.Progress = p;
                        await _renderRepository.UpdateAsync(render);
                        await _renderRepository.SaveChangesAsync();
                    }

                    // Phase 3: Encoding & Finalization (75 → 100%)
                    for (int p = 80; p <= 100; p += 5)
                    {
                        await Task.Delay(300);
                        render.Progress = p;
                        await _renderRepository.UpdateAsync(render);
                        await _renderRepository.SaveChangesAsync();
                    }

                    var elapsed = DateTime.UtcNow - render.CreatedAt;
                    render.Progress = 100;
                    render.RenderStatus = "Finished";
                    render.ProcessingDurationMs = (int)elapsed.TotalMilliseconds;
                    await _renderRepository.UpdateAsync(render);
                    await _renderRepository.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    try
                    {
                        render.RenderStatus = "Failed";
                        render.Progress = 0;
                        await _renderRepository.UpdateAsync(render);
                        await _renderRepository.SaveChangesAsync();
                    }
                    catch { /* final effort to mark failed */ }
                }
            });

            return render;
        }
    }
}
