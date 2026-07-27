using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests
{
    public class BasicSurfaceDetectionServiceTests
    {
        private readonly BasicSurfaceDetectionService _service;

        public BasicSurfaceDetectionServiceTests()
        {
            _service = new BasicSurfaceDetectionService();
        }

        [Fact]
        public async Task DetectAsync_Always_ThrowsInvalidOperationException()
        {
            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _service.DetectAsync("content-1", 1, 0, 100));

            Assert.Contains("engine_detection", ex.Message);
            Assert.Contains("yolo", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DetectAsync_WithAnyParameters_ThrowsWithClearInstructions()
        {
            // Act & Assert — verify the error message guides the operator
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _service.DetectAsync("any-id", 42, 1000, 2000));

            // Must mention the valid engine options
            Assert.Contains("yolo", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("grounding-dino", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("replicate", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("gemini", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("google", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DetectAsync_DoesNotReturnAnySurfaces()
        {
            // The whole point of the refactor: BasicSurfaceDetectionService
            // must NEVER return mock data — it must throw instead.
            await Assert.ThrowsAnyAsync<Exception>(
                () => _service.DetectAsync("content-1", 1, 0, 100));
        }
    }
}
