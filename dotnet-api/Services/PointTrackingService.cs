using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Tracks 4 corner points across video frames for planar homography compositing.
/// 
/// Current: stub that uses pre-stored per-frame quad data from TrackingDataJson.
/// Future: integrate CoTracker3 or SAM3 point-track mode for real-time tracking.
/// </summary>
public class PointTrackingService
{
    private readonly ILogger<PointTrackingService> _logger;

    public PointTrackingService(ILogger<PointTrackingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Per-frame quad corner data.
    /// </summary>
    public class FrameQuad
    {
        public int Frame { get; set; }
        public List<(int x, int y)> Corners { get; set; } = new();
        public bool TrackingLost { get; set; }
        public int LostCornerIndex { get; set; } = -1; // -1 = all corners tracked
    }

    /// <summary>
    /// Track 4 corner points across a frame range.
    /// </summary>
    /// <param name="videoPath">Source video path.</param>
    /// <param name="startFrame">First frame to track.</param>
    /// <param name="endFrame">Last frame to track.</param>
    /// <param name="initialQuad">4 corner points at the seed frame in pixel coordinates.</param>
    /// <param name="seedFrame">Frame where the initial quad was placed.</param>
    /// <param name="fps">Video frame rate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Per-frame quad data. Empty if tracking fails entirely.</returns>
    public async Task<List<FrameQuad>> TrackCornersAsync(
        string videoPath,
        int startFrame,
        int endFrame,
        List<(int x, int y)> initialQuad,
        int seedFrame,
        double fps,
        CancellationToken ct = default)
    {
        if (initialQuad.Count < 4)
        {
            _logger.LogWarning("[PointTrack] Invalid initial quad: {Count} corners", initialQuad.Count);
            return new List<FrameQuad>();
        }

        _logger.LogInformation(
            "[PointTrack] Tracking {Start}-{End} ({Total} frames) from seed frame {Seed}",
            startFrame, endFrame, endFrame - startFrame + 1, seedFrame);

        var result = new List<FrameQuad>();
        int totalFrames = endFrame - startFrame + 1;

        // ── Stub implementation: use stored TrackingDataJson if available ──
        // In production, this would call CoTracker3 API or SAM3 point-track mode.
        // For now, we propagate the initial quad to all frames (static surface assumption).

        for (int f = startFrame; f <= endFrame; f++)
        {
            ct.ThrowIfCancellationRequested();

            // Interpolate quad from seed frame to current frame
            // Stub: assume static surface — same quad for all frames
            var frameQuad = new FrameQuad
            {
                Frame = f,
                Corners = initialQuad.Select(c => (c.x, c.y)).ToList(),
                TrackingLost = false,
                LostCornerIndex = -1
            };

            result.Add(frameQuad);
        }

        _logger.LogInformation("[PointTrack] Completed: {Count} frames tracked", result.Count);
        return result;
    }

    /// <summary>
    /// Check if tracking has been lost on any corner.
    /// </summary>
    public bool HasTrackingLost(List<FrameQuad> frames)
    {
        return frames.Any(f => f.TrackingLost);
    }

    /// <summary>
    /// Interpolate missing frames from bracketing tracked frames.
    /// </summary>
    public void InterpolateGaps(List<FrameQuad> frames)
    {
        for (int i = 0; i < frames.Count; i++)
        {
            if (!frames[i].TrackingLost) continue;

            // Find previous good frame
            FrameQuad? prev = null;
            for (int p = i - 1; p >= 0; p--)
            {
                if (!frames[p].TrackingLost) { prev = frames[p]; break; }
            }

            // Find next good frame
            FrameQuad? next = null;
            for (int n = i + 1; n < frames.Count; n++)
            {
                if (!frames[n].TrackingLost) { next = frames[n]; break; }
            }

            if (prev != null && next != null)
            {
                // Linear interpolation between prev and next
                double t = (double)(i - prev.Frame) / (next.Frame - prev.Frame);
                frames[i].Corners = new List<(int, int)>();
                for (int c = 0; c < 4; c++)
                {
                    int x = (int)(prev.Corners[c].x + (next.Corners[c].x - prev.Corners[c].x) * t);
                    int y = (int)(prev.Corners[c].y + (next.Corners[c].y - prev.Corners[c].y) * t);
                    frames[i].Corners.Add((x, y));
                }
                frames[i].TrackingLost = false;
            }
        }
    }

    /// <summary>
    /// Serialize tracked frames to JSON for storage in TrackingDataJson.
    /// Format: [{frame, corners: [{x,y},{x,y},{x,y},{x,y}]}, ...]
    /// </summary>
    public static string SerializeToJson(List<FrameQuad> frames)
    {
        var data = frames.Select(f => new
        {
            frame = f.Frame,
            corners = f.Corners.Select(c => new { x = c.x, y = c.y }).ToList()
        }).ToList();

        return JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }
}
