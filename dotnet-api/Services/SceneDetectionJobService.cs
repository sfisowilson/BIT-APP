using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Hubs;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Hangfire background job entry point for scene + surface detection.
/// Delegates the full pipeline to SurfaceDetectionPipeline (Phase 1 unified flow).
/// Also broadcasts real-time progress via SignalR BitHub.
/// </summary>
public class SceneDetectionJobService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<BitHub, IBitClient> _hubContext;
    private readonly ILogger<SceneDetectionJobService> _logger;
    private readonly IEventLogService _eventLog;

    public SceneDetectionJobService(
        IServiceProvider serviceProvider,
        IHubContext<BitHub, IBitClient> hubContext,
        ILogger<SceneDetectionJobService> logger,
        IEventLogService eventLog)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
        _eventLog = eventLog;
    }

    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 86400)]
    public async Task RunDetectionPipeline(
        string contentId,
        string videoTitle,
        string splitMode,
        CancellationToken cancellationToken,
        bool runSurfaceDetection = true)
    {
        // Broadcast start of detection
        var modeLabel = string.Equals(splitMode, "cut", StringComparison.OrdinalIgnoreCase)
            ? "cut" : "scene";
        await _hubContext.Clients.All.DetectionProgress(
            contentId, 1, $"Starting ({modeLabel} split)", null);

        using var scope = _serviceProvider.CreateScope();
        var shotPipeline = scope.ServiceProvider.GetRequiredService<ShotDetectionPipeline>();
        var surfacePipeline = scope.ServiceProvider.GetRequiredService<SurfaceDetectionPipeline>();

        try
        {
            // ── Phase 1: Shot detection → embedding → clustering (1% → 30%) ──
            await _hubContext.Clients.All.DetectionProgress(
                contentId, 2, "Detecting shots", null);
            await shotPipeline.RunAsync(contentId, splitMode, cancellationToken);

            // ── Phase 2: Surface detection per scene (30% → 100%) ──
            await _hubContext.Clients.All.DetectionProgress(
                contentId, 30, "Detecting surfaces", null);
            await surfacePipeline.RunAsync(contentId, cancellationToken, runSurfaceDetection);

            // Broadcast completion
            await _hubContext.Clients.All.DetectionProgress(
                contentId, 100, "Completed", null);
            await _hubContext.Clients.All.ContentStatusChanged(
                contentId, "Ready", videoTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SceneDetectionJob] Full pipeline FAILED for {ContentId}", contentId);
            await _eventLog.LogEventAsync("SceneDetection", "PIPELINE_FAILED", "Error",
                $"Full detection pipeline failed for '{videoTitle}' ({contentId}): {ex.GetType().Name} — {ex.Message}");
            await _hubContext.Clients.All.DetectionProgress(
                contentId, 0, "Failed", null);
            await _hubContext.Clients.All.ContentStatusChanged(
                contentId, "Failed", videoTitle);
            throw;
        }
    }

    /// <summary>Per-scene surface detection. Triggered by user on a single scene.</summary>
    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 86400)]
    public async Task RunSceneSurfaceDetection(
        string sceneId,
        CancellationToken cancellationToken)
    {
        // Look up the contentId for this scene so we can broadcast progress
        string? contentId = null;
        string? videoTitle = null;
        using (var lookupScope = _serviceProvider.CreateScope())
        {
            var lookupDb = lookupScope.ServiceProvider.GetRequiredService<PostgresDbContext>();
            var scene = await lookupDb.SceneItems
                .FirstOrDefaultAsync(s => s.Id == sceneId, cancellationToken);
            if (scene != null)
            {
                contentId = scene.ContentId;
                var content = await lookupDb.ContentItems
                    .FirstOrDefaultAsync(c => c.Id == contentId, cancellationToken);
                videoTitle = content?.Title;
            }
        }

        if (contentId != null)
        {
            await _hubContext.Clients.All.DetectionProgress(
                contentId, 1, "Starting per-scene surface detection", null);
        }

        using var scope = _serviceProvider.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<SurfaceDetectionPipeline>();

        try
        {
            await pipeline.RunSurfaceDetectionForSceneAsync(sceneId, cancellationToken);

            // Broadcast completion so the frontend can refresh scenes for this content
            if (contentId != null)
            {
                await _hubContext.Clients.All.DetectionProgress(
                    contentId, 100, "Completed", null);
                await _hubContext.Clients.All.ContentStatusChanged(
                    contentId, "SurfacesReady", videoTitle ?? "");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SceneDetectionJob] Per-scene surface detection FAILED for scene {SceneId}", sceneId);
            await _eventLog.LogEventAsync("SceneDetection", "SCENE_SURFACE_DETECTION_FAILED", "Error",
                $"Per-scene surface detection failed for scene {sceneId}: {ex.GetType().Name} — {ex.Message}");
            if (contentId != null)
            {
                await _hubContext.Clients.All.DetectionProgress(
                    contentId, 0, "Failed", null);
                await _hubContext.Clients.All.ContentStatusChanged(
                    contentId, "Failed", videoTitle ?? "");
            }
            throw;
        }
    }

    /// <summary>
    /// Static helper to delete all scenes and surfaces for a content item.
    /// Used by ContentController and ContentService for content deletion / reset.
    /// </summary>
    public static async Task DeleteExistingScenes(Microsoft.EntityFrameworkCore.DbContext db, string contentId, CancellationToken ct)
    {
        var sceneItems = await db.Set<Afrobotics.Bit.Api.Models.SceneItem>()
            .Where(s => s.ContentId == contentId).ToListAsync(ct);
        foreach (var scene in sceneItems)
        {
            var surfaces = await db.Set<Afrobotics.Bit.Api.Models.SurfaceItem>()
                .Where(s => s.SceneId == scene.Id).ToListAsync(ct);
            db.RemoveRange(surfaces);
        }
        db.RemoveRange(sceneItems);
        await db.SaveChangesAsync(ct);
    }
}
