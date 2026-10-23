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

        var jobs = db.PrintJobs.AsNoTracking().Where(x => ActiveStoreIds.Contains(x.StoreId));

        var departureStatuses = Enum.GetValues<PrintJobStatus>()
            .Where(s => !DashboardPrintJobPredicates.QueueStatuses.Contains(s))
            .ToArray();

        var sql = db.PrintJobEvents
            .Where(e => e.NewStatus != null && departureStatuses.Contains(e.NewStatus.Value))
            .Join(jobs, e => e.JobId, j => j.JobId, (e, j) => new { e.JobId, e.OccurredAtUtc })
            .GroupBy(x => x.JobId)
            .Select(g => g.Min(x => x.OccurredAtUtc))
            .ToQueryString();

        Assert.Contains("printer_print_job_event", sql, StringComparison.OrdinalIgnoreCase);
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

    private static readonly List<int> ActiveStoreIds = [1, 2, 3];

    internal static ImpresorasDbContext PostgresContext() =>
        new(new DbContextOptionsBuilder<ImpresorasDbContext>()
            .UseNpgsql("Host=nohost;Database=x;Username=x")
            .Options);
}
