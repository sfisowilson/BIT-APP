using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests;

/// <summary>
/// Exercises RenderJobService.EnsureUnderPikaswapsSizeLimitAsync against real ffmpeg-generated
/// clips — see governance/rules/no-mock-code.md. Skips gracefully if ffmpeg isn't on PATH.
/// </summary>
public class RenderJobServiceTests : IDisposable
{
    private readonly string _workDir;

    public RenderJobServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"bit-renderjob-tests-{Guid.NewGuid():N}");
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

    /// <summary>Generates a clip deliberately over 8MB. Uses random noise frames (not a plain
    /// test pattern) at a forced constant bitrate — simple synthetic patterns compress far below
    /// their target bitrate, so they don't reliably reproduce the oversized-file scenario.</summary>
    private string GenerateOversizedClip(double durationSeconds, string name)
    {
        var path = Path.Combine(_workDir, name);
        var durationArg = durationSeconds.ToString("F3", CultureInfo.InvariantCulture);
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -hide_banner -loglevel error -f lavfi -i \"nullsrc=s=1280x720:r=30:d={durationArg}\" " +
                        $"-vf \"geq=random(1)*255:random(1)*255:random(1)*255\" " +
                        $"-c:v libx264 -b:v 20M -minrate 20M -maxrate 20M -bufsize 5M -pix_fmt yuv420p \"{path}\"",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit(30000);
        Assert.True(File.Exists(path), $"Failed to generate oversized test clip: {process.StandardError.ReadToEnd()}");
        return path;
    }

    [Fact]
    public async Task EnsureUnderPikaswapsSizeLimitAsync_OversizedClip_ReencodesUnder8MB()
    {
        if (!FfmpegAvailable()) return;

        var oversized = GenerateOversizedClip(4.5, "oversized.mp4");
        Assert.True(new FileInfo(oversized).Length > 8L * 1024 * 1024, "Test setup: clip should start over 8MB.");

        var result = await RenderJobService.EnsureUnderPikaswapsSizeLimitAsync(oversized, 4.5, default);

        Assert.NotEqual(oversized, result);
        Assert.True(File.Exists(result));
        Assert.True(new FileInfo(result).Length <= 8L * 1024 * 1024,
            $"Re-encoded clip is still {new FileInfo(result).Length} bytes — should fit fal.ai's 8MB Pikaswaps limit.");
    }

    [Fact]
    public async Task EnsureUnderPikaswapsSizeLimitAsync_ClipAlreadyUnderLimit_ReturnsSamePathUnchanged()
    {
        if (!FfmpegAvailable()) return;

        var path = Path.Combine(_workDir, "small.mp4");
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -hide_banner -loglevel error -f lavfi -i color=c=blue:s=64x64:d=1:r=10 " +
                        $"-c:v libx264 -pix_fmt yuv420p \"{path}\"",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit(30000);
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length <= 8L * 1024 * 1024, "Test setup: clip should already be small.");

        var result = await RenderJobService.EnsureUnderPikaswapsSizeLimitAsync(path, 1.0, default);

        Assert.Equal(path, result); // no unnecessary re-encode/quality loss for clips already under the cap
    }
}
