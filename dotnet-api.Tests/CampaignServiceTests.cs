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
        private readonly Mock<IEmailService> _mockEmail;
        private readonly CampaignService _service;

        public CampaignServiceTests()
        {
            _mockRepo = new Mock<ICampaignRepository>();
            _mockEmail = new Mock<IEmailService>();
            _service = new CampaignService(_mockRepo.Object, _mockEmail.Object, null!);
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
        public async Task CreateCampaign_With10CharSceneCode_Succeeds()
        {
            // MReq 1 allows both 8-char and 10-char scene codes (e.g. GEN23EP100_UNIL)
            var dto = new CreateCampaignDto
            {
                Name = "Unilever Winter Promo",
                NamingStructureCode = "GEN23EP100_UNIL",
                TargetRegion = "East Africa",
                TotalBudget = 200000
            };

            var result = await _service.CreateCampaignAsync(dto);

            Assert.NotNull(result);
            Assert.Equal("GEN23EP100_UNIL", result.NamingStructureCode);
            Assert.Equal("Draft", result.Status);
            _mockRepo.Verify(r => r.AddAsync(It.IsAny<CampaignItem>()), Times.Once);
        }

        [Fact]
        public async Task CreateCampaign_WithInvalidNamingStructure_ThrowsArgumentException()
        {
            // Arrange
            var dto = new CreateCampaignDto
            {
                Name = "Nike Air Max",
                NamingStructureCode = "INVALID_NAME_CODE", // Fails: no underscore, bad format
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

        [Theory]
        [InlineData("AB123456_COKE")]    // 8-char scene code
        [InlineData("GEN23EP100_UNIL")]  // 10-char scene code
        [InlineData("XY99ZZ77_PEPSI")]  // 8-char scene code
        [InlineData("ABCDEFGHIJ_BRAND")] // 10-char scene code
        public async Task CreateCampaign_WithValidNamingVariations_Succeeds(string namingCode)
        {
            var dto = new CreateCampaignDto
            {
                Name = "Test Campaign",
                NamingStructureCode = namingCode,
                TargetRegion = "ZAF",
                TotalBudget = 10000
            };

            var result = await _service.CreateCampaignAsync(dto);

            Assert.Equal(namingCode, result.NamingStructureCode);
            Assert.Equal("Draft", result.Status);
        }
    }
}
