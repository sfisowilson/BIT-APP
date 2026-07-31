using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests;

public class AssetServiceTests
{
    private (PostgresDbContext context, AssetService service) CreateService()
    {
        var options = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseInMemoryDatabase(databaseName: $"BitTestDb_{Guid.NewGuid()}")
            .Options;
        var context = new PostgresDbContext(options);
        var service = new AssetService(new AssetRepository(context), new CampaignRepository(context));
        return (context, service);
    }

    [Fact]
    public async Task GetAssetsAsync_UnassignedTrue_ReturnsOnlyAssetsWithoutCampaign()
    {
        var (context, service) = CreateService();
        context.CreativeAssets.AddRange(
            new CreativeAsset { Id = "as-1", Name = "Assigned Banner", Type = "Image", StorageKey = "s3://x", FileSize = "1MB", Dimensions = "100x100", BrandCategory = "Beverage", CampaignId = "c-1" },
            new CreativeAsset { Id = "as-2", Name = "Unassigned Logo", Type = "Logo", StorageKey = "s3://y", FileSize = "1MB", Dimensions = "100x100", BrandCategory = "Beverage", CampaignId = null }
        );
        await context.SaveChangesAsync();

        var result = await service.GetAssetsAsync(new AssetFilterParams { Unassigned = true });

        var item = Assert.Single(result.Items);
        Assert.Equal("as-2", item.Id);
    }

    [Fact]
    public async Task GetAssetsAsync_CampaignIdTakesPrecedenceOverUnassigned()
    {
        var (context, service) = CreateService();
        context.CreativeAssets.AddRange(
            new CreativeAsset { Id = "as-1", Name = "Campaign Asset", Type = "Image", StorageKey = "s3://x", FileSize = "1MB", Dimensions = "100x100", BrandCategory = "Beverage", CampaignId = "c-1" },
            new CreativeAsset { Id = "as-2", Name = "Unassigned Asset", Type = "Logo", StorageKey = "s3://y", FileSize = "1MB", Dimensions = "100x100", BrandCategory = "Beverage", CampaignId = null }
        );
        await context.SaveChangesAsync();

        // CampaignId set AND Unassigned=true — CampaignId filter wins, Unassigned is ignored
        var result = await service.GetAssetsAsync(new AssetFilterParams { CampaignId = "c-1", Unassigned = true });

        var item = Assert.Single(result.Items);
        Assert.Equal("as-1", item.Id);
    }

    [Fact]
    public async Task GetAssetsAsync_NoFilter_ReturnsAllAssets()
    {
        var (context, service) = CreateService();
        context.CreativeAssets.AddRange(
            new CreativeAsset { Id = "as-1", Name = "A", Type = "Image", StorageKey = "s3://x", FileSize = "1MB", Dimensions = "100x100", BrandCategory = "Beverage", CampaignId = "c-1" },
            new CreativeAsset { Id = "as-2", Name = "B", Type = "Logo", StorageKey = "s3://y", FileSize = "1MB", Dimensions = "100x100", BrandCategory = "Beverage", CampaignId = null }
        );
        await context.SaveChangesAsync();

        var result = await service.GetAssetsAsync(new AssetFilterParams());

        Assert.Equal(2, result.TotalCount);
    }
}
