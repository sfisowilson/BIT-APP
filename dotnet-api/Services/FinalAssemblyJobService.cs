using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
/// Assembles one final video per content item, combining every scene's queued render (see
/// RenderItem.IsQueuedForFinal) with the original footage for scenes that have none. This is
/// the "review scene by scene, queue what you're happy with, then combine everything" workflow's
/// last step — a genuinely different concern from RenderJobService (which produces one render
/// per surface/scene, not a whole-content assembly), kept in its own service accordingly.
/// </summary>
public class FinalAssemblyJobService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<BitHub, IBitClient> _hubContext;
    private readonly ILogger<FinalAssemblyJobService> _logger;

    public const string JobId = "final-assembly";

    public FinalAssemblyJobService(IServiceProvider serviceProvider, IHubContext<BitHub, IBitClient> hubContext, ILogger<FinalAssemblyJobService> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task ProcessFinalAssemblyJob(string contentId, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        var eventLog = scope.ServiceProvider.GetRequiredService<IEventLogService>();
        var chunker = scope.ServiceProvider.GetRequiredService<VideoChunkingService>();

        var content = await db.ContentItems.FindAsync(new object[] { contentId }, cancellationToken);
        if (content == null) return;

        try
        {
            content.FinalAssemblyStatus = "Processing";
            content.FinalAssemblyProgress = 0;
            content.FinalAssemblyErrorMessage = null;
            content.FinalAssemblyUpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.DetectionProgress(contentId, 0, "Starting final assembly", JobId);

            var videoPath = RenderJobService.ResolveVideoPath(content.StorageKey);
            if (videoPath == null || !File.Exists(videoPath))
                throw new InvalidOperationException("Source video file not found.");

            var scenes = await db.SceneItems
                .Where(s => s.ContentId == contentId)
                .OrderBy(s => s.SceneIndex)
                .ToListAsync(cancellationToken);
            if (scenes.Count == 0)
                throw new InvalidOperationException("This content has no scenes to assemble.");

            // Every render currently queued for a scene in this content. A given render's SceneId
            // is only set directly for PromptEdit renders — Interactive ones need SurfaceItem.SceneId.
            var queuedRenders = await db.Renders
                .Where(r => r.ContentId == contentId && r.IsQueuedForFinal)
                .ToListAsync(cancellationToken);
            var queuedSurfaceIds = queuedRenders.Where(r => r.SurfaceId != null).Select(r => r.SurfaceId!).ToList();
            var surfaceSceneById = queuedSurfaceIds.Count == 0
                ? new Dictionary<string, string>()
                : await db.SurfaceItems.Where(s => queuedSurfaceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.SceneId, cancellationToken);

            var queuedRenderBySceneId = new Dictionary<string, RenderItem>();
            foreach (var render in queuedRenders)
            {
                var sceneId = render.SceneId ?? (render.SurfaceId != null && surfaceSceneById.TryGetValue(render.SurfaceId, out var sid) ? sid : null);
                if (sceneId != null) queuedRenderBySceneId[sceneId] = render;
            }

            var (videoWidth, videoHeight) = VideoProbe.GetDimensions(videoPath);
            var fps = content.FrameRate > 0 ? content.FrameRate : 30;

            var segments = new List<(SceneItem scene, string? replacementClipPath)>();
            foreach (var scene in scenes)
            {
                string? clipPath = null;
                if (queuedRenderBySceneId.TryGetValue(scene.Id, out var render))
                {
                    if (render.RenderStatus == "Finished" || render.RenderStatus == "NeedsReview")
                        clipPath = RenderJobService.ResolveRenderOutputPath(render.SceneClipStorageKey);

                    if (clipPath == null)
                        await eventLog.LogEventAsync("RenderEngine", "FINAL_ASSEMBLY_FALLBACK", "Warning",
                            $"Content {contentId} scene {scene.SceneIndex}: queued render '{render.Id}' isn't currently usable " +
                            $"(status={render.RenderStatus}) — using original footage for this scene instead.");
                }
                segments.Add((scene, clipPath));
            }

            var workDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "tmp-final-assembly", contentId);
            Directory.CreateDirectory(workDir);

            var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "renders", $"BIT_Final_{contentId}.mp4");
            await chunker.SpliceFinalAssemblyAsync(videoPath, segments, fps, videoWidth, videoHeight, workDir, outputPath,
                onProgress: async (done, total) =>
                {
                    var pct = (int)(5 + 90.0 * done / total);
                    content.FinalAssemblyProgress = pct;
                    await db.SaveChangesAsync(cancellationToken);
                    await _hubContext.Clients.All.DetectionProgress(contentId, pct, $"Assembling scene {done}/{total}", JobId);
                });

            try { Directory.Delete(workDir, true); } catch { }

            content.FinalAssemblyStatus = "Finished";
            content.FinalAssemblyProgress = 100;
            content.FinalVideoStorageKey = $"/api/content/{contentId}/final-video";
            content.FinalAssemblyUpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.DetectionProgress(contentId, 100, "Final video ready", JobId);

            var queuedCount = segments.Count(s => s.replacementClipPath != null);
            await eventLog.LogEventAsync("RenderEngine", "FINAL_ASSEMBLY_COMPLETE", "Info",
                $"Content {contentId}: final video assembled from {scenes.Count} scenes ({queuedCount} queued, {scenes.Count - queuedCount} original) in {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FinalAssembly] Content {ContentId} FAILED", contentId);
            content.FinalAssemblyStatus = "Failed";
            content.FinalAssemblyErrorMessage = ex.Message;
            content.FinalAssemblyUpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await eventLog.LogEventAsync("RenderEngine", "FINAL_ASSEMBLY_FAILED", "Warning",
                $"Content {contentId} final assembly failed: {ex.Message}");
            await _hubContext.Clients.All.DetectionProgress(contentId, content.FinalAssemblyProgress, "Failed", JobId);
            throw;
        }
    }
}
