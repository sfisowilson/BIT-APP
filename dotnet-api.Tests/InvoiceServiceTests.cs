using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Services;
using Xunit;

namespace Afrobotics.Bit.Tests;

public class InvoiceServiceTests
{
    private PostgresDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseInMemoryDatabase(databaseName: $"BitTestDb_{Guid.NewGuid()}")
            .Options;
        return new PostgresDbContext(options);
    }

    [Fact]
    public async Task GenerateCampaignInvoiceAsync_Returns_ValidSummary()
    {
        // Arrange
        using var context = GetInMemoryDbContext();
        var campaignId = "c-test-01";
        
        context.Campaigns.Add(new CampaignItem
        {
            Id = campaignId,
            Name = "Coke Winter Campaign",
            TargetRegion = "SADC",
            NamingStructureCode = "UZ01EP12_COKE",
            Status = "Active"
        });

        context.Renders.Add(new RenderItem
        {
            Id = "r-test-1",
            CampaignId = campaignId,
            ContentId = "v-01",
            SurfaceId = "sf-01",
            RenderStatus = "Finished",
            ExportPreset = "Web-Ready MP4"
        });

        await context.SaveChangesAsync();

        var invoiceService = new InvoiceService(context);

        // Act
        var result = await invoiceService.GenerateCampaignInvoiceAsync(campaignId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(campaignId, result.CampaignId);
        Assert.Equal("Coke Winter Campaign", result.CampaignName);
        Assert.True(result.TotalAmount > 0);
        Assert.Single(result.LineItems);
    }

    [Fact]
    public async Task GenerateCampaignInvoiceAsync_UnknownCampaign_ThrowsKeyNotFoundException()
    {
        using var context = GetInMemoryDbContext();
        var invoiceService = new InvoiceService(context);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => invoiceService.GenerateCampaignInvoiceAsync("c-does-not-exist"));
    }

    [Fact]
    public async Task GenerateCampaignInvoiceAsync_NoFinishedRenders_ReturnsDefaultBookingLineItem()
    {
        // Arrange — campaign exists but has no Finished renders yet (e.g. brand new campaign).
        using var context = GetInMemoryDbContext();
        var campaignId = "c-test-02";

        context.Campaigns.Add(new CampaignItem
        {
            Id = campaignId,
            Name = "Nike Spring Launch",
            TargetRegion = "East Africa",
            NamingStructureCode = "GEN23EP100_NIKE",
            Status = "Draft"
        });
        await context.SaveChangesAsync();

        var invoiceService = new InvoiceService(context);

        // Act
        var result = await invoiceService.GenerateCampaignInvoiceAsync(campaignId);

        // Assert — falls back to the single "Campaign Setup & Surface Booking" placeholder line
        // item so a preview invoice is still valid before any render has actually finished.
        Assert.NotNull(result);
        Assert.Single(result.LineItems);
        Assert.Contains("Campaign Setup & Surface Booking", result.LineItems[0].Description);
        Assert.Equal(0m, result.RenderProcessingFees);
        Assert.True(result.TotalAmount > result.Subtotal); // tax was applied on top
    }

    [Fact]
    public async Task GenerateCampaignInvoiceAsync_MultipleFinishedRenders_SumsRenderProcessingFees()
    {
        // Arrange
        using var context = GetInMemoryDbContext();
        var campaignId = "c-test-03";

        context.Campaigns.Add(new CampaignItem
        {
            Id = campaignId,
            Name = "Multi-Render Campaign",
            TargetRegion = "SADC",
            NamingStructureCode = "UZ01EP12_MULTI",
            Status = "Active"
        });
        context.Renders.AddRange(
            new RenderItem { Id = "r-multi-1", CampaignId = campaignId, ContentId = "v-01", SurfaceId = "sf-01", RenderStatus = "Finished", ExportPreset = "Web-Ready MP4" },
            new RenderItem { Id = "r-multi-2", CampaignId = campaignId, ContentId = "v-01", SurfaceId = "sf-02", RenderStatus = "Finished", ExportPreset = "Web-Ready MP4" },
            // A non-Finished render for the same campaign must not be billed.
            new RenderItem { Id = "r-multi-3", CampaignId = campaignId, ContentId = "v-01", SurfaceId = "sf-03", RenderStatus = "Processing", ExportPreset = "Web-Ready MP4" }
        );
        await context.SaveChangesAsync();

        var invoiceService = new InvoiceService(context);

        // Act
        var result = await invoiceService.GenerateCampaignInvoiceAsync(campaignId);

        // Assert
        Assert.Equal(2, result.LineItems.Count);
        Assert.Equal(500.00m, result.RenderProcessingFees); // ZAR 250 × 2 Finished renders only
    }
}
