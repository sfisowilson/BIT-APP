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

namespace Afrobotics.Bit.Tests;

public class UserServiceTests
{
    private (PostgresDbContext context, UserService service) CreateService()
    {
        var options = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseInMemoryDatabase(databaseName: $"BitTestDb_{Guid.NewGuid()}")
            .Options;
        var context = new PostgresDbContext(options);
        var service = new UserService(new UserRepository(context), new Mock<IEmailService>().Object);
        return (context, service);
    }

    private static User MakeUser(string id, string role, string status = "Active", string name = "Test User", string email = "t@test.com") =>
        new User { Id = id, FullName = name, Email = email, PasswordHash = "x", Role = role, AccountStatus = status };

    [Fact]
    public async Task GetUsersAsync_Paginates()
    {
        var (context, service) = CreateService();
        for (int i = 0; i < 25; i++)
        {
            context.Users.Add(MakeUser($"u-{i}", "Editor", email: $"u{i}@test.com"));
        }
        await context.SaveChangesAsync();

        var page1 = await service.GetUsersAsync(new UserFilterParams { Page = 1, PageSize = 20 });
        var page2 = await service.GetUsersAsync(new UserFilterParams { Page = 2, PageSize = 20 });

        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(20, page1.Items.Count);
        Assert.Equal(5, page2.Items.Count);
        Assert.True(page1.HasNextPage);
        Assert.False(page2.HasNextPage);
    }

    [Fact]
    public async Task GetUsersAsync_FiltersByRole()
    {
        var (context, service) = CreateService();
        context.Users.AddRange(
            MakeUser("u-1", "Admin", email: "a@test.com"),
            MakeUser("u-2", "Editor", email: "b@test.com"),
            MakeUser("u-3", "Editor", email: "c@test.com")
        );
        await context.SaveChangesAsync();

        var result = await service.GetUsersAsync(new UserFilterParams { Role = "Editor" });

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, u => Assert.Equal("Editor", u.Role));
    }

    [Fact]
    public async Task GetUsersAsync_FiltersBySearch_MatchesNameEmailRoleOrStatus()
    {
        var (context, service) = CreateService();
        context.Users.AddRange(
            MakeUser("u-1", "Admin", name: "Sabelo Nkosi", email: "sabelo@afrobotics.co.za"),
            MakeUser("u-2", "Editor", name: "Sfiso Dlamini", email: "loverboy.sfiso@gmail.com")
        );
        await context.SaveChangesAsync();

        var result = await service.GetUsersAsync(new UserFilterParams { Search = "Sfiso" });

        var item = Assert.Single(result.Items);
        Assert.Equal("u-2", item.Id);
    }

    [Fact]
    public async Task GetUsersAsync_FiltersByAccountStatus()
    {
        var (context, service) = CreateService();
        context.Users.AddRange(
            MakeUser("u-1", "Editor", status: "Active", email: "a@test.com"),
            MakeUser("u-2", "Editor", status: "Suspended", email: "b@test.com")
        );
        await context.SaveChangesAsync();

        var result = await service.GetUsersAsync(new UserFilterParams { AccountStatus = "Suspended" });

        var item = Assert.Single(result.Items);
        Assert.Equal("u-2", item.Id);
    }

    [Fact]
    public async Task DeleteUserAsync_LastAdmin_StillBlockedRegardlessOfPagination()
    {
        // Regression guard: the last-admin protection in DeleteUserAsync/UpdateUserAsync uses
        // _userRepository.GetAllAsync() directly (the full set), not the new paginated
        // GetUsersAsync(filter) — must keep working even though the public list endpoint is
        // now paginated.
        var (context, service) = CreateService();
        context.Users.Add(MakeUser("u-admin", "Admin", email: "admin@test.com"));
        await context.SaveChangesAsync();

        var (success, error) = await service.DeleteUserAsync("u-admin", "some-other-user-id");

        Assert.False(success);
        Assert.Contains("last remaining administrator", error);
    }
}
