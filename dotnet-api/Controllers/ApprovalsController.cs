using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;

namespace Afrobotics.Bit.Api.Controllers;

/// <summary>MReq 11: Query approval audit trail.</summary>
[ApiController]
[Route("api/approvals")]
[Authorize]
public class ApprovalsController : ControllerBase
{
    private readonly PostgresDbContext _context;

    public ApprovalsController(PostgresDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<object>>> GetApprovals([FromQuery] ApprovalFilterParams filter)
    {
        var query = _context.Approvals.AsQueryable();

        if (!string.IsNullOrEmpty(filter.AdSlotId))
            query = query.Where(a => a.AdSlotId == filter.AdSlotId);
        if (!string.IsNullOrEmpty(filter.CampaignId))
            query = query.Where(a => a.CampaignId == filter.CampaignId);
        if (!string.IsNullOrEmpty(filter.Decision))
            query = query.Where(a => a.Decision == filter.Decision);

        query = query.OrderByDescending(a => a.Timestamp);

        var total = await query.CountAsync();
        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(a => new
            {
                a.Id,
                a.AdSlotId,
                a.CampaignId,
                a.ApproverEmail,
                a.Decision,
                Reason = a.RejectionReason,
                a.Timestamp
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
}

public class ApprovalFilterParams
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? AdSlotId { get; set; }
    public string? CampaignId { get; set; }
    public string? Decision { get; set; }
}
