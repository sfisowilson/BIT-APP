using System;
using System.Collections.Generic;
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
    public class SurfaceServiceTests
    {
        private readonly Mock<ISurfaceRepository> _mockRepo;
        private readonly Mock<IEmailService> _mockEmail;
        private readonly PostgresDbContext _context;
        private readonly SurfaceService _service;

        public SurfaceServiceTests()
        {
            _mockRepo = new Mock<ISurfaceRepository>();
            _mockEmail = new Mock<IEmailService>();
            var options = new DbContextOptionsBuilder<PostgresDbContext>()
                .UseInMemoryDatabase(databaseName: $"BitTestDb_{Guid.NewGuid()}")
                .Options;
            _context = new PostgresDbContext(options);
            _service = new SurfaceService(_mockRepo.Object, _mockEmail.Object, _context);
        }

        [Fact]
        public async Task ApproveSurface_WithApprovedDecision_UpdatesStatusAndProvisionsAdSlot()
        {
            // Arrange
            string surfaceId = "sf-01";
            var existingSurface = new SurfaceItem
            {
                Id = surfaceId,
                SceneId = "s-01",
                SurfaceType = "Stadium Perimeter LED Board",
                BoundaryCoordinatesJson = "[]",
                EstimatedDepth = 10,
                OrientationVectorJson = "{}",
                ConfidenceScore = 0.95,
                ViabilityScore = 0.85,
                Status = "Candidate"
            };

            _mockRepo.Setup(r => r.GetByIdAsync(surfaceId)).ReturnsAsync(existingSurface);

            var approvalDto = new ApprovalDto
            {
                Decision = "Approved"
            };

            // Act
            var result = await _service.ApproveSurfaceAsync(surfaceId, approvalDto, "approver@afrobotics.co.za");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Approved", result.Status);
            _mockRepo.Verify(r => r.GetByIdAsync(surfaceId), Times.Once);
            _mockRepo.Verify(r => r.AddAdSlotAsync(It.Is<AdSlotItem>(slot => slot.SurfaceId == surfaceId && slot.SlotStatus == "Available")), Times.Once);
            _mockRepo.Verify(r => r.AddApprovalAsync(It.Is<ApprovalItem>(app => app.ApproverEmail == "approver@afrobotics.co.za" && app.Decision == "Approved")), Times.Once);
            _mockRepo.Verify(r => r.UpdateAsync(existingSurface), Times.Once);
            _mockRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task ApproveSurface_WithExcludedDecision_UpdatesStatusAndSetsReason()
        {
            // Arrange
            string surfaceId = "sf-02";
            var existingSurface = new SurfaceItem
            {
                Id = surfaceId,
                SceneId = "s-01",
                SurfaceType = "Spectator Face (Close-up)",
                BoundaryCoordinatesJson = "[]",
                EstimatedDepth = 5,
                OrientationVectorJson = "{}",
                ConfidenceScore = 0.98,
                ViabilityScore = 0.1,
                Status = "Candidate"
            };

            _mockRepo.Setup(r => r.GetByIdAsync(surfaceId)).ReturnsAsync(existingSurface);

            var approvalDto = new ApprovalDto
            {
                Decision = "Excluded",
                RejectionReason = "Brand safety violation: close up of face overlay."
            };

            // Act
            var result = await _service.ApproveSurfaceAsync(surfaceId, approvalDto, "approver@afrobotics.co.za");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Excluded", result.Status);
            Assert.Equal("Brand safety violation: close up of face overlay.", result.ExclusionReason);
            _mockRepo.Verify(r => r.AddAdSlotAsync(It.IsAny<AdSlotItem>()), Times.Never);
            _mockRepo.Verify(r => r.AddApprovalAsync(It.IsAny<ApprovalItem>()), Times.Never);
            _mockRepo.Verify(r => r.UpdateAsync(existingSurface), Times.Once);
            _mockRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task ApproveSurface_NonExistingId_ThrowsKeyNotFoundException()
        {
            // Arrange
            _mockRepo.Setup(r => r.GetByIdAsync("invalid-id")).ReturnsAsync((SurfaceItem?)null);

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                _service.ApproveSurfaceAsync("invalid-id", new ApprovalDto { Decision = "Approved" }, "approver@afrobotics.co.za")
            );
        }

        [Fact]
        public async Task CreateFromClickAsync_ValidFrame_ResolvesCorrectSceneAndPersistsSurface()
        {
            // Arrange
            _context.SceneItems.Add(new SceneItem
            {
                Id = "sc-01", ContentId = "ct-01", SceneIndex = 0,
                StartFrame = 0, EndFrame = 99, DurationSeconds = 3.3,
            });
            _context.SceneItems.Add(new SceneItem
            {
                Id = "sc-02", ContentId = "ct-01", SceneIndex = 1,
                StartFrame = 100, EndFrame = 199, DurationSeconds = 3.3,
            });
            await _context.SaveChangesAsync();

            var dto = new CreateSurfaceFromClickRequest
            {
                ContentId = "ct-01",
                FrameIndex = 150, // inside sc-02's range, not sc-01's
                MaskPolygonJson = "[{\"x\":10,\"y\":10},{\"x\":50,\"y\":10},{\"x\":50,\"y\":50},{\"x\":10,\"y\":50}]",
                SurfaceType = "Billboard",
            };

            // Act
            var result = await _service.CreateFromClickAsync(dto);

            // Assert
            Assert.Equal("sc-02", result.SceneId);
            Assert.Equal("Generative", result.AssetType);
            Assert.Equal("Manual", result.Source);
            Assert.Equal("Approved", result.Status);
            Assert.Equal(150, result.DetectedAtFrame);
            _mockRepo.Verify(r => r.AddAsync(It.Is<SurfaceItem>(s => s.SceneId == "sc-02")), Times.Once);
            _mockRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task CreateFromClickAsync_FrameOutsideAnyScene_ThrowsArgumentException()
        {
            // Arrange
            _context.SceneItems.Add(new SceneItem
            {
                Id = "sc-01", ContentId = "ct-01", SceneIndex = 0,
                StartFrame = 0, EndFrame = 99, DurationSeconds = 3.3,
            });
            await _context.SaveChangesAsync();

            var dto = new CreateSurfaceFromClickRequest { ContentId = "ct-01", FrameIndex = 500, MaskPolygonJson = "[]" };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateFromClickAsync(dto));
        }

        [Fact]
        public async Task CreateFromQuadAsync_ValidFrame_PersistsPlanarSurface()
        {
            // Arrange
            _context.SceneItems.Add(new SceneItem
            {
                Id = "sc-01", ContentId = "ct-02", SceneIndex = 0,
                StartFrame = 0, EndFrame = 299, DurationSeconds = 10,
            });
            await _context.SaveChangesAsync();

            var dto = new CreateSurfaceFromQuadRequest
            {
                ContentId = "ct-02",
                FrameIndex = 42,
                QuadCornersJson = "[{\"x\":0,\"y\":0},{\"x\":100,\"y\":0},{\"x\":100,\"y\":100},{\"x\":0,\"y\":100}]",
                SurfaceType = "Wall Signage",
            };

            // Act
            var result = await _service.CreateFromQuadAsync(dto);

            // Assert
            Assert.Equal("sc-01", result.SceneId);
            Assert.Equal("Planar", result.AssetType);
            Assert.Equal("Manual", result.Source);
            Assert.Equal("Wall Signage", result.SurfaceType);
            _mockRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }
    }
}
