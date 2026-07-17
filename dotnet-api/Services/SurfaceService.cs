using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface ISurfaceService
    {
        Task<IEnumerable<SurfaceItem>> GetSurfacesAsync(string sceneId);
        Task<SurfaceItem> ApproveSurfaceAsync(string id, ApprovalDto dto, string approverEmail);
    }

    public class SurfaceService : ISurfaceService
    {
        private readonly ISurfaceRepository _surfaceRepository;

        public SurfaceService(ISurfaceRepository surfaceRepository)
        {
            _surfaceRepository = surfaceRepository;
        }

        public async Task<IEnumerable<SurfaceItem>> GetSurfacesAsync(string sceneId)
        {
            return await _surfaceRepository.GetSurfacesBySceneIdAsync(sceneId);
        }

        public async Task<SurfaceItem> ApproveSurfaceAsync(string id, ApprovalDto dto, string approverEmail)
        {
            var surface = await _surfaceRepository.GetByIdAsync(id);
            if (surface == null)
            {
                throw new KeyNotFoundException("Surface not found.");
            }

            if (dto.Decision == "Approved")
            {
                surface.Status = "Approved";

                // Provision an available Ad Slot
                var adSlot = new AdSlotItem
                {
                    Id = "asl-" + Guid.NewGuid().ToString().Substring(0, 4),
                    SurfaceId = id,
                    MarketRegion = "SADC Region",
                    PricingValue = Convert.ToDecimal(surface.ViabilityScore * 12000),
                    SlotStatus = "Available",
                    Dimensions = "1920x540",
                    CreatedAt = DateTime.UtcNow
                };
                await _surfaceRepository.AddAdSlotAsync(adSlot);

                // Add Double-Pass Human-In-The-Loop Approval record
                var approval = new ApprovalItem
                {
                    Id = "ap-" + Guid.NewGuid().ToString().Substring(0, 4),
                    AdSlotId = adSlot.Id,
                    CampaignId = "c-01", // Default active campaign
                    ApproverUserId = "usr-02",
                    ApproverEmail = approverEmail,
                    Decision = "Approved",
                    Timestamp = DateTime.UtcNow
                };
                await _surfaceRepository.AddApprovalAsync(approval);
            }
            else
            {
                surface.Status = "Excluded";
                surface.ExclusionReason = dto.RejectionReason ?? "Editor manual exclusion.";
            }

            await _surfaceRepository.UpdateAsync(surface);
            await _surfaceRepository.SaveChangesAsync();
            return surface;
        }
    }
}
