using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// MReq 20: Centralised event-logging service. Emits events from pipeline stages
/// so they appear automatically in the Telemetry tab without manual seed data.
/// </summary>
public interface IEventLogService
{
    Task LogEventAsync(string module, string eventCode, string severity, string description, string? userId = null);
}

public class EventLogService : IEventLogService
{
    private readonly PostgresDbContext _context;
    private readonly ILogger<EventLogService> _logger;

    public EventLogService(PostgresDbContext context, ILogger<EventLogService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogEventAsync(string module, string eventCode, string severity, string description, string? userId = null)
    {
        try
        {
            var entry = new EventLog
            {
                Timestamp = DateTime.UtcNow,
                Module = module,
                EventCode = eventCode,
                Severity = severity,
                Description = description,
                User = userId ?? "System"
            };

            _context.EventLogs.Add(entry);
            await _context.SaveChangesAsync();

            _logger.LogInformation("[{Module}] {EventCode} ({Severity}): {Description}", module, eventCode, severity, description);
        }
        catch (Exception ex)
        {
            // Never throw from logging — best-effort
            _logger.LogWarning(ex, "Failed to persist event log: {Description}", description);
        }
    }
}
