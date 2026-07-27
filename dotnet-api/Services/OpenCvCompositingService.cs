using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// FFmpeg-based compositing: extracts the video frame and overlays the brand asset
/// at the detected surface position. Activated when engine_compositing = "opencv".
/// Uses FFmpeg for frame extraction and image overlay — no native OpenCV dependency.
/// Falls back to BasicCompositingService if compositing fails.
/// </summary>
public class OpenCvCompositingService : ICompositingService
{
    private readonly PostgresDbContext _context;
    private readonly IHostEnvironment _env;
    private readonly ILogger<OpenCvCompositingService> _logger;
    private readonly ICompositingService _fallback;

    public OpenCvCompositingService(PostgresDbContext context, IHostEnvironment env, ILogger<OpenCvCompositingService> logger)
    {
        _context = context;
        _env = env;
        _logger = logger;
        _fallback = new BasicCompositingService(context, env);
    }

    public async Task<CompositedFrame> CompositeAsync(CompositingRequest request)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Look up asset
            var asset = await _context.CreativeAssets.FindAsync(request.AssetId);
            if (asset == null)
                throw new ArgumentException($"Asset {request.AssetId} not found.");

            var assetPath = ResolveAssetPath(asset.StorageKey);
            if (!File.Exists(assetPath))
                throw new InvalidOperationException($"Asset file not found: {assetPath}");

            // 2. Look up video file
            var content = await _context.ContentItems.FindAsync(request.ContentId);
            if (content == null)
                throw new ArgumentException($"Content {request.ContentId} not found.");

            var videoPath = ResolveVideoPath(content.StorageKey);
            if (!File.Exists(videoPath))
                throw new InvalidOperationException($"Video file not found: {videoPath}");

            // 3. Determine the frame to capture — use the requested frame, fall back to scene start
            var captureFrame = request.FrameNumber > 0
                ? request.FrameNumber
                : await GetSceneStartFrame(request.SurfaceId, content.FrameRate);

            // 4. Parse boundary coordinates to get overlay position
            var (x, y, w, h) = ParseOverlayBounds(request.BoundaryCoordinatesJson);
            if (w <= 0 || h <= 0)
                throw new ArgumentException("Invalid boundary coordinates — cannot determine overlay area.");

            // 5. Execute FFmpeg compositing pipeline
            var uploadsDir = Path.Combine(_env.ContentRootPath, "Uploads");
            var tempDir = Path.Combine(uploadsDir, "temp");
            Directory.CreateDirectory(tempDir);

            var framePath = Path.Combine(tempDir, $"{Guid.NewGuid():N}_frame.png");
            var outputPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}_composite.png");

            try
            {
                // Step A: Extract video frame at the capture position
                var safeVideo = videoPath.Replace("\\", "/");
                var extractArgs = $"-y -hide_banner -loglevel error -i \"{safeVideo}\" " +
                                  $"-vf \"select=eq(n\\,{captureFrame})\" -vframes 1 \"{framePath}\"";

                await RunFfmpegAsync(extractArgs);

                if (!File.Exists(framePath) || new FileInfo(framePath).Length < 100)
                {
                    // Try seeking by time instead of frame number
                    var fps = content.FrameRate > 0 ? content.FrameRate : 25;
                    var seekTime = captureFrame / (double)fps;
                    extractArgs = $"-y -hide_banner -loglevel error -ss {seekTime:F3} -i \"{safeVideo}\" " +
                                  $"-vframes 1 \"{framePath}\"";
                    await RunFfmpegAsync(extractArgs);
                }

                if (!File.Exists(framePath) || new FileInfo(framePath).Length < 100)
                    throw new InvalidOperationException("Failed to extract video frame.");

                // Step B: Overlay the asset onto the frame at the surface position
                var safeAsset = assetPath.Replace("\\", "/");
                var overlayArgs = $"-y -hide_banner -loglevel error " +
                                  $"-i \"{framePath}\" -i \"{safeAsset}\" " +
                                  $"-filter_complex \"[1:v]scale={w}:{h}:force_original_aspect_ratio=decrease[scaled];" +
                                  $"[0:v][scaled]overlay={x}:{y}:format=auto\" " +
                                  $"-vframes 1 \"{outputPath}\"";

                await RunFfmpegAsync(overlayArgs);

                if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 100)
                    throw new InvalidOperationException("Failed to composite asset onto frame.");

                // Step C: Read result as base64
                var resultBytes = await File.ReadAllBytesAsync(outputPath);
                var base64 = Convert.ToBase64String(resultBytes);

                sw.Stop();
                _logger.LogInformation(
                    "[OpenCV] Composited asset {AssetId} onto content {ContentId} surface at ({X},{Y} {W}x{H}) in {Ms}ms",
                    request.AssetId, request.ContentId, x, y, w, h, sw.ElapsedMilliseconds);

                return new CompositedFrame
                {
                    ImageBase64 = base64,
                    ContentType = "image/png",
                    EngineUsed = "OpenCvCompositor",
                    ProcessingMs = sw.ElapsedMilliseconds
                };
            }
            finally
            {
                // Cleanup temp files
                try { if (File.Exists(framePath)) File.Delete(framePath); } catch { }
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OpenCV] Compositing failed for asset {AssetId} — falling back to basic",
                request.AssetId);
            sw.Stop();
            // Fall back to basic on any failure
            var fallbackResult = await _fallback.CompositeAsync(request);
            fallbackResult.EngineUsed = "OpenCvCompositor (fallback to basic)";
            return fallbackResult;
        }
    }

    // ── Helpers ──

    private string ResolveAssetPath(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey) || !storageKey.StartsWith("/api/assets/file/"))
            throw new ArgumentException($"Invalid asset storage key: {storageKey}");
        var fileName = storageKey.Replace("/api/assets/file/", "");
        return Path.Combine(_env.ContentRootPath, "Uploads", "assets", fileName);
    }

    private string ResolveVideoPath(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey) || !storageKey.StartsWith("/api/content/file/"))
            throw new ArgumentException($"Invalid video storage key: {storageKey}");
        var fileName = storageKey.Replace("/api/content/file/", "");
        return Path.Combine(_env.ContentRootPath, "Uploads", fileName);
    }

    /// <summary>Gets the start frame of the scene containing the given surface.</summary>
    private async Task<int> GetSceneStartFrame(string surfaceId, int frameRate)
    {
        var surface = await _context.SurfaceItems.FindAsync(surfaceId);
        if (surface == null) return 0;

        var scene = await _context.SceneItems.FindAsync(surface.SceneId);
        if (scene == null) return 0;

        // Seek a few frames into the scene to avoid transition artifacts
        var fps = frameRate > 0 && frameRate <= 240 ? frameRate : 25;
        return scene.StartFrame + Math.Max(1, fps / 3);
    }

    /// <summary>
    /// Parses boundary coordinates JSON and returns (x, y, width, height) of the bounding box.
    /// Handles [{x,y}], [{X,Y}], and raw [[x,y]] Gemini formats.
    /// </summary>
    private static (int x, int y, int w, int h) ParseOverlayBounds(string boundaryJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(boundaryJson);
            var root = doc.RootElement;
            var xs = new List<int>();
            var ys = new List<int>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var pt in root.EnumerateArray())
                {
                    if (pt.ValueKind == JsonValueKind.Array && pt.GetArrayLength() >= 2)
                    {
                        xs.Add(pt[0].GetInt32());
                        ys.Add(pt[1].GetInt32());
                    }
                    else if (pt.TryGetProperty("x", out var lx) && pt.TryGetProperty("y", out var ly))
                    {
                        xs.Add(lx.GetInt32()); ys.Add(ly.GetInt32());
                    }
                    else if (pt.TryGetProperty("X", out var ux) && pt.TryGetProperty("Y", out var uy))
                    {
                        xs.Add(ux.GetInt32()); ys.Add(uy.GetInt32());
                    }
                }
            }

            if (xs.Count < 4) return (0, 0, 0, 0);
            var x = xs.Min(); var y = ys.Min();
            return (x, y, Math.Max(1, xs.Max() - x), Math.Max(1, ys.Max() - y));
        }
        catch { return (0, 0, 0, 0); }
    }

    /// <summary>Runs an FFmpeg command and waits for completion.</summary>
    private static async Task RunFfmpegAsync(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed (exit {process.ExitCode}): {stderr[..Math.Min(500, stderr.Length)]}");
    }

    private class CoordDto
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}
