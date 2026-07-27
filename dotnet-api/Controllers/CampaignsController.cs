using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        public CampaignsController(ICampaignService campaignService, IAssetService assetService)
        {
            _campaignService = campaignService;
            _assetService = assetService;
        }

        [HttpGet]
        public async Task<ActionResult<PaginatedResult<CampaignItem>>> GetCampaigns([FromQuery] CampaignFilterParams filter)
        {
            var result = await _campaignService.GetCampaignsAsync(filter);
            return Ok(result);
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
