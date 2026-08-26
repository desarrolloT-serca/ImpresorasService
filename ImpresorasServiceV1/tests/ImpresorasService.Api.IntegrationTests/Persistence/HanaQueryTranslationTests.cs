using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Persistence;

/// <summary>
/// El resto de la suite corre sobre SQLite, que traduce cualquier LINQ razonable. El provider de SAP
/// HANA no: ya ha tumbado el dashboard dos veces con SQL que SQLite acepta sin rechistar — el EXISTS
/// correlacionado de `overview` y el NOT IN de `timeseries` (NullReferenceException dentro de
/// HanaQuerySqlGenerator.VisitSqlUnary, endpoint entero en 500). Por construccion, ningun test sobre
/// SQLite puede detectar esa clase de fallo.
///
/// Estas pruebas generan el SQL con el provider real y NO conectan: el fallo ocurre al traducir
/// (GenerateRootCommand), asi que ToQueryString() lo reproduce en seco contra una cadena ficticia.
/// </summary>
public sealed class HanaQueryTranslationTests
{
    [Fact]
    public void QueueDepartureFilter_TranslatesOnHana()
    {
        using var db = HanaContext();

        var jobs = db.PrintJobs.AsNoTracking().Where(x => ActiveStoreIds.Contains(x.StoreId));

        // Complemento calculado en C# para que el SQL lleve un IN positivo. Con
        // `!QueueStatuses.Contains(...)` esta misma consulta lanza NullReferenceException.
        var departureStatuses = Enum.GetValues<PrintJobStatus>()
            .Where(s => !DashboardPrintJobPredicates.QueueStatuses.Contains(s))
            .ToArray();

        var sql = db.PrintJobEvents
            .Where(e => e.NewStatus != null && departureStatuses.Contains(e.NewStatus.Value))
            .Join(jobs, e => e.JobId, j => j.JobId, (e, j) => new { e.JobId, e.OccurredAtUtc })
            .GroupBy(x => x.JobId)
            .Select(g => g.Min(x => x.OccurredAtUtc))
            .ToQueryString();

        Assert.Contains("IN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NOT IN", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotInInsideAnd_StillBreaksHana_SoNobodyReintroducesIt()
    {
        using var db = HanaContext();

        var sql = () => db.PrintJobEvents
            .Where(e => e.NewStatus != null
                        && !DashboardPrintJobPredicates.QueueStatuses.Contains(e.NewStatus.Value))
            .Select(e => e.OccurredAtUtc)
            .ToQueryString();

        // Si algun dia SAP lo arregla, este test se pone rojo y hay que borrar el complemento
        // manual de GetTimeseries junto con esta prueba.
        Assert.Throws<NullReferenceException>(sql);
    }

    private static readonly List<int> ActiveStoreIds = [1, 2, 3];

    private static ImpresorasDbContext HanaContext()
    {
        var options = new DbContextOptionsBuilder<ImpresorasDbContext>();
        UseHana(options, "Server=nohost:30015;UID=x;PWD=x;");
        return new ImpresorasDbContext(options.Options);
    }

    /// <summary>Misma resolucion por reflexion que Infrastructure.DependencyInjection.</summary>
    private static void UseHana(DbContextOptionsBuilder options, string connectionString)
    {
        var assembly = Assembly.Load("Sap.EntityFrameworkCore.Hana.v8.0");
        var extensions = assembly.GetType("Microsoft.EntityFrameworkCore.HanaDbContextOptionsBuilderExtensions")
            ?? assembly.GetType("Sap.EntityFrameworkCore.Hana.Infrastructure.HanaDbContextOptionsBuilderExtensions")
            ?? assembly.GetTypes().First(t => t.Name == "HanaDbContextOptionsBuilderExtensions");

        var method = extensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => (m.Name == "UseHana" || m.Name == "AddHana")
                        && m.GetParameters().Length >= 1
                        && typeof(DbContextOptionsBuilder).IsAssignableFrom(m.GetParameters()[0].ParameterType))
            .OrderByDescending(m => m.GetParameters().Any(p => p.ParameterType == typeof(string)))
            .ThenByDescending(m => m.GetParameters().Length)
            .First();

        var args = method.GetParameters()
            .Select((p, i) => i == 0
                ? options
                : p.ParameterType == typeof(string)
                    ? connectionString
                    : p.HasDefaultValue
                        ? p.DefaultValue
                        : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
            .ToArray();

        method.Invoke(null, args);
    }
}
