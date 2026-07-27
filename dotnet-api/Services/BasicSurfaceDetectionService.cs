using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Basic detection: throws to force admin configuration.
/// The "basic" engine is intentionally non-functional — it exists only as a
/// placeholder to force operators to configure a real AI engine (yolo, replicate, or google).
/// Random mock data is NEVER acceptable for production or development.
/// </summary>
public class BasicSurfaceDetectionService : ISurfaceDetectionService
{
    public Task<List<SurfaceDetectionResult>> DetectAsync(string contentId, int sceneIndex, int startFrame, int endFrame,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "No AI detection engine is configured. " +
            "Set Platform Setting 'engine_detection' to 'yolo', 'grounding-dino', 'replicate', 'gemini', or 'google'. " +
            "Ensure the corresponding service is running (e.g., Python detection service on port 8001, or cloud API key configured).");
    }
}
