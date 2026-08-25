using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Hubs;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Detects shot boundaries in a video, generates SAM3 embeddings for each shot's
/// keyframe, and clusters shots into temporally contiguous scenes.
///
/// Pipeline: FFmpeg shot detection → keyframe extraction → SAM3 embed → cluster → persist.
/// </summary>
public class ShotDetectionPipeline
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<BitHub, IBitClient> _hubContext;
    private readonly ILogger<ShotDetectionPipeline> _logger;

    public ShotDetectionPipeline(
        IServiceProvider serviceProvider,
        IHubContext<BitHub, IBitClient> hubContext,
        ILogger<ShotDetectionPipeline> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Full pipeline for a content item: detect shots, embed keyframes, cluster into scenes.
    /// </summary>
    /// <param name="splitMode">"scene" (default) clusters shots into scenes via SAM3 embeddings;
    /// "cut" maps every camera cut 1:1 to a scene (no embedding or clustering).</param>
    public async Task RunAsync(string contentId, string splitMode = "scene", CancellationToken ct = default)
    {
        var isCutMode = string.Equals(splitMode, "cut", StringComparison.OrdinalIgnoreCase);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        var embedService = scope.ServiceProvider.GetRequiredService<FalAiImageEmbedService>();
        var clusterService = scope.ServiceProvider.GetRequiredService<ShotClusteringService>();

        var content = await db.ContentItems.FindAsync(new object[] { contentId }, ct);
        if (content == null)
        {
            _logger.LogWarning("[ShotPipeline] Content {ContentId} not found", contentId);
            return;
        }

        _logger.LogInformation("[ShotPipeline] Starting for {ContentId}", contentId);

        // Reports progress within the 2%→30% band SceneDetectionJobService brackets this
        // method with — writes to the DB (for polling clients) and broadcasts via SignalR
        // (for live push). Monotonic: never reports backwards or re-reports the same percent.
        // Also called concurrently during the embedding phase (FalAiImageEmbedService.EmbedBatchAsync
        // runs its onProgress callback from multiple parallel tasks), so the whole body is locked —
        // EF Core's DbContext isn't thread-safe, and without this, concurrent SaveChangesAsync calls
        // on the shared `db` instance would throw.
        var lastReportedPercent = 2;
        using var progressLock = new SemaphoreSlim(1, 1);
        async Task ReportProgress(int percent, string status)
        {
            await progressLock.WaitAsync(ct);
            try
            {
                if (percent <= lastReportedPercent) return;
                lastReportedPercent = percent;
                content.DetectionProgress = percent;
                await db.SaveChangesAsync(ct);
            }
            finally
            {
                progressLock.Release();
            }
            try
            {
                await _hubContext.Clients.All.DetectionProgress(contentId, percent, status, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ShotPipeline] Failed to broadcast progress for {ContentId}", contentId);
            }
        }

        // Short clips don't benefit from scene splitting — a single cut/shot boundary detection
        // pass on a <10s clip is pure overhead (and can even misfire on brief clips with no real
        // cuts). Treat the whole video as one scene/shot and skip straight to surface detection.
        var shortClipDurationSec = ParseDuration(content.Duration);
        if (shortClipDurationSec > 0 && shortClipDurationSec < 10)
        {
            await CreateSingleSceneWithoutSplittingAsync(db, content, contentId, shortClipDurationSec, ct);
            await ReportProgress(30, "Short clip — using the full video as one scene");
            return;
        }

        // ── Phase 1: Detect shot boundaries via FFmpeg ──
        var shots = await DetectShotsAsync(content, ct);
        _logger.LogInformation("[ShotPipeline] Detected {Count} shots", shots.Count);

        if (shots.Count == 0) return;

        await ReportProgress(4, $"Detected {shots.Count} shots");

        // Delete previous shots AND scenes for this content (clean re-run)
        var existingShots = await db.Shots.Where(s => s.ContentId == contentId).ToListAsync(ct);
        db.Shots.RemoveRange(existingShots);

        // Cascade: delete child surfaces/ad-slots/approvals, then old scenes
        var existingScenes = await db.SceneItems.Where(s => s.ContentId == contentId).ToListAsync(ct);
        foreach (var scene in existingScenes)
        {
            var surfaces = await db.SurfaceItems.Where(sf => sf.SceneId == scene.Id).ToListAsync(ct);
            foreach (var sf in surfaces)
            {
                var adSlots = await db.AdSlots.Where(a => a.SurfaceId == sf.Id).ToListAsync(ct);
                var adSlotIds = adSlots.Select(s => s.Id).ToList();
                if (adSlotIds.Count > 0)
                {
                    var approvals = await db.Approvals
                        .Where(a => a.AdSlotId != null && adSlotIds.Contains(a.AdSlotId))
                        .ToListAsync(ct);
                    db.Approvals.RemoveRange(approvals);
                }
                db.AdSlots.RemoveRange(adSlots);
            }
            db.SurfaceItems.RemoveRange(surfaces);
        }
        db.SceneItems.RemoveRange(existingScenes);

        // Persist new shot boundaries
        foreach (var shot in shots)
        {
            shot.ContentId = contentId;
            db.Shots.Add(shot);
        }
        await db.SaveChangesAsync(ct);

        // ── Cut mode: create 1 SceneItem per ShotItem (1:1 mapping, no AI clustering) ──
        if (isCutMode)
        {
            await ReportProgress(6, $"Creating {shots.Count} cut scenes (1:1)");
            var fps = content.FrameRate > 0 && content.FrameRate <= 240 ? content.FrameRate : 30;
            for (int i = 0; i < shots.Count; i++)
            {
                var shot = shots[i];
                var scene = new SceneItem
                {
                    Id = $"sc-{Guid.NewGuid()}",
                    ContentId = contentId,
                    SceneIndex = i,
                    StartFrame = shot.StartFrame,
                    EndFrame = shot.EndFrame,
                    // EndFrame is inclusive (the last frame belonging to the scene), so the frame
                    // count is EndFrame - StartFrame + 1 — omitting the +1 under-counts duration by
                    // exactly one frame, which final assembly (VideoChunkingService.SpliceFinalAssemblyAsync)
                    // uses directly as ffmpeg's -t, silently dropping the scene's last frame.
                    DurationSeconds = (shot.EndFrame - shot.StartFrame + 1) / (fps > 0 ? fps : 30),
                    QaStatus = "Unchecked",
                };
                db.SceneItems.Add(scene);
                shot.SceneId = scene.Id;
                await ReportProgress(6 + (int)(24.0 * (i + 1) / shots.Count),
                    $"Creating cut scenes ({i + 1}/{shots.Count})");
            }
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("[ShotPipeline] Cut mode: created {Count} scenes from {ShotCount} shots for {ContentId}",
                shots.Count, shots.Count, contentId);
            return;
        }

        // ── Phase 2: Extract keyframes ──
        var videoPath = ResolveVideoPath(content);
        var keyframeDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "keyframes", contentId);
        Directory.CreateDirectory(keyframeDir);

        // Build shot→keyframe URL mapping for embedding
        var shotUrlMap = new Dictionary<int, string>();
        var videoBaseUrl = await GetVideoBaseUrlAsync(scope);

        double extractFps = content.FrameRate > 0 && content.FrameRate <= 240 ? content.FrameRate : 30;

        for (int i = 0; i < shots.Count; i++)
        {
            var shot = shots[i];
            var midFrame = (shot.StartFrame + shot.EndFrame) / 2;
            var keyframeFile = Path.Combine(keyframeDir, $"shot_{shot.ShotIndex:D4}.jpg");

            if (await ExtractKeyframeAsync(videoPath, midFrame / extractFps, keyframeFile, ct))
            {
                shot.KeyframePath = $"keyframes/{contentId}/shot_{shot.ShotIndex:D4}.jpg";
                shot.KeyframeTimestamp = midFrame / (content.FrameRate > 0 ? content.FrameRate : 30);

                var keyframeUrl = $"{videoBaseUrl}/api/content/file/keyframes/{contentId}/shot_{shot.ShotIndex:D4}.jpg";
                shotUrlMap[shot.ShotIndex] = keyframeUrl;
            }

            await ReportProgress(4 + (int)(10.0 * (i + 1) / shots.Count), $"Extracting keyframes ({i + 1}/{shots.Count})");
        }
        await db.SaveChangesAsync(ct);

        // ── Phase 3: Generate SAM3 embeddings ──
        _logger.LogInformation("[ShotPipeline] Embedding {Count} keyframes", shotUrlMap.Count);
        var embeddings = await embedService.EmbedBatchAsync(shotUrlMap, ct,
            async (completed, total) =>
            {
                var pct = 14 + (int)(12.0 * completed / Math.Max(1, total));
                await ReportProgress(pct, $"Embedding keyframes ({completed}/{total})");
            },
            contentId);

        foreach (var (shotIdx, embedding) in embeddings)
        {
            var shot = shots.FirstOrDefault(s => s.ShotIndex == shotIdx);
            if (shot != null && embedding != null)
            {
                shot.KeyframeEmbeddingJson = System.Text.Json.JsonSerializer.Serialize(embedding);
            }
        }
        await db.SaveChangesAsync(ct);

        // ── Phase 4: Cluster shots into scenes ──
        _logger.LogInformation("[ShotPipeline] Clustering shots into scenes");
        await ReportProgress(26, $"Clustering {shots.Count} shots into scenes");
        var scenes = await clusterService.ClusterShotsAsync(contentId, ct: ct);
        await ReportProgress(29, $"Clustered into {scenes.Count} scenes");

        _logger.LogInformation("[ShotPipeline] Complete for {ContentId}", contentId);
    }

    // ── Shot detection via FFmpeg ──

    private static async Task<List<ShotItem>> DetectShotsAsync(ContentItem content, CancellationToken ct)
    {
        var fileName = content.StorageKey?.Replace("/api/content/file/", "") ?? "";
        var videoPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", fileName);
        var fps = content.FrameRate > 0 && content.FrameRate <= 240 ? content.FrameRate : 30;
        var totalDurationSec = ParseDuration(content.Duration);
        var totalFrames = (int)(totalDurationSec * fps);

        var timestamps = new List<double>();

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-hide_banner -i \"{videoPath}\" -vf \"select=gt(scene\\,0.4),showinfo\" -vsync vfr -f null NUL",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.Start();

            // This is a full-file decode (scene-change scoring reads every frame), so it can
            // legitimately take a while for a large/long video — but ReadToEnd() blocks until
            // ffmpeg closes its output, which happens only when the process exits. Read both
            // streams asynchronously so a full pipe buffer can't deadlock ffmpeg, and enforce
            // the timeout by killing the process rather than relying on a WaitForExit call that
            // never gets a chance to run until after the blocking read already returned.
            var readStdout = process.StandardOutput.ReadToEndAsync(ct);
            var readStderr = process.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch { } }
            }

            var stderr = await readStderr;
            await readStdout;

            var regex = new Regex(@"pts_time:([\d\.]+)");
            foreach (Match m in regex.Matches(stderr))
            {
                if (double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var ts) && ts > 0)
                    timestamps.Add(ts);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShotPipeline] FFmpeg shot detection failed: {ex.Message}");
        }

        return BuildShots(timestamps, totalDurationSec, fps, totalFrames);
    }

    private static List<ShotItem> BuildShots(List<double> timestamps, double totalDuration, int fps, int totalFrames)
    {
        if (totalDuration <= 0) totalDuration = 60;
        if (totalFrames <= 0) totalFrames = (int)(totalDuration * fps);

        var cuts = new List<double> { 0 };
        cuts.AddRange(timestamps.Where(t => t > 0.5 && t < totalDuration - 0.5));
        cuts.Add(totalDuration);
        cuts = cuts.Distinct().OrderBy(t => t).ToList();

        if (cuts.Count <= 2 && totalDuration > 3)
            cuts = new List<double> { 0, totalDuration };

        var shots = new List<ShotItem>();
        for (int i = 0; i < cuts.Count - 1; i++)
        {
            var startSec = cuts[i];
            var endSec = cuts[i + 1];
            var startFrame = (int)(startSec * fps);
            var endFrame = Math.Max(startFrame + 1, Math.Min((int)(endSec * fps) - 1, totalFrames - 1));

            shots.Add(new ShotItem
            {
                Id = $"sh-{Guid.NewGuid()}",
                ShotIndex = i,
                StartFrame = startFrame,
                EndFrame = endFrame,
            });
        }

        return shots;
    }

    // ── Keyframe extraction ──

    /// <summary>
    /// Extracts a single frame near <paramref name="timeSec"/> using a fast pre-input seek
    /// (jumps near the target instead of decoding from frame 0 every call — without this,
    /// extracting keyframes for N shots does O(N^2) total decode work and gets progressively
    /// slower as shot index increases). Drains stdout/stderr asynchronously — ffmpeg's verbose
    /// banner output can fill the OS pipe buffer and deadlock a synchronous WaitForExit if the
    /// redirected streams are never read. Matches the pattern already used in
    /// SurfaceDetectionPipeline.ExtractKeyFrameAsync.
    /// </summary>
    private static async Task<bool> ExtractKeyframeAsync(string videoPath, double timeSec, string outputPath, CancellationToken ct)
    {
        try
        {
            var preSeek = Math.Max(0, timeSec - 2);
            var postSeek = timeSec - preSeek;
            var preStr = preSeek.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var postStr = postSeek.ToString(System.Globalization.CultureInfo.InvariantCulture);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -ss {preStr} -noaccurate_seek -i \"{videoPath}\" -ss {postStr} -vframes 1 -q:v 2 \"{outputPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();

            var readStdout = process.StandardOutput.ReadToEndAsync(ct);
            var readStderr = process.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch { } }
            }

            await Task.WhenAll(readStdout, readStderr);

            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 100;
        }
        catch
        {
            return false;
        }
    }

    // ── Helpers ──

    private static string ResolveVideoPath(ContentItem content)
    {
        var fileName = content.StorageKey?.Replace("/api/content/file/", "") ?? "";
        return Path.Combine(Directory.GetCurrentDirectory(), "Uploads", fileName);
    }

    private static async Task<string> GetVideoBaseUrlAsync(IServiceScope scope)
    {
        var settings = scope.ServiceProvider.GetRequiredService<IPlatformSettingsService>();
        return await settings.GetAsync("sam3_video_base_url", "http://localhost:57220");
    }

    /// <summary>Parses ContentItem.Duration, which is stored as "HH:MM:SS" (not raw seconds).</summary>
    private static double ParseDuration(string? duration)
    {
        if (string.IsNullOrEmpty(duration)) return 60;
        var match = Regex.Match(duration, @"^(\d{2}):([0-5]\d):([0-5]\d)$");
        if (!match.Success) return 60;
        return int.Parse(match.Groups[1].Value) * 3600 +
               int.Parse(match.Groups[2].Value) * 60 +
               int.Parse(match.Groups[3].Value);
    }

    /// <summary>
    /// Short-clip path (&lt;10s): one SceneItem + one ShotItem spanning the entire video, no FFmpeg
    /// cut detection/embedding/clustering. SurfaceDetectionPipeline only reads SceneItem rows, so
    /// this alone is enough to reach "Completed"; the ShotItem is still persisted (rather than
    /// relying on ShotAwareTrackingService's synthetic-shot fallback) so anything that queries
    /// db.Shots directly at render time behaves identically to a normally-split video.
    /// </summary>
    private static async Task CreateSingleSceneWithoutSplittingAsync(
        PostgresDbContext db, ContentItem content, string contentId, double durationSec, CancellationToken ct)
    {
        // Clean re-run: same cascade-delete pattern as the normal path (surfaces → ad slots →
        // approvals → scenes → shots) so re-detecting a short clip doesn't leave orphans.
        var existingShots = await db.Shots.Where(s => s.ContentId == contentId).ToListAsync(ct);
        db.Shots.RemoveRange(existingShots);

        var existingScenes = await db.SceneItems.Where(s => s.ContentId == contentId).ToListAsync(ct);
        foreach (var scene in existingScenes)
        {
            var surfaces = await db.SurfaceItems.Where(sf => sf.SceneId == scene.Id).ToListAsync(ct);
            foreach (var sf in surfaces)
            {
                var adSlots = await db.AdSlots.Where(a => a.SurfaceId == sf.Id).ToListAsync(ct);
                var adSlotIds = adSlots.Select(s => s.Id).ToList();
                if (adSlotIds.Count > 0)
                {
                    var approvals = await db.Approvals
                        .Where(a => a.AdSlotId != null && adSlotIds.Contains(a.AdSlotId))
                        .ToListAsync(ct);
                    db.Approvals.RemoveRange(approvals);
                }
                db.AdSlots.RemoveRange(adSlots);
            }
            db.SurfaceItems.RemoveRange(surfaces);
        }
        db.SceneItems.RemoveRange(existingScenes);

        var fps = content.FrameRate > 0 && content.FrameRate <= 240 ? content.FrameRate : 30;
        var totalFrames = Math.Max(1, (int)(durationSec * fps));

        var newScene = new SceneItem
        {
            Id = $"sc-{Guid.NewGuid()}",
            ContentId = contentId,
            SceneIndex = 0,
            StartFrame = 0,
            EndFrame = totalFrames - 1,
            DurationSeconds = durationSec,
            QaStatus = "Unchecked",
        };
        db.SceneItems.Add(newScene);

        db.Shots.Add(new ShotItem
        {
            Id = $"sh-{Guid.NewGuid()}",
            ContentId = contentId,
            SceneId = newScene.Id,
            ShotIndex = 0,
            StartFrame = 0,
            EndFrame = totalFrames - 1,
        });

        await db.SaveChangesAsync(ct);
    }
}
