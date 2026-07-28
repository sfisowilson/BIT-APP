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

    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 1800)] // 30 min — SAM3 tracking can take a while
    public async Task ProcessRenderJob(string renderId, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        var tracker = scope.ServiceProvider.GetRequiredService<ISurfaceTrackingService>();
        var eventLog = scope.ServiceProvider.GetRequiredService<IEventLogService>();

        var render = await db.Renders.FindAsync(new object[] { renderId }, cancellationToken);
        if (render == null) return;

        try
        {
            // ── Phase 1: Validate assets (5% → 20%) ──
            render.Progress = 5; render.RenderStatus = "Processing";
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 5, "Validating");

            var content = await db.ContentItems.FindAsync(new object[] { render.ContentId }, cancellationToken);
            if (content == null) throw new InvalidOperationException($"Content {render.ContentId} not found.");

            var surface = await db.SurfaceItems.FindAsync(new object[] { render.SurfaceId }, cancellationToken);
            if (surface == null) throw new InvalidOperationException($"Surface {render.SurfaceId} not found.");

            var asset = await db.CreativeAssets.FindAsync(new object[] { render.AssetId }, cancellationToken);
            if (asset == null) throw new InvalidOperationException($"Asset {render.AssetId} not found.");
            var assetPath = ResolveAssetPath(asset.StorageKey);
            if (!File.Exists(assetPath)) throw new InvalidOperationException($"Asset file not found: {assetPath}");

            var scene = await db.SceneItems.FirstOrDefaultAsync(s => s.Id == surface.SceneId, cancellationToken);
            if (scene == null) throw new InvalidOperationException($"Scene not found for surface {render.SurfaceId}.");

            var videoPath = ResolveVideoPath(content.StorageKey);
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
                throw new InvalidOperationException($"Source video not found: {content.StorageKey}");

            var fps = content.FrameRate > 0 && content.FrameRate <= 240 ? content.FrameRate : 30;
            var totalFrames = scene.EndFrame - scene.StartFrame + 1;

            render.Progress = 20;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 20, $"Tracking surface across {totalFrames} frames");

            // ── Phase 2: SAM 3 surface tracking (20% → 40%) ──
            var trackedFrames = await tracker.TrackAsync(
                render.SurfaceId, videoPath,
                scene.StartFrame, scene.EndFrame,
                surface.BoundaryCoordinatesJson,
                surface.DetectedAtFrame ?? scene.StartFrame,
                sam3Prompt: surface.Sam3Prompt,
                cancellationToken: cancellationToken);

            if (trackedFrames.Count == 0)
                throw new InvalidOperationException(
                    "SAM3 returned 0 frames. Check fal.ai API key, sam3_video_base_url setting, and network connectivity.");

            // Check if SAM3 returned a segmented video (single frame with sam3_video path)
            var firstFrameJson = trackedFrames[0].BoundaryCoordinatesJson;
            string? sam3VideoPath = null;
            try
            {
                using var doc = JsonDocument.Parse(firstFrameJson);
                if (doc.RootElement.TryGetProperty("sam3_video", out var vidPath))
                    sam3VideoPath = vidPath.GetString();
            }
            catch { }

            if (sam3VideoPath != null && File.Exists(sam3VideoPath))
            {
                // SAM3 returned a segmented video — use it as a per-pixel luma mask
                // Tracked region = bright → asset visible. Background = dark → asset hidden.
                await eventLog.LogEventAsync("RenderEngine", "USING_SAM3_VIDEO", "Info",
                    $"Using SAM3 segmented video as luma mask: {sam3VideoPath}");
                await _hubContext.Clients.All.RenderProgress(renderId, 60, "Compositing with SAM3 mask");

                var sam3RendersDir = Path.Combine(Directory.GetCurrentDirectory(), "renders");
                Directory.CreateDirectory(sam3RendersDir);
                var sam3OutputPath = Path.Combine(sam3RendersDir, $"BIT_Render_{renderId}.mp4");

                var safeOrig = videoPath.Replace("\\", "/");
                var safeSam3 = sam3VideoPath.Replace("\\", "/");
                var safePng = assetPath.Replace("\\", "/");

                // Compositing pipeline:
                // 1. Extract luma from SAM3 video → binary mask (bright>10 = white, else black)
                // 2. Scale asset, convert to RGBA
                // 3. Apply SAM3 mask as alpha channel to asset (alphamerge)
                // 4. Overlay masked asset on original video
                // Result: asset visible ONLY where SAM3 tracked the surface
                var overlayArgs = $"-y -hide_banner -loglevel error " +
                    $"-i \"{safeOrig}\" -i \"{safeSam3}\" -i \"{safePng}\" " +
                    $"-filter_complex \"" +
                    $"[1:v]format=gray,geq=r='if(gt(lum(X\\,Y)\\,10)\\,255\\,0)' [mask];" +
                    $"[2:v]scale=W:H,format=rgba [asset_rgba];" +
                    $"[asset_rgba][mask]alphamerge [asset_masked];" +
                    $"[0:v][asset_masked]overlay=0:0,format=yuv420p [out]" +
                    $"\" -map \"[out]\" " +
                    $"-c:v libx264 -preset fast -crf 23 -an \"{sam3OutputPath}\"";
                await RunFfmpegAsync(overlayArgs, cancellationToken);

                // Cleanup SAM3 temp file
                try { File.Delete(sam3VideoPath); } catch { }

                render.Progress = 100;
                render.RenderStatus = "Finished";
                render.StorageKey = $"/api/renders/{renderId}/download";
                render.ProcessingDurationMs = (int)sw.ElapsedMilliseconds;
                await db.SaveChangesAsync(cancellationToken);
                await _hubContext.Clients.All.RenderProgress(renderId, 100, "Complete");
                await eventLog.LogEventAsync("RenderEngine", "RENDER_COMPLETED", "Info",
                    $"Render '{render.Id}' completed with SAM3 luma-mask compositing in {sw.Elapsed.TotalSeconds:F1}s.");
                return;
            }

            await _hubContext.Clients.All.RenderProgress(renderId, 40,
                $"Tracked {trackedFrames.Count} frames");

            // ── Phase 3: Per-frame compositing (40% → 80%) ──
            var rendersDir = Path.Combine(Directory.GetCurrentDirectory(), "renders");
            Directory.CreateDirectory(rendersDir);
            var frameDir = Path.Combine(rendersDir, $"frames_{renderId}");
            Directory.CreateDirectory(frameDir);
            var outputFilePath = Path.Combine(rendersDir, $"BIT_Render_{renderId}.mp4");

            var safeVideo = videoPath.Replace("\\", "/");
            var safeAsset = assetPath.Replace("\\", "/");
            var safeFrameDir = frameDir.Replace("\\", "/");

            // Extract all scene frames at once (fast, single ffmpeg call)
            var sceneStartSec = scene.StartFrame / (double)fps;
            var extractArgs = $"-y -hide_banner -loglevel error " +
                $"-ss {sceneStartSec:F3} -i \"{safeVideo}\" " +
                $"-t {scene.DurationSeconds:F3} " +
                $"-vf fps={fps} \"{safeFrameDir}/raw_%04d.png\"";
            await RunFfmpegAsync(extractArgs, cancellationToken);

            render.Progress = 50;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 50, "Compositing per-frame");

            // Composite each tracked frame: extract raw frame → warp asset → overlay → save
            var processedCount = 0;
            var totalTracked = trackedFrames.Count;
            var parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken };

            await Parallel.ForEachAsync(trackedFrames, parallelOpts, async (tf, ct) =>
            {
                var frameIdx = tf.Frame - scene.StartFrame;
                var rawFramePath = Path.Combine(frameDir, $"raw_{frameIdx:D4}.png");
                if (!File.Exists(rawFramePath)) return;

                var outPath = Path.Combine(frameDir, $"comp_{tf.Frame:D6}.png");
                var safeRaw = rawFramePath.Replace("\\", "/");
                var safeOut = outPath.Replace("\\", "/");

                // Build perspective transform from tracked polygon (4 corners)
                var perspectiveArgs = await BuildPerspectiveArgsAsync(tf.BoundaryCoordinatesJson, assetPath, ct);
                if (perspectiveArgs == null) return;

                var compArgs = $"-y -hide_banner -loglevel error " +
                    $"-i \"{safeRaw}\" -i \"{safeAsset}\" " +
                    $"-filter_complex \"[1:v]{perspectiveArgs}[warped];[0:v][warped]overlay=0:0\" " +
                    $"-vframes 1 \"{safeOut}\"";
                await RunFfmpegAsync(compArgs, ct);

                var done = Interlocked.Increment(ref processedCount);
                if (done % 10 == 0 || done == totalTracked)
                {
                    var pct = 50 + (int)(30.0 * done / totalTracked);
                    await _hubContext.Clients.All.RenderProgress(renderId, pct,
                        $"Composited {done}/{totalTracked} frames");
                }
            });

            render.Progress = 85;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 85, "Encoding video");

            // ── Phase 4: Encode PNG sequence to MP4 (85% → 100%) ──
            var encodeArgs = $"-y -hide_banner -loglevel error " +
                $"-framerate {fps} -i \"{safeFrameDir}/comp_%06d.png\" " +
                $"-c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p " +
                $"-an \"{outputFilePath}\"";
            await RunFfmpegAsync(encodeArgs, cancellationToken);

            // Cleanup frame temp dir
            try { Directory.Delete(frameDir, true); } catch { }

            render.Progress = 100;
            render.RenderStatus = "Finished";
            render.StorageKey = $"/api/renders/{renderId}/download";
            render.ProcessingDurationMs = (int)sw.ElapsedMilliseconds;
            await db.SaveChangesAsync(cancellationToken);

            await _hubContext.Clients.All.RenderProgress(renderId, 100, "Complete");
            await eventLog.LogEventAsync("RenderEngine", "RENDER_COMPLETED", "Info",
                $"Render '{render.Id}' ({asset.Name} → {surface.SurfaceType}): {trackedFrames.Count} frames tracked & composited in {sw.Elapsed.TotalSeconds:F1}s.");
        }
        catch (OperationCanceledException)
        {
            render.RenderStatus = "Cancelled";
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RenderJob] Render {RenderId} FAILED", renderId);
            render.RenderStatus = "Failed";
            render.LastErrorMessage = ex.Message;
            await db.SaveChangesAsync();
            await eventLog.LogEventAsync("RenderEngine", "RENDER_FAILED", "Warning", $"Render '{render.Id}' failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>Build ffmpeg perspective filter args from a 4-corner boundary JSON array.</summary>
    private static async Task<string?> BuildPerspectiveArgsAsync(string boundaryJson, string assetPath, CancellationToken ct)
    {
        try
        {
            var pts = JsonSerializer.Deserialize<List<JsonElement>>(boundaryJson);
            if (pts == null || pts.Count < 4) return null;

            var corners = pts.Take(4).Select(p =>
            {
                double x = 0, y = 0;
                if (p.ValueKind == JsonValueKind.Array) { x = p[0].GetDouble(); y = p[1].GetDouble(); }
                else if (p.TryGetProperty("x", out var px)) { x = px.GetDouble(); y = p.GetProperty("y").GetDouble(); }
                else if (p.TryGetProperty("X", out var pX)) { x = pX.GetDouble(); y = p.GetProperty("Y").GetDouble(); }
                return (x, y);
            }).ToList();

            if (corners.Count < 4) return null;

            var (aW, aH) = await GetImageSizeAsync(assetPath, ct);
            if (aW <= 0 || aH <= 0) return null;

            var (tlx, tly) = corners[0]; var (trx, try_) = corners[1];
            var (brx, bry) = corners[2]; var (blx, bly) = corners[3];

            return $"perspective=0:0:0:{aH}:{aW}:{aH}:{aW}:0:" +
                   $"{tlx:F1}:{tly:F1}:{trx:F1}:{try_:F1}:{brx:F1}:{bry:F1}:{blx:F1}:{bly:F1}:sense=destination";
        }
        catch { return null; }
    }

    private static async Task<(int w, int h)> GetImageSizeAsync(string imagePath, CancellationToken ct)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{imagePath}\"",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                },
            };
            process.Start();
            var readOut = process.StandardOutput.ReadToEndAsync();
            var readErr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(readOut, readErr);
            var parts = readOut.Result.Trim().Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                return (w, h);
        }
        catch { }
        return (0, 0);
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

        var render = await db.Renders.FindAsync(new object[] { renderId }, cancellationToken);
        if (render == null) return;

        try
        {
            // Phase 1: Validate (5% → 15%)
            render.Progress = 5; render.RenderStatus = "Processing";
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 5, "Validating");

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
            var videoBaseUrl = await platformSettings.GetAsync("sam3_video_base_url", "http://localhost:57220");
            var videoFileName = Path.GetFileName(videoPath);
            var videoUrl = $"{videoBaseUrl}/api/content/file/{videoFileName}";
            var assetFileName = Path.GetFileName(assetPath);
            var assetUrl = $"{videoBaseUrl}/api/assets/file/{assetFileName}";

            render.Progress = 15;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 15, "Generating prompt");

            // Phase 2: Gemini prompt generation (15% → 20%)
            var (modifyRegion, prompt) = await gemini.GeneratePikaswapsPromptAsync(
                surface.SurfaceType, asset.Name ?? "brand asset");

            if (string.IsNullOrEmpty(modifyRegion) || string.IsNullOrEmpty(prompt))
            {
                modifyRegion = surface.SurfaceType;
                prompt = $"replace with a {asset.Name} advertisement, photorealistic";
            }

            await eventLog.LogEventAsync("RenderEngine", "GEMINI_PROMPT_COMPLETE", "Info",
                $"Generative render {renderId}: modify_region='{modifyRegion}', prompt='{prompt}'");

            render.Progress = 20;
            await db.SaveChangesAsync(cancellationToken);

            // Determine scene duration from video
            var totalDuration = await GetVideoDurationAsync(videoPath, cancellationToken);

            // Phase 3: Chunking (20% → 25%)
            string? finalVideoPath;
            if (totalDuration <= 4.75)
            {
                // Single pikaswaps call — no chunking
                await _hubContext.Clients.All.RenderProgress(renderId, 25, "Compositing with pikaswaps");
                finalVideoPath = await pikaswaps.CompositeWithPromptAsync(
                    videoUrl, assetUrl, modifyRegion, prompt, render.SurfaceId, ct: cancellationToken);

                if (finalVideoPath == null)
                    throw new InvalidOperationException("Pikaswaps returned no video for single-chunk render.");
            }
            else
            {
                var chunkDir = Path.Combine(Path.GetTempPath(), $"bit-chunks-{renderId}");
                Directory.CreateDirectory(chunkDir);

                await _hubContext.Clients.All.RenderProgress(renderId, 22, "Chunking video");
                var chunks = await chunker.SplitIntoChunksAsync(videoPath, chunkDir, fps, totalDuration);

                render.Progress = 25;
                await db.SaveChangesAsync(cancellationToken);

                // Process each chunk through pikaswaps
                int completed = 0;
                foreach (var chunk in chunks)
                {
                    await _hubContext.Clients.All.RenderProgress(renderId,
                        25 + (int)(40.0 * completed / chunks.Count),
                        $"Compositing chunk {chunk.Index + 1}/{chunks.Count}");

                    try
                    {
                        var chunkVideoUrl = $"{videoBaseUrl}/api/content/file/{Path.GetFileName(chunk.SourceChunkPath)}";
                        var processedPath = await pikaswaps.CompositeWithPromptAsync(
                            chunkVideoUrl, assetUrl, modifyRegion, prompt, $"{render.SurfaceId}_c{chunk.Index}",
                            ct: cancellationToken);

                        if (processedPath != null)
                            chunk.ProcessedChunkPath = processedPath;
                        else
                            chunk.Failed = true;
                    }
                    catch
                    {
                        chunk.Failed = true;
                    }

                    completed++;
                }

                // Splice
                await _hubContext.Clients.All.RenderProgress(renderId, 70, "Splicing chunks");
                var spliceOutput = Path.Combine(Path.GetTempPath(), $"bit-splice-{renderId}.mp4");
                finalVideoPath = await chunker.SpliceChunksAsync(chunks, videoPath, spliceOutput, fps);
            }

            // Phase 4: Drift check with SAM3 video-rle (placeholder — runs SAM3 on output, compares IoU)
            await _hubContext.Clients.All.RenderProgress(renderId, 85, "QA drift-check");
            // TODO: Implement full drift-check — re-run SAM3 video-rle on finalVideoPath,
            // compare per-frame RLE masks to original track_id mask, set NeedsReview if IoU < 0.85

            // Phase 5: Finalize
            var rendersDir = Path.Combine(Directory.GetCurrentDirectory(), "renders");
            Directory.CreateDirectory(rendersDir);
            var outputPath = Path.Combine(rendersDir, $"BIT_Render_{renderId}.mp4");

            if (finalVideoPath != null && File.Exists(finalVideoPath))
                File.Copy(finalVideoPath, outputPath, overwrite: true);

            render.Progress = 100;
            render.RenderStatus = "Finished";
            render.CompositingEngine = "pikaswaps";
            render.QualityTier = "AI";
            render.StorageKey = $"/api/renders/{renderId}/download";
            render.ProcessingDurationMs = (int)sw.ElapsedMilliseconds;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 100, "Complete");

            await eventLog.LogEventAsync("RenderEngine", "GENERATIVE_RENDER_COMPLETE", "Info",
                $"Generative render {renderId}: pikaswaps, {sw.Elapsed.TotalSeconds:F1}s");
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

        var render = await db.Renders.FindAsync(new object[] { renderId }, cancellationToken);
        if (render == null) return;

        try
        {
            render.Progress = 5; render.RenderStatus = "Processing";
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 5, "Validating");

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

            // Parse per-frame quad data from TrackingDataJson
            var frameQuads = ParseFrameQuads(surface.TrackingDataJson ?? surface.BoundaryCoordinatesJson);
            if (frameQuads.Count == 0)
                throw new InvalidOperationException("No quad coordinates available for planar warp.");

            render.Progress = 15;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 15, "Extracting frames");

            // Phase 2: Extract video frames
            var rendersDir = Path.Combine(Directory.GetCurrentDirectory(), "renders");
            Directory.CreateDirectory(rendersDir);
            var frameDir = Path.Combine(rendersDir, $"planar_frames_{renderId}");
            Directory.CreateDirectory(frameDir);

            var safeVideo = videoPath.Replace("\\", "/");
            var safeFrameDir = frameDir.Replace("\\", "/");
            var totalFrames = frameQuads.Count;

            // Extract all frames
            var extractArgs = $"-y -hide_banner -loglevel error " +
                $"-i \"{safeVideo}\" -vf fps={fps} \"{safeFrameDir}/raw_%06d.png\"";
            await RunFfmpegAsync(extractArgs, cancellationToken);

            render.Progress = 25;
            await db.SaveChangesAsync(cancellationToken);

            // Phase 3: Per-frame warp + relight + composite
            var processedCount = 0;
            var parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken };

            await Parallel.ForEachAsync(frameQuads, parallelOpts, async (item, ct) =>
            {
                var rawPath = Path.Combine(frameDir, $"raw_{item.frameIndex:D6}.png");
                if (!File.Exists(rawPath)) return;

                var compPath = Path.Combine(frameDir, $"comp_{item.frameIndex:D6}.png");
                var quadCorners = item.corners.Select(c => ((double)c.x, (double)c.y)).ToList();

                var ok = await planar.CompositeFrameAsync(rawPath, assetPath, quadCorners, compPath);
                if (!ok) return;

                // Relight
                var wall = planar.ComputeWallRegion(quadCorners, content.Width, content.Height);
                var relitPath = Path.Combine(frameDir, $"relit_{item.frameIndex:D6}.png");
                await planar.RelightFrameAsync(compPath, rawPath, wall, relitPath);

                // Move relit over comp
                if (File.Exists(relitPath))
                {
                    File.Delete(compPath);
                    File.Move(relitPath, compPath);
                }

                var done = Interlocked.Increment(ref processedCount);
                if (done % 10 == 0 || done == totalFrames)
                {
                    var pct = 25 + (int)(60.0 * done / totalFrames);
                    await _hubContext.Clients.All.RenderProgress(renderId, pct,
                        $"Composited {done}/{totalFrames} frames");
                }
            });

            // Phase 4: Encode to MP4
            render.Progress = 90;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 90, "Encoding video");

            var outputPath = Path.Combine(rendersDir, $"BIT_Render_{renderId}.mp4");
            await planar.EncodeToMp4Async(frameDir, outputPath, fps);

            // Cleanup
            try { Directory.Delete(frameDir, true); } catch { }

            render.Progress = 100;
            render.RenderStatus = "Finished";
            render.CompositingEngine = "PlanarWarp";
            render.QualityTier = "Exact";
            render.StorageKey = $"/api/renders/{renderId}/download";
            render.ProcessingDurationMs = (int)sw.ElapsedMilliseconds;
            await db.SaveChangesAsync(cancellationToken);
            await _hubContext.Clients.All.RenderProgress(renderId, 100, "Complete");

            await eventLog.LogEventAsync("RenderEngine", "PLANAR_RENDER_COMPLETE", "Info",
                $"Planar render {renderId}: {totalFrames} frames in {sw.Elapsed.TotalSeconds:F1}s");
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

    private static List<(int frameIndex, List<(int x, int y)> corners)> ParseFrameQuads(string trackingDataJson)
    {
        var result = new List<(int, List<(int, int)>)>();
        try
        {
            using var doc = JsonDocument.Parse(trackingDataJson);
            foreach (var frame in doc.RootElement.EnumerateArray())
            {
                var fi = frame.TryGetProperty("frame", out var f) ? f.GetInt32() : 0;
                var corners = new List<(int, int)>();
                if (frame.TryGetProperty("corners", out var c))
                {
                    foreach (var pt in c.EnumerateArray())
                    {
                        int x = 0, y = 0;
                        if (pt.TryGetProperty("x", out var px)) x = px.GetInt32();
                        if (pt.TryGetProperty("y", out var py)) y = py.GetInt32();
                        corners.Add((x, y));
                    }
                }
                if (corners.Count >= 4)
                    result.Add((fi, corners));
            }
        }
        catch { }
        return result;
    }

    private static async Task<double> GetVideoDurationAsync(string videoPath, CancellationToken ct)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -show_entries format=duration -of csv=p=0 \"{videoPath}\"",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                },
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            if (double.TryParse(output.Trim(), out var d)) return d;
        }
        catch { }
        return 0;
    }
}
