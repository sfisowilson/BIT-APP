using System;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests
{
    public class CampaignServiceTests
    {
        private readonly Mock<ICampaignRepository> _mockRepo;
        private readonly CampaignService _service;

        public CampaignServiceTests()
        {
            _mockRepo = new Mock<ICampaignRepository>();
            _service = new CampaignService(_mockRepo.Object);
        }

        [Fact]
        public async Task CreateCampaign_WithValidNamingStructure_Succeeds()
        {
            // Arrange
            var dto = new CreateCampaignDto
            {
                Name = "Coca-Cola Summer Splash",
                NamingStructureCode = "UZ01EP12_COKE",
                TargetRegion = "SADC",
                TotalBudget = 150000
            };

            // Act
            var result = await _service.CreateCampaignAsync(dto);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Coca-Cola Summer Splash", result.Name);
            Assert.Equal("UZ01EP12_COKE", result.NamingStructureCode);
            Assert.Equal("Draft", result.Status);
            _mockRepo.Verify(r => r.AddAsync(It.IsAny<CampaignItem>()), Times.Once);
            _mockRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task CreateCampaign_WithInvalidNamingStructure_ThrowsArgumentException()
        {
            // Arrange
            var dto = new CreateCampaignDto
            {
                Name = "Nike Air Max",
                NamingStructureCode = "INVALID_NAME_CODE", // Fails 8-char code check
                TargetRegion = "Gauteng",
                TotalBudget = 50000
            };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.CreateCampaignAsync(dto)
            );

            Assert.Contains("Naming structure violation", exception.Message);
            _mockRepo.Verify(r => r.AddAsync(It.IsAny<CampaignItem>()), Times.Never);
        }
    }
}
