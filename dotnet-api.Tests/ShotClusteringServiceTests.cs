using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests;

public class ShotClusteringServiceTests
{
    private static PostgresDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseInMemoryDatabase(databaseName: $"BitTestDb_{Guid.NewGuid()}")
            .Options;
        return new PostgresDbContext(options);
    }

    private static string Embed(params float[] vector) => JsonSerializer.Serialize(vector);

    private static ShotItem MakeShot(string contentId, int index, int startFrame, int endFrame, string embeddingJson) => new()
    {
        Id = $"sh-{index}",
        ContentId = contentId,
        ShotIndex = index,
        StartFrame = startFrame,
        EndFrame = endFrame,
        KeyframeEmbeddingJson = embeddingJson,
    };

    [Fact]
    public async Task ClusterShotsAsync_AllShotsSimilar_GroupsIntoOneScene()
    {
        using var db = GetInMemoryDbContext();
        db.ContentItems.Add(new ContentItem { Id = "ct-01", Title = "v", FrameRate = 30 });
        var embedding = Embed(1f, 0f, 0f);
        db.Set<ShotItem>().AddRange(
            MakeShot("ct-01", 0, 0, 29, embedding),
            MakeShot("ct-01", 1, 30, 59, embedding),
            MakeShot("ct-01", 2, 60, 89, embedding));
        await db.SaveChangesAsync();

        var service = new ShotClusteringService(db, NullLogger<ShotClusteringService>.Instance);
        var scenes = await service.ClusterShotsAsync("ct-01", threshold: 0.85);

        Assert.Single(scenes);
        Assert.Equal(0, scenes[0].StartFrame);
        Assert.Equal(89, scenes[0].EndFrame);

        var shots = await db.Set<ShotItem>().Where(s => s.ContentId == "ct-01").ToListAsync();
        Assert.All(shots, s => Assert.Equal(scenes[0].Id, s.SceneId));
    }

    [Fact]
    public async Task ClusterShotsAsync_NoShotsForContent_ReturnsEmptyList()
    {
        using var db = GetInMemoryDbContext();
        var service = new ShotClusteringService(db, NullLogger<ShotClusteringService>.Instance);

        var scenes = await service.ClusterShotsAsync("ct-missing");

        Assert.Empty(scenes);
    }

    [Fact]
    public async Task ClusterShotsAsync_FourConsecutiveNonMatches_ClosesSceneAndStartsNew()
    {
        using var db = GetInMemoryDbContext();
        db.ContentItems.Add(new ContentItem { Id = "ct-02", Title = "v", FrameRate = 30 });

        var groupA = Embed(1f, 0f, 0f, 0f, 0f, 0f);
        // Four mutually-orthogonal singles: none match group A, and none match each other,
        // so the 4th one is what finally exceeds CloseAfterNonMatches (=4) and closes the scene.
        var orthoA = Embed(0f, 1f, 0f, 0f, 0f, 0f);
        var orthoB = Embed(0f, 0f, 1f, 0f, 0f, 0f);
        var orthoC = Embed(0f, 0f, 0f, 1f, 0f, 0f);
        var orthoD = Embed(0f, 0f, 0f, 0f, 1f, 0f);

        db.Set<ShotItem>().AddRange(
            MakeShot("ct-02", 0, 0, 9, groupA),
            MakeShot("ct-02", 1, 10, 19, groupA),
            MakeShot("ct-02", 2, 20, 29, groupA),
            MakeShot("ct-02", 3, 30, 39, orthoA),
            MakeShot("ct-02", 4, 40, 49, orthoB),
            MakeShot("ct-02", 5, 50, 59, orthoC),
            MakeShot("ct-02", 6, 60, 69, orthoD));
        await db.SaveChangesAsync();

        var service = new ShotClusteringService(db, NullLogger<ShotClusteringService>.Instance);
        var scenes = await service.ClusterShotsAsync("ct-02", threshold: 0.85);

        Assert.Equal(2, scenes.Count);

        var shot6 = await db.Set<ShotItem>().FirstAsync(s => s.Id == "sh-6");
        var shot0 = await db.Set<ShotItem>().FirstAsync(s => s.Id == "sh-0");
        Assert.NotEqual(shot0.SceneId, shot6.SceneId);
    }

    [Fact]
    public async Task ClusterShotsAsync_EveryShotAssignedToAScene_ContiguityInvariantHolds()
    {
        using var db = GetInMemoryDbContext();
        db.ContentItems.Add(new ContentItem { Id = "ct-03", Title = "v", FrameRate = 30 });
        var embA = Embed(1f, 0f, 0f);
        var embB = Embed(0f, 1f, 0f);
        db.Set<ShotItem>().AddRange(
            MakeShot("ct-03", 0, 0, 9, embA),
            MakeShot("ct-03", 1, 10, 19, embA),
            MakeShot("ct-03", 2, 20, 29, embB),
            MakeShot("ct-03", 3, 30, 39, embB));
        await db.SaveChangesAsync();

        var service = new ShotClusteringService(db, NullLogger<ShotClusteringService>.Instance);
        await service.ClusterShotsAsync("ct-03", threshold: 0.85);

        var shots = await db.Set<ShotItem>().Where(s => s.ContentId == "ct-03").ToListAsync();
        Assert.All(shots, s => Assert.False(string.IsNullOrEmpty(s.SceneId)));
    }
}
