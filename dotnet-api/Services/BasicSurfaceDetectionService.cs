using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Default detection: generates 2–4 random candidate surfaces per scene.
/// Used when no external AI engine is configured (engine_detection = "basic").
/// </summary>
public class BasicSurfaceDetectionService : ISurfaceDetectionService
{
    private static readonly string[] SurfaceTypes =
        { "Billboard", "Wall Banner", "Digital Screen", "Field Board", "Table Surface", "Window Signage" };

    public Task<List<SurfaceDetectionResult>> DetectAsync(string contentId, int sceneIndex, int startFrame, int endFrame)
    {
        var rng = new Random();
        var count = rng.Next(2, 5);
        var results = new List<SurfaceDetectionResult>();

        for (int i = 0; i < count; i++)
        {
            var w = 1280; var h = 720;
            var sx = rng.Next(100, w - 400);
            var sy = rng.Next(80, h - 200);
            var sw = rng.Next(200, 500);
            var sh = rng.Next(100, 300);

            var coords = JsonSerializer.Serialize(new[]
            {
                new { x = sx, y = sy },
                new { x = sx + sw, y = sy },
                new { x = sx + sw, y = sy + sh },
                new { x = sx, y = sy + sh }
            });

            results.Add(new SurfaceDetectionResult
            {
                SurfaceType = SurfaceTypes[rng.Next(SurfaceTypes.Length)],
                BoundaryCoordinatesJson = coords,
                EstimatedDepth = Math.Round(1.5 + rng.NextDouble() * 8.5, 1),
                OrientationVectorJson = JsonSerializer.Serialize(new { yaw = rng.Next(-15, 15), pitch = rng.Next(-5, 5), roll = rng.Next(-3, 3) }),
                ConfidenceScore = Math.Round(0.65 + rng.NextDouble() * 0.30, 2),
                ViabilityScore = Math.Round(0.55 + rng.NextDouble() * 0.40, 2)
            });
        }

        return Task.FromResult(results);
    }
}
