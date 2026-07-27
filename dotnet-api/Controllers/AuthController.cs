using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly PostgresDbContext _context;
        private readonly IEmailService _email;
        private readonly IConfiguration _configuration;

        public AuthController(IAuthService authService, PostgresDbContext context, IEmailService email, IConfiguration configuration)
        {
            _authService = authService;
            _context = context;
            _email = email;
            _configuration = configuration;
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

        /// <summary>Validates a stored JWT token on app mount to confirm session is still valid.</summary>
        [HttpPost("validate")]
        public IActionResult Validate([FromBody] TokenRefreshDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Token))
                return BadRequest(new { error = "Token is required." });

            try
            {
                var secret = _configuration["Jwt:Secret"]
                    ?? "AFROBOTICS_BIT_SUPER_SECRET_SECURITY_KEY_2026_JWT";
                var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(secret));

                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                handler.ValidateToken(dto.Token, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                }, out _);

                return Ok(new { valid = true });
            }
            catch
            {
                return Unauthorized(new { error = "Token is invalid or expired." });
            }
        }

        /// <summary>Sends a password reset link to the user's email.</summary>
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            // Always return success to prevent email enumeration
            if (user == null)
                return Ok(new { message = "If that email is registered, a reset link has been sent." });

            var token = Guid.NewGuid().ToString("N");
            _context.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await _context.SaveChangesAsync();

            var resetLink = $"http://localhost:3000/reset-password?token={token}";
            _email.Enqueue(user.Email, "BIT — Password Reset",
                $"Hello {user.FullName},\n\nA password reset was requested. Click below to reset:\n\n{resetLink}\n\nThis link expires in 1 hour. If you didn't request this, ignore this email.",
                "PasswordReset");

            return Ok(new { message = "If that email is registered, a reset link has been sent." });
        }

        /// <summary>Resets password using a valid token from email.</summary>
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            var resetToken = await _context.PasswordResetTokens
                .FirstOrDefaultAsync(t => t.Token == dto.Token && !t.Used && t.ExpiresAt > DateTime.UtcNow);
            if (resetToken == null)
                return BadRequest(new { error = "Invalid or expired reset link. Please request a new one." });

            var user = await _context.Users.FindAsync(resetToken.UserId);
            if (user == null)
                return BadRequest(new { error = "User account not found." });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            resetToken.Used = true;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Password reset successfully. You can now sign in." });
        }

        /// <summary>Authenticated user changes their own password.</summary>
        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return Unauthorized();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                return BadRequest(new { error = "Current password is incorrect." });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Password changed successfully." });
        }
    }
}
