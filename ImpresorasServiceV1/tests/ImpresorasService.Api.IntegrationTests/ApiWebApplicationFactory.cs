using System.Threading;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// Factory para tests de integración. Usa SQLite en memoria con conexión compartida
/// para mantener el esquema entre requests. Cada instancia usa una BD aislada.
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private static int _instanceId;
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("TestInstanceId", Interlocked.Increment(ref _instanceId).ToString());

        builder.ConfigureTestServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ImpresorasDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            services.AddDbContext<ImpresorasDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        db.Database.EnsureCreated();

        return host;
    }

    /// <summary>
    /// Limpia todas las tablas. Usar al inicio de tests que requieren BD vacía.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM PrintJobEvents");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM PrintJobs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM SourcePrintJobs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM RoutingRules");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Printers");
    }

    /// <summary>
    /// Crea un PrintJob en estado Pending para tests de enrutado.
    /// </summary>
    public async Task<Guid> SeedPendingPrintJobAsync(int storeId = 1, string documentType = "FACTURA", string channel = "DEFAULT")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob
        {
            JobId = Guid.NewGuid(),
            SourceSystem = "TEST",
            ExternalJobId = $"JOB-{Guid.NewGuid():N}",
            StoreId = storeId,
            DocumentType = documentType,
            Channel = channel,
            PdfBlob = new byte[] { 0x25, 0x50, 0x44, 0x46 },
            PdfSha256 = "abc123",
            Status = PrintJobStatus.Pending,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();
        return job.JobId;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection?.Dispose();
        }
        base.Dispose(disposing);
    }
}
