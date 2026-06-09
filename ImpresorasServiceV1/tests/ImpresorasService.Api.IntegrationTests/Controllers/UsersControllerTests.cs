using System.Net;
using System.Net.Http.Json;
using ImpresorasService.Api.IntegrationTests;
using ImpresorasService.Api.Security;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

public sealed class UsersControllerTests : IntegrationTestBase
{
    public UsersControllerTests(ApiWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Create_WithInvalidRole_Returns400()
    {
        await ResetUsersAsync();
        var request = new { login = UniqueLogin(), password = "secret123", role = "root", storeId = 1 };

        var response = await Client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_EmployeeWithUnknownStore_Returns400()
    {
        await ResetUsersAsync();
        var request = new { login = UniqueLogin(), password = "secret123", role = RoleCatalog.Employee, storeId = 99999 };

        var response = await Client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_EmployeeWithInactiveStore_Returns400()
    {
        await ResetUsersAsync();
        await SeedStoreAsync(98, isActive: false);
        var request = new { login = UniqueLogin(), password = "secret123", role = RoleCatalog.Employee, storeId = 98 };

        var response = await Client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_StoreManagerWithActiveStore_Returns201()
    {
        await ResetUsersAsync();
        var request = new { login = UniqueLogin(), password = "secret123", role = RoleCatalog.StoreManager, storeId = 1 };

        var response = await Client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_AdminWithStore_Returns400()
    {
        await ResetUsersAsync();
        var request = new { login = UniqueLogin(), password = "secret123", role = RoleCatalog.Admin, storeId = 1 };

        var response = await Client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_CurrentUser_Returns409()
    {
        await ResetUsersAsync();
        await SeedUserAsync(1, "integration-admin", RoleCatalog.Admin);
        await SeedUserAsync(2, "another-admin", RoleCatalog.Admin);

        var response = await Client.DeleteAsync("/api/users/1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_LastAdmin_Returns409()
    {
        await ResetUsersAsync();
        await SeedUserAsync(2, "only-admin", RoleCatalog.Admin);

        var response = await Client.DeleteAsync("/api/users/2");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Admin_WhenAnotherAdminExists_Returns204()
    {
        await ResetUsersAsync();
        await SeedUserAsync(2, "admin-a", RoleCatalog.Admin);
        await SeedUserAsync(3, "admin-b", RoleCatalog.Admin);

        var response = await Client.DeleteAsync("/api/users/2");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task ResetUsersAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM printer_user");
    }

    private async Task SeedStoreAsync(int storeId, bool isActive)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var store = await db.Stores.FindAsync(storeId);
        if (store is null)
        {
            db.Stores.Add(new Store
            {
                StoreId = storeId,
                Name = $"Store {storeId}",
                IsActive = isActive,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            store.IsActive = isActive;
            store.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedUserAsync(int userId, string login, string role, int? storeId = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        db.Users.Add(new User
        {
            UserId = userId,
            Login = login,
            DisplayName = login,
            Role = role,
            StoreId = storeId,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret123")
        });
        await db.SaveChangesAsync();
    }

    private static string UniqueLogin() => $"user-{Guid.NewGuid():N}"[..20];
}
