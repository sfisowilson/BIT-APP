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
    [Route("api/renders")]
    [Authorize]
    public class RendersController : ControllerBase
    {
        private readonly IRenderService _renderService;

        public RendersController(IRenderService renderService)
        {
            _renderService = renderService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<RenderItem>>> GetRenders()
        {
            var renders = await _renderService.GetRendersAsync();
            return Ok(renders);
        }

        [HttpPost]
        public async Task<IActionResult> DispatchRender([FromBody] CreateRenderDto dto)
        {
            try
            {
                var render = await _renderService.DispatchRenderAsync(dto);
                return Accepted(render);
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
