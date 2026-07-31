using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;

namespace Afrobotics.Bit.Api.Controllers;

/// <summary>Returns counts of items needing attention across the platform.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class AttentionController : ControllerBase
{
    private readonly PostgresDbContext _context;

    public AttentionController(PostgresDbContext context)
    {
        _context = context;
    }

    private static readonly HashSet<string> DismissibleCategories = new()
    {
        "pendingSurfaces", "failedRenders", "failedContent"
    };

    [HttpGet("attention")]
    public async Task<IActionResult> GetAttentionCounts()
    {
        var dismissals = await GetDismissalsAsync();
        return Ok(await ComputeCountsAsync(dismissals));
    }

    /// <summary>Dismisses the current backlog for one category — items created after this
    /// moment still count going forward, so genuinely new items surface again.</summary>
    [HttpPost("attention/dismiss")]
    public async Task<IActionResult> DismissCategory([FromBody] DismissAttentionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Category) || !DismissibleCategories.Contains(dto.Category))
        {
            return BadRequest(new { error = $"Category must be one of: {string.Join(", ", DismissibleCategories)}" });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return Unauthorized();

        var dismissals = DeserializeDismissals(user.AttentionDismissals);
        dismissals[dto.Category] = DateTime.UtcNow;
        user.AttentionDismissals = JsonSerializer.Serialize(dismissals);
        await _context.SaveChangesAsync();

        return Ok(await ComputeCountsAsync(dismissals));
    }

    private async Task<Dictionary<string, DateTime>> GetDismissalsAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId != null ? await _context.Users.FindAsync(userId) : null;
        return DeserializeDismissals(user?.AttentionDismissals);
    }

    private static Dictionary<string, DateTime> DeserializeDismissals(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, DateTime>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json) ?? new Dictionary<string, DateTime>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, DateTime>();
        }
    }

    private async Task<object> ComputeCountsAsync(Dictionary<string, DateTime> dismissals)
    {
        var isAdmin = User.IsInRole("Admin");

        // With database indexes, each COUNT is sub-millisecond — sequential is fine
        var pendingRoleRequests = isAdmin
            ? await _context.RoleRequests.CountAsync(r => r.Status == "Pending")
            : 0;

        var pendingSurfaces = dismissals.TryGetValue("pendingSurfaces", out var pSince)
            ? await _context.SurfaceItems.CountAsync(s => s.Status == "Candidate" && s.CreatedAt > pSince)
            : await _context.SurfaceItems.CountAsync(s => s.Status == "Candidate");

        var failedRenders = dismissals.TryGetValue("failedRenders", out var rSince)
            ? await _context.Renders.CountAsync(r => r.RenderStatus == "Failed" && r.CreatedAt > rSince)
            : await _context.Renders.CountAsync(r => r.RenderStatus == "Failed");

        var failedContent = dismissals.TryGetValue("failedContent", out var cSince)
            ? await _context.ContentItems.CountAsync(c => c.IngestionStatus == "Failed" && c.CreatedAt > cSince)
            : await _context.ContentItems.CountAsync(c => c.IngestionStatus == "Failed");

        var activeAlarms = await _context.Alarms.CountAsync(a => a.IsActive);

        var totalAttention = pendingRoleRequests + pendingSurfaces + failedRenders + failedContent + activeAlarms;

        return new
        {
            totalAttention,
            pendingRoleRequests,
            pendingSurfaces,
            failedRenders,
            failedContent,
            activeAlarms
        };
    }
}

public class DismissAttentionDto
{
    public string Category { get; set; } = string.Empty;
}
