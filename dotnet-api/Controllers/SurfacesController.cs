using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers
{
    [ApiController]
    [Route("api")]
    [Authorize]
    public class SurfacesController : ControllerBase
    {
        private readonly ISurfaceService _surfaceService;
        private readonly PostgresDbContext _context;
    private readonly IEventLogService _eventLog;
    private readonly ISurfaceTrackingService _trackingService;

    public SurfacesController(ISurfaceService surfaceService, PostgresDbContext context, IEventLogService eventLog, ISurfaceTrackingService trackingService)
    {
        _surfaceService = surfaceService;
        _context = context;
        _eventLog = eventLog;
        _trackingService = trackingService;
    }

        [HttpGet("scenes/{sceneId}/surfaces")]
        public async Task<ActionResult<IEnumerable<SurfaceItem>>> GetSurfaces(string sceneId)
        {
            var surfaces = await _surfaceService.GetSurfacesAsync(sceneId);
            return Ok(surfaces);
        }

        /// <summary>Fetch surfaces for multiple scenes in a single request.</summary>
        [HttpGet("scenes/surfaces/batch")]
        public async Task<ActionResult<IEnumerable<SurfaceItem>>> GetSurfacesBatch([FromQuery] string sceneIds)
        {
            if (string.IsNullOrEmpty(sceneIds))
                return Ok(Array.Empty<SurfaceItem>());

            var ids = sceneIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var surfaces = await _context.SurfaceItems
                .Where(sf => ids.Contains(sf.SceneId))
                .OrderBy(sf => sf.SceneId)
                .ThenBy(sf => sf.Id)
                .ToListAsync();

            return Ok(surfaces);
        }

        /// <summary>
        /// Preview-segment a clicked point on a video frame using SAM3 video-rle.
        /// Returns a decoded polygon for SVG overlay rendering in the placement editor.
        /// </summary>
        [HttpPost("surfaces/preview-segment")]
        public async Task<IActionResult> PreviewSegment([FromBody] SegmentPreviewRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.ContentId))
                    return BadRequest(new { error = "ContentId is required." });
                if (request.FrameIndex < 0)
                    return BadRequest(new { error = "FrameIndex must be >= 0." });
                if (request.X < 0 || request.Y < 0)
                    return BadRequest(new { error = "Coordinates must be non-negative." });

                var content = await _context.ContentItems.FindAsync(request.ContentId);
                if (content == null)
                    return NotFound(new { error = "Content not found." });

                var videoPath = ResolveVideoPath(content.StorageKey);
                if (string.IsNullOrEmpty(videoPath) || !System.IO.File.Exists(videoPath))
                    return BadRequest(new { error = "Source video file not accessible." });

                var result = await _trackingService.PreviewSegmentAsync(
                    request.ContentId, videoPath, request.FrameIndex, request.X, request.Y);

                if (result == null || result.MaskPolygon.Count == 0)
                    return Ok(new SegmentPreviewResponse
                    {
                        MaskPolygonJson = "[]",
                        Confidence = 0,
                        SurfaceType = "No distinct surface found — try 'Place Signage' mode instead."
                    });

                var response = new SegmentPreviewResponse
                {
                    MaskPolygonJson = RleDecoder.PolygonToJson(result.MaskPolygon),
                    Confidence = result.Confidence,
                    TrackId = result.TrackId,
                    SurfaceType = result.SurfaceType,
                    FrameIndex = result.FrameIndex,
                    BoundsXMin = result.Bounds.xMin,
                    BoundsYMin = result.Bounds.yMin,
                    BoundsXMax = result.Bounds.xMax,
                    BoundsYMax = result.Bounds.yMax
                };

                await _eventLog.LogEventAsync("SAM3", "PREVIEW_COMPLETE", "Info",
                    $"Preview segment: content={request.ContentId}, frame={request.FrameIndex}, " +
                    $"point=({request.X},{request.Y}), points={result.MaskPolygon.Count}, " +
                    $"confidence={result.Confidence:F2}, trackId={result.TrackId}");

                return Ok(response);
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("SAM3", "PREVIEW_ERROR", "Error",
                    $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Persist a SurfaceItem from an interactive "Insert Product" click (SAM3 preview-segment mask).
        /// Returns the real SurfaceId to use when dispatching a Generative interactive render —
        /// closes the gap where the editor previously sent surfaceId: '' expecting server-side creation.
        /// </summary>
        [HttpPost("surfaces/from-click")]
        public async Task<IActionResult> CreateFromClick([FromBody] CreateSurfaceFromClickRequest request)
        {
            try
            {
                var surface = await _surfaceService.CreateFromClickAsync(request);
                await _eventLog.LogEventAsync("Surface", "CREATED_FROM_CLICK", "Info",
                    $"Surface '{surface.Id}' created from click at frame {request.FrameIndex} (scene {surface.SceneId}).");
                return Ok(new CreateSurfaceResponse { SurfaceId = surface.Id, SceneId = surface.SceneId });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Surface", "CREATE_FROM_CLICK_ERROR", "Error",
                    $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Persist a SurfaceItem from an interactive "Place Signage" 4-corner quad.
        /// Returns the real SurfaceId to use when dispatching a Planar interactive render.
        /// </summary>
        [HttpPost("surfaces/from-quad")]
        public async Task<IActionResult> CreateFromQuad([FromBody] CreateSurfaceFromQuadRequest request)
        {
            try
            {
                var surface = await _surfaceService.CreateFromQuadAsync(request);
                await _eventLog.LogEventAsync("Surface", "CREATED_FROM_QUAD", "Info",
                    $"Surface '{surface.Id}' created from quad at frame {request.FrameIndex} (scene {surface.SceneId}).");
                return Ok(new CreateSurfaceResponse { SurfaceId = surface.Id, SceneId = surface.SceneId });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Surface", "CREATE_FROM_QUAD_ERROR", "Error",
                    $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>Delete a single surface and all its child ad slots and approvals.</summary>
        [HttpDelete("surfaces/{id}")]
        public async Task<IActionResult> DeleteSurface(string id)
        {
            var surface = await _context.SurfaceItems.FindAsync(id);
            if (surface == null)
                return NotFound(new { error = "Surface not found." });

            if (surface.Status == "Approved")
            {
                return BadRequest(new
                {
                    error = "Cannot delete surface: it is approved. Exclude or reject it before deleting."
                });
            }

            // EF Core cascade handles: SurfaceItem → AdSlots → Approvals, and RenderItem.SurfaceId → null
            _context.SurfaceItems.Remove(surface);
            await _context.SaveChangesAsync();

            await _eventLog.LogEventAsync("Surface", "DELETED", "Info", $"Surface '{id}' deleted.");
            return Ok(new { success = true, id, message = "Surface and all child entities deleted." });
        }

        /// <summary>Delete all surfaces for a scene.</summary>
        [HttpDelete("scenes/{sceneId}/surfaces")]
        public async Task<IActionResult> DeleteAllSurfaces(string sceneId)
        {
            var surfaces = await _context.SurfaceItems.Where(sf => sf.SceneId == sceneId).ToListAsync();
            if (surfaces.Count == 0)
                return Ok(new { success = true, deletedCount = 0, message = "No surfaces to delete." });

            var hasApproved = surfaces.Any(sf => sf.Status == "Approved");
            if (hasApproved)
            {
                return BadRequest(new
                {
                    error = "Cannot delete surfaces: approved surface(s) exist. " +
                            "Exclude or reject approved surfaces first."
                });
            }

            _context.SurfaceItems.RemoveRange(surfaces);
            await _context.SaveChangesAsync();

            await _eventLog.LogEventAsync("Surface", "DELETED_ALL", "Info",
                $"Deleted {surfaces.Count} surfaces for scene '{sceneId}'.");
            return Ok(new { success = true, deletedCount = surfaces.Count, message = "All surfaces deleted." });
        }

        private string? ResolveVideoPath(string storageKey)
        {
            if (string.IsNullOrEmpty(storageKey)) return null;
            var fileName = storageKey.StartsWith("/api/content/file/")
                ? storageKey["/api/content/file/".Length..]
                : storageKey;
            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
            return Path.Combine(uploadsDir, fileName);
        }

        /// <summary>
        /// Approve a surface for ad placement. If adjustedBoundaryJson is provided, the
        /// operator-adjusted boundary is saved as the new seed — actual tracking now happens
        /// per-shot inside the render job itself (ShotAwareTrackingService), not at approval time.
        /// </summary>
        [HttpPost("surfaces/{id}/approve")]
        public async Task<IActionResult> ApproveSurface(string id, [FromBody] ApprovalDto dto)
        {
            try
            {
                // If operator provided an adjusted boundary, save it before approval
                if (!string.IsNullOrEmpty(dto.AdjustedBoundaryJson))
                {
                    var surface = await _context.SurfaceItems.FindAsync(id);
                    if (surface != null)
                    {
                        surface.BoundaryCoordinatesJson = dto.AdjustedBoundaryJson;
                        _context.SaveChanges();
                    }
                }

                var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "system@afrobotics.co.za";
                var result = await _surfaceService.ApproveSurfaceAsync(id, dto, email);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                await _eventLog.LogEventAsync("Approval", "SURFACE_NOT_FOUND", "Warning", ex.Message);
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Approval", "APPROVAL_ERROR", "Error",
                    $"Surface {id}: {ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
