using System;
using System.Linq;
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

    [HttpGet("attention")]
    public async Task<IActionResult> GetAttentionCounts()
    {
        var isAdmin = User.IsInRole("Admin");

        var pendingRoleRequests = isAdmin
            ? await _context.RoleRequests.CountAsync(r => r.Status == "Pending")
            : 0;

        var pendingSurfaces = await _context.SurfaceItems.CountAsync(s => s.Status == "Candidate");

        var failedRenders = await _context.Renders.CountAsync(r => r.RenderStatus == "Failed");

        var failedContent = await _context.ContentItems.CountAsync(c => c.IngestionStatus == "Failed");

        var activeAlarms = await _context.Alarms.CountAsync(a => a.IsActive);

        var totalAttention = pendingRoleRequests + pendingSurfaces + failedRenders + failedContent + activeAlarms;

        return Ok(new
        {
            totalAttention,
            pendingRoleRequests,
            pendingSurfaces,
            failedRenders,
            failedContent,
            activeAlarms
        });
    }
}
