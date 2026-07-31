using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Planar homography compositing for flat signage/assets on tracked surfaces.
/// Uses deterministic ffmpeg perspective warp + histogram relighting — no AI generation.
///
/// Pipeline per frame:
///   1. warpPerspective: map asset 4 corners → tracked surface 4 corners
///   2. Histogram transfer: match wall/background luminance
///   3. Occlusion subtraction: remove signage where foreground objects overlap
///   4. Overlay on original video frame
///
/// Activated when engine_compositing = "planar-warp".
/// </summary>
public class PlanarWarpCompositingService : ICompositingService
{
    private readonly ILogger<PlanarWarpCompositingService> _logger;
    private readonly IEventLogService _eventLog;

    public PlanarWarpCompositingService(ILogger<PlanarWarpCompositingService> logger, IEventLogService eventLog)
    {
        _logger = logger;
        _eventLog = eventLog;
    }

    public Task<CompositedFrame> CompositeAsync(CompositingRequest request)
    {
        return Task.FromResult(new CompositedFrame
        {
            ImageBase64 = string.Empty,
            ContentType = "text/plain",
            EngineUsed = "PlanarWarp",
            ProcessingMs = 0
        });
    }

    /// <summary>
    /// Composite a single frame using planar homography warp.
    /// </summary>
    /// <param name="sourceFramePath">Path to the extracted video frame PNG.</param>
    /// <param name="assetPath">Path to the brand asset PNG.</param>
    /// <param name="quadCorners">4 corner points [{x,y}×4] in pixel coordinates for this frame.</param>
    /// <param name="outputPath">Path to write the composited frame PNG.</param>
    /// <param name="occlusionMaskPath">Optional path to foreground occlusion mask PNG. White=occluded pixels.</param>
    public async Task<bool> CompositeFrameAsync(
        string sourceFramePath,
        string assetPath,
        List<(double x, double y)> quadCorners,
        string outputPath,
        string? occlusionMaskPath = null)
    {
        try
        {
            if (quadCorners.Count < 4) return false;

            var (aW, aH) = await GetImageSizeAsync(assetPath);
            if (aW <= 0 || aH <= 0) return false;

            var (tlx, tly) = quadCorners[0];
            var (trx, try_) = quadCorners[1];
            var (brx, bry) = quadCorners[2];
            var (blx, bly) = quadCorners[3];

            var safeSource = sourceFramePath.Replace("\\", "/");
            var safeAsset = assetPath.Replace("\\", "/");
            var safeOut = outputPath.Replace("\\", "/");

            string filterComplex;
            // ffmpeg's perspective filter takes exactly 8 numbers — the *destination* coordinates
            // for the input rectangle's top-left, top-right, bottom-left, bottom-right corners (in
            // that order; note bottom-left comes before bottom-right, unlike a clockwise quad walk).
            // No leading "identity rect" prefix — confirmed via a real failure: ffmpeg rejected the
            // 16-number string this used to build with "No option name near ...sense=destination",
            // silently failing every single frame composite (CompositeFrameAsync swallows the
            // exception and returns false) and passing the unmodified source frame through instead.
            if (!string.IsNullOrEmpty(occlusionMaskPath) && File.Exists(occlusionMaskPath))
            {
                var safeOcclusion = occlusionMaskPath.Replace("\\", "/");
                // Warp asset → apply occlusion mask as alpha → overlay on source
                filterComplex =
                    $"[1:v]perspective={tlx:F1}:{tly:F1}:{trx:F1}:{try_:F1}:{blx:F1}:{bly:F1}:{brx:F1}:{bry:F1}:sense=destination,format=rgba [warped];" +
                    $"[2:v]format=gray,negate [occlusion_alpha];" +
                    $"[warped][occlusion_alpha]alphamerge [asset_masked];" +
                    $"[0:v][asset_masked]overlay=0:0";
            }
            else
            {
                // Warp asset → overlay on source (no occlusion)
                filterComplex =
                    $"[1:v]perspective={tlx:F1}:{tly:F1}:{trx:F1}:{try_:F1}:{blx:F1}:{bly:F1}:{brx:F1}:{bry:F1}:sense=destination [warped];" +
                    $"[0:v][warped]overlay=0:0";
            }

            var inputs = new List<string> { "-i", safeSource, "-i", safeAsset };
            if (!string.IsNullOrEmpty(occlusionMaskPath) && File.Exists(occlusionMaskPath))
                inputs.AddRange(new[] { "-i", occlusionMaskPath.Replace("\\", "/") });

            var args = $"-y -hide_banner -loglevel error " +
                string.Join(" ", inputs) + " " +
                $"-filter_complex \"{filterComplex}\" " +
                $"-vframes 1 \"{safeOut}\"";

            await RunFfmpegAsync(args);
            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PlanarWarp] Frame composite failed");
            return false;
        }
    }

    /// <summary>
    /// Apply luminance histogram transfer from the surrounding wall region to the composited frame.
    /// Makes the signage look naturally lit by matching the wall's brightness distribution.
    /// </summary>
    /// <param name="compositedFramePath">Path to the composited frame (warped asset overlaid).</param>
    /// <param name="sourceFramePath">Path to the original video frame.</param>
    /// <param name="wallRegion">Approximate wall region bounds {xMin,yMin,xMax,yMax} for sampling.</param>
    /// <param name="outputPath">Path to write the relit frame.</param>
    public async Task<bool> RelightFrameAsync(
        string compositedFramePath,
        string sourceFramePath,
        (int xMin, int yMin, int xMax, int yMax) wallRegion,
        string outputPath)
    {
        try
        {
            var safeComposited = compositedFramePath.Replace("\\", "/");
            var safeSource = sourceFramePath.Replace("\\", "/");
            var safeOut = outputPath.Replace("\\", "/");

            // Use ffmpeg histogram matching: transfer color distribution from wall region to composited frame
            var args = $"-y -hide_banner -loglevel error " +
                $"-i \"{safeComposited}\" -i \"{safeSource}\" " +
                $"-filter_complex \"" +
                $"[1:v]crop={wallRegion.xMax - wallRegion.xMin}:{wallRegion.yMax - wallRegion.yMin}:" +
                $"{wallRegion.xMin}:{wallRegion.yMin} [wall];" +
                $"[0:v][wall]histmatch [out]" +
                $"\" -map \"[out]\" -vframes 1 \"{safeOut}\"";

            await RunFfmpegAsync(args);
            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PlanarWarp] Relighting failed, using un-relit frame");
            // Fallback: copy composited frame as-is
            if (File.Exists(compositedFramePath) && compositedFramePath != outputPath)
                File.Copy(compositedFramePath, outputPath, overwrite: true);
            return File.Exists(outputPath);
        }
    }

    /// <summary>
    /// Compute the wall region around the placed quad for relighting sampling.
    /// Expands the quad bounding box outward by margin pixels.
    /// </summary>
    public (int xMin, int yMin, int xMax, int yMax) ComputeWallRegion(
        List<(double x, double y)> quadCorners, int frameWidth, int frameHeight, int margin = 20)
    {
        if (quadCorners.Count < 4) return (0, 0, frameWidth, frameHeight);

        int xMin = Math.Max(0, (int)quadCorners.Min(p => p.x) - margin);
        int yMin = Math.Max(0, (int)quadCorners.Min(p => p.y) - margin);
        int xMax = Math.Min(frameWidth, (int)quadCorners.Max(p => p.x) + margin);
        int yMax = Math.Min(frameHeight, (int)quadCorners.Max(p => p.y) + margin);

        return (xMin, yMin, xMax, yMax);
    }

    /// <summary>
    /// Encode a directory of PNG frames into an MP4 video.
    /// </summary>
    /// <param name="frameDir">Directory containing comp_XXXXXX.png frames.</param>
    /// <param name="outputPath">Output MP4 path.</param>
    /// <param name="fps">Frame rate.</param>
    public async Task EncodeToMp4Async(string frameDir, string outputPath, double fps)
    {
        var safeDir = frameDir.Replace("\\", "/");
        var safeOut = outputPath.Replace("\\", "/");

        var args = $"-y -hide_banner -loglevel error " +
            $"-framerate {fps} -i \"{safeDir}/comp_%06d.png\" " +
            $"-c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p -an \"{safeOut}\"";

        await RunFfmpegAsync(args);
    }

    // ── Helpers ──

    private async Task<(int w, int h)> GetImageSizeAsync(string imagePath)
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
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var parts = output.Trim().Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                return (w, h);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PlanarWarp] ffprobe dimensions probe failed");
        }
        return (0, 0);
    }

    private async Task RunFfmpegAsync(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process == null) throw new InvalidOperationException("Failed to start ffmpeg.");

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var trimmed = stderr.Length > 500 ? stderr[..500] : stderr;
            _logger.LogError("[PlanarWarp] ffmpeg error (exit {Code}): {Error}", process.ExitCode, trimmed);
            throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}");
        }
    }
}
