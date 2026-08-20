using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
        private readonly GeminiKontextPromptService _kontextPromptService;

        public RendersController(IRenderService renderService, PostgresDbContext context, IEventLogService eventLog, GeminiKontextPromptService kontextPromptService)
        {
            _renderService = renderService;
            _context = context;
            _eventLog = eventLog;
            _kontextPromptService = kontextPromptService;
        }

        [HttpGet]
        public async Task<ActionResult<PaginatedResult<RenderItemResponse>>> GetRenders([FromQuery] RenderFilterParams filter)
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

        /// <summary>
        /// Dispatch a surface-anchored render — the "Anchor &amp; Generate" flow.
        /// Anchors on a real detected surface (DetectedAtFrame + Gemini SurfaceType), composites
        /// the asset into that exact frame via FLUX.1 Kontext, then propagates across the full
        /// scene via Kling O1 Edit with the composited frame as a visual reference.
        /// </summary>
        [HttpPost("surface-anchor")]
        public async Task<IActionResult> DispatchSurfaceAnchorRender([FromBody] CreateSurfaceAnchorRenderDto dto)
        {
            try
            {
                var render = await _renderService.DispatchSurfaceAnchorRenderAsync(dto);
                return Accepted(render);
            }
            catch (ArgumentException ex)
            {
                await _eventLog.LogEventAsync("Render", "SURFACE_ANCHOR_DISPATCH_INVALID", "Warning", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Render", "SURFACE_ANCHOR_DISPATCH_ERROR", "Error",
                    $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Step 1 of the interactive Kontext→Kling workflow. Generates just the FLUX.1 Kontext
        /// composited frame (no Kling) so the user can review/redo the frame before proceeding.
        /// Returns the render in "Queued" status; poll for RenderStatus "KontextReady".
        /// </summary>
        [HttpPost("surface-anchor/kontext-frame")]
        public async Task<IActionResult> DispatchKontextFrame([FromBody] CreateKontextFrameDto dto)
        {
            try
            {
                var render = await _renderService.DispatchKontextFrameAsync(dto);
                return Accepted(render);
            }
            catch (ArgumentException ex)
            {
                await _eventLog.LogEventAsync("Render", "KONTEXT_FRAME_DISPATCH_INVALID", "Warning", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Render", "KONTEXT_FRAME_DISPATCH_ERROR", "Error",
                    $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Alternative to Step 1: the user already has a reference frame (from a prior attempt or
        /// an external tool) and wants to skip FLUX.1 Kontext generation. Stores the uploaded image
        /// as the composited frame and creates the render directly in "KontextReady" status.
        /// </summary>
        [HttpPost("surface-anchor/upload-kontext-frame")]
        [RequestSizeLimit(50_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 50_000_000)]
        public async Task<IActionResult> UploadKontextFrame(
            [FromForm] string contentId,
            [FromForm] string sceneId,
            [FromForm] string? surfaceId,
            [FromForm] string campaignId,
            [FromForm] string assetId,
            [FromForm] int frameNumber,
            [FromForm] string? promptText,
            IFormFile file)
        {
            try
            {
                var dto = new UploadKontextFrameDto
                {
                    ContentId = contentId,
                    SceneId = sceneId,
                    SurfaceId = surfaceId,
                    CampaignId = campaignId,
                    AssetId = assetId,
                    FrameNumber = frameNumber,
                    PromptText = promptText,
                };
                var render = await _renderService.UploadKontextFrameAsync(dto, file);
                return Accepted(render);
            }
            catch (ArgumentException ex)
            {
                await _eventLog.LogEventAsync("Render", "KONTEXT_FRAME_UPLOAD_INVALID", "Warning", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Render", "KONTEXT_FRAME_UPLOAD_ERROR", "Error",
                    $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Asks Gemini to rewrite a rough Kontext placement idea into a precise instruction,
        /// grounded in the actual scene frame and asset image — not just the text alone. Read-only:
        /// does not create or modify any render. The caller decides whether to use the suggestion.
        /// </summary>
        [HttpPost("suggest-kontext-prompt")]
        public async Task<IActionResult> SuggestKontextPrompt([FromBody] SuggestKontextPromptDto dto)
        {
            try
            {
                var result = await _kontextPromptService.SuggestPromptAsync(dto);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Render", "KONTEXT_PROMPT_SUGGEST_ERROR", "Error",
                    $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Step 2 of the interactive Kontext→Kling workflow. Propagates the stored Kontext
        /// composited frame through Kling O1 Edit to produce a video preview. The render must
        /// be in "KontextReady" status. An optional updated promptText can be provided.
        /// </summary>
        [HttpPost("{id}/propagate-kling")]
        public async Task<IActionResult> PropagateKling(string id, [FromBody] PropagateKlingDto dto)
        {
            try
            {
                var render = await _renderService.DispatchKlingPropagationAsync(id, dto);
                return Accepted(render);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                await _eventLog.LogEventAsync("Render", "KLING_PROPAGATION_DISPATCH_INVALID", "Warning", ex.Message);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Render", "KLING_PROPAGATION_DISPATCH_ERROR", "Error",
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

        /// <summary>Marks (or unmarks) this render as the chosen one for its scene in the content's final assembled video. At most one render per scene can be queued at a time.</summary>
        [HttpPut("{id}/queue-for-final")]
        public async Task<IActionResult> SetQueuedForFinal(string id, [FromBody] SetQueuedForFinalDto dto)
        {
            try
            {
                var render = await _renderService.SetQueuedForFinalAsync(id, dto.Queued);
                return Ok(render);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Render", "RENDER_QUEUE_ERROR", "Error", $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRender(string id)
        {
            try
            {
                await _renderService.DeleteRenderAsync(id);
                return Ok(new { success = true });
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                await _eventLog.LogEventAsync("Render", "RENDER_DELETE_ERROR", "Error", $"{ex.GetType().Name} — {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>Serves the composited output trimmed to just this render's scene (not the full video) — see RenderItem.SceneClipStorageKey.</summary>
        [HttpGet("{id}/scene-clip")]
        [AllowAnonymous] // Allow direct video player stream, matching /download and /preview
        public async Task<IActionResult> DownloadSceneClip(string id)
        {
            var render = await _context.Renders.FindAsync(id);
            if (render == null)
            {
                return NotFound(new { error = $"Render '{id}' not found." });
            }

            var localPath = Path.Combine(Directory.GetCurrentDirectory(), "renders", $"BIT_SceneClip_{id}.mp4");
            if (!System.IO.File.Exists(localPath))
            {
                return NotFound(new { error = "Scene clip not found on disk." });
            }

            var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "video/mp4", $"BIT_SceneClip_{id}.mp4", enableRangeProcessing: true);
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
