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
    public class AuthServiceTests
    {
        private readonly Mock<IUserRepository> _mockUserRepo;
        private readonly AuthService _service;

        public AuthServiceTests()
        {
            _mockUserRepo = new Mock<IUserRepository>();
            _service = new AuthService(_mockUserRepo.Object);
        }

        [Fact]
        public async Task Login_WithCorrectCredentials_Succeeds()
        {
            // Arrange
            var user = new User
            {
                Id = "usr-01",
                FullName = "Sfiso Dlamini",
                Email = "loverboy.sfiso@gmail.com",
                PasswordHash = "editor123",
                Role = "Editor",
                AccountStatus = "Active"
            };

            _mockUserRepo.Setup(r => r.GetByEmailAsync("loverboy.sfiso@gmail.com")).ReturnsAsync(user);

            var request = new LoginRequestDto
            {
                Email = "loverboy.sfiso@gmail.com",
                Password = "editor123"
            };

            // Act
            var result = await _service.LoginAsync(request);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("usr-01", result.User.Id);
            Assert.Equal("Sfiso Dlamini", result.User.FullName);
            Assert.Equal("Editor", result.User.Role);
            _mockUserRepo.Verify(r => r.UpdateAsync(user), Times.Once);
            _mockUserRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task Login_WithIncorrectPassword_ReturnsNull()
        {
            // Arrange
            var user = new User
            {
                Id = "usr-01",
                FullName = "Sfiso Dlamini",
                Email = "loverboy.sfiso@gmail.com",
                PasswordHash = "editor123",
                Role = "Editor",
                AccountStatus = "Active"
            };

            _mockUserRepo.Setup(r => r.GetByEmailAsync("loverboy.sfiso@gmail.com")).ReturnsAsync(user);

            var request = new LoginRequestDto
            {
                Email = "loverboy.sfiso@gmail.com",
                Password = "WRONG_PASSWORD"
            };

            // Act
            var result = await _service.LoginAsync(request);

            // Assert
            Assert.Null(result);
            _mockUserRepo.Verify(r => r.UpdateAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task Login_WithSuspendedAccount_ThrowsInvalidOperationException()
        {
            // Arrange
            var user = new User
            {
                Id = "usr-02",
                FullName = "Suspended User",
                Email = "suspended@afrobotics.co.za",
                PasswordHash = "password123",
                Role = "Editor",
                AccountStatus = "Suspended"
            };

            _mockUserRepo.Setup(r => r.GetByEmailAsync("suspended@afrobotics.co.za")).ReturnsAsync(user);

            var request = new LoginRequestDto
            {
                Email = "suspended@afrobotics.co.za",
                Password = "password123"
            };

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _service.LoginAsync(request)
            );
        }
    }
}
