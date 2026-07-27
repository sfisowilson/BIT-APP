using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;
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
        private readonly SurfaceService _service;

        public SurfaceServiceTests()
        {
            _mockRepo = new Mock<ISurfaceRepository>();
            _mockEmail = new Mock<IEmailService>();
            _service = new SurfaceService(_mockRepo.Object, _mockEmail.Object);
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
    }
}
