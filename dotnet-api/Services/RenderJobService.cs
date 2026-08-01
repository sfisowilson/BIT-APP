using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Hubs;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Hangfire background job for processing render tasks: compositing + video stitching.
/// Broadcasts real-time progress via SignalR BitHub.
/// </summary>
public class RenderJobService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<BitHub, IBitClient> _hubContext;
    private readonly ILogger<RenderJobService> _logger;

    public RenderJobService(IServiceProvider serviceProvider, IHubContext<BitHub, IBitClient> hubContext, ILogger<RenderJobService> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    private static string ResolveAssetPath(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey)) throw new ArgumentException("Invalid asset storage key");
        var fileName = storageKey.Replace("/api/assets/file/", "");
        return Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "assets", fileName);
    }

    private static string? ResolveVideoPath(string? storageKey)
    {
        if (string.IsNullOrEmpty(storageKey)) return null;
        var fileName = storageKey.Replace("/api/content/file/", "");
        var path = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", fileName);
        if (File.Exists(path)) return path;
        var proxyPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "proxy", fileName.Replace(".mov", "_proxy.mp4").Replace(".avi", "_proxy.mp4"));
        return File.Exists(proxyPath) ? proxyPath : null;
    }

    private static async Task RunFfmpegAsync(string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.Start();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch { } }
            throw;
        }

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"FFmpeg failed (exit {process.ExitCode}): {stderr[..Math.Min(500, stderr.Length)]}");
        }
    }

    private const long PikaswapsMaxInputBytes = 8L * 1024 * 1024;

    /// <summary>
    /// fal.ai's Pikaswaps rejects any input file over 8MB (observed live: a "downstream service
    /// error" / HTTP 413 fetching the video). Our shot chunks are extracted with "-c copy" (no
    /// re-encoding), so they inherit the source video's bitrate — easily over 8MB for a few
    /// seconds of anything above low bitrate. Only chunks that actually exceed the cap get
    /// re-encoded, at a bitrate computed to fit the remaining budget for that chunk's duration.
    ///
    /// A single calculated bitrate isn't a hard guarantee: libx264's -maxrate/-bufsize allow
    /// short-term bursts above the average, and hard-to-compress content (fast motion, noise)
    /// can still land over budget even with margin. Verify the actual output size and retry at
    /// a lower target instead of trusting the math to be exact on the first pass.
    /// </summary>
    public static async Task<string> EnsureUnderPikaswapsSizeLimitAsync(string inputPath, double durationSeconds, CancellationToken ct)
    {
        if (new FileInfo(inputPath).Length <= PikaswapsMaxInputBytes)
            return inputPath;

        const int audioBitrateBps = 96_000;
        var outputPath = Path.Combine(
            Path.GetDirectoryName(inputPath)!,
            $"{Path.GetFileNameWithoutExtension(inputPath)}_8mb.mp4");

        var marginFactor = 0.85;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var targetBits = (long)(PikaswapsMaxInputBytes * 8 * marginFactor);
            var videoBitrateBps = Math.Max(150_000, (long)(targetBits / Math.Max(0.1, durationSeconds)) - audioBitrateBps);

            var args = $"-y -hide_banner -loglevel error -i \"{inputPath.Replace("\\", "/")}\" " +
                $"-c:v libx264 -preset fast -b:v {videoBitrateBps} -maxrate {videoBitrateBps} -bufsize {videoBitrateBps / 2} " +
                $"-c:a aac -b:a {audioBitrateBps} -pix_fmt yuv420p \"{outputPath.Replace("\\", "/")}\"";
            await RunFfmpegAsync(args, ct);

            if (File.Exists(outputPath) && new FileInfo(outputPath).Length <= PikaswapsMaxInputBytes)
                return outputPath;

            marginFactor *= 0.65; // still over budget — cut the target harder and retry
        }

        return File.Exists(outputPath) ? outputPath : inputPath;
    }

    // ═══════════════════════════════════════════════════════════════
    // Interactive Placement — Generative Path (pikaswaps)
    // ═══════════════════════════════════════════════════════════════

    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task ProcessGenerativeRenderJob(string renderId, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        var eventLog = scope.ServiceProvider.GetRequiredService<IEventLogService>();
        var pikaswaps = scope.ServiceProvider.GetRequiredService<PikaswapsCompositingService>();
        var chunker = scope.ServiceProvider.GetRequiredService<VideoChunkingService>();
        var gemini = scope.ServiceProvider.GetRequiredService<GeminiDetectionService>();
        var platformSettings = scope.ServiceProvider.GetRequiredService<IPlatformSettingsService>();
        var shotTracker = scope.ServiceProvider.GetRequiredService<IShotAwareTrackingService>();
        var tracker = scope.ServiceProvider.GetRequiredService<ISurfaceTrackingService>();

        var render = await db.Renders.FindAsync(new object[] { renderId }, cancellationToken);
        if (render == null) return;

        try
        {
            // Phase 1: Validate (5% → 10%)
            render.Progress = 5; render.RenderStatus = "Processing";
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 5, "Validating");

            if (string.IsNullOrEmpty(render.SurfaceId))
                throw new InvalidOperationException("ProcessGenerativeRenderJob requires a SurfaceId (Interactive placement only — PromptEdit renders use ProcessPromptPreviewJob).");

            var content = await db.ContentItems.FindAsync(new object[] { render.ContentId }, cancellationToken);
            var surface = await db.SurfaceItems.FindAsync(new object[] { render.SurfaceId }, cancellationToken);
            var asset = await db.CreativeAssets.FindAsync(new object[] { render.AssetId }, cancellationToken);
            if (content == null || surface == null || asset == null)
                throw new InvalidOperationException("Content, surface, or asset not found.");

            var videoPath = ResolveVideoPath(content.StorageKey);
            var assetPath = ResolveAssetPath(asset.StorageKey);
            if (!File.Exists(videoPath) || !File.Exists(assetPath))
                throw new InvalidOperationException("Video or asset file not found.");

            var fps = content.FrameRate > 0 ? content.FrameRate : 30;
            var (videoWidth, videoHeight) = VideoProbe.GetDimensions(videoPath);
            var videoBaseUrl = await platformSettings.GetAsync("sam3_video_base_url", "http://localhost:57220");
            var assetFileName = Path.GetFileName(assetPath);
            var assetUrl = $"{videoBaseUrl}/api/assets/file/{assetFileName}";

            // ── Phase 2: Shot-aware mask tracking (10% → 20%) — feeds the drift-check and luma fallback ──
            await _hubContext.Clients.All.RenderProgress(renderId, 10, "Tracking across shots");

            var seedPoints = ParsePoints(surface.BoundaryCoordinatesJson);
            var seedBox = seedPoints.Count >= 2
                ? (xMin: seedPoints.Min(p => p.x), yMin: seedPoints.Min(p => p.y), xMax: seedPoints.Max(p => p.x), yMax: seedPoints.Max(p => p.y))
                : (xMin: 0, yMin: 0, xMax: videoWidth, yMax: videoHeight);

            var trackResult = await shotTracker.TrackMaskAcrossShotsAsync(
                surface.SceneId, videoPath, seedBox, surface.DetectedAtFrame ?? 0,
                surface.Sam3Prompt, surface.SurfaceType, cancellationToken);

            surface.TrackingDataJson = trackResult.TrackingDataJson;
            surface.TrackingPointsJson = trackResult.TrackingPointsJson;
            surface.TrackingStatus = trackResult.OverallStatus;
            await db.SaveChangesAsync(cancellationToken);

            // Pikaswaps composites from modify_region/prompt TEXT alone (see CompositeWithPromptAsync's
            // call site below — it never receives trackResult's mask/box data). Tracking is only used
            // here for shot boundaries (redundant with the DB's own ShotItem rows in the common case)
            // and the post-compositing drift-check, which already tolerates missing frames per shot.
            // So a tracking failure — even a LockLost seed shot — doesn't mean Pikaswaps can't do its
            // job; don't fail the whole render over it, just log it for visibility.
            if (trackResult.OverallStatus == "LockLost")
                await eventLog.LogEventAsync("RenderEngine", "TRACKING_LOCK_LOST", "Warning",
                    $"Render {renderId}: shot-aware tracking lost the surface in its seed shot. " +
                    "Compositing will still be attempted on every shot via Pikaswaps' own text-driven " +
                    "detection; the drift-check quality score may be less reliable without tracked frames.");

            var segments = ParseGenerativeShotSegments(trackResult.TrackingDataJson);
            if (segments.Count == 0)
                throw new InvalidOperationException("No shot segments available for generative compositing.");

            render.Progress = 20;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 20, "Generating prompt");

            // ── Phase 3: Gemini prompt (20% → 25%) — shared across shots in this scene, since
            // ShotClusteringService only groups visually-similar shots into one scene ──
            var (modifyRegion, prompt) = await gemini.GeneratePikaswapsPromptAsync(
                surface.SurfaceType, asset.Name ?? "brand asset");

            if (string.IsNullOrEmpty(modifyRegion) || string.IsNullOrEmpty(prompt))
            {
                modifyRegion = surface.SurfaceType;
                prompt = $"replace with a {asset.Name} advertisement, photorealistic, preserving the asset's exact text, wording, logo, and colors — only adjust lighting and perspective to fit naturally";
            }

            await eventLog.LogEventAsync("RenderEngine", "GEMINI_PROMPT_COMPLETE", "Info",
                $"Generative render {renderId}: modify_region='{modifyRegion}', prompt='{prompt}'");

            render.Progress = 25;
            await db.SaveChangesAsync(cancellationToken);

            // Work files live under Uploads/ (not the OS temp dir) so they're servable via
            // /api/content/file/... — pikaswaps fetches chunks by URL, not by local path.
            var workRelDir = $"tmp-renders/{renderId}";
            var workAbsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "tmp-renders", renderId);
            Directory.CreateDirectory(workAbsDir);

            // ── Phase 4: Per-shot compositing, never straddling a cut (25% → 80%) ──
            var shotChunks = new List<VideoChunkingService.VideoChunk>();
            var totalShots = segments.Count;
            var shotsDone = 0;

            foreach (var seg in segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var shotStartSec = seg.StartFrame / fps;
                var shotDurationSec = (seg.EndFrame - seg.StartFrame + 1) / fps;
                string shotOutputPath;

                // Every shot gets a genuine Pikaswaps attempt regardless of tracking coverage — see
                // the note above Phase 2: Pikaswaps composites from modify_region/prompt text alone,
                // so a shot tracking couldn't lock onto is not a reason to skip it.
                var shotSourcePath = Path.Combine(workAbsDir, $"shot_{seg.ShotIndex}_src.mp4");
                var extractArgs = $"-y -hide_banner -loglevel error " +
                    $"-ss {shotStartSec:F3} -i \"{videoPath.Replace("\\", "/")}\" " +
                    $"-t {shotDurationSec:F3} -c copy \"{shotSourcePath.Replace("\\", "/")}\"";
                await RunFfmpegAsync(extractArgs, cancellationToken);

                if (shotDurationSec <= 4.75)
                {
                    var compositeSourcePath = await EnsureUnderPikaswapsSizeLimitAsync(shotSourcePath, shotDurationSec, cancellationToken);
                    var shotVideoUrl = $"{videoBaseUrl}/api/content/file/{workRelDir}/{Path.GetFileName(compositeSourcePath)}";
                    var processedPath = await pikaswaps.CompositeWithPromptAsync(
                        shotVideoUrl, assetUrl, modifyRegion, prompt, $"{render.SurfaceId}_s{seg.ShotIndex}", ct: cancellationToken);
                    shotOutputPath = processedPath ?? shotSourcePath; // fall back to un-composited shot on failure
                }
                else
                {
                    // Shot exceeds pikaswaps' 4.75s limit — sub-split just this shot's own clip.
                    var subDir = Path.Combine(workAbsDir, $"shot_{seg.ShotIndex}_sub");
                    Directory.CreateDirectory(subDir);
                    var subChunks = await chunker.SplitByShotBoundariesAsync(
                        shotSourcePath, subDir, fps, new List<(double, double)> { (0, shotDurationSec) });

                    foreach (var sub in subChunks)
                    {
                        var compositeSubPath = await EnsureUnderPikaswapsSizeLimitAsync(sub.SourceChunkPath, sub.DurationSeconds, cancellationToken);
                        var subUrl = $"{videoBaseUrl}/api/content/file/{workRelDir}/shot_{seg.ShotIndex}_sub/{Path.GetFileName(compositeSubPath)}";
                        try
                        {
                            var processed = await pikaswaps.CompositeWithPromptAsync(
                                subUrl, assetUrl, modifyRegion, prompt, $"{render.SurfaceId}_s{seg.ShotIndex}_c{sub.Index}", ct: cancellationToken);
                            if (processed != null) sub.ProcessedChunkPath = processed; else sub.Failed = true;
                        }
                        catch { sub.Failed = true; }
                    }

                    shotOutputPath = Path.Combine(workAbsDir, $"shot_{seg.ShotIndex}.mp4");
                    await chunker.SpliceChunksAsync(subChunks, shotSourcePath, shotOutputPath, fps);
                    try { Directory.Delete(subDir, true); } catch { }
                }

                shotChunks.Add(new VideoChunkingService.VideoChunk
                {
                    Index = seg.ShotIndex, SourceChunkPath = shotOutputPath,
                    StartTimeSeconds = shotStartSec, DurationSeconds = shotDurationSec,
                });

                shotsDone++;
                var pct = 25 + (int)(55.0 * shotsDone / totalShots);
                await _hubContext.Clients.All.RenderProgress(renderId, pct, $"Composited shot {shotsDone}/{totalShots}");
            }

            // ── Phase 5: Splice shots back into one scene-length clip (80% → 85%) ──
            await _hubContext.Clients.All.RenderProgress(renderId, 80, "Splicing shots");
            var finalVideoPath = Path.Combine(workAbsDir, $"spliced_{renderId}.mp4");
            await chunker.SpliceChunksAsync(shotChunks.OrderBy(c => c.Index).ToList(), videoPath, finalVideoPath, fps);

            // ── Phase 6: Drift check (85% → 90%) — re-detect the surface in the composited output
            // and compare against the pre-composite tracked mask; flags NeedsReview, doesn't fail the render ──
            await _hubContext.Clients.All.RenderProgress(renderId, 85, "QA drift-check");
            var driftIoU = await RunDriftCheckAsync(
                tracker, finalVideoPath, segments, surface.Sam3Prompt, surface.SurfaceType,
                videoWidth, videoHeight, cancellationToken);

            // ── Phase 7: Finalize ──
            var rendersDir = Path.Combine(Directory.GetCurrentDirectory(), "renders");
            Directory.CreateDirectory(rendersDir);
            var outputPath = Path.Combine(rendersDir, $"BIT_Render_{renderId}.mp4");
            File.Copy(finalVideoPath, outputPath, overwrite: true);

            try { Directory.Delete(workAbsDir, true); } catch { }

            var needsReview = trackResult.OverallStatus == "PartialCoverage" || driftIoU < 0.85;
            render.Progress = 100;
            render.RenderStatus = needsReview ? "NeedsReview" : "Finished";
            render.CompositingEngine = "pikaswaps";
            render.QualityTier = "AI";
            render.StorageKey = $"/api/renders/{renderId}/download";
            render.ProcessingDurationMs = (int)sw.ElapsedMilliseconds;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 100, "Complete");

            await eventLog.LogEventAsync("RenderEngine", "GENERATIVE_RENDER_COMPLETE", "Info",
                $"Generative render {renderId}: {totalShots} shots ({trackResult.OverallStatus}), driftIoU={driftIoU:F2}, {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GenerativeRender] Render {RenderId} FAILED", renderId);
            render.RenderStatus = "Failed";
            render.LastErrorMessage = ex.Message;
            render.CompositingEngine = "pikaswaps";
            await db.SaveChangesAsync(cancellationToken);
            await eventLog.LogEventAsync("RenderEngine", "GENERATIVE_RENDER_FAILED", "Warning",
                $"Render {renderId} failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Re-detects the surface in the composited output at each shot's seed frame and compares
    /// its bounding box against the pre-composite tracked mask via IoU. A low score means the
    /// compositing pass likely drifted off the intended surface. Returns 1.0 (no penalty) if
    /// there's nothing trackable to compare.
    /// </summary>
    private static async Task<double> RunDriftCheckAsync(
        ISurfaceTrackingService tracker, string finalVideoPath, List<GenerativeShotSegment> segments,
        string? sam3Prompt, string surfaceType, int videoWidth, int videoHeight, CancellationToken ct)
    {
        var ious = new List<double>();
        var reanchorText = string.IsNullOrWhiteSpace(sam3Prompt) ? $"the {surfaceType}" : sam3Prompt;

        foreach (var seg in segments)
        {
            if (seg.Frames.Count == 0) continue;

            var (sampleFrame, sampleRle) = seg.Frames[0];
            var originalPolygon = RleDecoder.MaskToPolygon(RleDecoder.Decode(sampleRle, videoWidth, videoHeight));
            if (originalPolygon.Count < 3) continue;
            var originalBounds = RleDecoder.PolygonBounds(originalPolygon);

            var redetected = await tracker.SegmentVideoRleAsync(
                finalVideoPath, sampleFrame, sampleFrame, textPrompt: reanchorText, promptFrame: sampleFrame, cancellationToken: ct);

            var obj = redetected.FirstOrDefault(f => f.FrameIndex == sampleFrame)?.Objects
                .OrderByDescending(o => o.Confidence).FirstOrDefault();

            if (obj == null || string.IsNullOrEmpty(obj.Rle)) { ious.Add(0); continue; }

            var newPolygon = RleDecoder.MaskToPolygon(RleDecoder.Decode(obj.Rle, videoWidth, videoHeight));
            if (newPolygon.Count < 3) { ious.Add(0); continue; }

            ious.Add(ComputeBoundsIoU(originalBounds, RleDecoder.PolygonBounds(newPolygon)));
        }

        return ious.Count > 0 ? ious.Average() : 1.0;
    }

    private static double ComputeBoundsIoU(
        (int xMin, int yMin, int xMax, int yMax) a, (int xMin, int yMin, int xMax, int yMax) b)
    {
        var ix1 = Math.Max(a.xMin, b.xMin); var iy1 = Math.Max(a.yMin, b.yMin);
        var ix2 = Math.Min(a.xMax, b.xMax); var iy2 = Math.Min(a.yMax, b.yMax);
        double interArea = Math.Max(0, ix2 - ix1) * (double)Math.Max(0, iy2 - iy1);

        double areaA = Math.Max(0, a.xMax - a.xMin) * (double)Math.Max(0, a.yMax - a.yMin);
        double areaB = Math.Max(0, b.xMax - b.xMin) * (double)Math.Max(0, b.yMax - b.yMin);
        var union = areaA + areaB - interArea;

        return union > 0 ? interArea / union : 0;
    }

    // ═══════════════════════════════════════════════════════════════
    // Interactive Placement — Planar Path (homography warp)
    // ═══════════════════════════════════════════════════════════════

    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task ProcessPlanarRenderJob(string renderId, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        var eventLog = scope.ServiceProvider.GetRequiredService<IEventLogService>();
        var planar = scope.ServiceProvider.GetRequiredService<PlanarWarpCompositingService>();
        var chunker = scope.ServiceProvider.GetRequiredService<VideoChunkingService>();
        var shotTracker = scope.ServiceProvider.GetRequiredService<IShotAwareTrackingService>();

        var render = await db.Renders.FindAsync(new object[] { renderId }, cancellationToken);
        if (render == null) return;

        try
        {
            render.Progress = 5; render.RenderStatus = "Processing";
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 5, "Validating");

            if (string.IsNullOrEmpty(render.SurfaceId))
                throw new InvalidOperationException("ProcessPlanarRenderJob requires a SurfaceId (Interactive placement only — PromptEdit renders use ProcessPromptPreviewJob).");

            var content = await db.ContentItems.FindAsync(new object[] { render.ContentId }, cancellationToken);
            var surface = await db.SurfaceItems.FindAsync(new object[] { render.SurfaceId }, cancellationToken);
            var asset = await db.CreativeAssets.FindAsync(new object[] { render.AssetId }, cancellationToken);
            if (content == null || surface == null || asset == null)
                throw new InvalidOperationException("Content, surface, or asset not found.");

            var videoPath = ResolveVideoPath(content.StorageKey);
            var assetPath = ResolveAssetPath(asset.StorageKey);
            if (!File.Exists(videoPath) || !File.Exists(assetPath))
                throw new InvalidOperationException("Video or asset file not found.");

            var fps = content.FrameRate > 0 ? content.FrameRate : 30;

            // ── Phase 2: Shot-aware quad tracking (5% → 25%) — re-anchors at every cut within the scene ──
            await _hubContext.Clients.All.RenderProgress(renderId, 10, "Tracking across shots");

            var seedQuad = ParsePoints(surface.BoundaryCoordinatesJson);
            if (seedQuad.Count < 4)
                throw new InvalidOperationException("Surface has no valid seed quad to track.");

            var trackResult = await shotTracker.TrackQuadAcrossShotsAsync(
                surface.SceneId, videoPath, seedQuad, surface.DetectedAtFrame ?? 0,
                surface.Sam3Prompt, surface.SurfaceType, cancellationToken);

            surface.TrackingDataJson = trackResult.TrackingDataJson;
            surface.TrackingPointsJson = trackResult.TrackingPointsJson;
            surface.TrackingStatus = trackResult.OverallStatus;
            await db.SaveChangesAsync(cancellationToken);

            if (trackResult.OverallStatus == "LockLost")
                throw new InvalidOperationException(
                    "Shot-aware tracking lost the surface in its seed shot (or every shot in the scene was skipped) — nothing to render.");

            var segments = ParsePlanarShotSegments(trackResult.TrackingDataJson);
            if (segments.Count == 0)
                throw new InvalidOperationException("No shot segments available for planar warp.");

            render.Progress = 25;
            await db.SaveChangesAsync(cancellationToken);

            // ── Phase 3: Per-shot extraction + compositing (25% → 80%) ──
            var rendersDir = Path.Combine(Directory.GetCurrentDirectory(), "renders");
            Directory.CreateDirectory(rendersDir);
            var workDir = Path.Combine(rendersDir, $"planar_{renderId}");
            Directory.CreateDirectory(workDir);

            var shotChunks = new List<VideoChunkingService.VideoChunk>();
            var totalShots = segments.Count;
            var shotsDone = 0;

            foreach (var seg in segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var shotStartSec = seg.StartFrame / fps;
                var shotDurationSec = (seg.EndFrame - seg.StartFrame + 1) / fps;

                if (seg.Status == "Skipped" || seg.Frames.Count == 0)
                {
                    // No tracking data for this shot — pass the source video through unmodified.
                    var passThroughPath = Path.Combine(workDir, $"shot_{seg.ShotIndex}_passthrough.mp4");
                    var passArgs = $"-y -hide_banner -loglevel error " +
                        $"-ss {shotStartSec:F3} -i \"{videoPath.Replace("\\", "/")}\" " +
                        $"-t {shotDurationSec:F3} -c copy \"{passThroughPath.Replace("\\", "/")}\"";
                    await RunFfmpegAsync(passArgs, cancellationToken);
                    shotChunks.Add(new VideoChunkingService.VideoChunk
                    {
                        Index = seg.ShotIndex, SourceChunkPath = passThroughPath,
                        StartTimeSeconds = shotStartSec, DurationSeconds = shotDurationSec,
                    });
                }
                else
                {
                    var shotFrameDir = Path.Combine(workDir, $"shot_{seg.ShotIndex}_frames");
                    Directory.CreateDirectory(shotFrameDir);
                    var safeShotDir = shotFrameDir.Replace("\\", "/");

                    var extractArgs = $"-y -hide_banner -loglevel error " +
                        $"-ss {shotStartSec:F3} -i \"{videoPath.Replace("\\", "/")}\" " +
                        $"-t {shotDurationSec:F3} -vf fps={fps} \"{safeShotDir}/raw_%06d.png\"";
                    await RunFfmpegAsync(extractArgs, cancellationToken);

                    var quadByFrame = seg.Frames.ToDictionary(f => f.frame, f => f.corners);
                    var frameCount = (int)Math.Round(shotDurationSec * (double)fps);
                    var parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken };

                    await Parallel.ForEachAsync(Enumerable.Range(1, Math.Max(frameCount, 1)), parallelOpts, async (relFrame, ct) =>
                    {
                        var rawPath = Path.Combine(shotFrameDir, $"raw_{relFrame:D6}.png");
                        if (!File.Exists(rawPath)) return;

                        var compPath = Path.Combine(shotFrameDir, $"comp_{relFrame:D6}.png");
                        var absFrame = seg.StartFrame + (relFrame - 1);

                        if (!quadByFrame.TryGetValue(absFrame, out var corners) || corners.Count < 4)
                        {
                            // Occlusion mid-shot or gap — pass this single frame through unmodified.
                            File.Copy(rawPath, compPath, overwrite: true);
                            return;
                        }

                        var quadCorners = corners.Select(c => ((double)c.x, (double)c.y)).ToList();
                        var ok = await planar.CompositeFrameAsync(rawPath, assetPath, quadCorners, compPath);
                        if (!ok) { File.Copy(rawPath, compPath, overwrite: true); return; }

                        var wall = planar.ComputeWallRegion(quadCorners, content.Width, content.Height);
                        var relitPath = Path.Combine(shotFrameDir, $"relit_{relFrame:D6}.png");
                        await planar.RelightFrameAsync(compPath, rawPath, wall, relitPath);
                        if (File.Exists(relitPath))
                        {
                            File.Delete(compPath);
                            File.Move(relitPath, compPath);
                        }
                    });

                    var shotOutputPath = Path.Combine(workDir, $"shot_{seg.ShotIndex}.mp4");
                    await planar.EncodeToMp4Async(shotFrameDir, shotOutputPath, fps);
                    shotChunks.Add(new VideoChunkingService.VideoChunk
                    {
                        Index = seg.ShotIndex, SourceChunkPath = shotOutputPath,
                        StartTimeSeconds = shotStartSec, DurationSeconds = shotDurationSec,
                    });

                    try { Directory.Delete(shotFrameDir, true); } catch { }
                }

                shotsDone++;
                var pct = 25 + (int)(55.0 * shotsDone / totalShots);
                await _hubContext.Clients.All.RenderProgress(renderId, pct, $"Composited shot {shotsDone}/{totalShots}");
            }

            // ── Phase 4: Splice shots back into one scene-length clip (80% → 100%) ──
            render.Progress = 85;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 85, "Splicing shots");

            var outputPath = Path.Combine(rendersDir, $"BIT_Render_{renderId}.mp4");
            await chunker.SpliceChunksAsync(
                shotChunks.OrderBy(c => c.Index).ToList(), videoPath, outputPath, fps);

            // Cleanup
            try { Directory.Delete(workDir, true); } catch { }

            render.Progress = 100;
            render.RenderStatus = trackResult.OverallStatus == "PartialCoverage" ? "NeedsReview" : "Finished";
            render.CompositingEngine = "PlanarWarp";
            render.QualityTier = "Exact";
            render.StorageKey = $"/api/renders/{renderId}/download";
            render.ProcessingDurationMs = (int)sw.ElapsedMilliseconds;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 100, "Complete");

            await eventLog.LogEventAsync("RenderEngine", "PLANAR_RENDER_COMPLETE", "Info",
                $"Planar render {renderId}: {totalShots} shots ({trackResult.OverallStatus}) in {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PlanarRender] Render {RenderId} FAILED", renderId);
            render.RenderStatus = "Failed";
            render.LastErrorMessage = ex.Message;
            render.CompositingEngine = "PlanarWarp";
            await db.SaveChangesAsync(cancellationToken);
            await eventLog.LogEventAsync("RenderEngine", "PLANAR_RENDER_FAILED", "Warning",
                $"Render {renderId} failed: {ex.Message}");
            throw;
        }
    }

    // ── Helpers for new render jobs ──

    /// <summary>One shot's tracking outcome, parsed from the shot-segmented TrackingDataJson.</summary>
    private class PlanarShotSegment
    {
        public int ShotIndex { get; set; }
        public int StartFrame { get; set; }
        public int EndFrame { get; set; }
        public string Status { get; set; } = "Skipped";
        public List<(int frame, List<(int x, int y)> corners)> Frames { get; set; } = new();
    }

    private static List<(int x, int y)> ParsePoints(string json)
    {
        var result = new List<(int, int)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var pt in doc.RootElement.EnumerateArray())
            {
                int x = pt.TryGetProperty("x", out var px) ? px.GetInt32() : pt.TryGetProperty("X", out var pX) ? pX.GetInt32() : 0;
                int y = pt.TryGetProperty("y", out var py) ? py.GetInt32() : pt.TryGetProperty("Y", out var pY) ? pY.GetInt32() : 0;
                result.Add((x, y));
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Parse ShotAwareTrackingService's shot-segmented TrackingDataJson into per-shot quad data.
    /// Falls back to treating a legacy flat frame array (pre-shot-aware surfaces) as one segment.
    /// </summary>
    private static List<PlanarShotSegment> ParsePlanarShotSegments(string trackingDataJson)
    {
        var result = new List<PlanarShotSegment>();
        try
        {
            using var doc = JsonDocument.Parse(trackingDataJson);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("shotSegments", out var segs))
            {
                foreach (var seg in segs.EnumerateArray())
                {
                    var parsed = new PlanarShotSegment
                    {
                        ShotIndex = seg.TryGetProperty("shotIndex", out var si) ? si.GetInt32() : 0,
                        StartFrame = seg.TryGetProperty("startFrame", out var sf) ? sf.GetInt32() : 0,
                        EndFrame = seg.TryGetProperty("endFrame", out var ef) ? ef.GetInt32() : 0,
                        Status = seg.TryGetProperty("status", out var st) ? (st.GetString() ?? "Skipped") : "Skipped",
                    };

                    if (seg.TryGetProperty("frames", out var frames))
                    {
                        foreach (var frame in frames.EnumerateArray())
                            AddQuadFrame(parsed.Frames, frame);
                    }
                    result.Add(parsed);
                }
                return result;
            }

            // Legacy flat-array fallback (surfaces tracked before shot-aware tracking existed).
            if (root.ValueKind == JsonValueKind.Array)
            {
                var legacyFrames = new List<(int frame, List<(int x, int y)> corners)>();
                foreach (var frame in root.EnumerateArray())
                    AddQuadFrame(legacyFrames, frame);

                if (legacyFrames.Count > 0)
                {
                    result.Add(new PlanarShotSegment
                    {
                        ShotIndex = 0,
                        StartFrame = legacyFrames.Min(f => f.frame),
                        EndFrame = legacyFrames.Max(f => f.frame),
                        Status = "Tracked",
                        Frames = legacyFrames,
                    });
                }
            }
        }
        catch { }
        return result;
    }

    private static void AddQuadFrame(List<(int frame, List<(int x, int y)> corners)> into, JsonElement frame)
    {
        var fi = frame.TryGetProperty("frame", out var f) ? f.GetInt32() : 0;
        var corners = new List<(int x, int y)>();
        if (frame.TryGetProperty("corners", out var c))
        {
            foreach (var pt in c.EnumerateArray())
            {
                int x = pt.TryGetProperty("x", out var px) ? px.GetInt32() : 0;
                int y = pt.TryGetProperty("y", out var py) ? py.GetInt32() : 0;
                corners.Add((x, y));
            }
        }
        if (corners.Count >= 4) into.Add((fi, corners));
    }

    /// <summary>One shot's tracking outcome for the Generative path (raw RLE per frame, not a fitted quad).</summary>
    private class GenerativeShotSegment
    {
        public int ShotIndex { get; set; }
        public int StartFrame { get; set; }
        public int EndFrame { get; set; }
        public string Status { get; set; } = "Skipped";
        public List<(int frame, string rle)> Frames { get; set; } = new();
    }

    /// <summary>Parse ShotAwareTrackingService's shot-segmented TrackingDataJson for the Generative path.</summary>
    private static List<GenerativeShotSegment> ParseGenerativeShotSegments(string trackingDataJson)
    {
        var result = new List<GenerativeShotSegment>();
        try
        {
            using var doc = JsonDocument.Parse(trackingDataJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("shotSegments", out var segs))
                return result;

            foreach (var seg in segs.EnumerateArray())
            {
                var parsed = new GenerativeShotSegment
                {
                    ShotIndex = seg.TryGetProperty("shotIndex", out var si) ? si.GetInt32() : 0,
                    StartFrame = seg.TryGetProperty("startFrame", out var sf) ? sf.GetInt32() : 0,
                    EndFrame = seg.TryGetProperty("endFrame", out var ef) ? ef.GetInt32() : 0,
                    Status = seg.TryGetProperty("status", out var st) ? (st.GetString() ?? "Skipped") : "Skipped",
                };

                if (seg.TryGetProperty("frames", out var frames))
                {
                    foreach (var frame in frames.EnumerateArray())
                    {
                        var fi = frame.TryGetProperty("frame", out var f) ? f.GetInt32() : 0;
                        var rle = frame.TryGetProperty("rle", out var r) ? r.GetString() ?? string.Empty : string.Empty;
                        if (!string.IsNullOrEmpty(rle)) parsed.Frames.Add((fi, rle));
                    }
                }
                result.Add(parsed);
            }
        }
        catch { }
        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    // Interactive Placement — Prompt-Based Path (Kling O1)
    //
    // No SurfaceItem, no click/quad geometry, no SAM3 shot-aware tracking — the AI model infers
    // placement purely from a free-text prompt + the asset image. Two-phase, human-in-the-loop:
    // ProcessPromptPreviewJob generates a preview and stops (RenderStatus "PreviewReady"); only
    // ProcessPromptSpliceJob (enqueued by a separate user approval) commits it into the final video.
    // ═══════════════════════════════════════════════════════════════

    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task ProcessPromptPreviewJob(string renderId, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        var eventLog = scope.ServiceProvider.GetRequiredService<IEventLogService>();
        var kling = scope.ServiceProvider.GetRequiredService<KlingPromptEditService>();
        var chunker = scope.ServiceProvider.GetRequiredService<VideoChunkingService>();
        var platformSettings = scope.ServiceProvider.GetRequiredService<IPlatformSettingsService>();

        var render = await db.Renders.FindAsync(new object[] { renderId }, cancellationToken);
        if (render == null) return;

        try
        {
            // Phase 1: Validate (5% → 10%)
            render.Progress = 5; render.RenderStatus = "Processing";
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 5, "Validating");

            if (string.IsNullOrEmpty(render.SceneId))
                throw new InvalidOperationException("ProcessPromptPreviewJob requires a SceneId.");
            if (string.IsNullOrEmpty(render.PromptText))
                throw new InvalidOperationException("ProcessPromptPreviewJob requires PromptText.");

            var content = await db.ContentItems.FindAsync(new object[] { render.ContentId }, cancellationToken);
            var scene = await db.SceneItems.FindAsync(new object[] { render.SceneId }, cancellationToken);
            var asset = await db.CreativeAssets.FindAsync(new object[] { render.AssetId }, cancellationToken);
            if (content == null || scene == null || asset == null)
                throw new InvalidOperationException("Content, scene, or asset not found.");

            // Re-validate duration server-side — belt-and-suspenders alongside the controller's
            // own check, same pattern as the existing jobs re-checking File.Exists.
            if (scene.DurationSeconds < KlingPromptEditService.MinPromptEditDurationSeconds ||
                scene.DurationSeconds > KlingPromptEditService.MaxPromptEditDurationSeconds)
                throw new InvalidOperationException(
                    $"Scene duration {scene.DurationSeconds:F1}s is outside the allowed " +
                    $"{KlingPromptEditService.MinPromptEditDurationSeconds}-{KlingPromptEditService.MaxPromptEditDurationSeconds}s window for AI-generated placement.");

            var assetPath = ResolveAssetPath(asset.StorageKey);
            if (!File.Exists(assetPath))
                throw new InvalidOperationException("Asset file not found.");

            var videoBaseUrl = await platformSettings.GetAsync("sam3_video_base_url", "http://localhost:57220");
            var assetFileName = Path.GetFileName(assetPath);
            var assetUrl = $"{videoBaseUrl}/api/assets/file/{assetFileName}";

            render.Progress = 10;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 10, "Extracting scene clip");

            // ── Extract the scene's own clip (10% → 20%) — no shot chunking, one clip, one call ──
            var workDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "tmp-renders", renderId);
            Directory.CreateDirectory(workDir);
            var sceneClipPath = Path.Combine(workDir, "scene_src.mp4");
            await chunker.ExtractSceneClipAsync(scene, content, sceneClipPath, cancellationToken,
                maxDimension: KlingPromptEditService.MaxPromptEditResolutionPx);
            var sceneClipUrl = $"{videoBaseUrl}/api/content/file/tmp-renders/{renderId}/scene_src.mp4";

            render.Progress = 20;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 20, "Generating with Kling O1");

            // ── Call Kling O1 (20% → 85%) — single call, no per-shot loop, no SAM3 tracking ──
            var previewPath = await kling.EditWithPromptAsync(
                sceneClipUrl, assetUrl, render.PromptText, renderId, ct: cancellationToken);

            if (previewPath == null)
                throw new InvalidOperationException("Kling O1 did not return a generated video.");

            render.Progress = 85;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 85, "Saving preview");

            // ── Stop for preview (85% → 90%) — the job ends here. No splice, no Finished status. ──
            var rendersDir = Path.Combine(Directory.GetCurrentDirectory(), "renders");
            Directory.CreateDirectory(rendersDir);
            var previewDestPath = Path.Combine(rendersDir, $"BIT_Preview_{renderId}.mp4");
            File.Copy(previewPath, previewDestPath, overwrite: true);

            render.PreviewStorageKey = $"/api/renders/{renderId}/preview";
            render.RenderStatus = "PreviewReady";
            render.Progress = 90;
            render.ProcessingDurationMs = (int)sw.ElapsedMilliseconds;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 90, "Preview ready — awaiting approval");

            await eventLog.LogEventAsync("RenderEngine", "PROMPT_PREVIEW_COMPLETE", "Info",
                $"Prompt preview {renderId}: scene {scene.Id} in {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PromptPreview] Render {RenderId} FAILED", renderId);
            render.RenderStatus = "Failed";
            render.LastErrorMessage = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            await eventLog.LogEventAsync("RenderEngine", "PROMPT_PREVIEW_FAILED", "Warning",
                $"Render {renderId} failed: {ex.Message}");
            // A guard-check failure can throw before any progress push above ever fires — without
            // this, a connected client has no live signal that the job died and is stuck showing
            // whatever phase it last saw (e.g. "Generating...") indefinitely until a manual refresh.
            await _hubContext.Clients.All.RenderProgress(renderId, render.Progress, "Failed");
            throw;
        }
    }

    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task ProcessPromptSpliceJob(string renderId, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        var eventLog = scope.ServiceProvider.GetRequiredService<IEventLogService>();

        var render = await db.Renders.FindAsync(new object[] { renderId }, cancellationToken);
        if (render == null) return;

        try
        {
            // ApproveSpliceAsync always flips status to "Processing" before enqueueing this job
            // (closing the double-click race window), so "Processing" — not "PreviewReady" — is
            // the correct expected state here.
            if (render.RenderStatus != "Processing")
                throw new InvalidOperationException(
                    $"Render '{renderId}' is not awaiting approval (status: '{render.RenderStatus}').");
            if (string.IsNullOrEmpty(render.SceneId))
                throw new InvalidOperationException("ProcessPromptSpliceJob requires a SceneId.");

            var content = await db.ContentItems.FindAsync(new object[] { render.ContentId }, cancellationToken);
            var scene = await db.SceneItems.FindAsync(new object[] { render.SceneId }, cancellationToken);
            if (content == null || scene == null)
                throw new InvalidOperationException("Content or scene not found.");

            var videoPath = ResolveVideoPath(content.StorageKey);
            if (videoPath == null || !File.Exists(videoPath))
                throw new InvalidOperationException("Source video file not found.");

            var previewPath = Path.Combine(Directory.GetCurrentDirectory(), "renders", $"BIT_Preview_{renderId}.mp4");
            if (!File.Exists(previewPath))
                throw new InvalidOperationException("Approved preview clip not found on disk.");

            render.Progress = 90; render.RenderStatus = "Processing";
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 90, "Preparing final splice");

            var fps = content.FrameRate > 0 ? content.FrameRate : 30;
            var (videoWidth, videoHeight) = VideoProbe.GetDimensions(videoPath);

            var workDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "tmp-renders", renderId);
            Directory.CreateDirectory(workDir);

            // ── Normalize the Kling clip to match the source's resolution/fps (90% → 95%) ──
            // Kling O1's output resolution/fps won't match the source; ffmpeg concat with -c copy
            // (used below) requires matching stream parameters across every segment.
            var normalizedPath = Path.Combine(workDir, "preview_normalized.mp4");
            await RunFfmpegAsync(
                $"-y -hide_banner -loglevel error -i \"{previewPath.Replace("\\", "/")}\" " +
                $"-vf \"scale={videoWidth}:{videoHeight},setsar=1\" -r {fps:F3} " +
                $"-c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p -an " +
                $"\"{normalizedPath.Replace("\\", "/")}\"",
                cancellationToken);

            render.Progress = 95;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 95, "Splicing into full video");

            // ── Splice the normalized clip into the full source in place of the original scene span (95% → 98%) ──
            var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "renders", $"BIT_Render_{renderId}.mp4");
            await SpliceSceneReplacementAsync(videoPath, normalizedPath, scene, fps, videoWidth, videoHeight, workDir, outputPath, cancellationToken);

            render.Progress = 100;
            render.RenderStatus = "Finished";
            render.CompositingEngine = "kling-o1-edit";
            render.QualityTier = "AI";
            render.StorageKey = $"/api/renders/{renderId}/download";
            render.ProcessingDurationMs += (int)sw.ElapsedMilliseconds;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 100, "Complete");

            await eventLog.LogEventAsync("RenderEngine", "PROMPT_SPLICE_COMPLETE", "Info",
                $"Prompt splice {renderId}: scene {scene.Id} in {sw.Elapsed.TotalSeconds:F1}s");

            try { Directory.Delete(workDir, true); } catch { }
            try { File.Delete(previewPath); } catch { }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PromptSplice] Render {RenderId} FAILED", renderId);
            render.RenderStatus = "Failed";
            render.LastErrorMessage = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            await eventLog.LogEventAsync("RenderEngine", "PROMPT_SPLICE_FAILED", "Warning",
                $"Render {renderId} failed: {ex.Message}");
            // See the matching comment in ProcessPromptPreviewJob's catch block — a guard-check
            // failure here throws before the first progress push, so without this the approving
            // client never learns the splice died.
            await _hubContext.Clients.All.RenderProgress(renderId, render.Progress, "Failed");
            throw;
        }
    }

    /// <summary>
    /// Splice a single normalized clip into the full source video in place of one scene's frame
    /// span: [0, sceneStart) + clip + (sceneEnd, videoEnd]. Deliberately not routed through
    /// VideoChunkingService.SpliceChunksAsync — that method tiles a video into consecutive
    /// shot-aligned chunks, which doesn't fit "replace exactly one scene, keep everything else
    /// untouched" (this flow has no shots/tracking data to chunk against).
    ///
    /// v1 limitation: all three segments are re-encoded audio-free (-an). ffmpeg's concat demuxer
    /// with -c copy requires every segment to share identical stream layouts, and the Kling clip's
    /// audio (if any) can't be guaranteed to match the source's codec/sample rate — muting
    /// uniformly is the reliable choice for a first version. Audio preservation for prompt-edited
    /// scenes is a documented follow-up, not silently half-implemented here.
    /// </summary>
    private static async Task SpliceSceneReplacementAsync(
        string sourceVideoPath, string normalizedClipPath, SceneItem scene, double fps,
        int videoWidth, int videoHeight, string workDir, string outputPath, CancellationToken ct)
    {
        var sceneStart = scene.StartFrame / fps;
        var sceneEnd = scene.EndFrame / fps;
        var totalDuration = VideoProbe.GetDurationSeconds(sourceVideoPath);

        var hasBefore = sceneStart > 0.05;
        var hasAfter = totalDuration > 0 && sceneEnd < totalDuration - 0.05;

        var beforePath = Path.Combine(workDir, "splice_before.mp4");
        var afterPath = Path.Combine(workDir, "splice_after.mp4");

        if (hasBefore)
        {
            await RunFfmpegAsync(
                $"-y -hide_banner -loglevel error -i \"{sourceVideoPath.Replace("\\", "/")}\" -t {sceneStart:F3} " +
                $"-vf \"scale={videoWidth}:{videoHeight},setsar=1\" -r {fps:F3} " +
                $"-c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p -an " +
                $"\"{beforePath.Replace("\\", "/")}\"",
                ct);
        }

        if (hasAfter)
        {
            await RunFfmpegAsync(
                $"-y -hide_banner -loglevel error -ss {sceneEnd:F3} -i \"{sourceVideoPath.Replace("\\", "/")}\" " +
                $"-vf \"scale={videoWidth}:{videoHeight},setsar=1\" -r {fps:F3} " +
                $"-c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p -an " +
                $"\"{afterPath.Replace("\\", "/")}\"",
                ct);
        }

        var concatListPath = Path.Combine(workDir, "concat_scene_splice.txt");
        var lines = new List<string>();
        if (hasBefore) lines.Add($"file '{beforePath.Replace("\\", "/")}'");
        lines.Add($"file '{normalizedClipPath.Replace("\\", "/")}'");
        if (hasAfter) lines.Add($"file '{afterPath.Replace("\\", "/")}'");
        await File.WriteAllLinesAsync(concatListPath, lines, ct);

        await RunFfmpegAsync(
            $"-y -hide_banner -loglevel error -f concat -safe 0 -i \"{concatListPath.Replace("\\", "/")}\" " +
            $"-c copy \"{outputPath.Replace("\\", "/")}\"",
            ct);

        try { File.Delete(concatListPath); } catch { }
    }
}
