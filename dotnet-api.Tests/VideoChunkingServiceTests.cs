using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Afrobotics.Bit.Api.Models;
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

    /// <summary>Generates a test video with both a video and an audio track — plain
    /// GenerateTestVideo has no audio, which can't exercise an audio/video seek-alignment bug.</summary>
    private string GenerateTestVideoWithAudio(double durationSeconds, string name)
    {
        var path = Path.Combine(_workDir, name);
        var durationArg = durationSeconds.ToString("F3", CultureInfo.InvariantCulture);
        var fpsArg = Fps.ToString("F3", CultureInfo.InvariantCulture);
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -hide_banner -loglevel error " +
                        $"-f lavfi -i color=c=blue:s=64x64:d={durationArg}:r={fpsArg} " +
                        $"-f lavfi -i sine=frequency=1000:duration={durationArg} " +
                        $"-c:v libx264 -pix_fmt yuv420p -c:a aac -shortest \"{path}\"",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit(30000);
        Assert.True(File.Exists(path), $"Failed to generate test video with audio: {process.StandardError.ReadToEnd()}");
        return path;
    }

    /// <summary>ffprobe a single stream's duration in seconds, or null if that stream type isn't present.</summary>
    private static double? ProbeStreamDuration(string path, string streamType)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -select_streams {streamType[0]} -show_entries stream=duration " +
                        $"-of default=noprint_wrappers=1:nokey=1 \"{path}\"",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(10000);
        if (string.IsNullOrEmpty(output)) return null;
        return double.Parse(output, CultureInfo.InvariantCulture);
    }

    private static bool FfprobeAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("ffprobe", "-version")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            });
            p!.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task ExtractSceneClipAsync_SceneNotAtStartOfVideo_AudioAndVideoStreamsStayInSync()
    {
        if (!FfmpegAvailable() || !FfprobeAvailable()) return; // environment without ffmpeg/ffprobe — skip gracefully

        var chunker = new VideoChunkingService(NullLogger<VideoChunkingService>.Instance);

        // Source is long enough that the scene we're extracting starts well past frame 0 —
        // this is what actually exercises the seek (a scene starting at t=0 wouldn't).
        var sourcePath = GenerateTestVideoWithAudio(10.0, "source_with_audio.mp4");

        // ExtractSceneClipAsync resolves ContentItem.StorageKey relative to CWD/Uploads —
        // stage the source there so the "locally uploaded file" guard passes.
        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
        Directory.CreateDirectory(uploadsDir);
        var stagedFileName = $"test-{Guid.NewGuid():N}.mp4";
        var stagedPath = Path.Combine(uploadsDir, stagedFileName);
        File.Copy(sourcePath, stagedPath);

        try
        {
            var content = new ContentItem
            {
                Id = "v-test", Title = "Test", Duration = "00:00:10", Resolution = "64x64",
                Width = 64, Height = 64, FrameRate = (int)Fps, SourceChannel = "Test",
                StorageKey = $"/api/content/file/{stagedFileName}",
            };
            // Scene starts 4s in (frame 40 at 10fps) and runs for 3s — well past the source's start.
            var scene = new SceneItem
            {
                Id = "sc-test", ContentId = content.Id, StartFrame = 40, EndFrame = 70,
                SceneIndex = 0, DurationSeconds = 3.0,
            };

            var outputPath = Path.Combine(_workDir, "extracted_scene.mp4");
            await chunker.ExtractSceneClipAsync(scene, content, outputPath);

            Assert.True(File.Exists(outputPath));

            var videoDuration = ProbeStreamDuration(outputPath, "video");
            var audioDuration = ProbeStreamDuration(outputPath, "audio");

            Assert.NotNull(videoDuration);
            Assert.NotNull(audioDuration);
            // Both streams should reflect the requested 3s scene duration, and — critically for
            // audio/video sync — agree with each other. A misaligned coarse-only seek (the bug
            // this test guards against) tends to manifest as the two streams drifting apart.
            Assert.Equal(3.0, videoDuration!.Value, 1);
            Assert.Equal(3.0, audioDuration!.Value, 1);
            Assert.Equal(videoDuration.Value, audioDuration.Value, 1);
        }
        finally
        {
            try { File.Delete(stagedPath); } catch { }
        }
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

    [Fact]
    public async Task SpliceFinalAssemblyAsync_MixOfReplacementAndOriginalScenes_ProducesFullDurationOutput()
    {
        if (!FfmpegAvailable() || !FfprobeAvailable()) return;

        var chunker = new VideoChunkingService(NullLogger<VideoChunkingService>.Instance);
        var sourcePath = GenerateTestVideo(4.0, "source.mp4"); // two 2s scenes
        var replacementClipPath = GenerateTestVideo(2.0, "replacement.mp4"); // stands in for scene 0's queued render

        var scenes = new List<(SceneItem scene, string? replacementClipPath)>
        {
            (new SceneItem { Id = "sc-0", ContentId = "c-01", SceneIndex = 0, StartFrame = 0, EndFrame = 19, DurationSeconds = 2.0 }, replacementClipPath),
            (new SceneItem { Id = "sc-1", ContentId = "c-01", SceneIndex = 1, StartFrame = 20, EndFrame = 39, DurationSeconds = 2.0 }, null), // no queued render — original footage
        };

        var workDir = Path.Combine(_workDir, "final-assembly-work");
        var outputPath = Path.Combine(_workDir, "final.mp4");
        var progressCalls = new List<(int done, int total)>();

        var result = await chunker.SpliceFinalAssemblyAsync(
            sourcePath, scenes, Fps, 64, 64, workDir, outputPath,
            onProgress: (done, total) => { progressCalls.Add((done, total)); return Task.CompletedTask; });

        Assert.True(File.Exists(result));
        var outputDuration = ProbeStreamDuration(result, "video");
        Assert.NotNull(outputDuration);
        Assert.Equal(4.0, outputDuration!.Value, 1); // both 2s scenes present, regardless of source

        Assert.Equal(new[] { (1, 2), (2, 2) }, progressCalls);
    }
}
