using System;
using System.Threading;
using System.Threading.Tasks;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Default tracking: throws to force admin configuration.
/// The "basic" engine is intentionally non-functional — it exists only to
/// force operators to configure a real tracking engine (sam3).
/// </summary>
public class BasicTrackingService : ISurfaceTrackingService
{
    public Task<List<FrameBoundary>> TrackAsync(
        string surfaceId, string videoPath, int startFrame, int endFrame,
        string seedBoundaryJson, int promptFrame = -1, string? sam3Prompt = null, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "No tracking engine is configured. " +
            "Set Platform Setting 'engine_tracking' to 'sam3' to enable per-frame surface tracking. " +
            "Ensure the Fal.ai API key is configured ('falai_api_key' setting).");
    }

    /// <summary>Preview segmentation is not supported without a real tracking engine.</summary>
    public Task<SegmentPreviewResult?> PreviewSegmentAsync(
        string contentId, string videoPath, int frameIndex, int x, int y, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<SegmentPreviewResult?>(null);
    }
}
