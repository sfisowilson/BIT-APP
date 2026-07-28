using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
        try { File.Delete(concatListPath); } catch { }

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
