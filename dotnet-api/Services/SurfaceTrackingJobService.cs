using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Hangfire background job entry point for surface tracking.
/// Called after an operator adjusts and approves a surface boundary.
/// </summary>
public class SurfaceTrackingJobService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SurfaceTrackingJobService> _logger;

    public SurfaceTrackingJobService(IServiceProvider serviceProvider, ILogger<SurfaceTrackingJobService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task TrackSurfaceAsync(
        string surfaceId,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        var trackingEngine = scope.ServiceProvider.GetRequiredService<ISurfaceTrackingService>();
        var eventLog = scope.ServiceProvider.GetRequiredService<IEventLogService>();

        var surface = await db.SurfaceItems.FindAsync(new object[] { surfaceId }, cancellationToken);
        if (surface == null)
        {
            _logger.LogWarning("[TrackJob] Surface {SurfaceId} not found.", surfaceId);
            return;
        }

        // Resolve scene and content to get video path + frame range
        var scene = await db.SceneItems.FindAsync(new object[] { surface.SceneId }, cancellationToken);
        if (scene == null)
        {
            _logger.LogWarning("[TrackJob] Scene {SceneId} not found for surface {SurfaceId}.", surface.SceneId, surfaceId);
            return;
        }

        var content = await db.ContentItems.FindAsync(new object[] { scene.ContentId }, cancellationToken);
        if (content == null)
        {
            _logger.LogWarning("[TrackJob] Content {ContentId} not found for scene {SceneId}.", scene.ContentId, scene.Id);
            return;
        }

        var videoPath = ResolveVideoPath(content.StorageKey);
        if (string.IsNullOrEmpty(videoPath))
        {
            _logger.LogWarning("[TrackJob] Cannot resolve video path for content {ContentId}.", content.Id);
            return;
        }

        _logger.LogInformation(
            "[TrackJob] Starting tracking for surface {SurfaceId} ({Type}) scene {Scene} frames {Start}-{End}",
            surfaceId, surface.SurfaceType, scene.SceneIndex, scene.StartFrame, scene.EndFrame);

        try
        {
            var frames = await trackingEngine.TrackAsync(
                surfaceId, videoPath,
                scene.StartFrame, scene.EndFrame,
                surface.BoundaryCoordinatesJson,
                promptFrame: surface.DetectedAtFrame ?? scene.StartFrame,
                sam3Prompt: surface.Sam3Prompt,
                cancellationToken: cancellationToken);

            if (frames.Count > 0)
            {
                surface.TrackedBoundariesJson = JsonSerializer.Serialize(frames.Select(f => new
                {
                    frame = f.Frame,
                    boundary = JsonSerializer.Deserialize<object>(f.BoundaryCoordinatesJson),
                    driftConfidence = f.DriftConfidence,
                }));

                _logger.LogInformation(
                    "[TrackJob] Surface {SurfaceId}: {Count} frames tracked. Drift: {MinDrift:F2}–{MaxDrift:F2}",
                    surfaceId, frames.Count,
                    frames.Min(f => f.DriftConfidence),
                    frames.Max(f => f.DriftConfidence));

                await eventLog.LogEventAsync("SurfaceTracking", "TRACKING_COMPLETED", "Info",
                    $"Surface '{surface.SurfaceType}' ({surfaceId}): {frames.Count} frames tracked.");
            }
            else
            {
                _logger.LogWarning("[TrackJob] No frames returned for surface {SurfaceId}.", surfaceId);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TrackJob] Tracking failed for surface {SurfaceId}", surfaceId);

            await eventLog.LogEventAsync("SurfaceTracking", "TRACKING_FAILED", "Error",
                $"Tracking failed for surface '{surface.SurfaceType}' ({surfaceId}): {ex.Message}");

            throw;
        }
    }

    private static string? ResolveVideoPath(string? storageKey)
    {
        if (string.IsNullOrEmpty(storageKey)) return null;
        var fileName = storageKey.Replace("/api/content/file/", "");
        var path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Uploads", fileName);
        if (System.IO.File.Exists(path)) return path;
        return null;
    }
}
