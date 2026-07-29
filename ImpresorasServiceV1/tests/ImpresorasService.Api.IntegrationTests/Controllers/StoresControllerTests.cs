using System.Net;
using System.Net.Http.Json;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

public sealed class StoresControllerTests : IntegrationTestBase
{
    public StoresControllerTests(ApiWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Create_StoreIdZero_ReturnsCreated()
    {
        await DeleteStoreIfExistsAsync(0);

        var response = await Client.PostAsJsonAsync("/api/stores", new
        {
            storeId = 0,
            name = "Almacen Central",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var store = await db.Stores.AsNoTracking().SingleAsync(x => x.StoreId == 0);
        Assert.Equal("Almacen Central", store.Name);
        Assert.True(store.IsActive);
    }

    [Fact]
    public async Task Create_NegativeStoreId_ReturnsBadRequest()
    {
        var response = await Client.PostAsJsonAsync("/api/stores", new
        {
            storeId = -1,
            name = "No valida",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SoftDelete_ActiveStore_DeactivatesStoreAndActivePrinters()
    {
        await SeedStoreAsync(88, isActive: true);
        await SeedPrinterAsync(88, isActive: true);

        var response = await Client.DeleteAsync("/api/stores/88");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<StoreDeleteResponse>();
        Assert.NotNull(body);
        Assert.Equal("Tienda desactivada correctamente.", body.Message);
        Assert.Equal(1, body.AffectedPrinters);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var store = await db.Stores.AsNoTracking().SingleAsync(x => x.StoreId == 88);
        var printer = await db.Printers.AsNoTracking().SingleAsync(x => x.StoreId == 88);
        Assert.False(store.IsActive);
        Assert.False(printer.IsActive);
    }

    [Fact]
    public async Task SoftDelete_InactiveStore_ReturnsIdempotentMessage()
    {
        await SeedStoreAsync(89, isActive: false);

        var response = await Client.DeleteAsync("/api/stores/89");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<StoreDeleteResponse>();
        Assert.NotNull(body);
        Assert.Equal("La tienda ya estaba desactivada.", body.Message);
        Assert.Equal(0, body.AffectedPrinters);
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

    private async Task DeleteStoreIfExistsAsync(int storeId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var store = await db.Stores.FindAsync(storeId);
        if (store is not null)
        {
            db.Stores.Remove(store);
            await db.SaveChangesAsync();
        }
    }

    private async Task SeedPrinterAsync(int storeId, bool isActive)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        db.Printers.Add(new Printer
        {
            PrinterName = $"Printer {Guid.NewGuid():N}"[..30],
            SpoolQueue = $"\\\\srv\\q{Guid.NewGuid():N}"[..30],
            StoreId = storeId,
            IsActive = isActive,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private sealed record StoreDeleteResponse(string Message, int AffectedPrinters);
}
