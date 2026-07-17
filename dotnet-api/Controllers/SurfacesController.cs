using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        public SurfacesController(ISurfaceService surfaceService)
        {
            _surfaceService = surfaceService;
        }

        [HttpGet("scenes/{sceneId}/surfaces")]
        public async Task<ActionResult<IEnumerable<SurfaceItem>>> GetSurfaces(string sceneId)
        {
            var surfaces = await _surfaceService.GetSurfacesAsync(sceneId);
            return Ok(surfaces);
        }

        [HttpPost("surfaces/{id}/approve")]
        public async Task<IActionResult> ApproveSurface(string id, [FromBody] ApprovalDto dto)
        {
            try
            {
                var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "system@afrobotics.co.za";
                var result = await _surfaceService.ApproveSurfaceAsync(id, dto, email);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
