using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Controllers;

/// <summary>MReq 22: Query and export usage records.</summary>
[ApiController]
[Route("api/usage")]
[Authorize(Roles = "Admin")]
public class UsageController : ControllerBase
{
    private readonly PostgresDbContext _context;

    public UsageController(PostgresDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<object>>> GetUsage([FromQuery] UsageFilterParams filter)
    {
        var query = _context.UsageRecords.AsQueryable();

        if (!string.IsNullOrEmpty(filter.UserId))
            query = query.Where(r => r.UserId == filter.UserId);
        if (!string.IsNullOrEmpty(filter.UserEmail))
            query = query.Where(r => r.UserEmail != null && r.UserEmail.Contains(filter.UserEmail));
        if (filter.From.HasValue)
            query = query.Where(r => r.Timestamp >= filter.From.Value);
        if (filter.To.HasValue)
            query = query.Where(r => r.Timestamp <= filter.To.Value);

        query = query.OrderByDescending(r => r.Timestamp);

        var total = await query.CountAsync();
        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(r => new
            {
                r.Id,
                r.Timestamp,
                r.UserId,
                r.UserEmail,
                r.RequestPath,
                r.HttpMethod,
                r.StatusCode,
                r.DurationMs,
                r.IpAddress
            })
            .ToListAsync();

        var totalPages = (int)Math.Ceiling(total / (double)filter.PageSize);
        return Ok(new
        {
            items,
            totalCount = total,
            page = filter.Page,
            pageSize = filter.PageSize,
            totalPages,
            hasPreviousPage = filter.Page > 1,
            hasNextPage = filter.Page < totalPages
        });
    }

    [HttpPost("export")]
    public async Task<IActionResult> ExportUsage([FromBody] UsageExportDto dto)
    {
        var query = _context.UsageRecords.AsQueryable();
        if (dto.From.HasValue)
            query = query.Where(r => r.Timestamp >= dto.From.Value);
        if (dto.To.HasValue)
            query = query.Where(r => r.Timestamp <= dto.To.Value);

        var records = await query.OrderByDescending(r => r.Timestamp).Take(10000).ToListAsync();

        var csv = "Timestamp,UserId,UserEmail,Path,Method,StatusCode,DurationMs,IpAddress\n";
        foreach (var r in records)
        {
            csv += $"{r.Timestamp:O},{r.UserId},{r.UserEmail},{r.RequestPath},{r.HttpMethod},{r.StatusCode},{r.DurationMs},{r.IpAddress}\n";
        }

        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"usage_export_{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}

public class UsageFilterParams
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? UserId { get; set; }
    public string? UserEmail { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class UsageExportDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
