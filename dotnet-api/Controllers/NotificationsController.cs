using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;

namespace Afrobotics.Bit.Api.Controllers;

/// <summary>MReq 12, 15: Query notification history.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly PostgresDbContext _context;

    public NotificationsController(PostgresDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<object>>> GetNotifications([FromQuery] NotificationFilterParams filter)
    {
        var query = _context.Notifications.AsQueryable();

        if (!string.IsNullOrEmpty(filter.RecipientEmail))
            query = query.Where(n => n.RecipientEmail.Contains(filter.RecipientEmail));
        if (!string.IsNullOrEmpty(filter.Type))
            query = query.Where(n => n.Type == filter.Type);
        if (!string.IsNullOrEmpty(filter.Status))
            query = query.Where(n => n.Status == filter.Status);

        query = query.OrderByDescending(n => n.Timestamp);

        var total = await query.CountAsync();
        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(n => new
            {
                n.Id,
                n.Timestamp,
                n.RecipientEmail,
                n.Type,
                n.Subject,
                n.Status
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

public class NotificationFilterParams
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? RecipientEmail { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
}
