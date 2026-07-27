using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers;

[ApiController]
[Route("api/placements")]
[Authorize]
public class PlacementsController : ControllerBase
{
    private readonly IAiPlacementService _placementService;

    public PlacementsController(IAiPlacementService placementService)
    {
        _placementService = placementService;
    }

    [HttpPost("suggest")]
    public async Task<IActionResult> SuggestPlacements([FromBody] AiPlacementRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
                return BadRequest(new { error = "Prompt is required." });
            if (request.Surfaces.Count == 0)
                return BadRequest(new { error = "At least one surface is required." });
            if (request.Assets.Count == 0)
                return BadRequest(new { error = "At least one asset is required." });

            var result = await _placementService.SuggestPlacementsAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
