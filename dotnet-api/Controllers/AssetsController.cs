using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
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
        private readonly IHostEnvironment _env;

        public AssetsController(IAssetService assetService, IHostEnvironment env)
        {
            _assetService = assetService;
            _env = env;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<CreativeAsset>>> GetAssets([FromQuery] string? campaignId = null)
        {
            var assets = await _assetService.GetAssetsAsync(campaignId);
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

        /// <summary>Phase 5: Upload asset image file with metadata.</summary>
        [HttpPost("upload")]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> UploadAsset(
            [FromForm] string name,
            [FromForm] string type,
            [FromForm] string brandCategory,
            [FromForm] string? campaignId,
            IFormFile? file)
        {
            try
            {
                var uploadsDir = Path.Combine(_env.ContentRootPath, "Uploads", "assets");
                Directory.CreateDirectory(uploadsDir);

                string storageKey;
                string fileSize = "0 MB";
                string dimensions = "1920x1080";

                if (file != null && file.Length > 0)
                {
                    var ext = Path.GetExtension(file.FileName);
                    var safeName = $"asset_{Guid.NewGuid():N}{ext}";
                    var filePath = Path.Combine(uploadsDir, safeName);
                    await using var stream = new FileStream(filePath, FileMode.Create);
                    await file.CopyToAsync(stream);
                    storageKey = $"/api/assets/file/{safeName}";
                    fileSize = file.Length < 1024 * 1024
                        ? $"{file.Length / 1024.0:F0} KB"
                        : $"{file.Length / (1024.0 * 1024.0):F1} MB";
                }
                else
                {
                    storageKey = $"s3://afrobotics-assets/{name.Replace(" ", "_").ToLower()}";
                }

                var dto = new CreateAssetDto
                {
                    Name = name,
                    Type = type,
                    BrandCategory = brandCategory,
                    CampaignId = campaignId
                };

                var asset = await _assetService.CreateAssetWithFileAsync(dto, storageKey, fileSize, dimensions);
                return Ok(asset);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>Phase 5: Serve uploaded asset image files.</summary>
        [HttpGet("file/{fileName}")]
        [AllowAnonymous]
        public IActionResult GetAssetFile(string fileName)
        {
            var uploadsDir = Path.Combine(_env.ContentRootPath, "Uploads", "assets");
            var filePath = Path.Combine(uploadsDir, fileName);

            var fullPath = Path.GetFullPath(filePath);
            var fullUploadsDir = Path.GetFullPath(uploadsDir);
            if (!fullPath.StartsWith(fullUploadsDir + Path.DirectorySeparatorChar)
                && fullPath != fullUploadsDir)
                return BadRequest(new { error = "Invalid file path." });

            if (!System.IO.File.Exists(filePath))
                return NotFound(new { error = "Asset file not found." });

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".bmp" => "image/bmp",
                _ => "application/octet-stream"
            };

            return PhysicalFile(filePath, contentType);
        }

        /// <summary>Update asset properties (name, type, category, campaign).</summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateAsset(string id, [FromBody] UpdateAssetDto dto)
        {
            try
            {
                var asset = await _assetService.UpdateAssetAsync(id, dto);
                if (asset == null)
                    return NotFound(new { error = "Asset not found" });
                return Ok(asset);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>Update asset with optional new file upload.</summary>
        [HttpPut("{id}/upload")]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> UpdateAssetWithFile(
            string id,
            [FromForm] string? name,
            [FromForm] string? type,
            [FromForm] string? brandCategory,
            [FromForm] string? campaignId,
            IFormFile? file)
        {
            try
            {
                var dto = new UpdateAssetDto
                {
                    Name = name,
                    Type = type,
                    BrandCategory = brandCategory,
                    CampaignId = campaignId
                };

                var asset = await _assetService.UpdateAssetAsync(id, dto);
                if (asset == null)
                    return NotFound(new { error = "Asset not found" });

                if (file != null && file.Length > 0)
                {
                    var uploadsDir = Path.Combine(_env.ContentRootPath, "Uploads", "assets");
                    Directory.CreateDirectory(uploadsDir);

                    var ext = Path.GetExtension(file.FileName);
                    var safeName = $"asset_{Guid.NewGuid():N}{ext}";
                    var filePath = Path.Combine(uploadsDir, safeName);
                    await using var stream = new FileStream(filePath, FileMode.Create);
                    await file.CopyToAsync(stream);

                    asset.StorageKey = $"/api/assets/file/{safeName}";
                    asset.FileSize = file.Length < 1024 * 1024
                        ? $"{file.Length / 1024.0:F0} KB"
                        : $"{file.Length / (1024.0 * 1024.0):F1} MB";
                    await _assetService.UpdateAssetAsync(id, new UpdateAssetDto()); // save storage key changes
                }

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
