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
