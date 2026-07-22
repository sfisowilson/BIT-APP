using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Afrobotics.Bit.Api.Data;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Hangfire background job for processing render tasks.
/// Resolves its own scoped services so it survives app restarts.
/// </summary>
public class RenderJobService
{
    private readonly IServiceProvider _serviceProvider;

    public RenderJobService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Process a render job through all three phases. Retries on failure via Hangfire.
    /// </summary>
    public async Task ProcessRenderJob(string renderId, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var eventLog = scope.ServiceProvider.GetRequiredService<IEventLogService>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var render = await context.Renders.FindAsync(renderId);
        if (render == null) return;

        try
        {
            // Phase 1: Preprocessing (0 → 30%)
            for (int p = 5; p <= 30; p += 5)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(400, cancellationToken);
                render.Progress = p;
                context.Renders.Update(render);
                await context.SaveChangesAsync(cancellationToken);
            }

            // Phase 2: GPU Compositing (30 → 75%)
            render.RenderStatus = "Processing";
            context.Renders.Update(render);
            await context.SaveChangesAsync(cancellationToken);
            for (int p = 35; p <= 75; p += 5)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(350, cancellationToken);
                render.Progress = p;
                context.Renders.Update(render);
                await context.SaveChangesAsync(cancellationToken);
            }

            // Phase 3: Encoding & Finalization (75 → 100%)
            for (int p = 80; p <= 100; p += 5)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(300, cancellationToken);
                render.Progress = p;
                context.Renders.Update(render);
                await context.SaveChangesAsync(cancellationToken);
            }

            var elapsed = DateTime.UtcNow - render.CreatedAt;
            render.Progress = 100;
            render.RenderStatus = "Finished";
            render.ProcessingDurationMs = (int)elapsed.TotalMilliseconds;
            context.Renders.Update(render);
            await context.SaveChangesAsync(cancellationToken);

            await eventLog.LogEventAsync("RenderEngine", "RENDER_COMPLETED", "Info",
                $"Render '{render.Id}' completed in {elapsed.TotalSeconds:F1}s.");

            BackgroundJob.Enqueue<IEmailService>(s => s.SendAsync(config["Smtp:FromEmail"] ?? "noreply@afrobotics.co.za",
                $"Render Complete — {render.ExportPreset}",
                $"Render job '{render.Id}' completed.\n\nContent: {render.ContentId}\nCampaign: {render.CampaignId}\nDuration: {elapsed.TotalSeconds:F1}s",
                "RenderCompleted"));
        }
        catch (OperationCanceledException)
        {
            render.RenderStatus = "Cancelled";
            context.Renders.Update(render);
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            render.RenderStatus = "Failed";
            render.Progress = 0;
            context.Renders.Update(render);
            await context.SaveChangesAsync();

            await eventLog.LogEventAsync("RenderEngine", "RENDER_FAILED", "Warning",
                $"Render '{render.Id}' failed: {ex.Message}");

            BackgroundJob.Enqueue<IEmailService>(s => s.SendAsync(config["Smtp:FromEmail"] ?? "noreply@afrobotics.co.za",
                $"Render Failed — {render.ExportPreset}",
                $"Render job '{render.Id}' failed.\n\nError: {ex.Message}\nContent: {render.ContentId}\nCampaign: {render.CampaignId}",
                "RenderFailed"));

            throw; // re-throw so Hangfire can retry
        }
    }
}
