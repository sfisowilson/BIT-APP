using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests
{
    /// <summary>
    /// Covers RenderService.SetQueuedForFinalAsync (the "queue this render for its scene's final
    /// assembly" workflow) and DeleteRenderAsync.
    /// </summary>
    public class RenderServiceQueueAndDeleteTests
    {
        private readonly PostgresDbContext _context;
        private readonly RenderService _service;

        public RenderServiceQueueAndDeleteTests()
        {
            var options = new DbContextOptionsBuilder<PostgresDbContext>()
                .UseInMemoryDatabase(databaseName: $"BitTestDb_{Guid.NewGuid()}")
                .Options;
            _context = new PostgresDbContext(options);
            var repository = new RenderRepository(_context);
            var mockEventLog = new Mock<IEventLogService>();
            var mockEmail = new Mock<IEmailService>();
            _service = new RenderService(repository, _context, mockEventLog.Object, mockEmail.Object, null!);
        }

        [Fact]
        public async Task SetQueuedForFinalAsync_FinishedRenderWithSceneClip_Succeeds()
        {
            _context.Renders.Add(new RenderItem
            {
                Id = "r-01", ContentId = "c-01", SceneId = "sc-01", CampaignId = "camp-01", AssetId = "a-01",
                RenderStatus = "Finished", SceneClipStorageKey = "/api/renders/r-01/scene-clip",
            });
            await _context.SaveChangesAsync();

            var result = await _service.SetQueuedForFinalAsync("r-01", true);

            Assert.True(result.IsQueuedForFinal);
        }

        [Fact]
        public async Task SetQueuedForFinalAsync_ProcessingRender_ThrowsInvalidOperationException()
        {
            _context.Renders.Add(new RenderItem
            {
                Id = "r-01", ContentId = "c-01", SceneId = "sc-01", CampaignId = "camp-01", AssetId = "a-01",
                RenderStatus = "Processing",
            });
            await _context.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetQueuedForFinalAsync("r-01", true));
        }

        [Fact]
        public async Task SetQueuedForFinalAsync_NoSceneClip_ThrowsInvalidOperationException()
        {
            _context.Renders.Add(new RenderItem
            {
                Id = "r-01", ContentId = "c-01", SceneId = "sc-01", CampaignId = "camp-01", AssetId = "a-01",
                RenderStatus = "Finished", SceneClipStorageKey = null,
            });
            await _context.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetQueuedForFinalAsync("r-01", true));
        }

        [Fact]
        public async Task SetQueuedForFinalAsync_QueuingNewRenderForSameScene_UnqueuesThePreviousOne()
        {
            // Two Interactive-mode renders (different surfaces) that both resolve to the same scene.
            _context.SceneItems.Add(new SceneItem { Id = "sc-01", ContentId = "c-01", SceneIndex = 0, StartFrame = 0, EndFrame = 100, DurationSeconds = 3 });
            _context.SurfaceItems.Add(new SurfaceItem { Id = "sf-01", SceneId = "sc-01", SurfaceType = "wall", BoundaryCoordinatesJson = "[]", EstimatedDepth = 1 });
            _context.SurfaceItems.Add(new SurfaceItem { Id = "sf-02", SceneId = "sc-01", SurfaceType = "table", BoundaryCoordinatesJson = "[]", EstimatedDepth = 1 });
            _context.Renders.Add(new RenderItem
            {
                Id = "r-old", ContentId = "c-01", SurfaceId = "sf-01", CampaignId = "camp-01", AssetId = "a-01",
                RenderStatus = "Finished", SceneClipStorageKey = "/api/renders/r-old/download", IsQueuedForFinal = true,
            });
            _context.Renders.Add(new RenderItem
            {
                Id = "r-new", ContentId = "c-01", SurfaceId = "sf-02", CampaignId = "camp-01", AssetId = "a-02",
                RenderStatus = "Finished", SceneClipStorageKey = "/api/renders/r-new/download",
            });
            await _context.SaveChangesAsync();

            await _service.SetQueuedForFinalAsync("r-new", true);

            var old = await _context.Renders.FindAsync("r-old");
            var updated = await _context.Renders.FindAsync("r-new");
            Assert.False(old!.IsQueuedForFinal);
            Assert.True(updated!.IsQueuedForFinal);
        }

        [Fact]
        public async Task SetQueuedForFinalAsync_UnqueuingDoesNotAffectOtherScenes()
        {
            _context.Renders.Add(new RenderItem
            {
                Id = "r-01", ContentId = "c-01", SceneId = "sc-01", CampaignId = "camp-01", AssetId = "a-01",
                RenderStatus = "Finished", SceneClipStorageKey = "/api/renders/r-01/download", IsQueuedForFinal = true,
            });
            _context.Renders.Add(new RenderItem
            {
                Id = "r-02", ContentId = "c-01", SceneId = "sc-02", CampaignId = "camp-01", AssetId = "a-02",
                RenderStatus = "Finished", SceneClipStorageKey = "/api/renders/r-02/download", IsQueuedForFinal = true,
            });
            await _context.SaveChangesAsync();

            await _service.SetQueuedForFinalAsync("r-01", false);

            var untouched = await _context.Renders.FindAsync("r-02");
            Assert.True(untouched!.IsQueuedForFinal); // different scene — must not be touched
        }

        [Fact]
        public async Task DeleteRenderAsync_RemovesTheRenderRow()
        {
            _context.Renders.Add(new RenderItem
            {
                Id = "r-01", ContentId = "c-01", CampaignId = "camp-01", AssetId = "a-01", RenderStatus = "Failed",
            });
            await _context.SaveChangesAsync();

            await _service.DeleteRenderAsync("r-01");

            Assert.Null(await _context.Renders.FindAsync("r-01"));
        }

        [Fact]
        public async Task DeleteRenderAsync_UnknownRender_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _service.DeleteRenderAsync("r-missing"));
        }
    }
}
