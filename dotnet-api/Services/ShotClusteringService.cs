using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Clusters shots into scenes using SAM3 image embeddings and visual similarity.
///
/// Core invariant: scenes are TEMPORALLY CONTIGUOUS. Shot indices within a scene
/// must be consecutive with no gaps. A scene-closing rule prevents interleaving:
/// after N consecutive shots fail to match the current open scene, that scene is
/// finalized and a new scene begins.
/// </summary>
public class ShotClusteringService
{
    private readonly PostgresDbContext _db;
    private readonly ILogger<ShotClusteringService> _logger;

    /// <summary>Number of consecutive non-matching shots before closing the current scene.</summary>
    private const int CloseAfterNonMatches = 4;

    /// <summary>Default cosine similarity threshold for shot pairing. Overridable per request.</summary>
    private const double DefaultThreshold = 0.85;

    public ShotClusteringService(PostgresDbContext db, ILogger<ShotClusteringService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Cluster unassigned shots for a given content item into scenes.
    /// </summary>
    /// <param name="contentId">Content to cluster.</param>
    /// <param name="threshold">Cosine similarity threshold (0.0–1.0). Default 0.85.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<List<SceneItem>> ClusterShotsAsync(
        string contentId,
        double threshold = DefaultThreshold,
        CancellationToken ct = default)
    {
        var shots = await _db.Set<ShotItem>()
            .Where(s => s.ContentId == contentId)
            .OrderBy(s => s.ShotIndex)
            .ToListAsync(ct);

        if (shots.Count == 0)
        {
            _logger.LogWarning("[ShotCluster] No shots found for content {ContentId}", contentId);
            return new List<SceneItem>();
        }

        // ── Phase 1: Build scene groups with contiguity enforcement ──
        var sceneGroups = BuildSceneGroups(shots, threshold);

        // ── Phase 2: Persist assignments & create/update SceneItems ──
        var scenes = new List<SceneItem>();
        int sceneIdx = 0;

        foreach (var group in sceneGroups)
        {
            var groupShots = group.OrderBy(s => s.ShotIndex).ToList();
            var firstShot = groupShots.First();
            var lastShot = groupShots.Last();

            var scene = new SceneItem
            {
                Id = $"sc-{Guid.NewGuid()}",
                ContentId = contentId,
                SceneIndex = sceneIdx++,
                StartFrame = firstShot.StartFrame,
                EndFrame = lastShot.EndFrame,
                DurationSeconds = (lastShot.EndFrame - firstShot.StartFrame)
                    / await GetFrameRateAsync(contentId, ct),
                QaStatus = "Unchecked",
            };

            _db.SceneItems.Add(scene);

            // Assign shots to scene (single source of truth)
            foreach (var shot in groupShots)
            {
                shot.SceneId = scene.Id;
            }

            scenes.Add(scene);
            _logger.LogInformation(
                "[ShotCluster] Scene {SceneId}: shots {First}-{Last} ({Count} shots)",
                scene.Id, firstShot.ShotIndex, lastShot.ShotIndex, groupShots.Count);

            // Save each scene as soon as it's built rather than batching all of them — lets
            // polling clients see scenes appear incrementally instead of all at once at the end.
            await _db.SaveChangesAsync(ct);
        }

        // ── Phase 3: Verify contiguity invariant ──
        ValidateContiguity(scenes, shots, contentId);

        return scenes;
    }

    // ── Contiguity-aware clustering ──

    private List<List<ShotItem>> BuildSceneGroups(List<ShotItem> shots, double threshold)
    {
        var groups = new List<List<ShotItem>>();
        var currentGroup = new List<ShotItem>();
        int nonMatchStreak = 0;

        for (int i = 0; i < shots.Count; i++)
        {
            var shot = shots[i];

            if (currentGroup.Count == 0)
            {
                // Start a new scene
                currentGroup.Add(shot);
                nonMatchStreak = 0;
                continue;
            }

            // Compare against shots in the CURRENT OPEN SCENE only — NOT a global
            // rolling window. This enforces temporal contiguity: a shot cannot
            // merge back into a scene that closed several shots ago.
            bool matched = MatchesAnyInGroup(currentGroup, shot, threshold);

            if (matched)
            {
                currentGroup.Add(shot);
                nonMatchStreak = 0;
            }
            else
            {
                nonMatchStreak++;

                if (nonMatchStreak >= CloseAfterNonMatches)
                {
                    // Close current scene — consecutive non-matches exceeded threshold
                    groups.Add(currentGroup);
                    currentGroup = new List<ShotItem> { shot };
                    nonMatchStreak = 0;
                }
                else
                {
                    // Still within grace window — add to current scene despite no match
                    currentGroup.Add(shot);
                }
            }
        }

        if (currentGroup.Count > 0)
            groups.Add(currentGroup);

        // Merge tiny scenes (1-2 shots) into neighbors — likely false scene breaks
        groups = MergeTinyScenes(groups, threshold);

        return groups;
    }

    private bool MatchesAnyInGroup(List<ShotItem> group, ShotItem candidate, double threshold)
    {
        var candEmbedding = DeserializeEmbedding(candidate.KeyframeEmbeddingJson);
        if (candEmbedding == null) return false;

        foreach (var member in group)
        {
            var memEmbedding = DeserializeEmbedding(member.KeyframeEmbeddingJson);
            if (memEmbedding == null) continue;

            if (CosineSimilarity(candEmbedding, memEmbedding) >= threshold)
                return true;
        }

        return false;
    }

    // ── Tiny scene merging (prevents over-segmentation) ──

    private List<List<ShotItem>> MergeTinyScenes(List<List<ShotItem>> groups, double threshold)
    {
        if (groups.Count <= 1) return groups;

        var result = new List<List<ShotItem>>();
        var merged = new bool[groups.Count];

        for (int i = 0; i < groups.Count; i++)
        {
            if (merged[i]) continue;

            var current = groups[i];

            // Try to merge forward into next group if current is tiny
            if (current.Count <= 2 && i + 1 < groups.Count)
            {
                var next = groups[i + 1];
                // Check if boundary shots are visually similar
                var lastOfCurrent = current.Last();
                var firstOfNext = next.First();
                var emb1 = DeserializeEmbedding(lastOfCurrent.KeyframeEmbeddingJson);
                var emb2 = DeserializeEmbedding(firstOfNext.KeyframeEmbeddingJson);

                if (emb1 != null && emb2 != null &&
                    CosineSimilarity(emb1, emb2) >= threshold)
                {
                    current.AddRange(next);
                    merged[i + 1] = true;
                }
            }

            result.Add(current);
        }

        return result;
    }

    // ── Invariant validation ──

    private void ValidateContiguity(List<SceneItem> scenes, List<ShotItem> allShots, string contentId)
    {
        var shotIndexToScene = allShots
            .Where(s => s.SceneId != null)
            .ToDictionary(s => s.ShotIndex, s => s.SceneId!);

        foreach (var scene in scenes)
        {
            var sceneShots = allShots
                .Where(s => s.SceneId == scene.Id)
                .OrderBy(s => s.ShotIndex)
                .ToList();

            if (sceneShots.Count == 0) continue;

            int minIdx = sceneShots.First().ShotIndex;
            int maxIdx = sceneShots.Last().ShotIndex;

            // Every shot index between min and max MUST belong to this scene
            for (int idx = minIdx; idx <= maxIdx; idx++)
            {
                if (!shotIndexToScene.TryGetValue(idx, out var owner) || owner != scene.Id)
                {
                    _logger.LogError(
                        "[ShotCluster] CONTIGUITY VIOLATION: Scene {SceneId} claims shots {Min}-{Max} " +
                        "but shot index {ViolatingIdx} belongs to {Owner}",
                        scene.Id, minIdx, maxIdx, idx, owner ?? "unassigned");
                }
            }
        }

        _logger.LogInformation("[ShotCluster] Contiguity validation complete for content {ContentId}", contentId);
    }

    // ── Helpers ──

    private static float[]? DeserializeEmbedding(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<float[]>(json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShotCluster] DeserializeEmbedding failed: {ex.Message}");
            return null;
        }
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private async Task<double> GetFrameRateAsync(string contentId, CancellationToken ct)
    {
        var content = await _db.ContentItems.FindAsync(new object[] { contentId }, ct);
        return content?.FrameRate > 0 && content.FrameRate <= 240 ? content.FrameRate : 30;
    }
}
