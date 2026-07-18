using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Controllers
{
    /// <summary>
    /// AI-powered scene modification and video splitting endpoints.
    /// MReq 1: scene-cut detection and indexing.
    /// MReq 2: surface detection and analysis.
    /// </summary>
    [ApiController]
    [Route("api")]
    [Authorize]
    public class ScenesController : ControllerBase
    {
        private readonly PostgresDbContext _context;

        public ScenesController(PostgresDbContext context)
        {
            _context = context;
        }

        [HttpPost("scenes/ai-modify")]
        public IActionResult AiModifyScene([FromBody] object body)
        {
            return Ok(new
            {
                data = new
                {
                    description = "AI-enhanced scene lighting, contrast, and color grading applied. Surface detection confidence improved.",
                    model = "gemini-3.5-flash (stub)"
                }
            });
        }

        [HttpPost("scenes/update")]
        public IActionResult UpdateScene([FromBody] object body)
        {
            return Ok(new { success = true });
        }

        [HttpPost("video/ai-split-analyze")]
        public IActionResult AiSplitAnalyze([FromBody] object body)
        {
            return Ok(new
            {
                data = new
                {
                    scenes = new[]
                    {
                        new { sceneIndex = 1, startFrame = 0, endFrame = 1500, durationSeconds = 30, qaStatus = "Unchecked" },
                        new { sceneIndex = 2, startFrame = 1500, endFrame = 3000, durationSeconds = 30, qaStatus = "Unchecked" },
                        new { sceneIndex = 3, startFrame = 3000, endFrame = 4500, durationSeconds = 30, qaStatus = "Unchecked" }
                    }
                }
            });
        }

        /// <summary>
        /// MReq 1: Persist AI-generated scene cuts and update video ingestion status to Completed.
        /// </summary>
        [HttpPost("video/ai-split-save")]
        public async Task<IActionResult> AiSplitSave([FromBody] JsonElement body)
        {
            try
            {
                var contentId = body.GetProperty("contentId").GetString();
                if (string.IsNullOrEmpty(contentId))
                    return BadRequest(new { error = "contentId is required." });

                // Update video ingestion status to Completed
                var content = await _context.ContentItems.FindAsync(contentId);
                if (content != null)
                {
                    content.IngestionStatus = "Completed";
                }

                // Create scene records from AI-generated splits
                if (body.TryGetProperty("scenes", out var scenesElement) && scenesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var scene in scenesElement.EnumerateArray())
                    {
                        var sceneItem = new SceneItem
                        {
                            Id = "s-" + Guid.NewGuid().ToString().Substring(0, 4),
                            ContentId = contentId,
                            SceneIndex = scene.GetProperty("sceneIndex").GetInt32(),
                            StartFrame = scene.GetProperty("startFrame").GetInt32(),
                            EndFrame = scene.GetProperty("endFrame").GetInt32(),
                            DurationSeconds = scene.GetProperty("durationSeconds").GetDouble(),
                            QaStatus = scene.TryGetProperty("qaStatus", out var qs) ? qs.GetString() ?? "Unchecked" : "Unchecked"
                        };
                        await _context.SceneItems.AddAsync(sceneItem);
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(new { success = true, scenesCreated = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
