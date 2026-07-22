using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Controllers;

/// <summary>MReq 4: Admin-only CRUD for the permanent brand-safety exclusion list. Add-only, never silently removed.</summary>
[ApiController]
[Route("api/admin/brand-safety")]
[Authorize(Roles = "Admin")]
public class BrandSafetyController : ControllerBase
{
    private readonly PostgresDbContext _context;

    public BrandSafetyController(PostgresDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var rules = await _context.BrandSafetyRules
            .OrderBy(r => r.Category)
            .ToListAsync();
        return Ok(rules);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BrandSafetyRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Category))
            return BadRequest(new { error = "Category is required." });

        rule.Id = Guid.NewGuid().ToString();
        rule.CreatedAt = DateTime.UtcNow;
        rule.IsActive = true;

        _context.BrandSafetyRules.Add(rule);
        await _context.SaveChangesAsync();

        return Ok(rule);
    }

    /// <summary>Toggle active/inactive. Rules are never deleted — only deactivated.</summary>
    [HttpPost("{id}/toggle")]
    public async Task<IActionResult> Toggle(string id)
    {
        var rule = await _context.BrandSafetyRules.FindAsync(id);
        if (rule == null) return NotFound();

        rule.IsActive = !rule.IsActive;
        await _context.SaveChangesAsync();

        return Ok(rule);
    }
}
