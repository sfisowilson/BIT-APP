using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            try
            {
                var response = await _authService.LoginAsync(request);
                if (response == null)
                {
                    return Unauthorized(new { error = "Invalid credentials. Please verify your email and password." });
                }
                return Ok(response);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>MReq 8: Refresh an expiring JWT token silently.</summary>
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] TokenRefreshDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto?.Token))
                    return BadRequest(new { error = "Token is required." });

                var response = await _authService.RefreshTokenAsync(dto.Token);
                if (response == null)
                    return Unauthorized(new { error = "Token expired beyond refresh window. Please sign in again." });

                return Ok(response);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
