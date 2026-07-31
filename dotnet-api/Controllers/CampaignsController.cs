using System;
using System.Collections.Generic;
using System.Linq;
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
    [Route("api/campaigns")]
    [Authorize]
    public class CampaignsController : ControllerBase
    {
        private readonly ICampaignService _campaignService;
        private readonly IAssetService _assetService;
        private readonly PostgresDbContext _context;

        public CampaignsController(ICampaignService campaignService, IAssetService assetService, PostgresDbContext context)
        {
            _campaignService = campaignService;
            _assetService = assetService;
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<PaginatedResult<CampaignItem>>> GetCampaigns([FromQuery] CampaignFilterParams filter)
        {
            var result = await _campaignService.GetCampaignsAsync(filter);
            return Ok(result);
        }

        /// <summary>
        /// Lightweight per-campaign summary for pipeline-status indicators (e.g. the Campaign
        /// Dashboard's "Placements" step) that need to know "has anything actually been approved
        /// yet" without the full content/scene/surface fetch the Placement Workbench does.
        /// </summary>
        [HttpGet("{id}/summary")]
        public async Task<IActionResult> GetCampaignSummary(string id)
        {
            var hasApprovedPlacements = await (
                from surface in _context.SurfaceItems
                join scene in _context.SceneItems on surface.SceneId equals scene.Id
                join content in _context.ContentItems on scene.ContentId equals content.Id
                where content.CampaignId == id && surface.Status == "Approved"
                select surface.Id
            ).AnyAsync();

            return Ok(new { hasApprovedPlacements });
        }

        [HttpGet("{id}/assets")]
        public async Task<ActionResult> GetCampaignAssets(string id)
        {
            var campaign = await _campaignService.GetCampaignByIdAsync(id);
            if (campaign == null)
                return NotFound(new { error = "Campaign not found" });

            var assets = await _assetService.GetAssetsByCampaignAsync(id);
            return Ok(new { campaign, assets });
        }

        [HttpPost]
        public async Task<IActionResult> CreateCampaign([FromBody] CreateCampaignDto dto)
        {
            try
            {
                var campaign = await _campaignService.CreateCampaignAsync(dto);
                return Ok(campaign);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCampaign(string id, [FromBody] UpdateCampaignDto dto)
        {
            try
            {
                var campaign = await _campaignService.UpdateCampaignAsync(id, dto);
                if (campaign == null)
                {
                    return NotFound(new { error = "Campaign not found" });
                }
                return Ok(campaign);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCampaign(string id)
        {
            try
            {
                var deleted = await _campaignService.DeleteCampaignAsync(id);
                if (!deleted)
                {
                    return NotFound(new { error = "Campaign not found" });
                }
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
