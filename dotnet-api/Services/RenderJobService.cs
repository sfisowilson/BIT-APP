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
                    $"[1:v]format=gray,geq=r='if(gt(lum(X\\,Y),10),255,0)' [mask];" +
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
}
