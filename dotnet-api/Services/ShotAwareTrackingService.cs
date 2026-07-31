using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Services;

public interface IShotAwareTrackingService
{
    /// <summary>
    /// Track a Planar (flat signage) quad across every shot in its scene. Fits a 4-corner
    /// quad per tracked frame from SAM3 video-rle masks. Re-anchors with a text prompt at
    /// each shot boundary after the first.
    /// </summary>
    Task<ShotAwareTrackingResult> TrackQuadAcrossShotsAsync(
        string sceneId,
        string videoPath,
        List<(int x, int y)> seedQuad,
        int seedFrame,
        string? sam3Prompt,
        string surfaceType,
        CancellationToken ct = default);

    /// <summary>
    /// Track a Generative (3D product) mask across every shot in its scene. Stores the raw
    /// RLE per tracked frame (used for luma-mask compositing and drift-check). Re-anchors
    /// with a text prompt at each shot boundary after the first.
    /// </summary>
    Task<ShotAwareTrackingResult> TrackMaskAcrossShotsAsync(
        string sceneId,
        string videoPath,
        (int xMin, int yMin, int xMax, int yMax) seedBox,
        int seedFrame,
        string? sam3Prompt,
        string surfaceType,
        CancellationToken ct = default);
}

/// <summary>Result of tracking a placement across every shot in its scene.</summary>
public class ShotAwareTrackingResult
{
    /// <summary>Shot-segmented JSON — see <see cref="Models.SurfaceItem.TrackingDataJson"/>.</summary>
    public string TrackingDataJson { get; set; } = "{\"shotSegments\":[]}";

    /// <summary>
    /// Lightweight per-frame centroid JSON — see <see cref="Models.SurfaceItem.TrackingPointsJson"/>.
    /// A flat, frame-ordered array `[{frame,x,y}, ...]` across every shot segment, derived from
    /// the same tracking pass as TrackingDataJson (quad corners averaged for Planar, decoded RLE
    /// mask pixels averaged for Generative) — cheap for the frontend to binary-search into during
    /// playback without needing to understand the full shot-segmented structure or decode RLE itself.
    /// </summary>
    public string TrackingPointsJson { get; set; } = "[]";

    /// <summary>Tracked | PartialCoverage | LockLost — mirrors <see cref="Models.SurfaceItem.TrackingStatus"/>.</summary>
    public string OverallStatus { get; set; } = "NotTracked";
}

/// <summary>
/// Tracks a Planar quad or Generative mask across every shot in a scene, standardizing both
/// compositing paths on fal-ai/sam-3/video-rle (via <see cref="ISurfaceTrackingService"/>).
///
/// The shot containing the seed click/quad is tracked continuously via a box prompt. Every
/// subsequent shot is re-anchored with a text prompt describing the surface — a hard cut
/// changes the surface's screen position but not its semantic identity, so the previous
/// shot's pixel coordinates are meaningless in the new camera angle. A shot where nothing
/// clears the detection threshold is marked Skipped: the source video passes through
/// unmodified for that shot's frames rather than failing the whole render.
/// </summary>
public class ShotAwareTrackingService : IShotAwareTrackingService
{
    private readonly PostgresDbContext _context;
    private readonly ISurfaceTrackingService _tracking;

    public ShotAwareTrackingService(PostgresDbContext context, ISurfaceTrackingService tracking)
    {
        _context = context;
        _tracking = tracking;
    }

    public async Task<ShotAwareTrackingResult> TrackQuadAcrossShotsAsync(
        string sceneId, string videoPath, List<(int x, int y)> seedQuad, int seedFrame,
        string? sam3Prompt, string surfaceType, CancellationToken ct = default)
    {
        var seedBox = QuadToBox(seedQuad);
        var (videoWidth, videoHeight) = VideoProbe.GetDimensions(videoPath);
        var segments = await TrackAcrossShotsAsync(sceneId, videoPath, seedBox, seedFrame, sam3Prompt, surfaceType, ct);

        var trackingPoints = new List<(int frame, double x, double y)>();

        var shotSegments = segments.Select(seg => new
        {
            shotId = seg.Shot.Id,
            shotIndex = seg.Shot.ShotIndex,
            startFrame = seg.Shot.StartFrame,
            endFrame = seg.Shot.EndFrame,
            status = seg.Status,
            trackId = seg.TrackId,
            confidence = seg.Confidence,
            frames = seg.Frames.Select(f =>
            {
                var polygon = RleDecoder.MaskToPolygon(RleDecoder.Decode(f.rle, videoWidth, videoHeight));
                var corners = polygon.Count >= 3 ? MinAreaRectFitter.FitQuad(polygon) : seedQuad;
                trackingPoints.Add((f.frame, corners.Average(c => c.x), corners.Average(c => c.y)));
                return new
                {
                    frame = f.frame,
                    corners = corners.Select(c => new { x = c.x, y = c.y }),
                };
            }).ToList(),
        }).ToList();

        return new ShotAwareTrackingResult
        {
            TrackingDataJson = JsonSerializer.Serialize(new { shotSegments }),
            TrackingPointsJson = BuildTrackingPointsJson(trackingPoints),
            OverallStatus = ComputeOverallStatus(segments),
        };
    }

    public async Task<ShotAwareTrackingResult> TrackMaskAcrossShotsAsync(
        string sceneId, string videoPath, (int xMin, int yMin, int xMax, int yMax) seedBox, int seedFrame,
        string? sam3Prompt, string surfaceType, CancellationToken ct = default)
    {
        var segments = await TrackAcrossShotsAsync(sceneId, videoPath, seedBox, seedFrame, sam3Prompt, surfaceType, ct);
        var (videoWidth, videoHeight) = VideoProbe.GetDimensions(videoPath);

        var trackingPoints = new List<(int frame, double x, double y)>();

        var shotSegments = segments.Select(seg => new
        {
            shotId = seg.Shot.Id,
            shotIndex = seg.Shot.ShotIndex,
            startFrame = seg.Shot.StartFrame,
            endFrame = seg.Shot.EndFrame,
            status = seg.Status,
            trackId = seg.TrackId,
            confidence = seg.Confidence,
            frames = seg.Frames.Select(f =>
            {
                var centroid = RleCentroid(f.rle, videoWidth, videoHeight);
                if (centroid.HasValue) trackingPoints.Add((f.frame, centroid.Value.x, centroid.Value.y));
                return new { frame = f.frame, rle = f.rle, trackId = seg.TrackId };
            }).ToList(),
        }).ToList();

        return new ShotAwareTrackingResult
        {
            TrackingDataJson = JsonSerializer.Serialize(new { shotSegments }),
            TrackingPointsJson = BuildTrackingPointsJson(trackingPoints),
            OverallStatus = ComputeOverallStatus(segments),
        };
    }

    /// <summary>Average pixel position of an SAM3 video-rle mask — cheap approximation of "where
    /// the tracked surface is" for a single moving point, without needing the full contour trace
    /// that MaskToPolygon does for the Planar path's quad-fitting.</summary>
    private static (double x, double y)? RleCentroid(string rle, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(rle)) return null;
        var mask = RleDecoder.Decode(rle, width, height);
        long sumX = 0, sumY = 0, count = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (mask[y, x]) { sumX += x; sumY += y; count++; }
        return count > 0 ? ((double)sumX / count, (double)sumY / count) : null;
    }

    private static string BuildTrackingPointsJson(List<(int frame, double x, double y)> points)
    {
        if (points.Count == 0) return "[]";
        var ordered = points.OrderBy(p => p.frame)
            .Select(p => new { frame = p.frame, x = Math.Round(p.x, 1), y = Math.Round(p.y, 1) });
        return JsonSerializer.Serialize(ordered);
    }

    // ── Shared shot-loop ──

    private class ShotSegment
    {
        public ShotItem Shot { get; set; } = null!;
        public string Status { get; set; } = "Skipped"; // Tracked | Reanchored | Skipped | LockLost
        public int TrackId { get; set; }
        public double Confidence { get; set; }
        public List<(int frame, string rle)> Frames { get; set; } = new();
    }

    private async Task<List<ShotSegment>> TrackAcrossShotsAsync(
        string sceneId, string videoPath, (int xMin, int yMin, int xMax, int yMax) seedBox, int seedFrame,
        string? sam3Prompt, string surfaceType, CancellationToken ct)
    {
        var shots = await ResolveShotsAsync(sceneId, ct);
        if (shots.Count == 0) return new List<ShotSegment>();

        var reanchorPrompt = string.IsNullOrWhiteSpace(sam3Prompt) ? $"the {surfaceType}" : sam3Prompt;
        var segments = new List<ShotSegment>();

        // The shot containing the seed frame is tracked first, continuously, via the box prompt.
        var seedShot = shots.FirstOrDefault(s => seedFrame >= s.StartFrame && seedFrame <= s.EndFrame) ?? shots[0];

        foreach (var shot in shots)
        {
            ct.ThrowIfCancellationRequested();

            List<RleFrameResult> rleFrames;
            bool isSeedShot = shot.Id == seedShot.Id;

            if (isSeedShot)
            {
                // fal.ai rejects box/point prompts that aren't at frame 0 of the submitted clip
                // ("No prompts available for this video chunk") — confirmed by reproducing the
                // exact failing request. Start the trimmed range AT the seed frame itself (instead
                // of the shot's start) so the prompt always lands at clip-relative frame 0. This
                // means only frames from the seed point forward within this shot get tracked —
                // an acceptable tradeoff, since a seed click/quad only has meaning going forward
                // in time anyway.
                // Pass the same semantic text hint alongside the box (not box-only) — a box alone
                // gives SAM3 no cue about which content inside it to segment, which measurably
                // fails on low-texture/low-contrast surfaces (e.g. a plain wall) even though the
                // identical text prompt reliably finds the same surface in every other shot.
                var trimStart = Math.Clamp(seedFrame, shot.StartFrame, shot.EndFrame);
                rleFrames = await SegmentWithThresholdFallbackAsync(videoPath, trimStart, shot.EndFrame,
                    seedBox: seedBox, textPrompt: reanchorPrompt, promptFrame: trimStart, ct: ct);
            }
            else
            {
                rleFrames = await SegmentWithThresholdFallbackAsync(videoPath, shot.StartFrame, shot.EndFrame,
                    textPrompt: reanchorPrompt, promptFrame: shot.StartFrame, ct: ct);
            }

            segments.Add(BuildSegment(shot, rleFrames, isSeedShot));
        }

        return segments;
    }

    /// <summary>fal.ai's own docs recommend dropping detection_threshold to 0.2-0.3 when a
    /// prompt fails to find anything at the default 0.5 — retry once at a lower threshold
    /// before accepting "nothing found" for a shot.</summary>
    private async Task<List<RleFrameResult>> SegmentWithThresholdFallbackAsync(
        string videoPath, int startFrame, int endFrame,
        (int xMin, int yMin, int xMax, int yMax)? seedBox = null,
        string? textPrompt = null, int promptFrame = -1, CancellationToken ct = default)
    {
        var frames = await _tracking.SegmentVideoRleAsync(
            videoPath, startFrame, endFrame,
            seedBox: seedBox, textPrompt: textPrompt, promptFrame: promptFrame,
            detectionThreshold: 0.5, cancellationToken: ct);

        if (frames.Sum(f => f.Objects.Count) > 0) return frames;

        return await _tracking.SegmentVideoRleAsync(
            videoPath, startFrame, endFrame,
            seedBox: seedBox, textPrompt: textPrompt, promptFrame: promptFrame,
            detectionThreshold: 0.25, cancellationToken: ct);
    }

    /// <summary>Picks the best-confidence track_id from the first frame with any detection,
    /// then follows that same track_id through the rest of the shot (falling back to the
    /// highest-confidence object in frames where that exact track_id is absent).</summary>
    private static ShotSegment BuildSegment(ShotItem shot, List<RleFrameResult> rleFrames, bool isSeedShot)
    {
        var firstWithObjects = rleFrames.FirstOrDefault(f => f.Objects.Count > 0);
        if (firstWithObjects == null)
        {
            return new ShotSegment
            {
                Shot = shot,
                Status = isSeedShot ? "LockLost" : "Skipped",
                Frames = new List<(int, string)>(),
            };
        }

        var chosen = firstWithObjects.Objects.OrderByDescending(o => o.Confidence).First();
        var frames = new List<(int frame, string rle)>();
        double confSum = 0;
        int confCount = 0;

        foreach (var f in rleFrames)
        {
            var obj = f.Objects.FirstOrDefault(o => o.TrackId == chosen.TrackId)
                       ?? f.Objects.OrderByDescending(o => o.Confidence).FirstOrDefault();
            if (obj == null || string.IsNullOrEmpty(obj.Rle)) continue;

            frames.Add((f.FrameIndex, obj.Rle));
            confSum += obj.Confidence;
            confCount++;
        }

        return new ShotSegment
        {
            Shot = shot,
            Status = isSeedShot ? "Tracked" : "Reanchored",
            TrackId = chosen.TrackId,
            Confidence = confCount > 0 ? confSum / confCount : chosen.Confidence,
            Frames = frames,
        };
    }

    private static string ComputeOverallStatus(List<ShotSegment> segments)
    {
        if (segments.Count == 0) return "LockLost";
        if (segments.Any(s => s.Status == "LockLost")) return "LockLost";
        if (segments.All(s => s.Frames.Count > 0)) return "Tracked";
        if (segments.Any(s => s.Frames.Count > 0)) return "PartialCoverage";
        return "LockLost"; // every shot skipped — nothing to render
    }

    private async Task<List<ShotItem>> ResolveShotsAsync(string sceneId, CancellationToken ct)
    {
        var shots = await _context.Shots
            .Where(s => s.SceneId == sceneId)
            .OrderBy(s => s.ShotIndex)
            .ToListAsync(ct);

        if (shots.Count > 0) return shots;

        // Backward compatibility: scenes detected before shot clustering existed have no
        // ShotItem rows. Treat the whole scene as a single synthetic shot so tracking still
        // works, just without cross-cut re-anchoring.
        var scene = await _context.SceneItems.FindAsync(new object[] { sceneId }, ct);
        if (scene == null) return new List<ShotItem>();

        return new List<ShotItem>
        {
            new ShotItem
            {
                Id = scene.Id,
                ContentId = scene.ContentId,
                SceneId = scene.Id,
                ShotIndex = 0,
                StartFrame = scene.StartFrame,
                EndFrame = scene.EndFrame,
            },
        };
    }

    private static (int xMin, int yMin, int xMax, int yMax) QuadToBox(List<(int x, int y)> quad)
    {
        var xs = quad.Select(c => c.x).ToList();
        var ys = quad.Select(c => c.y).ToList();
        return (xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }
}
