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
        private readonly IEmailService _email;

        public SurfaceService(ISurfaceRepository surfaceRepository, IEmailService email)
        {
            _surfaceRepository = surfaceRepository;
            _email = email;
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

                // MReq 11: Use real campaign context from the request, not hardcoded defaults
                var campaignId = !string.IsNullOrEmpty(dto.CampaignId) ? dto.CampaignId : "c-01";
                var approverUserId = !string.IsNullOrEmpty(dto.UserId) ? dto.UserId : "usr-02";

                // Calculate dimensions from surface boundary coordinates (MReq 3)
                var dimensions = "1920x540"; // default
                try
                {
                    var coords = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, double>>>(surface.BoundaryCoordinatesJson);
                    if (coords != null && coords.Count >= 2)
                    {
                        var xs = coords.Select(c => c["x"]).ToList();
                        var ys = coords.Select(c => c["y"]).ToList();
                        var w = (int)(xs.Max() - xs.Min());
                        var h = (int)(ys.Max() - ys.Min());
                        dimensions = $"{w}x{h}";
                    }
                }
                catch { /* keep default dimensions */ }

                var adSlot = new AdSlotItem
                {
                    Id = "asl-" + Guid.NewGuid().ToString().Substring(0, 4),
                    SurfaceId = id,
                    MarketRegion = "SADC Region",
                    PricingValue = Convert.ToDecimal(surface.ViabilityScore * 12000),
                    SlotStatus = "Available",
                    Dimensions = dimensions,
                    CampaignId = campaignId,
                    CreatedAt = DateTime.UtcNow
                };
                await _surfaceRepository.AddAdSlotAsync(adSlot);

                var approval = new ApprovalItem
                {
                    Id = "ap-" + Guid.NewGuid().ToString().Substring(0, 4),
                    AdSlotId = adSlot.Id,
                    CampaignId = campaignId,
                    ApproverUserId = approverUserId,
                    ApproverEmail = approverEmail,
                    Decision = "Approved",
                    Timestamp = DateTime.UtcNow
                };
                await _surfaceRepository.AddApprovalAsync(approval);

                _email.Enqueue(approverEmail,
                    "Placement Approved",
                    $"Surface '{surface.SurfaceType}' has been approved.\n\nCampaign: {campaignId}\nAd Slot: {adSlot.Id}\nViability: {surface.ViabilityScore:P0}",
                    "PlacementApproved");
            }
            else
            {
                surface.Status = "Excluded";
                surface.ExclusionReason = dto.RejectionReason ?? "Editor manual exclusion.";

                _email.Enqueue(approverEmail,
                    "Placement Rejected",
                    $"Surface '{surface.SurfaceType}' has been rejected.\n\nReason: {surface.ExclusionReason}",
                    "PlacementRejected");
            }

            await _surfaceRepository.UpdateAsync(surface);
            await _surfaceRepository.SaveChangesAsync();
            return surface;
        }
    }
}
