using System;
using System.Collections.Generic;
using System.Linq;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Persistence;

/// <summary>
/// El resto de la suite corre sobre SQLite, que traduce cualquier LINQ razonable. El provider de
/// produccion puede no hacerlo, y ningun test sobre SQLite detecta esa clase de fallo.
///
/// Estas pruebas generan el SQL con el provider real (Npgsql) y NO conectan: el fallo ocurriria al
/// traducir, asi que ToQueryString() lo reproduce en seco contra una cadena ficticia.
/// </summary>
public sealed class PostgresQueryTranslationTests
{
    [Fact]
    public void QueueDepartureFilter_TranslatesOnPostgres()
    {
        using var db = PostgresContext();

        // Mismas formas que DashboardController: EXISTS correlacionado contra tiendas activas y
        // NOT IN de los estados de cola (las dos rompían el proveedor anterior).
        var activeStores = db.Stores.Where(s => s.IsActive);
        var jobs = db.PrintJobs.AsNoTracking().Where(x => activeStores.Any(s => s.StoreId == x.StoreId));

        var sql = db.PrintJobEvents
            .Where(e => e.NewStatus != null && !DashboardPrintJobPredicates.QueueStatuses.Contains(e.NewStatus.Value))
            .Join(jobs, e => e.JobId, j => j.JobId, (e, j) => new { e.JobId, e.OccurredAtUtc })
            .GroupBy(x => x.JobId)
            .Select(g => g.Min(x => x.OccurredAtUtc))
            .ToQueryString();

        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedWithoutRetryCurrentPredicate_TranslatesOnPostgres()
    {
        using var db = PostgresContext();

        var sql = db.PrintJobs
            .Where(DashboardPrintJobPredicates.FailedWithoutRetryCurrent)
            .GroupBy(x => x.StoreId)
            .Select(x => new { StoreId = x.Key, Count = x.Count() })
            .ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dates_AreNativeOnPostgres_AndTextOnSqlite_InTheSameProcess()
    {
        using var pg = PostgresContext();
        using var sqlite = new ImpresorasDbContext(new DbContextOptionsBuilder<ImpresorasDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        static string ColumnType(ImpresorasDbContext db) => db.Model
            .FindEntityType(typeof(ImpresorasService.Domain.Entities.PrintJob))!
            .FindProperty(nameof(ImpresorasService.Domain.Entities.PrintJob.CreatedAtUtc))!
            .GetRelationalTypeMapping().StoreType;

        // Si el modelo se cachease solo por tipo de contexto, los dos verían el mismo.
        Assert.Equal("timestamp with time zone", ColumnType(pg));
        Assert.Equal("TEXT", ColumnType(sqlite));
    }

    [Fact]
    public void DateRangeFilter_TranslatesOnPostgres()
    {
        using var db = PostgresContext();
        var from = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(2));

        var sql = db.PrintJobs.Where(x => x.CreatedAtUtc >= from && x.NextRetryAtUtc != null).ToQueryString();

        Assert.Contains("created_at_utc >=", sql, StringComparison.OrdinalIgnoreCase);
        // 10:00 +02:00 llega a la BD como 08:00 UTC: Npgsql rechaza offsets distintos de 0.
        Assert.Contains("2026-10-23T08:00:00", sql);
    }

    internal static ImpresorasDbContext PostgresContext() =>
        new(new DbContextOptionsBuilder<ImpresorasDbContext>()
            .UseNpgsql("Host=nohost;Database=x;Username=x")
            .Options);
}
