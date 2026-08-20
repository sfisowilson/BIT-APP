using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Afrobotics.Bit.Api.Services;

/// <summary>Small ffprobe-backed helper for reading a video's native pixel dimensions and frame rate.</summary>
public static class VideoProbe
{
    private static readonly Dictionary<string, (int width, int height)> _cache = new();
    private static readonly Dictionary<string, double> _fpsCache = new();

    /// <summary>Probes a video's frame rate (frames per second) via ffprobe. Falls back to 30 on failure.</summary>
    public static double GetFrameRate(string videoPath)
    {
        if (_fpsCache.TryGetValue(videoPath, out var cached)) return cached;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=r_frame_rate -of csv=p=0 \"{videoPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            // ffprobe returns rational rates like "25/1" or "30000/1001".
            var parts = output.Split('/');
            if (parts.Length == 2 && double.TryParse(parts[0], out var num) && double.TryParse(parts[1], out var den) && den > 0)
            {
                var fps = num / den;
                _fpsCache[videoPath] = fps;
                return fps;
            }
            if (parts.Length == 1 && double.TryParse(parts[0], out var single) && single > 0)
            {
                _fpsCache[videoPath] = single;
                return single;
            }
        }
        catch { /* fall through to default */ }

        return 30;
    }

    /// <summary>Probes a video's total duration in seconds via ffprobe. Falls back to 0 on failure.</summary>
    public static double GetDurationSeconds(string videoPath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            if (double.TryParse(output, out var duration)) return duration;
        }
        catch { /* fall through to default */ }

        return 0;
    }

    /// <summary>Returns whether the video has at least one audio stream, via ffprobe. Falls back to false on failure.</summary>
    public static bool HasAudioStream(string videoPath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -select_streams a:0 -show_entries stream=codec_type -of csv=p=0 \"{videoPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            return output.Equals("audio", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Probes a video's overall bitrate (bits/sec) via ffprobe — prefers the video
    /// stream's own bit_rate, falling back to the container-level bitrate if the stream doesn't
    /// report one (common for some remuxed/VFR sources). Falls back to a safe 8 Mbps default
    /// (solid quality for 1080p) if probing fails entirely.</summary>
    public static long GetBitRate(string videoPath)
    {
        const long fallbackBps = 8_000_000;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=bit_rate -show_entries format=bit_rate -of default=noprint_wrappers=1 \"{videoPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            long? streamBitRate = null, formatBitRate = null;
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("bit_rate=") && long.TryParse(trimmed.AsSpan(9), out var val) && val > 0)
                {
                    if (streamBitRate == null) streamBitRate = val;
                    else formatBitRate = val;
                }
            }
            var resolved = streamBitRate ?? formatBitRate;
            if (resolved.HasValue && resolved.Value > 0) return resolved.Value;
        }
        catch { /* fall through to default */ }

        return fallbackBps;
    }

    /// <summary>Builds "-b:v/-maxrate/-bufsize" ffmpeg args targeting the source video's own
    /// bitrate instead of a fixed CRF. A fixed CRF re-encodes every clip down to a generic
    /// "acceptable web quality" regardless of how high-quality the actual source was — this
    /// silently threw away most of the bitrate headroom on high-bitrate professional sources
    /// (observed: 16 Mbps source -> ~3.6 Mbps output at CRF 23, same resolution/framerate).</summary>
    public static string GetTargetBitrateArgs(string videoPath)
    {
        var kbps = Math.Max(1000, GetBitRate(videoPath) / 1000);
        return $"-b:v {kbps}k -maxrate {(long)(kbps * 1.5)}k -bufsize {kbps * 2}k";
    }

    public static (int width, int height) GetDimensions(string videoPath)
    {
        if (_cache.TryGetValue(videoPath, out var cached)) return cached;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 \"{videoPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            var parts = output.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
            {
                _cache[videoPath] = (w, h);
                return (w, h);
            }
        }
        catch { /* fall through to default */ }

        return (1920, 1080);
    }
}
