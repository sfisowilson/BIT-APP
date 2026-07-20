using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers
{
    [ApiController]
    [Route("api/compositing")]
    [Authorize]
    public class CompositingController : ControllerBase
    {
        private readonly ICompositingService _compositingService;

        public CompositingController(ICompositingService compositingService)
        {
            _compositingService = compositingService;
        }

        [HttpPost("preview")]
        public async Task<IActionResult> PreviewComposite([FromBody] CompositingRequest request)
        {
            try
            {
                var result = await _compositingService.CompositeAsync(request);
                return Ok(result);
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
    }
}
