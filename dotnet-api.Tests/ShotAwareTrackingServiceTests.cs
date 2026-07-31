using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests;

public class ShotAwareTrackingServiceTests
{
    private static PostgresDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseInMemoryDatabase(databaseName: $"BitTestDb_{Guid.NewGuid()}")
            .Options;
        return new PostgresDbContext(options);
    }

    private static RleFrameResult Frame(int frameIndex, int trackId, string rle, double confidence = 0.9) => new()
    {
        FrameIndex = frameIndex,
        Objects = new List<RleObjectResult> { new() { TrackId = trackId, Rle = rle, Confidence = confidence } },
    };

    private const string SomeRle = "0 999999999"; // whole-frame foreground — decodes to a valid non-degenerate mask

    [Fact]
    public async Task TrackMaskAcrossShotsAsync_SingleShotScene_TracksSeedShotOnlyAndReturnsTracked()
    {
        using var db = GetInMemoryDbContext();
        db.SceneItems.Add(new SceneItem { Id = "sc-01", ContentId = "ct-01", SceneIndex = 0, StartFrame = 0, EndFrame = 29, DurationSeconds = 1 });
        db.Set<ShotItem>().Add(new ShotItem { Id = "sh-0", ContentId = "ct-01", SceneId = "sc-01", ShotIndex = 0, StartFrame = 0, EndFrame = 29 });
        await db.SaveChangesAsync();

        var mockTracking = new Mock<ISurfaceTrackingService>();
        mockTracking
            .Setup(t => t.SegmentVideoRleAsync(
                It.IsAny<string>(), 0, 29,
                It.IsAny<(int, int, int, int)?>(), It.IsAny<(int, int)?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RleFrameResult> { Frame(0, 0, SomeRle), Frame(1, 0, SomeRle) });

        var service = new ShotAwareTrackingService(db, mockTracking.Object);
        var result = await service.TrackMaskAcrossShotsAsync(
            "sc-01", "fake-video.mp4", (10, 10, 100, 100), seedFrame: 0,
            sam3Prompt: null, surfaceType: "Billboard");

        Assert.Equal("Tracked", result.OverallStatus);
        using var doc = JsonDocument.Parse(result.TrackingDataJson);
        var segments = doc.RootElement.GetProperty("shotSegments");
        Assert.Equal(1, segments.GetArrayLength());
        Assert.Equal("Tracked", segments[0].GetProperty("status").GetString());
        Assert.Equal(2, segments[0].GetProperty("frames").GetArrayLength());
    }

    [Fact]
    public async Task TrackMaskAcrossShotsAsync_MultiShotScene_ReanchorsSuccessfullyAtEachCut()
    {
        using var db = GetInMemoryDbContext();
        db.SceneItems.Add(new SceneItem { Id = "sc-02", ContentId = "ct-02", SceneIndex = 0, StartFrame = 0, EndFrame = 59, DurationSeconds = 2 });
        db.Set<ShotItem>().AddRange(
            new ShotItem { Id = "sh-0", ContentId = "ct-02", SceneId = "sc-02", ShotIndex = 0, StartFrame = 0, EndFrame = 29 },
            new ShotItem { Id = "sh-1", ContentId = "ct-02", SceneId = "sc-02", ShotIndex = 1, StartFrame = 30, EndFrame = 59 });
        await db.SaveChangesAsync();

        var mockTracking = new Mock<ISurfaceTrackingService>();
        // Seed shot (box prompt)
        mockTracking
            .Setup(t => t.SegmentVideoRleAsync(
                It.IsAny<string>(), 0, 29,
                It.Is<(int, int, int, int)?>(b => b.HasValue), It.IsAny<(int, int)?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RleFrameResult> { Frame(0, 0, SomeRle) });
        // Re-anchor shot (text prompt)
        mockTracking
            .Setup(t => t.SegmentVideoRleAsync(
                It.IsAny<string>(), 30, 59,
                It.IsAny<(int, int, int, int)?>(), It.IsAny<(int, int)?>(), It.Is<string?>(p => p != null),
                It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RleFrameResult> { Frame(30, 5, SomeRle) });

        var service = new ShotAwareTrackingService(db, mockTracking.Object);
        var result = await service.TrackMaskAcrossShotsAsync(
            "sc-02", "fake-video.mp4", (10, 10, 100, 100), seedFrame: 0,
            sam3Prompt: "the billboard", surfaceType: "Billboard");

        Assert.Equal("Tracked", result.OverallStatus);
        using var doc = JsonDocument.Parse(result.TrackingDataJson);
        var segments = doc.RootElement.GetProperty("shotSegments");
        Assert.Equal(2, segments.GetArrayLength());
        Assert.Equal("Tracked", segments[0].GetProperty("status").GetString());
        Assert.Equal("Reanchored", segments[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task TrackMaskAcrossShotsAsync_ReanchorFindsNothing_MarksThatShotSkippedButOverallPartialCoverage()
    {
        using var db = GetInMemoryDbContext();
        db.SceneItems.Add(new SceneItem { Id = "sc-03", ContentId = "ct-03", SceneIndex = 0, StartFrame = 0, EndFrame = 59, DurationSeconds = 2 });
        db.Set<ShotItem>().AddRange(
            new ShotItem { Id = "sh-0", ContentId = "ct-03", SceneId = "sc-03", ShotIndex = 0, StartFrame = 0, EndFrame = 29 },
            new ShotItem { Id = "sh-1", ContentId = "ct-03", SceneId = "sc-03", ShotIndex = 1, StartFrame = 30, EndFrame = 59 });
        await db.SaveChangesAsync();

        var mockTracking = new Mock<ISurfaceTrackingService>();
        mockTracking
            .Setup(t => t.SegmentVideoRleAsync(
                It.IsAny<string>(), 0, 29,
                It.Is<(int, int, int, int)?>(b => b.HasValue), It.IsAny<(int, int)?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RleFrameResult> { Frame(0, 0, SomeRle) });
        // Re-anchor call finds nothing (occluded / off-screen in this shot)
        mockTracking
            .Setup(t => t.SegmentVideoRleAsync(
                It.IsAny<string>(), 30, 59,
                It.IsAny<(int, int, int, int)?>(), It.IsAny<(int, int)?>(), It.Is<string?>(p => p != null),
                It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RleFrameResult>());

        var service = new ShotAwareTrackingService(db, mockTracking.Object);
        var result = await service.TrackMaskAcrossShotsAsync(
            "sc-03", "fake-video.mp4", (10, 10, 100, 100), seedFrame: 0,
            sam3Prompt: "the billboard", surfaceType: "Billboard");

        Assert.Equal("PartialCoverage", result.OverallStatus);
        using var doc = JsonDocument.Parse(result.TrackingDataJson);
        var segments = doc.RootElement.GetProperty("shotSegments");
        Assert.Equal("Tracked", segments[0].GetProperty("status").GetString());
        Assert.Equal("Skipped", segments[1].GetProperty("status").GetString());
        Assert.Equal(0, segments[1].GetProperty("frames").GetArrayLength());
    }

    [Fact]
    public async Task TrackMaskAcrossShotsAsync_SeedShotFindsNothing_ReturnsLockLost()
    {
        using var db = GetInMemoryDbContext();
        db.SceneItems.Add(new SceneItem { Id = "sc-04", ContentId = "ct-04", SceneIndex = 0, StartFrame = 0, EndFrame = 29, DurationSeconds = 1 });
        db.Set<ShotItem>().Add(new ShotItem { Id = "sh-0", ContentId = "ct-04", SceneId = "sc-04", ShotIndex = 0, StartFrame = 0, EndFrame = 29 });
        await db.SaveChangesAsync();

        var mockTracking = new Mock<ISurfaceTrackingService>();
        mockTracking
            .Setup(t => t.SegmentVideoRleAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<(int, int, int, int)?>(), It.IsAny<(int, int)?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RleFrameResult>());

        var service = new ShotAwareTrackingService(db, mockTracking.Object);
        var result = await service.TrackMaskAcrossShotsAsync(
            "sc-04", "fake-video.mp4", (10, 10, 100, 100), seedFrame: 0,
            sam3Prompt: null, surfaceType: "Billboard");

        Assert.Equal("LockLost", result.OverallStatus);
    }

    [Fact]
    public async Task TrackQuadAcrossShotsAsync_ProducesFourCornerQuadPerTrackedFrame()
    {
        using var db = GetInMemoryDbContext();
        db.SceneItems.Add(new SceneItem { Id = "sc-05", ContentId = "ct-05", SceneIndex = 0, StartFrame = 0, EndFrame = 29, DurationSeconds = 1 });
        db.Set<ShotItem>().Add(new ShotItem { Id = "sh-0", ContentId = "ct-05", SceneId = "sc-05", ShotIndex = 0, StartFrame = 0, EndFrame = 29 });
        await db.SaveChangesAsync();

        var mockTracking = new Mock<ISurfaceTrackingService>();
        mockTracking
            .Setup(t => t.SegmentVideoRleAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<(int, int, int, int)?>(), It.IsAny<(int, int)?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RleFrameResult> { Frame(0, 0, SomeRle) });

        var service = new ShotAwareTrackingService(db, mockTracking.Object);
        var seedQuad = new List<(int x, int y)> { (0, 0), (50, 0), (50, 50), (0, 50) };
        var result = await service.TrackQuadAcrossShotsAsync(
            "sc-05", "fake-video.mp4", seedQuad, seedFrame: 0, sam3Prompt: null, surfaceType: "Signage");

        Assert.Equal("Tracked", result.OverallStatus);
        using var doc = JsonDocument.Parse(result.TrackingDataJson);
        var frame = doc.RootElement.GetProperty("shotSegments")[0].GetProperty("frames")[0];
        Assert.Equal(4, frame.GetProperty("corners").GetArrayLength());
    }
}
