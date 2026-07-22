using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers;

/// <summary>MReq 9: Admin-only endpoint to review and decide on role elevation requests.</summary>
[ApiController]
[Route("api/admin/role-requests")]
[Authorize(Roles = "Admin")]
public class AdminRoleRequestsController : ControllerBase
{
    private readonly PostgresDbContext _context;
    private readonly IEmailService _email;

    public AdminRoleRequestsController(PostgresDbContext context, IEmailService email)
    {
        _context = context;
        _email = email;
    }

    /// <summary>List all role requests with user details.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status)
    {
        var query = _context.RoleRequests
            .Join(_context.Users,
                r => r.UserId,
                u => u.Id,
                (r, u) => new { r, u })
            .AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(x => x.r.Status == status);

        var results = await query
            .OrderByDescending(x => x.r.Status == "Pending")
            .ThenByDescending(x => x.r.RequestedAt)
            .Select(x => new
            {
                x.r.Id,
                x.r.UserId,
                x.u.FullName,
                x.u.Email,
                x.u.Role,
                x.r.RequestedRole,
                x.r.Reason,
                x.r.Status,
                x.r.RequestedAt,
                x.r.ReviewedBy,
                x.r.ReviewedAt
            })
            .ToListAsync();

        return Ok(results);
    }

    /// <summary>Approve a role request and elevate the user.</summary>
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id)
    {
        var reviewerEmail = User.FindFirstValue(ClaimTypes.Email) ?? "admin";

        var request = await _context.RoleRequests.FindAsync(id);
        if (request == null)
            return NotFound(new { error = "Role request not found." });
        if (request.Status != "Pending")
            return BadRequest(new { error = $"Request is already {request.Status}." });

        var user = await _context.Users.FindAsync(request.UserId);
        if (user == null)
            return NotFound(new { error = "User not found." });

        // Elevate the user's role
        user.Role = request.RequestedRole;

        // Mark request as approved
        request.Status = "Approved";
        request.ReviewedBy = reviewerEmail;
        request.ReviewedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Notify user
        _email.Enqueue(user.Email, "BIT — Role Request Approved",
            $"Hello {user.FullName},\n\nYour request to be elevated to '{request.RequestedRole}' has been approved.\n\nYou now have {request.RequestedRole} access.",
            "RoleRequestApproved");

        return Ok(new { success = true, message = $"{user.FullName} has been elevated to {request.RequestedRole}." });
    }

    /// <summary>Reject a role request.</summary>
    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(string id)
    {
        var reviewerEmail = User.FindFirstValue(ClaimTypes.Email) ?? "admin";

        var request = await _context.RoleRequests.FindAsync(id);
        if (request == null)
            return NotFound(new { error = "Role request not found." });
        if (request.Status != "Pending")
            return BadRequest(new { error = $"Request is already {request.Status}." });

        var user = await _context.Users.FindAsync(request.UserId);
        if (user == null)
            return NotFound(new { error = "User not found." });

        request.Status = "Rejected";
        request.ReviewedBy = reviewerEmail;
        request.ReviewedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Notify user
        _email.Enqueue(user.Email, "BIT — Role Request Update",
            $"Hello {user.FullName},\n\nYour request to be elevated to '{request.RequestedRole}' was not approved at this time.\n\nPlease contact your administrator for more information.",
            "RoleRequestRejected");

        return Ok(new { success = true, message = "Role request rejected." });
    }
}
