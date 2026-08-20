using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Phase 1 unified surface detection pipeline.
///
/// Replaces the old Python-dependent pipeline with a pure .NET flow:
///   1. FFmpeg scene cut detection
///   2. Gemini 2.0 Flash -> surface candidates + brand safety
///   3. Fal.ai SAM 2 -> pixel-perfect polygon masks (on high-viability surfaces)
///   4. Persist to database
/// </summary>
public class SurfaceDetectionPipeline
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SurfaceDetectionPipeline> _logger;

    public SurfaceDetectionPipeline(IServiceProvider serviceProvider, ILogger<SurfaceDetectionPipeline> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Run the full detection pipeline for a content item.
    /// Called as a Hangfire background job.
    /// </summary>
    public async Task RunAsync(string contentId, CancellationToken ct, bool runSurfaceDetection = true)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        var contentService = scope.ServiceProvider.GetRequiredService<IContentService>();
        var engineFactory = scope.ServiceProvider.GetRequiredService<IEngineFactory>();
        var sam2 = scope.ServiceProvider.GetRequiredService<FalAiSam2Service>();
        var settings = scope.ServiceProvider.GetRequiredService<IPlatformSettingsService>();
        var eventLog = scope.ServiceProvider.GetRequiredService<IEventLogService>();

        var content = await db.ContentItems.FindAsync(contentId);
        if (content == null) return;

        // Resolved once per run (not per scene) — the configured engine_detection setting
        // doesn't change mid-run. Was previously hardcoded to GeminiDetectionService,
        // silently ignoring engine_detection entirely.
        var surfaceDetection = await engineFactory.GetSurfaceDetectionEngineAsync();

        try
        {
            content.JobState = "Processing";
            await TransitionToSceneDetecting(contentService, content, ct);

            content.DetectionProgress = 5;
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("[Pipeline] Starting scene detection for {ContentId} ({Title})", contentId, content.Title);

            content.DetectionProgress = 10;
            await db.SaveChangesAsync(ct);

            // ── Scenes already clustered by ShotDetectionPipeline — read from DB ──
            var sceneItems = await db.SceneItems
                .Where(s => s.ContentId == contentId)
                .OrderBy(s => s.SceneIndex)
                .ToListAsync(ct);

            var fps = content.FrameRate > 0 && content.FrameRate <= 240 ? content.FrameRate : 30;
            var totalFrames = (int)(ParseDuration(content.Duration) * fps);

            var scenes = sceneItems.Select(s => new SceneCut
            {
                SceneIndex = s.SceneIndex,
                StartFrame = s.StartFrame,
                EndFrame = s.EndFrame,
                DurationSeconds = s.DurationSeconds,
            }).ToList();

            _logger.LogInformation("[Pipeline] Reading {Count} clustered scenes from DB for {Title}", scenes.Count, content.Title);

            content.DetectionProgress = 40;
            await db.SaveChangesAsync(ct);

            var videoPath = ResolveVideoPath(content);
            if (string.IsNullOrEmpty(videoPath))
            {
                throw new InvalidOperationException($"Cannot resolve video path for {contentId}");
            }

            var totalScenes = scenes.Count;
            var surfaceCount = 0;
            var failedScenes = 0;
            var persistedScenes = new List<SceneItem>();

            for (int i = 0; i < scenes.Count; i++)
            {
                await CheckPauseOrCancellationAsync(db, contentId, ct);
                var scene = scenes[i];
                // The real, already-persisted entity from ShotClusteringService — reuse it
                // rather than creating a duplicate SceneItem row for the same scene index.
                var sceneItem = sceneItems[i];

                content.DetectionProgress = 42 + (int)(40.0 * i / totalScenes);
                await db.SaveChangesAsync(ct);

                try
                {
                    _logger.LogInformation("[Pipeline] Processing scene {Index}/{Total} frames {Start}-{End}",
                        scene.SceneIndex, totalScenes, scene.StartFrame, scene.EndFrame);

                    content.DetectionProgress = 44;
                    await db.SaveChangesAsync(ct);

                    // Gemini surface detection is the expensive part of this pipeline (up to
                    // maxFrames sampled frames per scene, each a Gemini call + rate-limit delay).
                    // Skipping it lets users get scene/shot cuts quickly and run surface
                    // detection later per-scene via RunSurfaceDetectionForSceneAsync instead.
                    var surfaces = runSurfaceDetection
                        ? await DetectSurfacesAcrossSceneAsync(
                            sceneItem, videoPath, surfaceDetection, sam2, settings, ct)
                        : new List<SurfaceItem>();

                    content.DetectionProgress = 48;
                    await db.SaveChangesAsync(ct);

                    persistedScenes.Add(sceneItem);

                    foreach (var surface in surfaces)
                    {
                        surface.SceneId = sceneItem.Id;
                        db.SurfaceItems.Add(surface);
                        surfaceCount++;
                    }

                    content.DetectionProgress = 10 + (int)(80.0 * (i + 1) / totalScenes);
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    failedScenes++;
                    _logger.LogError(ex, "[Pipeline] Scene {Index} failed for {ContentId}", scene.SceneIndex, contentId);

                    content.DetectionProgress = 10 + (int)(80.0 * (i + 1) / totalScenes);
                    await db.SaveChangesAsync(ct);

                    await eventLog.LogEventAsync("SceneDetection", "SceneWarning", "Warning",
                        $"Scene #{scene.SceneIndex} failed: {ex.Message}");
                }
            }

            content.DetectionProgress = 90;
            await db.SaveChangesAsync(ct);

            if (persistedScenes.Count > 0 && !string.IsNullOrEmpty(videoPath))
            {
                var thumbDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "thumbnails");
                Directory.CreateDirectory(thumbDir);

                for (int i = 0; i < persistedScenes.Count; i++)
                {
                    await CheckPauseOrCancellationAsync(db, contentId, ct);
                    var sceneItem = persistedScenes[i];
                    var middleFrame = (sceneItem.StartFrame + sceneItem.EndFrame) / 2;
                    var thumbFile = $"scene-{sceneItem.Id}.jpg";
                    var thumbPath = Path.Combine(thumbDir, thumbFile);

                    try
                    {
                        await GenerateThumbnailAsync(videoPath, middleFrame, thumbPath, ct);
                        sceneItem.ThumbnailPath = $"thumbnails/{thumbFile}";
                        _logger.LogInformation("[Pipeline] Thumbnail for scene {Index} -> {Path}",
                            sceneItem.SceneIndex, sceneItem.ThumbnailPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Pipeline] Thumbnail failed for scene {Index}", sceneItem.SceneIndex);
                    }
                }
                await db.SaveChangesAsync(ct);
            }

            content.IngestionStatus = PipelineStages.Completed;
            content.JobState = "Completed";
            content.IsDetectionPaused = false;
            content.SceneDetectingCompletedAt = DateTime.UtcNow;
            content.DetectionProgress = 100;
            await db.SaveChangesAsync(ct);

            var msg = $"Detection complete: {totalScenes} scenes, {surfaceCount} surfaces";
            if (failedScenes > 0) msg += $" ({failedScenes} scenes failed)";
            await eventLog.LogEventAsync("SceneDetection", "Completed", "Info", msg);

            _logger.LogInformation("[Pipeline] {Msg}", msg);
        }
        catch (OperationCanceledException)
        {
            content.IngestionStatus = PipelineStages.Failed;
            content.JobState = "Cancelled";
            content.LastErrorMessage = "Detection was cancelled.";
            content.LastErrorAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Pipeline] FAILED for {ContentId}: {Message}", contentId, ex.Message);
            content.IngestionStatus = PipelineStages.Failed;
            content.JobState = "Failed";
            content.LastErrorMessage = $"[{ex.GetType().Name}] {ex.Message}";
            content.LastErrorAt = DateTime.UtcNow;
            content.DetectionProgress = Math.Max(content.DetectionProgress, 0);
            await db.SaveChangesAsync();
            throw;
        }
    }

    /// <summary>
    /// Detect surfaces for a SINGLE scene. Triggered by the user per-scene to control costs.
    /// </summary>
    public async Task RunSurfaceDetectionForSceneAsync(string sceneId, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        var engineFactory = scope.ServiceProvider.GetRequiredService<IEngineFactory>();
        var settings = scope.ServiceProvider.GetRequiredService<IPlatformSettingsService>();
        var eventLog = scope.ServiceProvider.GetRequiredService<IEventLogService>();

        var sceneItem = await db.SceneItems.FindAsync(sceneId);
        if (sceneItem == null)
        {
            _logger.LogWarning("[Pipeline:PerScene] Scene {SceneId} not found", sceneId);
            return;
        }

        var content = await db.ContentItems.FindAsync(sceneItem.ContentId);
        if (content == null) return;

        var videoPath = ResolveVideoPath(content);
        if (string.IsNullOrEmpty(videoPath))
        {
            _logger.LogWarning("[Pipeline:PerScene] Cannot resolve video path for {ContentId}", sceneItem.ContentId);
            sceneItem.SurfaceStatus = "Failed";
            await db.SaveChangesAsync(ct);
            return;
        }

        try
        {
            sceneItem.SurfaceStatus = "Detecting";
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("[Pipeline:PerScene] Surface detection for scene {Index} ({Id})",
                sceneItem.SceneIndex, sceneId);

            var oldSurfaces = await db.SurfaceItems.Where(s => s.SceneId == sceneId).ToListAsync(ct);
            db.SurfaceItems.RemoveRange(oldSurfaces);
            await db.SaveChangesAsync(ct);

            // ── Auto-correct scene frame range using real video metadata ──
            var (realFps, realDuration) = await GetVideoInfoAsync(videoPath, ct);
            if (realFps > 0 && realDuration > 0)
            {
                var maxValidFrame = (int)(realDuration * realFps);
                if (sceneItem.EndFrame > maxValidFrame || sceneItem.StartFrame > maxValidFrame)
                {
                    _logger.LogWarning("[Pipeline:PerScene] Scene {Index} frame range {Start}-{End} is outside video ({Duration:F1}s @ {Fps}fps ≈ {MaxFrame} frames). Correcting.",
                        sceneItem.SceneIndex, sceneItem.StartFrame, sceneItem.EndFrame, realDuration, realFps, maxValidFrame);
                    sceneItem.StartFrame = Math.Max(0, Math.Min(sceneItem.StartFrame, maxValidFrame - 1));
                    sceneItem.EndFrame = Math.Min(sceneItem.EndFrame, maxValidFrame);
                    // EndFrame is inclusive — see the equivalent note in ShotDetectionPipeline.cs.
                    sceneItem.DurationSeconds = Math.Max(0.5, (sceneItem.EndFrame - sceneItem.StartFrame + 1) / realFps);
                    await db.SaveChangesAsync(ct);
                }
            }

            // Multi-frame sampling across the scene's full duration (not just one frame) —
            // see DetectSurfacesAcrossSceneAsync. sam2: null preserves this path's existing
            // behavior (no mask refinement here; only the bulk pipeline does that).
            var surfaceDetection = await engineFactory.GetSurfaceDetectionEngineAsync();
            var surfaces = await DetectSurfacesAcrossSceneAsync(
                sceneItem, videoPath, surfaceDetection, sam2: null, settings, ct);

            foreach (var surface in surfaces)
            {
                db.SurfaceItems.Add(surface);
            }

            sceneItem.SurfaceStatus = "Completed";
            await db.SaveChangesAsync(ct);

            await eventLog.LogEventAsync("SceneDetection", "SurfaceDetected", "Info",
                $"Scene #{sceneItem.SceneIndex}: {surfaces.Count} surfaces detected");
            _logger.LogInformation("[Pipeline:PerScene] Scene {Index}: {Count} surfaces",
                sceneItem.SceneIndex, surfaces.Count);
        }
        catch (OperationCanceledException)
        {
            sceneItem.SurfaceStatus = "Failed";
            await db.SaveChangesAsync();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Pipeline:PerScene] FAILED for scene {SceneId}", sceneId);
            sceneItem.SurfaceStatus = "Failed";
            await db.SaveChangesAsync();
            throw;
        }
    }

    /// <summary>
    /// Evenly distributes up to maxFrames sample points across a scene's frame range, spaced
    /// roughly sampleIntervalSec apart. Pure/static so it's unit-testable without any I/O — this
    /// is the piece that determines whether detection actually covers a scene's full length or
    /// just a single point in it.
    /// </summary>
    public static List<int> ComputeSampleFrames(int startFrame, int endFrame, double durationSeconds, double sampleIntervalSec, int maxFrames)
    {
        var sceneFrameCount = endFrame - startFrame + 1;
        var frameStep = Math.Max(1, (int)(sampleIntervalSec * (sceneFrameCount / Math.Max(0.5, durationSeconds))));
        var sampleFrames = new List<int>();

        var totalSteps = Math.Min(maxFrames, Math.Max(1, sceneFrameCount / Math.Max(1, frameStep)));
        for (int i = 0; i < totalSteps; i++)
        {
            var t = totalSteps == 1 ? 0.5 : i / (double)(totalSteps - 1);
            var frame = startFrame + (int)(t * (sceneFrameCount - 1));
            if (!sampleFrames.Contains(frame))
                sampleFrames.Add(frame);
        }
        return sampleFrames;
    }

    /// <summary>
    /// Detects surfaces across a scene by sampling multiple frames evenly across its full
    /// duration (not just the midpoint), running Gemini detection + SAM2 mask refinement per
    /// sampled frame, then deduplicating overlapping detections across frames. Shared by the
    /// bulk "AI Split Analyze" pipeline (RunAsync) and the manual per-scene re-detection path
    /// (RunSurfaceDetectionForSceneAsync) — a scene can contain surfaces that are only visible
    /// partway through it (a passing bus, a screen that lights up later), which checking only
    /// the midpoint frame would miss entirely. Pass sam2=null to skip mask refinement.
    /// </summary>
    private async Task<List<SurfaceItem>> DetectSurfacesAcrossSceneAsync(
        SceneItem sceneItem,
        string videoPath,
        ISurfaceDetectionService surfaceDetection,
        FalAiSam2Service? sam2,
        IPlatformSettingsService settings,
        CancellationToken ct)
    {
        var sampleInterval = 2.0;
        var maxFrames = 5;
        try
        {
            var intervalStr = await settings.GetAsync("gemini_sample_interval_sec", "2");
            sampleInterval = double.TryParse(intervalStr, out var iv) && iv >= 0.5 ? iv : 2.0;
            var maxStr = await settings.GetAsync("gemini_max_frames_per_scene", "5");
            maxFrames = int.TryParse(maxStr, out var mf) && mf >= 1 ? mf : 5;
        }
        catch { /* use defaults */ }

        var sampleFrames = ComputeSampleFrames(
            sceneItem.StartFrame, sceneItem.EndFrame, sceneItem.DurationSeconds, sampleInterval, maxFrames);

        _logger.LogInformation("[Pipeline] Sampling {Count} frames across scene {Index} ({Duration:F1}s, frames {Start}-{End})",
            sampleFrames.Count, sceneItem.SceneIndex, sceneItem.DurationSeconds, sceneItem.StartFrame, sceneItem.EndFrame);

        var allDetections = new List<(int frameNumber, SurfaceDetectionResult detection)>();

        foreach (var frame in sampleFrames)
        {
            ct.ThrowIfCancellationRequested();
            var frameBase64 = await ExtractKeyFrameAsync(videoPath, frame, ct);
            if (string.IsNullOrEmpty(frameBase64))
            {
                _logger.LogWarning("[Pipeline] Failed to extract frame {Frame}, skipping", frame);
                continue;
            }

            var results = await surfaceDetection.DetectAsync(
                sceneItem.ContentId, sceneItem.SceneIndex, frame, frame, ct);

            if (sam2 != null && results.Count > 0)
            {
                var boxesForSam2 = new List<List<double>>();
                var sam2Indices = new List<int>();
                for (int i = 0; i < results.Count; i++)
                {
                    var s = results[i];
                    if (s.ViabilityScore >= 0.35)
                    {
                        var coords = JsonSerializer.Deserialize<List<Coord>>(s.BoundaryCoordinatesJson);
                        if (coords != null && coords.Count >= 4)
                        {
                            var xs = coords.Select(c => (double)c.X).ToList();
                            var ys = coords.Select(c => (double)c.Y).ToList();
                            boxesForSam2.Add(new List<double> { xs.Min(), ys.Min(), xs.Max(), ys.Max() });
                            sam2Indices.Add(i);
                        }
                    }
                }

                if (boxesForSam2.Count > 0)
                {
                    _logger.LogInformation("[Pipeline] SAM2: {Count} boxes for scene {Index} frame {Frame}",
                        boxesForSam2.Count, sceneItem.SceneIndex, frame);
                    var sam2Masks = await sam2.GenerateMasksAsync(frameBase64, boxesForSam2, ct);
                    for (int sam2Idx = 0; sam2Idx < sam2Indices.Count && sam2Idx < sam2Masks.Count; sam2Idx++)
                    {
                        if (sam2Masks[sam2Idx].Count >= 4)
                        {
                            results[sam2Indices[sam2Idx]].BoundaryCoordinatesJson = JsonSerializer.Serialize(sam2Masks[sam2Idx]);
                        }
                    }
                }
            }

            foreach (var det in results)
                allDetections.Add((frame, det));

            _logger.LogInformation("[Pipeline] Frame {Frame}: {Count} surface candidates", frame, results.Count);

            // Rate-limit: pause between Gemini calls to avoid 429 when sampling multiple frames
            if (sampleFrames.Count > 1)
                await Task.Delay(1500, ct);
        }

        if (allDetections.Count == 0)
        {
            _logger.LogInformation("[Pipeline] No surfaces found in scene {Index}", sceneItem.SceneIndex);
            return new List<SurfaceItem>();
        }

        // Deduplicate across frames — same surface type + overlapping boundary → keep highest confidence
        var uniqueSurfaces = DeduplicateSurfaces(allDetections);

        var surfaces = new List<SurfaceItem>();
        foreach (var (frameNumber, det) in uniqueSurfaces)
        {
            surfaces.Add(new SurfaceItem
            {
                Id = $"sf-{Guid.NewGuid()}",
                SceneId = sceneItem.Id,
                SurfaceType = det.SurfaceType,
                BoundaryCoordinatesJson = det.BoundaryCoordinatesJson,
                EstimatedDepth = det.EstimatedDepth,
                OrientationVectorJson = det.OrientationVectorJson,
                ConfidenceScore = det.ConfidenceScore,
                ViabilityScore = det.ViabilityScore,
                Status = string.IsNullOrEmpty(det.ExclusionReason) ? "Candidate" : "Excluded",
                ExclusionReason = det.ExclusionReason,
                DetectedAtFrame = frameNumber,
                Sam3Prompt = det.Sam3Prompt,
            });
        }

        return surfaces;
    }

    private async Task TransitionToSceneDetecting(IContentService contentService, ContentItem content, CancellationToken ct)
    {
        if (content.IngestionStatus == PipelineStages.Staging)
            await contentService.TransitionStageAsync(content.Id, PipelineStages.Transcoding);
        if (content.IngestionStatus is PipelineStages.Transcoding or PipelineStages.Completed)
            await contentService.TransitionStageAsync(content.Id, PipelineStages.SceneDetecting);
    }

    /// <summary>Generate a thumbnail at the given frame number. Uses ffprobe for real FPS.</summary>
    private static async Task GenerateThumbnailAsync(string videoPath, int frameNumber, string outputPath, CancellationToken ct)
    {
        try
        {
            var (realFps, realDuration) = await GetVideoInfoAsync(videoPath, ct);
            if (realFps <= 0) realFps = 30;
            if (realDuration <= 0) realDuration = 60;
            var timeSec = Math.Clamp(frameNumber / realFps, 0.1, Math.Max(0.1, realDuration - 0.1));

            var preSeek = Math.Max(0, timeSec - 2);
            var postSeek = timeSec - preSeek;
            var preStr = preSeek.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var postStr = postSeek.ToString(System.Globalization.CultureInfo.InvariantCulture);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -ss {preStr} -noaccurate_seek -i \"{videoPath}\" -ss {postStr} -vframes 1 -q:v 3 \"{outputPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();

            // Drain stdout + stderr asynchronously to prevent buffer deadlock
            var drainStdout = process.StandardOutput.ReadToEndAsync();
            var drainStderr = process.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch { } }
            }

            await Task.WhenAll(drainStdout, drainStderr);
        }
        catch
        {
            // Non-fatal thumbnail failure
        }
    }

    /// <summary>
    /// Extract a single frame as base64 JPEG from a video file.
    /// Uses ffprobe to get the REAL video FPS (not the DB value which may be wrong),
    /// converts frame number to seconds, clamps to video duration, then uses two-pass
    /// seeking for speed across all file sizes.
    /// </summary>
    private static async Task<string?> ExtractKeyFrameAsync(string videoPath, int frameNumber, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"bit-pipe-{Guid.NewGuid():N}.jpg");

        // Get REAL video info via ffprobe — don't trust DB metadata
        var (realFps, realDurationSec) = await GetVideoInfoAsync(videoPath, ct);
        if (realFps <= 0) realFps = 30;
        if (realDurationSec <= 0) realDurationSec = 60;

        // Convert frame number → seconds using real FPS, clamp to video bounds
        var timeSec = Math.Clamp(frameNumber / realFps, 0.1, Math.Max(0.1, realDurationSec - 0.1));

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
                    Arguments = $"-y -ss {preStr} -noaccurate_seek -i \"{videoPath}\" -ss {postStr} -vf \"scale='min(1024,iw)':'min(1024,ih)':force_original_aspect_ratio=decrease\" -vframes 1 -q:v 2 \"{tempFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();

            // Drain stdout + stderr asynchronously — MANDATORY to prevent buffer deadlock
            var readStdout = process.StandardOutput.ReadToEndAsync();
            var readStderr = process.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch { } }
                throw new InvalidOperationException(
                    $"ffmpeg timed out after 60 s for video '{videoPath}' at {timeSec:F1}s");
            }

            // Ensure streams are fully drained before checking exit code
            await Task.WhenAll(readStdout, readStderr);

            if (process.ExitCode != 0)
            {
                var stderr = readStderr.Result;
                throw new InvalidOperationException(
                    $"ffmpeg exited with code {process.ExitCode} for video '{videoPath}' at {timeSec:F1}s: {stderr.Trim()}");
            }

            if (!File.Exists(tempFile) || new FileInfo(tempFile).Length < 100)
            {
                if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch { } }
                throw new InvalidOperationException(
                    $"ffmpeg produced no output for video '{videoPath}' at {timeSec:F1}s (file missing or too small)");
            }

            var bytes = await File.ReadAllBytesAsync(tempFile, ct);
            return Convert.ToBase64String(bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Failed to extract key frame at frame {frameNumber} ({timeSec:F1}s) from '{videoPath}': {ex.Message}", ex);
        }
        finally { try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { } }
    }

    private static string? ResolveVideoPath(ContentItem content)
    {
        if (!string.IsNullOrEmpty(content.StorageKey))
        {
            var fileName = content.StorageKey.Replace("/api/content/file/", "");
            var path = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", fileName);
            if (File.Exists(path)) return path;

            var proxyPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "proxies", fileName);
            if (File.Exists(proxyPath)) return proxyPath;

            var assetPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", fileName);
            if (File.Exists(assetPath)) return assetPath;
        }

        var assetsDir = Path.Combine(Directory.GetCurrentDirectory(), "assets");
        if (Directory.Exists(assetsDir))
        {
            var mp4Files = Directory.GetFiles(assetsDir, "*.mp4");
            if (mp4Files.Length > 0) return mp4Files[0];
        }
        var rootAssets = Path.Combine(Directory.GetCurrentDirectory(), "..", "assets");
        if (Directory.Exists(rootAssets))
        {
            var mp4Files = Directory.GetFiles(rootAssets, "*.mp4");
            if (mp4Files.Length > 0) return mp4Files[0];
        }
        return null;
    }

    /// <summary>Get REAL video FPS and duration from ffprobe (doesn't trust DB metadata).</summary>
    private static async Task<(double fps, double durationSec)> GetVideoInfoAsync(string videoPath, CancellationToken ct)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=r_frame_rate,duration -of csv=p=0 \"{videoPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();

            var readOut = process.StandardOutput.ReadToEndAsync();
            var readErr = process.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            await process.WaitForExitAsync(linkedCts.Token);
            await Task.WhenAll(readOut, readErr);

            var output = readOut.Result.Trim();
            // ffprobe outputs: "30/1,12.370000" or "30000/1001,12.370000"
            var parts = output.Split(',');
            double fps = 30, duration = 60;
            if (parts.Length >= 1)
            {
                var fpsPart = parts[0].Trim();
                var slashIdx = fpsPart.IndexOf('/');
                if (slashIdx > 0 &&
                    double.TryParse(fpsPart.AsSpan(0, slashIdx), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var num) &&
                    double.TryParse(fpsPart.AsSpan(slashIdx + 1), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var den) && den > 0)
                {
                    fps = num / den;
                }
                else if (double.TryParse(fpsPart, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var f))
                {
                    fps = f;
                }
            }
            if (parts.Length >= 2 && double.TryParse(parts[1].Trim(),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
            {
                duration = d;
            }
            return (fps, duration);
        }
        catch
        {
            return (30, 60); // safe fallback
        }
    }

    private static double ParseDuration(string? duration)
    {
        if (string.IsNullOrEmpty(duration)) return 60;
        var match = System.Text.RegularExpressions.Regex.Match(duration, @"^(\d{2}):([0-5]\d):([0-5]\d)$");
        if (!match.Success) return 60;
        return int.Parse(match.Groups[1].Value) * 3600 +
               int.Parse(match.Groups[2].Value) * 60 +
               int.Parse(match.Groups[3].Value);
    }

    private async Task CheckPauseOrCancellationAsync(PostgresDbContext db, string contentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var item = await db.ContentItems.FindAsync(new object[] { contentId }, ct);
        if (item == null) return;

        if (item.JobState == "Cancelled")
        {
            throw new OperationCanceledException("Detection job was cancelled by user.");
        }

        while (item.IsDetectionPaused)
        {
            _logger.LogInformation("[Pipeline] Detection job for {ContentId} is paused. Waiting...", contentId);
            await Task.Delay(2000, ct);
            await db.Entry(item).ReloadAsync(ct);
            if (item.JobState == "Cancelled")
            {
                throw new OperationCanceledException("Detection job was cancelled by user while paused.");
            }
        }
    }

    /// <summary>
    /// Deduplicate surfaces detected across multiple frames.
    /// Two surfaces are considered the same if they have the same type AND
    /// their bounding boxes overlap significantly (IoU ≥ 0.5).
    /// Keeps the highest-confidence detection for each group.
    /// </summary>
    private static List<(int frameNumber, SurfaceDetectionResult detection)> DeduplicateSurfaces(
        List<(int frameNumber, SurfaceDetectionResult detection)> allDetections)
    {
        if (allDetections.Count <= 1) return allDetections;

        // Group by surface type, then deduplicate within each group by IoU
        var byType = allDetections.GroupBy(d => d.detection.SurfaceType ?? "Unknown");
        var result = new List<(int frameNumber, SurfaceDetectionResult detection)>();

        foreach (var group in byType)
        {
            var items = group.ToList();
            var kept = new bool[items.Count];
            for (int i = 0; i < items.Count; i++) kept[i] = true;

            for (int i = 0; i < items.Count; i++)
            {
                if (!kept[i]) continue;
                var boxA = ParseBoundingBox(items[i].detection.BoundaryCoordinatesJson);
                if (boxA == null) continue;

                for (int j = i + 1; j < items.Count; j++)
                {
                    if (!kept[j]) continue;
                    var boxB = ParseBoundingBox(items[j].detection.BoundaryCoordinatesJson);
                    if (boxB == null) continue;

                    var iou = ComputeIoU(boxA.Value, boxB.Value);
                    if (iou >= 0.5)
                    {
                        // Keep the one with higher confidence
                        if (items[j].detection.ConfidenceScore > items[i].detection.ConfidenceScore)
                            kept[i] = false;
                        else
                            kept[j] = false;
                    }
                }
            }

            for (int i = 0; i < items.Count; i++)
                if (kept[i]) result.Add(items[i]);
        }

        return result;
    }

    /// <summary>Parse boundary JSON into a bounding box (minX, minY, maxX, maxY).</summary>
    private static (double minX, double minY, double maxX, double maxY)? ParseBoundingBox(string? boundaryJson)
    {
        if (string.IsNullOrEmpty(boundaryJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(boundaryJson);
            var root = doc.RootElement;

            // Gemini returns polygon points: [[x1,y1], [x2,y2], ...]
            if (root.ValueKind == JsonValueKind.Array)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                bool hasPoints = false;

                foreach (var point in root.EnumerateArray())
                {
                    if (point.ValueKind == JsonValueKind.Array && point.GetArrayLength() >= 2)
                    {
                        var x = point[0].GetDouble();
                        var y = point[1].GetDouble();
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                        hasPoints = true;
                    }
                }
                if (hasPoints) return (minX, minY, maxX, maxY);
            }

            // Fallback: try {x,y,width,height} format
            if (root.TryGetProperty("x", out var rx) && root.TryGetProperty("y", out var ry) &&
                root.TryGetProperty("width", out var rw) && root.TryGetProperty("height", out var rh))
            {
                var x = rx.GetDouble(); var y = ry.GetDouble();
                var w = rw.GetDouble(); var h = rh.GetDouble();
                return (x, y, x + w, y + h);
            }
        }
        catch { }
        return null;
    }

    /// <summary>Intersection over Union between two bounding boxes.</summary>
    private static double ComputeIoU(
        (double minX, double minY, double maxX, double maxY) a,
        (double minX, double minY, double maxX, double maxY) b)
    {
        var interX = Math.Max(0, Math.Min(a.maxX, b.maxX) - Math.Max(a.minX, b.minX));
        var interY = Math.Max(0, Math.Min(a.maxY, b.maxY) - Math.Max(a.minY, b.minY));
        var interArea = interX * interY;
        var areaA = Math.Max(0, (a.maxX - a.minX) * (a.maxY - a.minY));
        var areaB = Math.Max(0, (b.maxX - b.minX) * (b.maxY - b.minY));
        var unionArea = areaA + areaB - interArea;
        return unionArea > 0 ? interArea / unionArea : 0;
    }
}
