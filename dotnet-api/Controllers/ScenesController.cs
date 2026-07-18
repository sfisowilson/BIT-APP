using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Afrobotics.Bit.Api.Controllers
{
    /// <summary>
    /// Stub controller for AI-powered scene modification and video splitting.
    /// These endpoints exist for frontend compatibility; real AI integration
    /// will replace the stub responses in a future iteration.
    /// </summary>
    [ApiController]
    [Route("api")]
    [Authorize]
    public class ScenesController : ControllerBase
    {
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

        [HttpPost("video/ai-split-save")]
        public IActionResult AiSplitSave([FromBody] object body)
        {
            return Ok(new { success = true });
        }
    }
}
