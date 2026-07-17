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
    [Route("api/content")]
    [Authorize]
    public class ContentController : ControllerBase
    {
        private readonly IContentService _contentService;

        public ContentController(IContentService contentService)
        {
            _contentService = contentService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ContentItem>>> GetContent()
        {
            var content = await _contentService.GetContentAsync();
            return Ok(content);
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
    }
}
