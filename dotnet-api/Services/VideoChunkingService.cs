using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Splits video into ≤5-second chunks with overlap for pikaswaps processing,
/// then splices results back into a single video.
///
/// pikaswaps has a hard 5-second input limit. Videos longer than 5s must be
/// chunked, processed independently, and spliced back together.
/// </summary>
public class VideoChunkingService
{
    private readonly ILogger<VideoChunkingService> _logger;
    private const double MaxChunkDuration = 4.75; // Slightly under 5s for safety margin
    private const double OverlapDuration = 0.25;   // 0.25s overlap for smooth splice blending

    public VideoChunkingService(ILogger<VideoChunkingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Represents a chunk of video to be processed independently.
    /// </summary>
    public class VideoChunk
    {
        public int Index { get; set; }
        public string SourceChunkPath { get; set; } = string.Empty;
        public string? ProcessedChunkPath { get; set; }
        public double StartTimeSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public bool Failed { get; set; }
    }

    /// <summary>
    /// Split a video into ≤MaxChunkDuration chunks with overlap.
    /// Returns list of chunks ready for processing.
    /// </summary>
    /// <param name="videoPath">Absolute path to the source video.</param>
    /// <param name="outputDir">Directory to write chunk files to.</param>
    /// <param name="fps">Video frame rate.</param>
    /// <param name="totalDurationSeconds">Total duration of the scene in seconds.</param>
    /// <returns>List of video chunks.</returns>
    public async Task<List<VideoChunk>> SplitIntoChunksAsync(
        string videoPath, string outputDir, double fps, double totalDurationSeconds)
    {
        var chunks = new List<VideoChunk>();

        if (totalDurationSeconds <= MaxChunkDuration)
        {
            // Single chunk — no splitting needed
            var chunkPath = Path.Combine(outputDir, "chunk_0.mp4");
            chunks.Add(new VideoChunk
            {
                Index = 0,
                SourceChunkPath = chunkPath,
                StartTimeSeconds = 0,
                DurationSeconds = totalDurationSeconds
            });

            // Copy source directly (or use ffmpeg trim for precision)
            if (!File.Exists(chunkPath))
                File.Copy(videoPath, chunkPath, overwrite: true);

            _logger.LogInformation("[Chunking] Single chunk: {Duration}s (no split needed)", totalDurationSeconds);
            return chunks;
        }

        double currentStart = 0;
        int index = 0;

        while (currentStart < totalDurationSeconds)
        {
            double chunkDuration = Math.Min(MaxChunkDuration, totalDurationSeconds - currentStart);
            var chunkPath = Path.Combine(outputDir, $"chunk_{index}.mp4");

            // Extract chunk with ffmpeg
            var args = $"-y -hide_banner -loglevel error " +
                $"-ss {currentStart:F3} -i \"{videoPath.Replace("\\", "/")}\" " +
                $"-t {chunkDuration:F3} -c copy \"{chunkPath.Replace("\\", "/")}\"";

            await RunFfmpegAsync(args);

            chunks.Add(new VideoChunk
            {
                Index = index,
                SourceChunkPath = chunkPath,
                StartTimeSeconds = currentStart,
                DurationSeconds = chunkDuration
            });

            // Advance: next chunk starts with overlap from previous
            currentStart += chunkDuration - OverlapDuration;
            index++;
        }

        _logger.LogInformation("[Chunking] Split {Duration}s into {Count} chunks", totalDurationSeconds, chunks.Count);
        return chunks;
    }

    /// <summary>
    /// Split a video into chunks that never straddle a shot boundary. Each shot becomes its
    /// own chunk (or, if a single shot exceeds MaxChunkDuration, is itself sub-split using the
    /// same overlap logic as <see cref="SplitIntoChunksAsync"/> — but only within that shot's
    /// own span). Used so per-shot re-prompting/re-anchoring never has to deal with a chunk
    /// that spans two different camera angles.
    /// </summary>
    /// <param name="videoPath">Absolute path to the source video (already scoped to the scene).</param>
    /// <param name="outputDir">Directory to write chunk files to.</param>
    /// <param name="fps">Video frame rate.</param>
    /// <param name="shotBoundaries">Each shot's (startTimeSeconds, durationSeconds), scene-relative and in shot order.</param>
    public async Task<List<VideoChunk>> SplitByShotBoundariesAsync(
        string videoPath, string outputDir, double fps,
        List<(double startTimeSeconds, double durationSeconds)> shotBoundaries)
    {
        var chunks = new List<VideoChunk>();
        int index = 0;

        foreach (var (shotStart, shotDuration) in shotBoundaries)
        {
            var shotEnd = shotStart + shotDuration;
            var subStart = shotStart;

            while (subStart < shotEnd)
            {
                var subDuration = Math.Min(MaxChunkDuration, shotEnd - subStart);
                var chunkPath = Path.Combine(outputDir, $"chunk_{index}.mp4");

                var args = $"-y -hide_banner -loglevel error " +
                    $"-ss {subStart:F3} -i \"{videoPath.Replace("\\", "/")}\" " +
                    $"-t {subDuration:F3} -c copy \"{chunkPath.Replace("\\", "/")}\"";
                await RunFfmpegAsync(args);

                chunks.Add(new VideoChunk
                {
                    Index = index,
                    SourceChunkPath = chunkPath,
                    StartTimeSeconds = subStart,
                    DurationSeconds = subDuration,
                });

                // Sub-splits within an over-long shot get the same overlap as the time-based
                // splitter (smooth splice blending); a shot that fits in one chunk needs none.
                subStart += subDuration;
                if (subStart < shotEnd) subStart -= OverlapDuration;
                index++;
            }
        }

        _logger.LogInformation("[Chunking] Split {ShotCount} shots into {ChunkCount} chunks (shot-boundary-aware)",
            shotBoundaries.Count, chunks.Count);
        return chunks;
    }

    /// <summary>
    /// Extract a single scene's frame range from its source video as a standalone MP4 clip.
    /// Shared by ScenesController's /scenes/{id}/clip export endpoint and the prompt-based
    /// placement pipeline (which sends this clip's URL to Kling O1). Throws
    /// <see cref="InvalidOperationException"/> on any resolution/extraction failure — callers
    /// translate that into the appropriate HTTP response or render-failure message.
    /// </summary>
    /// <param name="maxDimension">If set and the source exceeds it on either axis, the clip is
    /// downscaled (never upscaled) to fit, preserving aspect ratio — e.g. Kling O1's hard
    /// 2160px cap, which the plain clip-preview download endpoint has no need for and so leaves
    /// unset.</param>
    public async Task<string> ExtractSceneClipAsync(SceneItem scene, ContentItem content, string outputPath, CancellationToken ct = default, int? maxDimension = null)
    {
        var storageKey = content.StorageKey;
        if (string.IsNullOrEmpty(storageKey) || !storageKey.StartsWith("/api/content/file/"))
            throw new InvalidOperationException("Scene clip export requires a locally uploaded video file.");

        var fileName = storageKey.Replace("/api/content/file/", "");
        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
        var sourcePath = Path.Combine(uploadsDir, fileName);
        var fullSourcePath = Path.GetFullPath(sourcePath);

        // Directory traversal guard
        if (!fullSourcePath.StartsWith(Path.GetFullPath(uploadsDir)))
            throw new InvalidOperationException("Invalid file path.");

        if (!File.Exists(fullSourcePath))
            throw new InvalidOperationException("Source video file not found.");

        var fps = content.FrameRate > 0 && content.FrameRate <= 240 ? content.FrameRate : 50;
        var startTime = (double)scene.StartFrame / fps;
        var duration = (double)(scene.EndFrame - scene.StartFrame) / fps;

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir)) Directory.CreateDirectory(outputDir);

        var scaleArg = "";
        if (maxDimension.HasValue && content.Width > 0 && content.Height > 0 &&
            (content.Width > maxDimension.Value || content.Height > maxDimension.Value))
        {
            var ratio = Math.Min((double)maxDimension.Value / content.Width, (double)maxDimension.Value / content.Height);
            var targetWidth = Math.Max(2, (int)(content.Width * ratio / 2) * 2);
            var targetHeight = Math.Max(2, (int)(content.Height * ratio / 2) * 2);
            scaleArg = $"-vf \"scale={targetWidth}:{targetHeight}\" ";
        }

        // Two-stage seek: a coarse -ss before -i (fast, keyframe-granularity input seeking) gets
        // close to the target without decoding the whole file up to that point, then a small
        // -ss after -i does the remaining <=2s via frame-accurate output seeking. Audio and video
        // streams don't share the same keyframe/frame boundaries, so a single coarse seek alone
        // can land the two streams at slightly different actual start times even though both get
        // re-encoded — this is what produced audibly-misaligned audio in generated previews.
        var coarseSeek = Math.Max(0, startTime - 2.0);
        var fineSeek = startTime - coarseSeek;

        var args = $"-hide_banner -loglevel error -ss {coarseSeek:F3} -i \"{fullSourcePath}\" " +
                   $"-ss {fineSeek:F3} -t {duration:F3} {scaleArg}-c:v libx264 -preset fast -crf 23 " +
                   $"-c:a aac -b:a 128k -pix_fmt yuv420p -movflags +faststart " +
                   $"\"{outputPath}\" -y";

        await RunFfmpegAsync(args);
        return outputPath;
    }

    /// <summary>
    /// Splice processed chunks back into a single video using ffmpeg concat.
    /// Handles failed chunks by filling gaps with original video frames.
    /// </summary>
    /// <param name="chunks">List of chunks with ProcessedChunkPath set for successful ones.</param>
    /// <param name="sourceVideoPath">Original source video for gap-filling.</param>
    /// <param name="outputPath">Output file path for the spliced video.</param>
    /// <param name="fps">Video frame rate.</param>
    public async Task<string> SpliceChunksAsync(
        List<VideoChunk> chunks, string sourceVideoPath, string outputPath, double fps)
    {
        // Build concat file list
        var concatListPath = Path.Combine(Path.GetDirectoryName(outputPath)!, "concat_list.txt");
        var lines = new List<string>();

        foreach (var chunk in chunks.OrderBy(c => c.Index))
        {
            if (chunk.Failed)
            {
                // Extract original frames for this chunk's timespan as gap fill
                var gapPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $"gap_{chunk.Index}.mp4");
                var gapArgs = $"-y -hide_banner -loglevel error " +
                    $"-ss {chunk.StartTimeSeconds:F3} -i \"{sourceVideoPath.Replace("\\", "/")}\" " +
                    $"-t {chunk.DurationSeconds:F3} -c copy \"{gapPath.Replace("\\", "/")}\"";
                await RunFfmpegAsync(gapArgs);
                lines.Add($"file '{gapPath.Replace("\\", "/")}'");
                _logger.LogWarning("[Chunking] Chunk {Index}: using original frames (pikaswaps failed)", chunk.Index);
            }
            else if (!string.IsNullOrEmpty(chunk.ProcessedChunkPath) && File.Exists(chunk.ProcessedChunkPath))
            {
                lines.Add($"file '{chunk.ProcessedChunkPath.Replace("\\", "/")}'");
            }
            else
            {
                lines.Add($"file '{chunk.SourceChunkPath.Replace("\\", "/")}'");
            }
        }

        await File.WriteAllLinesAsync(concatListPath, lines);

        // ffmpeg concat
        var concatArgs = $"-y -hide_banner -loglevel error " +
            $"-f concat -safe 0 -i \"{concatListPath.Replace("\\", "/")}\" " +
            $"-c copy \"{outputPath.Replace("\\", "/")}\"";

        await RunFfmpegAsync(concatArgs);

        // Cleanup
        try { File.Delete(concatListPath); } catch (Exception ex) { _logger.LogWarning(ex, "[VideoChunking] Failed to delete concat temp file"); }

        _logger.LogInformation("[Chunking] Spliced {Count} chunks → {Output}", chunks.Count, outputPath);
        return outputPath;
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
            _logger.LogError("[Chunking] ffmpeg error (exit {Code}): {Error}", process.ExitCode, stderr[..Math.Min(500, stderr.Length)]);
            throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}");
        }
    }
}
