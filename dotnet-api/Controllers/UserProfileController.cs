using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Controllers;

/// <summary>MReq 9: User-facing endpoints — any authenticated user can request roles and view their requests.</summary>
[ApiController]
[Route("api/user")]
[Authorize]
public class UserProfileController : ControllerBase
{
    private readonly PostgresDbContext _context;

    public UserProfileController(PostgresDbContext context)
    {
        _context = context;
    }

    /// <summary>Request a role elevation.</summary>
    [HttpPost("request-role")]
    public async Task<IActionResult> RequestRole([FromBody] RoleRequestDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "Unable to verify your identity." });

        var validRoles = new[] { "Admin", "Editor", "Advertiser" };
        if (!validRoles.Contains(dto.RequestedRole))
            return BadRequest(new { error = $"Invalid role. Must be one of: {string.Join(", ", validRoles)}." });

        // Check for duplicate pending request
        var existing = await _context.RoleRequests
            .AnyAsync(r => r.UserId == userId && r.RequestedRole == dto.RequestedRole && r.Status == "Pending");
        if (existing)
            return BadRequest(new { error = $"You already have a pending request for the {dto.RequestedRole} role." });

        var request = new RoleRequest
        {
            UserId = userId,
            RequestedRole = dto.RequestedRole,
            Reason = dto.Reason
        };

        _context.RoleRequests.Add(request);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = $"Role request for '{dto.RequestedRole}' submitted. An administrator will review it." });
    }

    /// <summary>Get the current user's role request history.</summary>
    [HttpGet("my-requests")]
    public async Task<IActionResult> MyRequests()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var requests = await _context.RoleRequests
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.RequestedAt)
            .Select(r => new { r.Id, r.RequestedRole, r.Reason, r.Status, r.RequestedAt, r.ReviewedAt })
            .ToListAsync();

        return Ok(requests);
    }

    /// <summary>Get the current user's muted notification types.</summary>
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var user = await _context.Users.FindAsync(userId);
        if (user == null) return NotFound();

        var muted = Array.Empty<string>();
        try { muted = JsonSerializer.Deserialize<string[]>(user.MutedNotifications) ?? Array.Empty<string>(); }
        catch { }

        return Ok(new { mutedNotifications = muted });
    }

    /// <summary>Update the current user's muted notification types.</summary>
    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] NotificationPreferencesDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var user = await _context.Users.FindAsync(userId);
        if (user == null) return NotFound();

        user.MutedNotifications = JsonSerializer.Serialize(dto.MutedNotifications ?? Array.Empty<string>());
        await _context.SaveChangesAsync();

        return Ok(new { mutedNotifications = dto.MutedNotifications });
    }
}
