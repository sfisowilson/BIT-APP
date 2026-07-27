using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Afrobotics.Bit.Api.Hubs;

// ── Strongly-typed client contract ────────────────────────────────────
// Services call IHubContext<BitHub, IBitClient>.Clients.All / .Group(...)
// and get compile-time type-checking on method names + parameters.
public interface IBitClient
{
    /// <summary>Detection progress for a content item (0–100).</summary>
    Task DetectionProgress(string contentId, int percent, string status, string? jobId);

    /// <summary>Render progress for a render job (0–100).</summary>
    Task RenderProgress(string renderId, int percent, string status);

    /// <summary>Content status changed (e.g. Uploaded → Detecting → ... → Ready).</summary>
    Task ContentStatusChanged(string contentId, string newStatus, string? message);

    /// <summary>New alarm or alarm state change.</summary>
    Task AlarmEvent(dynamic alarm);

    /// <summary>General notification / event log entry.</summary>
    Task Notification(string type, string title, string message, DateTime timestamp);
}

// [Authorize] is intentionally omitted — SignalR uses the JWT token from the
// query string (access_token) which is configured client-side via accessTokenFactory.
// Cookie-based auth doesn't work with WebSockets, so we validate manually in OnConnectedAsync.
public class BitHub : Hub<IBitClient>
{
    public override async Task OnConnectedAsync()
    {
        // Token is validated by the JWT bearer middleware before reaching the hub
        // when sent via ?access_token= query string parameter
        await base.OnConnectedAsync();
    }
}
