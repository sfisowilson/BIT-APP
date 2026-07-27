using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;

namespace Afrobotics.Bit.Api.Controllers;

/// <summary>MReq 19: BI / Statistics summary and throughput endpoints.</summary>
[ApiController]
[Route("api/stats")]
[Authorize]
public class StatsController : ControllerBase
{
    private readonly PostgresDbContext _context;

    public StatsController(PostgresDbContext context)
    {
        _context = context;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);

        // With indexes, each COUNT is sub-millisecond — sequential is fine and avoids DbContext threading issues
        var totalContent = await _context.ContentItems.CountAsync();
        var totalScenes = await _context.SceneItems.CountAsync();
        var totalSurfaces = await _context.SurfaceItems.CountAsync();
        var totalRenders = await _context.Renders.CountAsync();
        var totalCampaigns = await _context.Campaigns.Where(c => c.Status == "Active").CountAsync();
        var activeAlarms = await _context.Alarms.Where(a => a.IsActive).CountAsync();
        var rendersLast7Days = await _context.Renders.Where(r => r.CreatedAt >= sevenDaysAgo).CountAsync();
        var contentLast7Days = await _context.ContentItems.Where(c => c.CreatedAt >= sevenDaysAgo).CountAsync();
        var avgRenderTime = await _context.Renders
            .Where(r => r.ProcessingDurationMs > 0)
            .Select(r => (double?)r.ProcessingDurationMs)
            .AverageAsync() ?? 0;

        return Ok(new
        {
            totalContent,
            totalScenes,
            totalSurfaces,
            totalRenders,
            totalCampaigns,
            activeAlarms,
            rendersLast7Days,
            contentLast7Days,
            avgRenderTimeMs = Math.Round(avgRenderTime, 0)
        });
    }

    [HttpGet("throughput")]
    public async Task<IActionResult> GetThroughput([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var dailyIngestion = await _context.ContentItems
            .Where(c => c.CreatedAt >= fromDate && c.CreatedAt <= toDate)
            .GroupBy(c => c.CreatedAt.Date)
            .Select(g => new { date = g.Key, count = g.Count() })
            .OrderBy(g => g.date)
            .ToListAsync();

        var dailyRenders = await _context.Renders
            .Where(r => r.CreatedAt >= fromDate && r.CreatedAt <= toDate)
            .GroupBy(r => r.CreatedAt.Date)
            .Select(g => new { date = g.Key, count = g.Count() })
            .OrderBy(g => g.date)
            .ToListAsync();

        return Ok(new { dailyIngestion, dailyRenders });
    }
}
