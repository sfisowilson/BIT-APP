using System;
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
    /// Covers the prompt-based "AI Placement Assistant → Generate New" dispatch/approve/reject
    /// paths in RenderService — specifically the validation logic that runs before Hangfire
    /// enqueues a job. Happy-path dispatch (which reaches BackgroundJob.Enqueue) isn't covered
    /// here, consistent with the rest of this suite: no test in this project exercises Hangfire's
    /// static enqueue, which requires a configured JobStorage the unit tests don't set up.
    /// </summary>
    public class RenderServicePromptTests
    {
        private readonly Mock<IRenderRepository> _mockRepo;
        private readonly Mock<IEventLogService> _mockEventLog;
        private readonly Mock<IEmailService> _mockEmail;
        private readonly PostgresDbContext _context;
        private readonly RenderService _service;

        public RenderServicePromptTests()
        {
            _mockRepo = new Mock<IRenderRepository>();
            _mockEventLog = new Mock<IEventLogService>();
            _mockEmail = new Mock<IEmailService>();
            var options = new DbContextOptionsBuilder<PostgresDbContext>()
                .UseInMemoryDatabase(databaseName: $"BitTestDb_{Guid.NewGuid()}")
                .Options;
            _context = new PostgresDbContext(options);
            _service = new RenderService(_mockRepo.Object, _context, _mockEventLog.Object, _mockEmail.Object, null!);
        }

        [Fact]
        public async Task DispatchPromptPreviewRenderAsync_WithMissingPromptText_ThrowsArgumentException()
        {
            var dto = new CreatePromptRenderDto
            {
                ContentId = "c-01",
                SceneId = "s-01",
                CampaignId = "camp-01",
                AssetId = "a-01",
                PromptText = "   ",
            };

            await Assert.ThrowsAsync<ArgumentException>(() => _service.DispatchPromptPreviewRenderAsync(dto));
        }

        [Fact]
        public async Task DispatchPromptPreviewRenderAsync_WithUnknownScene_ThrowsArgumentException()
        {
            var dto = new CreatePromptRenderDto
            {
                ContentId = "c-01",
                SceneId = "s-missing",
                CampaignId = "camp-01",
                AssetId = "a-01",
                PromptText = "Add a mounted TV on the white wall.",
            };

            await Assert.ThrowsAsync<ArgumentException>(() => _service.DispatchPromptPreviewRenderAsync(dto));
        }

        [Theory]
        [InlineData(1.5)]   // below MinPromptEditDurationSeconds (3.0)
        [InlineData(15.0)]  // above MaxPromptEditDurationSeconds (10.05)
        public async Task DispatchPromptPreviewRenderAsync_WithOutOfRangeSceneDuration_ThrowsArgumentException(double durationSeconds)
        {
            var scene = new SceneItem
            {
                Id = "s-01",
                ContentId = "c-01",
                SceneIndex = 1,
                StartFrame = 0,
                EndFrame = 100,
                DurationSeconds = durationSeconds,
            };
            _context.SceneItems.Add(scene);
            await _context.SaveChangesAsync();

            var dto = new CreatePromptRenderDto
            {
                ContentId = "c-01",
                SceneId = "s-01",
                CampaignId = "camp-01",
                AssetId = "a-01",
                PromptText = "Add a mounted TV on the white wall.",
            };

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.DispatchPromptPreviewRenderAsync(dto));
            Assert.Contains("outside the allowed", ex.Message);
        }

        [Fact]
        public async Task ApproveSpliceAsync_WhenRenderNotPreviewReady_ThrowsInvalidOperationException()
        {
            var render = new RenderItem { Id = "r-01", RenderStatus = "Processing", RenderMode = "PromptEdit" };
            _mockRepo.Setup(r => r.GetByIdAsync("r-01")).ReturnsAsync(render);

            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApproveSpliceAsync("r-01"));
        }

        [Fact]
        public async Task ApproveSpliceAsync_WhenRenderNotFound_ThrowsArgumentException()
        {
            _mockRepo.Setup(r => r.GetByIdAsync("r-missing")).ReturnsAsync((RenderItem?)null);

            await Assert.ThrowsAsync<ArgumentException>(() => _service.ApproveSpliceAsync("r-missing"));
        }

        [Fact]
        public async Task RejectPromptRenderAsync_WhenPreviewReady_SetsRejectedStatus()
        {
            var render = new RenderItem { Id = "r-01", RenderStatus = "PreviewReady", RenderMode = "PromptEdit" };
            _mockRepo.Setup(r => r.GetByIdAsync("r-01")).ReturnsAsync(render);
            _mockRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _service.RejectPromptRenderAsync("r-01", "Not what I wanted.");

            Assert.Equal("Rejected", render.RenderStatus);
            Assert.Equal("Not what I wanted.", render.LastErrorMessage);
            _mockRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task RejectPromptRenderAsync_WithNoReason_UsesDefaultMessage()
        {
            var render = new RenderItem { Id = "r-01", RenderStatus = "PreviewReady", RenderMode = "PromptEdit" };
            _mockRepo.Setup(r => r.GetByIdAsync("r-01")).ReturnsAsync(render);

            await _service.RejectPromptRenderAsync("r-01", null);

            Assert.Equal("Rejected by user after preview.", render.LastErrorMessage);
        }

        [Fact]
        public async Task RejectPromptRenderAsync_WhenNotPreviewReady_ThrowsInvalidOperationException()
        {
            var render = new RenderItem { Id = "r-01", RenderStatus = "Finished", RenderMode = "PromptEdit" };
            _mockRepo.Setup(r => r.GetByIdAsync("r-01")).ReturnsAsync(render);

            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RejectPromptRenderAsync("r-01", null));
        }

        [Fact]
        public async Task RetryRenderAsync_WhenNotFailed_ThrowsInvalidOperationException()
        {
            var render = new RenderItem { Id = "r-01", RenderStatus = "Processing", RenderMode = "PromptEdit" };
            _mockRepo.Setup(r => r.GetByIdAsync("r-01")).ReturnsAsync(render);

            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RetryRenderAsync("r-01"));
        }
    }
}
