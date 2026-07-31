using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers
{
    [ApiController]
    [Route("api/renders")]
    [Authorize]
    public class RendersController : ControllerBase
    {
        private readonly IRenderService _renderService;
        private readonly PostgresDbContext _context;
        private readonly IEventLogService _eventLog;

        public RendersController(IRenderService renderService, PostgresDbContext context, IEventLogService eventLog)
        {
            _renderService = renderService;
            _context = context;
            _eventLog = eventLog;
        }

        [HttpGet]
        public async Task<ActionResult<PaginatedResult<RenderItem>>> GetRenders([FromQuery] RenderFilterParams filter)
        {
            var renders = await _renderService.GetRendersAsync(filter);
            return Ok(renders);
        }

        /// <summary>
        /// Dispatch an interactive placement render — routes to generative (pikaswaps) or planar (warp) path.
        /// </summary>
        [HttpPost("interactive")]
        public async Task<IActionResult> DispatchInteractiveRender([FromBody] CreateInteractiveRenderDto dto)
        {
            try
            {
                var render = await _renderService.DispatchInteractiveRenderAsync(dto);
                return Accepted(render);
            }
            catch (ArgumentException ex)
            {
                await _eventLog.LogEventAsync("Render", "INTERACTIVE_DISPATCH_INVALID", "Warning", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Render", "INTERACTIVE_DISPATCH_ERROR", "Error",
                    $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Dispatch a prompt-based AI placement preview — the "AI Placement Assistant → Generate
        /// New" flow. No pre-existing surface required; generates a preview clip the user must
        /// separately approve (see approve-splice) before it's committed into the final video.
        /// </summary>
        [HttpPost("prompt-preview")]
        public async Task<IActionResult> DispatchPromptPreview([FromBody] CreatePromptRenderDto dto)
        {
            try
            {
                var render = await _renderService.DispatchPromptPreviewRenderAsync(dto);
                return Accepted(render);
            }
            catch (ArgumentException ex)
            {
                await _eventLog.LogEventAsync("Render", "PROMPT_PREVIEW_DISPATCH_INVALID", "Warning", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Render", "PROMPT_PREVIEW_DISPATCH_ERROR", "Error",
                    $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>Approve a PreviewReady prompt-placement render — splices it into the full source video.</summary>
        [HttpPost("{id}/approve-splice")]
        public async Task<IActionResult> ApproveSplice(string id)
        {
            try
            {
                var render = await _renderService.ApproveSpliceAsync(id);
                return Ok(render);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                await _eventLog.LogEventAsync("Render", "PROMPT_SPLICE_APPROVE_INVALID", "Warning", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Render", "PROMPT_SPLICE_APPROVE_ERROR", "Error",
                    $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>Reject a PreviewReady prompt-placement render — no splice, no final video produced.</summary>
        [HttpPost("{id}/reject-prompt")]
        public async Task<IActionResult> RejectPrompt(string id, [FromBody] RejectPromptRenderDto? dto)
        {
            try
            {
                await _renderService.RejectPromptRenderAsync(id, dto?.Reason);
                return Ok(new { success = true });
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                await _eventLog.LogEventAsync("Render", "PROMPT_REJECT_INVALID", "Warning", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Render", "PROMPT_REJECT_ERROR", "Error",
                    $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>Retry a failed render.</summary>
        [HttpPost("{id}/retry")]
        public async Task<IActionResult> RetryRender(string id)
        {
            try
            {
                var render = await _renderService.RetryRenderAsync(id);
                return Ok(render);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                await _eventLog.LogEventAsync("Render", "RENDER_RETRY_INVALID", "Warning", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Render", "RENDER_RETRY_ERROR", "Error",
                    $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{id}/status")]
        public async Task<IActionResult> GetStatus(string id)
        {
            var render = await _context.Renders.FindAsync(id);
            if (render == null)
            {
                return NotFound(new { error = $"Render '{id}' not found." });
            }

            return Ok(new
            {
                render.Id,
                render.RenderStatus,
                render.Progress,
                render.ProcessingDurationMs,
                render.StorageKey,
                render.CreatedAt
            });
        }

        [HttpGet("{id}/download")]
        [AllowAnonymous] // Allow direct video download / player stream
        public async Task<IActionResult> DownloadRender(string id)
        {
            var render = await _context.Renders.FindAsync(id);
            if (render == null)
            {
                return NotFound(new { error = $"Render '{id}' not found." });
            }

            // Check if local rendered video exists
            var localPath = Path.Combine(Directory.GetCurrentDirectory(), "renders", $"BIT_Render_{id}.mp4");
            if (!System.IO.File.Exists(localPath))
            {
                // Fallback sample video if render job was mock/simulated
                localPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "v-01.mp4");
            }

            if (!System.IO.File.Exists(localPath))
            {
                return NotFound(new { error = "Render file not found on disk." });
            }

            var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "video/mp4", $"BIT_Export_{id}.mp4", enableRangeProcessing: true);
        }

        /// <summary>Serves the not-yet-approved Kling preview clip for the video preview player.</summary>
        [HttpGet("{id}/preview")]
        [AllowAnonymous] // Allow direct video player stream, matching /download
        public async Task<IActionResult> DownloadPreview(string id)
        {
            var render = await _context.Renders.FindAsync(id);
            if (render == null)
            {
                return NotFound(new { error = $"Render '{id}' not found." });
            }

            var localPath = Path.Combine(Directory.GetCurrentDirectory(), "renders", $"BIT_Preview_{id}.mp4");
            if (!System.IO.File.Exists(localPath))
            {
                return NotFound(new { error = "Preview file not found on disk." });
            }

            var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "video/mp4", $"BIT_Preview_{id}.mp4", enableRangeProcessing: true);
        }
    }
}
