using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Controllers;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;
using Xunit;

namespace Afrobotics.Bit.Tests;

public class AttentionControllerTests
{
    private PostgresDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseInMemoryDatabase(databaseName: $"BitTestDb_{Guid.NewGuid()}")
            .Options;
        return new PostgresDbContext(options);
    }

    private AttentionController CreateController(PostgresDbContext context, string userId)
    {
        var controller = new AttentionController(context);
        var claims = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(claims) }
        };
        return controller;
    }

    [Fact]
    public async Task GetAttentionCounts_NoDismissal_ReturnsFullBacklog()
    {
        using var context = GetInMemoryDbContext();
        context.Users.Add(new User { Id = "u-1", FullName = "Test User", Email = "t@test.com", PasswordHash = "x" });
        context.SurfaceItems.AddRange(
            new SurfaceItem { Id = "sf-1", SceneId = "sc-1", SurfaceType = "Wall", BoundaryCoordinatesJson = "[]", OrientationVectorJson = "{}", Status = "Candidate", CreatedAt = DateTime.UtcNow.AddDays(-5) },
            new SurfaceItem { Id = "sf-2", SceneId = "sc-1", SurfaceType = "Wall", BoundaryCoordinatesJson = "[]", OrientationVectorJson = "{}", Status = "Candidate", CreatedAt = DateTime.UtcNow.AddDays(-1) }
        );
        await context.SaveChangesAsync();

        var controller = CreateController(context, "u-1");

        var result = Assert.IsType<OkObjectResult>(await controller.GetAttentionCounts());
        var pendingSurfaces = (int)result.Value!.GetType().GetProperty("pendingSurfaces")!.GetValue(result.Value)!;

        Assert.Equal(2, pendingSurfaces);
    }

    [Fact]
    public async Task DismissCategory_HidesExistingBacklog_ButNotFutureItems()
    {
        using var context = GetInMemoryDbContext();
        context.Users.Add(new User { Id = "u-2", FullName = "Test User", Email = "t2@test.com", PasswordHash = "x" });
        context.SurfaceItems.Add(new SurfaceItem
        {
            Id = "sf-old", SceneId = "sc-1", SurfaceType = "Wall", BoundaryCoordinatesJson = "[]",
            OrientationVectorJson = "{}", Status = "Candidate", CreatedAt = DateTime.UtcNow.AddDays(-3)
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "u-2");

        // Dismiss the existing backlog
        var dismissResult = Assert.IsType<OkObjectResult>(
            await controller.DismissCategory(new DismissAttentionDto { Category = "pendingSurfaces" }));
        var countAfterDismiss = (int)dismissResult.Value!.GetType().GetProperty("pendingSurfaces")!.GetValue(dismissResult.Value)!;
        Assert.Equal(0, countAfterDismiss);

        // A genuinely new candidate surface created after the dismissal must still surface
        context.SurfaceItems.Add(new SurfaceItem
        {
            Id = "sf-new", SceneId = "sc-1", SurfaceType = "Wall", BoundaryCoordinatesJson = "[]",
            OrientationVectorJson = "{}", Status = "Candidate", CreatedAt = DateTime.UtcNow.AddMinutes(1)
        });
        await context.SaveChangesAsync();

        var afterNewItem = Assert.IsType<OkObjectResult>(await controller.GetAttentionCounts());
        var countAfterNewItem = (int)afterNewItem.Value!.GetType().GetProperty("pendingSurfaces")!.GetValue(afterNewItem.Value)!;
        Assert.Equal(1, countAfterNewItem);
    }

    [Fact]
    public async Task DismissCategory_InvalidCategory_ReturnsBadRequest()
    {
        using var context = GetInMemoryDbContext();
        context.Users.Add(new User { Id = "u-3", FullName = "Test User", Email = "t3@test.com", PasswordHash = "x" });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "u-3");

        var result = await controller.DismissCategory(new DismissAttentionDto { Category = "activeAlarms" });

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
