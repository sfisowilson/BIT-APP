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
    [Route("api/assets")]
    [Authorize]
    public class AssetsController : ControllerBase
    {
        private readonly IAssetService _assetService;

        public AssetsController(IAssetService assetService)
        {
            _assetService = assetService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<CreativeAsset>>> GetAssets()
        {
            var assets = await _assetService.GetAssetsAsync();
            return Ok(assets);
        }

        [HttpGet("campaign/{campaignId}")]
        public async Task<ActionResult<IEnumerable<CreativeAsset>>> GetAssetsByCampaign(string campaignId)
        {
            var assets = await _assetService.GetAssetsByCampaignAsync(campaignId);
            return Ok(assets);
        }

        [HttpPost]
        public async Task<IActionResult> CreateAsset([FromBody] CreateAssetDto dto)
        {
            try
            {
                var asset = await _assetService.CreateAssetAsync(dto);
                return Ok(asset);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("{assetId}/campaign/{campaignId}")]
        public async Task<IActionResult> AssociateAssetWithCampaign(string assetId, string campaignId)
        {
            try
            {
                var success = await _assetService.AssociateAssetWithCampaignAsync(assetId, campaignId);
                if (!success)
                    return NotFound(new { error = "Asset or Campaign not found" });
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("{assetId}/unassociate")]
        public async Task<IActionResult> UnassociateAsset(string assetId)
        {
            try
            {
                var success = await _assetService.UnassociateAssetAsync(assetId);
                if (!success)
                    return NotFound(new { error = "Asset not found" });
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsset(string id)
        {
            try
            {
                var deleted = await _assetService.DeleteAssetAsync(id);
                if (!deleted)
                {
                    return NotFound(new { error = "Asset not found" });
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
