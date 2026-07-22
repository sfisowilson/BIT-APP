using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Middleware;

/// <summary>
/// MReq 22: Tracks every authenticated API request for usage auditing.
/// Logs path, method, status code, duration, user, and IP.
/// </summary>
public class UsageTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UsageTrackingMiddleware> _logger;

    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/content/file/",  // partial — skip static file serving
        "/api/content/proxy/",
        "/api/content/proxy-status/",
        "/favicon.ico",
    };

    public UsageTrackingMiddleware(RequestDelegate next, ILogger<UsageTrackingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip excluded paths and unauthenticated requests
        var path = context.Request.Path.Value ?? "/";
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        foreach (var excluded in ExcludedPaths)
        {
            if (path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }
        }

        // Record the request
        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            try
            {
                var db = context.RequestServices.GetRequiredService<PostgresDbContext>();
                var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var userEmail = context.User.FindFirstValue(ClaimTypes.Email);
                var ip = context.Connection.RemoteIpAddress?.ToString();

                var record = new UsageRecord
                {
                    Timestamp = DateTime.UtcNow,
                    UserId = userId,
                    UserEmail = userEmail,
                    RequestPath = path.Length > 500 ? path[..500] : path,
                    HttpMethod = context.Request.Method,
                    StatusCode = context.Response.StatusCode,
                    DurationMs = sw.ElapsedMilliseconds,
                    IpAddress = ip?.Length > 50 ? ip[..50] : ip
                };

                db.UsageRecords.Add(record);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Never fail a request because usage tracking failed
                _logger.LogWarning(ex, "Usage tracking write failed for {Path}", path);
            }
        }
    }
}
