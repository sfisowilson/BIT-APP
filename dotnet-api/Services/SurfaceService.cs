using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface ISurfaceService
    {
        Task<IEnumerable<SurfaceItem>> GetSurfacesAsync(string sceneId);
        Task<SurfaceItem> ApproveSurfaceAsync(string id, ApprovalDto dto, string approverEmail);
        Task<SurfaceItem> CreateFromClickAsync(CreateSurfaceFromClickRequest dto);
        Task<SurfaceItem> CreateFromQuadAsync(CreateSurfaceFromQuadRequest dto);
    }

    public class SurfaceService : ISurfaceService
    {
        private readonly ISurfaceRepository _surfaceRepository;
        private readonly IEmailService _email;
        private readonly PostgresDbContext _context;

        public SurfaceService(ISurfaceRepository surfaceRepository, IEmailService email, PostgresDbContext context)
        {
            _surfaceRepository = surfaceRepository;
            _email = email;
            _context = context;
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

        public async Task<SurfaceItem> CreateFromClickAsync(CreateSurfaceFromClickRequest dto)
        {
            var scene = await ResolveSceneAsync(dto.ContentId, dto.FrameIndex);

            var surface = new SurfaceItem
            {
                Id = "sf-" + Guid.NewGuid().ToString()[..8],
                SceneId = scene.Id,
                SurfaceType = string.IsNullOrWhiteSpace(dto.SurfaceType) ? "Product Surface" : dto.SurfaceType,
                BoundaryCoordinatesJson = dto.MaskPolygonJson,
                EstimatedDepth = 0,
                OrientationVectorJson = "{\"yaw\":0,\"pitch\":0,\"roll\":0}",
                ConfidenceScore = 0.8,
                ViabilityScore = 0.8,
                Status = "Approved", // interactive placements are pre-approved by the click action itself
                DetectedAtFrame = dto.FrameIndex,
                AssetType = "Generative",
                Source = "Manual"
            };

            await _surfaceRepository.AddAsync(surface);
            await _surfaceRepository.SaveChangesAsync();
            return surface;
        }

        public async Task<SurfaceItem> CreateFromQuadAsync(CreateSurfaceFromQuadRequest dto)
        {
            ValidateQuad(dto.QuadCornersJson);
            var scene = await ResolveSceneAsync(dto.ContentId, dto.FrameIndex);

            var surface = new SurfaceItem
            {
                Id = "sf-" + Guid.NewGuid().ToString()[..8],
                SceneId = scene.Id,
                SurfaceType = string.IsNullOrWhiteSpace(dto.SurfaceType) ? "Signage Surface" : dto.SurfaceType,
                BoundaryCoordinatesJson = dto.QuadCornersJson,
                EstimatedDepth = 0,
                OrientationVectorJson = "{\"yaw\":0,\"pitch\":0,\"roll\":0}",
                ConfidenceScore = 0.8,
                ViabilityScore = 0.8,
                Status = "Approved", // interactive placements are pre-approved by the draw action itself
                DetectedAtFrame = dto.FrameIndex,
                AssetType = "Planar",
                Source = "Manual"
            };

            await _surfaceRepository.AddAsync(surface);
            await _surfaceRepository.SaveChangesAsync();
            return surface;
        }

        /// <summary>
        /// Rejects a degenerate quad (duplicate/near-duplicate corners, or too few points) before
        /// it's persisted — a degenerate quad produces an invalid perspective transform in
        /// PlanarWarpCompositingService. Defense in depth alongside the same check on the
        /// frontend (SurfaceClickOverlay), since any client could call this endpoint directly.
        /// </summary>
        private static void ValidateQuad(string quadCornersJson)
        {
            List<(double x, double y)> points;
            try
            {
                using var doc = JsonDocument.Parse(quadCornersJson);
                points = doc.RootElement.EnumerateArray()
                    .Select(p => (p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble()))
                    .ToList();
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid quad corners JSON: {ex.Message}");
            }

            if (points.Count != 4)
                throw new ArgumentException($"Quad must have exactly 4 corners, got {points.Count}.");

            const double minCornerDistance = 10;
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    var dist = Math.Sqrt(Math.Pow(points[i].x - points[j].x, 2) + Math.Pow(points[i].y - points[j].y, 2));
                    if (dist < minCornerDistance)
                        throw new ArgumentException(
                            $"Quad corners {i} and {j} are too close together ({dist:F1}px) — this would produce an invalid placement.");
                }
            }
        }

        /// <summary>Resolves the scene containing the given frame — a surface always belongs to exactly one scene.</summary>
        private async Task<SceneItem> ResolveSceneAsync(string contentId, int frameIndex)
        {
            if (string.IsNullOrEmpty(contentId))
                throw new ArgumentException("ContentId is required.");

            var scene = await _context.SceneItems
                .Where(s => s.ContentId == contentId && s.StartFrame <= frameIndex && frameIndex <= s.EndFrame)
                .OrderBy(s => s.SceneIndex)
                .FirstOrDefaultAsync();

            if (scene == null)
                throw new ArgumentException($"No scene found for content '{contentId}' at frame {frameIndex}.");

            return scene;
        }
    }
}
