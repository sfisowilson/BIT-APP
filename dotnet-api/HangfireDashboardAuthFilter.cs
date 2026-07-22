using Hangfire.Dashboard;

namespace Afrobotics.Bit.Api;

/// <summary>Restricts the Hangfire dashboard to admin users only.</summary>
public class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true
               && httpContext.User.IsInRole("Admin");
    }
}
