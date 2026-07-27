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
}
