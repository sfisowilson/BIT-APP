using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests;

/// <summary>
/// Exercises VideoChunkingService against small real ffmpeg-generated test clips (this
/// codebase has no fake/mock video pipeline — see governance/rules/no-mock-code.md).
/// Skips gracefully if ffmpeg isn't on PATH in the test environment.
/// </summary>
public class VideoChunkingServiceTests : IDisposable
{
    private const double Fps = 10;
    private readonly string _workDir;

    public VideoChunkingServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"bit-chunk-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, true); } catch { }
    }

    private static bool FfmpegAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            });
            p!.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private string GenerateTestVideo(double durationSeconds, string name)
    {
        var path = Path.Combine(_workDir, name);
        // Explicit invariant-culture formatting — ffmpeg needs '.' as the decimal separator
        // regardless of the host's locale (see the fix in Program.cs for the app itself).
        var durationArg = durationSeconds.ToString("F3", CultureInfo.InvariantCulture);
        var fpsArg = Fps.ToString("F3", CultureInfo.InvariantCulture);
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -hide_banner -loglevel error -f lavfi -i color=c=blue:s=64x64:d={durationArg}:r={fpsArg} " +
                        $"-c:v libx264 -pix_fmt yuv420p \"{path}\"",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit(30000);
        Assert.True(File.Exists(path), $"Failed to generate test video: {process.StandardError.ReadToEnd()}");
        return path;
    }

    [Fact]
    public async Task SplitByShotBoundariesAsync_ThreeShots_NeverStraddlesAShotBoundary()
    {
        if (!FfmpegAvailable()) return; // environment without ffmpeg — skip gracefully

        var chunker = new VideoChunkingService(NullLogger<VideoChunkingService>.Instance);
        var videoPath = GenerateTestVideo(3.0, "source.mp4");
        var outputDir = Path.Combine(_workDir, "out");
        Directory.CreateDirectory(outputDir);

        // Three shots of 1.0s, 0.7s, 1.3s — each well under the 4.75s pikaswaps limit,
        // so each shot should become exactly one chunk with no sub-splitting.
        var shotBoundaries = new List<(double startTimeSeconds, double durationSeconds)>
        {
            (0.0, 1.0), (1.0, 0.7), (1.7, 1.3),
        };

        var chunks = await chunker.SplitByShotBoundariesAsync(videoPath, outputDir, Fps, shotBoundaries);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.True(File.Exists(c.SourceChunkPath)));

        Assert.Equal(0.0, chunks[0].StartTimeSeconds, 2);
        Assert.Equal(1.0, chunks[0].DurationSeconds, 2);
        Assert.Equal(1.0, chunks[1].StartTimeSeconds, 2);
        Assert.Equal(0.7, chunks[1].DurationSeconds, 2);
        Assert.Equal(1.7, chunks[2].StartTimeSeconds, 2);
        Assert.Equal(1.3, chunks[2].DurationSeconds, 2);
    }

    [Fact]
    public async Task SplitByShotBoundariesAsync_ShotLongerThanMaxChunkDuration_SubSplitsWithinThatShotOnly()
    {
        if (!FfmpegAvailable()) return;

        var chunker = new VideoChunkingService(NullLogger<VideoChunkingService>.Instance);
        var videoPath = GenerateTestVideo(6.0, "source.mp4");
        var outputDir = Path.Combine(_workDir, "out");
        Directory.CreateDirectory(outputDir);

        // A single 6s shot exceeds the 4.75s pikaswaps limit — must be sub-split into 2+ chunks,
        // all of which stay within [0, 6.0] (this shot's own span).
        var shotBoundaries = new List<(double startTimeSeconds, double durationSeconds)> { (0.0, 6.0) };

        var chunks = await chunker.SplitByShotBoundariesAsync(videoPath, outputDir, Fps, shotBoundaries);

        Assert.True(chunks.Count >= 2, "A 6s shot should be sub-split into at least 2 chunks.");
        Assert.All(chunks, c =>
        {
            Assert.True(c.StartTimeSeconds >= 0);
            Assert.True(c.StartTimeSeconds + c.DurationSeconds <= 6.01); // small ffmpeg rounding tolerance
        });
    }

    [Fact]
    public async Task SpliceChunksAsync_ProcessedChunks_ProducesPlayableOutput()
    {
        if (!FfmpegAvailable()) return;

        var chunker = new VideoChunkingService(NullLogger<VideoChunkingService>.Instance);
        var videoPath = GenerateTestVideo(2.0, "source.mp4");
        var outputDir = Path.Combine(_workDir, "out");
        Directory.CreateDirectory(outputDir);

        var chunks = await chunker.SplitIntoChunksAsync(videoPath, outputDir, Fps, 2.0);
        Assert.Single(chunks); // 2s < MaxChunkDuration — single chunk, no split needed

        var splicedPath = Path.Combine(_workDir, "spliced.mp4");
        var result = await chunker.SpliceChunksAsync(chunks, videoPath, splicedPath, Fps);

        Assert.True(File.Exists(result));
        Assert.True(new FileInfo(result).Length > 0);
    }
}
