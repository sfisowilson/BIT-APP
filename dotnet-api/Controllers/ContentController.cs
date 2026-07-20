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
    [Route("api/content")]
    [Authorize]
    public class ContentController : ControllerBase
    {
        private readonly IContentService _contentService;
        private readonly IHostEnvironment _env;

        public ContentController(IContentService contentService, IHostEnvironment env)
        {
            _contentService = contentService;
            _env = env;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ContentItem>>> GetContent([FromQuery] string? campaignId = null)
        {
            var content = await _contentService.GetContentAsync(campaignId);
            return Ok(content);
        }

        /// <summary>MReq 1: Upload actual video file and register metadata.</summary>
        [HttpPost("upload")]
        [RequestSizeLimit(500_000_000)] // 500 MB
        public async Task<IActionResult> UploadVideo(
            [FromForm] string title,
            [FromForm] string resolution,
            [FromForm] int frameRate,
            [FromForm] string duration,
            [FromForm] string sourceChannel,
            [FromForm] string? campaignId,
            IFormFile? file)
        {
            try
            {
                var uploadsDir = Path.Combine(_env.ContentRootPath, "Uploads");
                Directory.CreateDirectory(uploadsDir);

                string storageKey;
                if (file != null && file.Length > 0)
                {
                    var ext = Path.GetExtension(file.FileName);
                    var safeName = $"{Guid.NewGuid():N}{ext}";
                    var filePath = Path.Combine(uploadsDir, safeName);
                    await using var stream = new FileStream(filePath, FileMode.Create);
                    await file.CopyToAsync(stream);
                    storageKey = $"/api/content/file/{safeName}";
                }
                else
                {
                    storageKey = $"s3://afrobotics-raw-ingest/{title.Replace(" ", "_").ToLower()}.mov";
                }

                var dto = new IngestVideoDto
                {
                    Title = title,
                    Resolution = resolution,
                    FrameRate = frameRate,
                    Duration = duration,
                    SourceChannel = sourceChannel,
                    StorageKey = storageKey,
                    CampaignId = campaignId
                };

                var content = await _contentService.IngestVideoAsync(dto);
                return Ok(content);
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

        /// <summary>Serve uploaded video files for playback.</summary>
        [HttpGet("file/{fileName}")]
        [AllowAnonymous]
        public IActionResult GetVideoFile(string fileName)
        {
            var uploadsDir = Path.Combine(_env.ContentRootPath, "Uploads");
            var filePath = Path.Combine(uploadsDir, fileName);

            // Prevent directory traversal
            if (!filePath.StartsWith(uploadsDir) || !System.IO.File.Exists(filePath))
                return NotFound();

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = ext switch
            {
                ".mp4" => "video/mp4",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".mxf" => "application/mxf",
                ".webm" => "video/webm",
                _ => "application/octet-stream"
            };

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, contentType, enableRangeProcessing: true);
        }

        [HttpPost]
        public async Task<IActionResult> IngestVideo([FromBody] IngestVideoDto dto)
        {
            try
            {
                var content = await _contentService.IngestVideoAsync(dto);
                return Ok(content);
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

        [HttpGet("{contentId}/scenes")]
        public async Task<ActionResult<IEnumerable<SceneItem>>> GetScenes(string contentId)
        {
            var scenes = await _contentService.GetScenesAsync(contentId);
            return Ok(scenes);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteContent(string id)
        {
            try
            {
                var deleted = await _contentService.DeleteContentAsync(id);
                if (!deleted)
                {
                    return NotFound(new { error = "Content not found" });
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
