using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers;

/// <summary>MReq 18: Admin-only endpoint to read and write platform settings.</summary>
[ApiController]
[Route("api/admin/settings")]
[Authorize(Roles = "Admin")]
public class AdminSettingsController : ControllerBase
{
    private readonly IPlatformSettingsService _settings;
    private readonly IEmailService _email;

    public AdminSettingsController(IPlatformSettingsService settings, IEmailService email)
    {
        _settings = settings;
        _email = email;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var settings = await _settings.GetAllAsync();
        return Ok(settings);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateAll([FromBody] Dictionary<string, string> settings)
    {
        foreach (var (key, value) in settings)
        {
            await _settings.SetAsync(key, value);
        }
        var updated = await _settings.GetAllAsync();
        return Ok(updated);
    }

    /// <summary>Send a test email to verify SMTP configuration.</summary>
    [HttpPost("test-email")]
    public async Task<IActionResult> TestEmail([FromBody] TestEmailDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.ToEmail))
            return BadRequest(new { error = "Recipient email is required." });

        await _email.SendAsync(dto.ToEmail, "BIT Platform — SMTP Test",  // test email — must be synchronous to give instant feedback
            "This is a test email from the Afrobotics BIT platform.\n\nIf you received this, your SMTP configuration is working correctly.",
            "TestEmail");

        return Ok(new { success = true, message = $"Test email sent to {dto.ToEmail}." });
    }
}

public class TestEmailDto
{
    public string ToEmail { get; set; } = string.Empty;
}
