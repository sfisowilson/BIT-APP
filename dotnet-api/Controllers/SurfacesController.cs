using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
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

    public SurfacesController(ISurfaceService surfaceService, PostgresDbContext context, IEventLogService eventLog)
    {
        _surfaceService = surfaceService;
        _context = context;
        _eventLog = eventLog;
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
        /// Approve a surface for ad placement. If adjustedBoundaryJson is provided,
        /// the operator-adjusted boundary is saved and a tracking Hangfire job is enqueued.
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

                        // Enqueue tracking job — tracks the adjusted boundary through all frames
                        var jobId = BackgroundJob.Enqueue<SurfaceTrackingJobService>(
                            s => s.TrackSurfaceAsync(id, CancellationToken.None));
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

        /// <summary>
        /// Manually trigger per-frame surface tracking for an existing surface.
        /// Enqueues a Hangfire background job and returns the job ID for polling.
        /// </summary>
        [HttpPost("surfaces/{id}/track")]
        public async Task<IActionResult> TrackSurface(string id)
        {
            var surface = await _context.SurfaceItems.FindAsync(id);
            if (surface == null)
                return NotFound(new { error = "Surface not found." });

            var jobId = BackgroundJob.Enqueue<SurfaceTrackingJobService>(
                s => s.TrackSurfaceAsync(id, CancellationToken.None));

            return Ok(new
            {
                jobId,
                surfaceId = id,
                message = $"Tracking job enqueued for surface '{surface.SurfaceType}' ({id})."
            });
        }
    }
}
