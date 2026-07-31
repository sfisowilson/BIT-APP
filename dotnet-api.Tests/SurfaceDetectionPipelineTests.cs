using System.Linq;
using Xunit;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests;

public class SurfaceDetectionPipelineTests
{
    // Regression guard for the bug where automatic "AI Split Analyze" only ever checked a
    // scene's exact midpoint frame, missing surfaces only visible partway through the scene.
    [Fact]
    public void ComputeSampleFrames_LongScene_SpansFullRangeNotJustMidpoint()
    {
        // 10s scene at 30fps: frames 0-299.
        var frames = SurfaceDetectionPipeline.ComputeSampleFrames(
            startFrame: 0, endFrame: 299, durationSeconds: 10.0, sampleIntervalSec: 2.0, maxFrames: 5);

        Assert.True(frames.Count > 1, "A 10s scene should be sampled at more than one frame.");
        var midpoint = (0 + 299) / 2;
        Assert.Contains(frames, f => f < midpoint - 30);
        Assert.Contains(frames, f => f > midpoint + 30);
    }

    [Fact]
    public void ComputeSampleFrames_ShortScene_ReturnsSingleFrame()
    {
        // A scene shorter than one sample interval should still yield at least one frame.
        var frames = SurfaceDetectionPipeline.ComputeSampleFrames(
            startFrame: 10, endFrame: 15, durationSeconds: 0.2, sampleIntervalSec: 2.0, maxFrames: 5);

        Assert.Single(frames);
        Assert.InRange(frames[0], 10, 15);
    }

    [Fact]
    public void ComputeSampleFrames_NeverExceedsMaxFrames()
    {
        var frames = SurfaceDetectionPipeline.ComputeSampleFrames(
            startFrame: 0, endFrame: 3000, durationSeconds: 100.0, sampleIntervalSec: 0.5, maxFrames: 5);

        Assert.True(frames.Count <= 5);
    }

    [Fact]
    public void ComputeSampleFrames_AllFramesWithinSceneBounds()
    {
        var frames = SurfaceDetectionPipeline.ComputeSampleFrames(
            startFrame: 40, endFrame: 70, durationSeconds: 3.0, sampleIntervalSec: 2.0, maxFrames: 5);

        Assert.All(frames, f => Assert.InRange(f, 40, 70));
        Assert.Equal(frames.Distinct().Count(), frames.Count);
    }
}
