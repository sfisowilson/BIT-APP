using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests
{
    /// <summary>
    /// Covers RenderService.GetRendersAsync's display-field enrichment (Render Queue "more
    /// information" feature) — specifically the scene-resolution logic that mirrors the same
    /// PromptEdit-vs-Interactive split fixed earlier in InvoiceService.
    /// </summary>
    public class RenderServiceGetRendersTests
    {
        private readonly Mock<IRenderRepository> _mockRepo;
        private readonly PostgresDbContext _context;
        private readonly RenderService _service;

        public RenderServiceGetRendersTests()
        {
            _mockRepo = new Mock<IRenderRepository>();
            var mockEventLog = new Mock<IEventLogService>();
            var mockEmail = new Mock<IEmailService>();
            var options = new DbContextOptionsBuilder<PostgresDbContext>()
                .UseInMemoryDatabase(databaseName: $"BitTestDb_{Guid.NewGuid()}")
                .Options;
            _context = new PostgresDbContext(options);
            _service = new RenderService(_mockRepo.Object, _context, mockEventLog.Object, mockEmail.Object, null!);
        }

        private async Task SeedRendersAsync(params RenderItem[] renders)
        {
            // EF Core's async LINQ operators (CountAsync/ToListAsync, used inside
            // ToPaginatedResultAsync) require a real EF-backed IQueryable, not a plain
            // List.AsQueryable() — seed into the in-memory DbContext instead.
            _context.Renders.AddRange(renders);
            await _context.SaveChangesAsync();
            _mockRepo.Setup(r => r.GetAllQueryable()).Returns(_context.Renders.AsQueryable());
        }

        [Fact]
        public async Task GetRendersAsync_InteractiveRender_ResolvesSceneIdViaSurface()
        {
            _context.ContentItems.Add(new ContentItem { Id = "c-01", Title = "B'dazzled_S01_Ep001", Duration = "00:05:00", Resolution = "1080x1920" });
            _context.SceneItems.Add(new SceneItem { Id = "sc-01", ContentId = "c-01", SceneIndex = 4, StartFrame = 0, EndFrame = 100, DurationSeconds = 3 });
            _context.SurfaceItems.Add(new SurfaceItem
            {
                Id = "sf-01", SceneId = "sc-01", SurfaceType = "interior wall",
                BoundaryCoordinatesJson = "[]", EstimatedDepth = 1,
            });
            _context.CreativeAssets.Add(new CreativeAsset { Id = "a-01", Name = "Coca-Cola Ad", Type = "Image", StorageKey = "k", FileSize = "1", Dimensions = "1x1", BrandCategory = "Beverage" });
            await _context.SaveChangesAsync();

            await SeedRendersAsync(new RenderItem
            {
                Id = "r-01", ContentId = "c-01", SurfaceId = "sf-01", SceneId = null,
                CampaignId = "camp-01", AssetId = "a-01", RenderStatus = "Finished",
            });

            var result = await _service.GetRendersAsync(new RenderFilterParams());

            var item = Assert.Single(result.Items);
            Assert.Equal("sc-01", item.SceneId);
            Assert.Equal(4, item.SceneIndex);
            Assert.Equal("interior wall", item.SurfaceType);
            Assert.Equal("B'dazzled_S01_Ep001", item.ContentTitle);
            Assert.Equal("Coca-Cola Ad", item.AssetName);
        }

        [Fact]
        public async Task GetRendersAsync_PromptEditRender_UsesSceneIdDirectly_NoSurfaceType()
        {
            _context.ContentItems.Add(new ContentItem { Id = "c-01", Title = "Slime_Ep002", Duration = "00:04:00", Resolution = "1080x1920" });
            _context.SceneItems.Add(new SceneItem { Id = "sc-02", ContentId = "c-01", SceneIndex = 7, StartFrame = 0, EndFrame = 90, DurationSeconds = 3 });
            _context.CreativeAssets.Add(new CreativeAsset { Id = "a-02", Name = "Pepsi Ad", Type = "Image", StorageKey = "k", FileSize = "1", Dimensions = "1x1", BrandCategory = "Beverage" });
            await _context.SaveChangesAsync();

            await SeedRendersAsync(new RenderItem
            {
                Id = "r-02", ContentId = "c-01", SurfaceId = null, SceneId = "sc-02",
                CampaignId = "camp-01", AssetId = "a-02", RenderStatus = "PreviewReady",
                RenderMode = "PromptEdit", PromptText = "place the pepsi can on the table",
            });

            var result = await _service.GetRendersAsync(new RenderFilterParams());

            var item = Assert.Single(result.Items);
            Assert.Equal("sc-02", item.SceneId);
            Assert.Equal(7, item.SceneIndex);
            Assert.Null(item.SurfaceType);
            Assert.Equal("Pepsi Ad", item.AssetName);
        }

        [Fact]
        public async Task GetRendersAsync_DeletedContentAndAsset_ReturnsNullDisplayFieldsNotError()
        {
            await SeedRendersAsync(new RenderItem
            {
                Id = "r-03", ContentId = "c-missing", SurfaceId = null, SceneId = null,
                CampaignId = "camp-01", AssetId = "a-missing", RenderStatus = "Failed",
            });

            var result = await _service.GetRendersAsync(new RenderFilterParams());

            var item = Assert.Single(result.Items);
            Assert.Null(item.ContentTitle);
            Assert.Null(item.AssetName);
            Assert.Null(item.SceneIndex);
            Assert.Null(item.SceneId);
        }
    }
}
