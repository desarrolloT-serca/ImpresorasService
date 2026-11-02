using System.Net.Http.Json;
using System.Security.Claims;
using ImpresorasService.Api.Controllers;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

public sealed class DashboardControllerTests : IntegrationTestBase
{
    private const int DashboardTestStoreId = 880;

    public DashboardControllerTests(ApiWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetOverview_WithSeededData_ReturnsExpectedKpis()
    {
        await SeedDashboardScenarioAsync();

        var response = await Client.GetAsync($"/api/dashboard/overview?window=today&storeId={DashboardTestStoreId}");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<DashboardOverviewResponse>(response);
        Assert.NotNull(body);
        Assert.NotNull(body.Kpis);
        Assert.Equal(3, body.Kpis!.Received);
        Assert.Equal(1, body.Kpis.Printed);
        Assert.Equal(1, body.Kpis.Failed);
        Assert.Equal(1, body.Kpis.QueueCurrent);
        Assert.Equal(1, body.Kpis.FailedWithoutRetryCurrent);
        Assert.Equal(1, body.Kpis.ActivePrinters);
        Assert.Equal(1, body.Kpis.ActiveStores);
        Assert.NotNull(body.Stores);
        Assert.Single(body.Stores!);
        Assert.Equal(DashboardTestStoreId, body.Stores![0].StoreId);
        Assert.Equal("warning", body.Stores![0].Health);
    }

    [Fact]
    public async Task GetOverview_FilterByStoreId_ScopesKpis()
    {
        await SeedDashboardScenarioAsync();
        await SeedSecondStoreWithJobAsync();

        var response = await Client.GetAsync($"/api/dashboard/overview?window=today&storeId={DashboardTestStoreId}");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<DashboardOverviewResponse>(response);
        Assert.NotNull(body);
        Assert.Equal(3, body!.Kpis!.Received);
        Assert.Single(body.Stores!);
        Assert.Equal(DashboardTestStoreId, body.Stores![0].StoreId);
    }

    [Fact]
    public async Task GetOverview_ReturnsPerPrinterBreakdown()
    {
        const int storeId = 985;
        const int printerId = 5985;
        var now = DateTimeOffset.UtcNow;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

            if (!await db.Stores.AnyAsync(s => s.StoreId == storeId))
            {
                db.Stores.Add(new Store { StoreId = storeId, Name = "Printer Breakdown Store", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            if (!await db.Printers.AnyAsync(p => p.PrinterId == printerId))
            {
                db.Printers.Add(new Printer { PrinterId = printerId, StoreId = storeId, PrinterName = "Breakdown Printer", SpoolQueue = @"\\host\q985", Host = "host", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: printerId));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Routed, storeId: storeId, printerId: printerId));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.ErrorFinal, storeId: storeId, printerId: printerId, attemptCount: 2));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: null));
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/api/dashboard/overview?window=today&storeId={storeId}");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<DashboardOverviewResponse>(response);
        var store = Assert.Single(body!.Stores!);
        Assert.Equal(3, store.QueuedCurrent);           // 2 en printer 5985 (Pending+Routed) + 1 sin asignar
        Assert.Equal(1, store.UnassignedQueueCurrent);
        var printer = Assert.Single(store.Printers!);
        Assert.Equal(printerId, printer.PrinterId);
        Assert.Equal(2, printer.QueueCurrent);   // Pending + Routed
        Assert.Equal(1, printer.FailedWindow);   // ErrorFinal
        Assert.Equal(3, printer.ReceivedWindow); // los 3 jobs asignados a esta impresora, creados hoy
    }

    /// <summary>
    /// Umbrales opcionales (27/10/2026): se guardan y se leen con su horario, y un cliente que no
    /// los conoce (el PHP manda solo las cuatro métricas de siempre) no los borra. Cifras que no
    /// pueden saltar en otros tests (el fichero de reglas es compartido); al final se desactivan.
    /// </summary>
    [Fact]
    public async Task ThresholdRules_OpcionalesYHorario_SeGuardanYUnClienteAnteriorNoLosBorra()
    {
        var actuales = await (await Client.GetAsync("/api/dashboard/threshold-rules")).Content.ReadFromJsonAsync<ReglasDto>();
        Assert.NotNull(actuales);
        object Basicas(object? silent, object? reprints, object? horario) => new
        {
            actuales!.Queue, actuales.Failed, actuales.MissingHost, actuales.Conn, silent, reprints, horario
        };
        try
        {
            var put = await Client.PutAsJsonAsync("/api/dashboard/threshold-rules", Basicas(
                new[] { new { min = 600, severity = "warning" } },
                new[] { new { min = 50, severity = "critical" } },
                new { dias = new[] { 1, 2, 3, 4, 5 }, desde = "08:30", hasta = "20:00" }));
            put.EnsureSuccessStatusCode();

            // Como el PHP: sin silent, reprints ni horario.
            var php = await Client.PutAsJsonAsync("/api/dashboard/threshold-rules", new
            {
                actuales!.Queue, actuales.Failed, actuales.MissingHost, actuales.Conn
            });
            php.EnsureSuccessStatusCode();

            var leidas = await (await Client.GetAsync("/api/dashboard/threshold-rules")).Content.ReadFromJsonAsync<ReglasDto>();
            Assert.Equal(600, Assert.Single(leidas!.Silent).Min);
            Assert.Equal("critical", Assert.Single(leidas.Reprints).Severity);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, leidas.Horario.Dias);
            Assert.Equal("08:30", leidas.Horario.Desde);

            var malo = await Client.PutAsJsonAsync("/api/dashboard/threshold-rules", Basicas(
                Array.Empty<object>(), Array.Empty<object>(), new { dias = Array.Empty<int>(), desde = "20:00", hasta = "08:00" }));
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, malo.StatusCode);
        }
        finally
        {
            var limpio = await Client.PutAsJsonAsync("/api/dashboard/threshold-rules", Basicas(
                Array.Empty<object>(), Array.Empty<object>(), new { dias = new[] { 1, 2, 3, 4, 5, 6 }, desde = "09:00", hasta = "21:00" }));
            limpio.EnsureSuccessStatusCode();
        }
    }

    private sealed record ReglaDto(int Min, string Severity);
    private sealed record HorarioDto(int[] Dias, string Desde, string Hasta);
    private sealed record ReglasDto(
        ReglaDto[] Queue, ReglaDto[] Failed, ReglaDto[] MissingHost, ReglaDto[] Conn,
        ReglaDto[] Silent, ReglaDto[] Reprints, HorarioDto Horario);

    /// <summary>
    /// N1: una impresora activa sin ningún trabajo también sale en el desglose de su tienda, a 0
    /// (antes solo salían las que tenían trabajos y una tienda sin actividad parecía no tener
    /// impresoras). Las inactivas no salen.
    /// </summary>
    [Fact]
    public async Task GetOverview_PerPrinterBreakdown_IncludesActivePrintersWithoutJobs()
    {
        const int storeId = 976;
        var now = DateTimeOffset.UtcNow;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            if (!await db.Stores.AnyAsync(s => s.StoreId == storeId))
                db.Stores.Add(new Store { StoreId = storeId, Name = "Idle Store", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            foreach (var (printerId, activa) in new[] { (5976, true), (5977, true), (5978, false) })
                if (!await db.Printers.AnyAsync(p => p.PrinterId == printerId))
                    db.Printers.Add(new Printer { PrinterId = printerId, StoreId = storeId, PrinterName = $"Idle {printerId}", Host = "host", IsActive = activa, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/api/dashboard/overview?window=today&storeId={storeId}");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<DashboardOverviewResponse>(response);
        var store = Assert.Single(body!.Stores!);
        Assert.Equal(new[] { 5976, 5977 }, store.Printers!.Select(p => p.PrinterId).OrderBy(id => id));
        Assert.All(store.Printers!, p =>
        {
            Assert.Equal(0, p.QueueCurrent);
            Assert.Equal(0, p.FailedWindow);
            Assert.Equal(0, p.ReceivedWindow);
        });
    }

    /// <summary>
    /// "Desde" de las incidencias priorizadas del dashboard: la antiguedad sale del trabajo en cola
    /// mas viejo (CreatedAtUtc) y del fallo sin reenviar mas viejo (UpdatedAtUtc), sin ventana —
    /// una incidencia abierta hace tres dias sigue abierta hoy. Null cuando no hay incidencia.
    /// </summary>
    [Fact]
    public async Task GetOverview_ReturnsOldestQueuedAndFailedTimestamps()
    {
        const int storeId = 986;
        var now = DateTimeOffset.UtcNow;
        var oldestQueued = now.AddMinutes(-90);
        var oldestFailed = now.AddMinutes(-45);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

            if (!await db.Stores.AnyAsync(s => s.StoreId == storeId))
            {
                db.Stores.Add(new Store { StoreId = storeId, Name = "Oldest Timestamps Store", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());

            var queuedOld = MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: null);
            queuedOld.CreatedAtUtc = oldestQueued;
            var queuedNew = MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: null);
            queuedNew.CreatedAtUtc = now.AddMinutes(-10);
            // Llegó hace tres días y se reimprimió hace 30 min: cuenta desde la reimpresión.
            var reprinted = MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: null);
            reprinted.CreatedAtUtc = now.AddDays(-3);
            reprinted.LastRequeuedAtUtc = now.AddMinutes(-30);
            var failedOld = MakeJob(now, PrintJobStatus.ErrorFinal, storeId: storeId, printerId: null, attemptCount: 2);
            failedOld.UpdatedAtUtc = oldestFailed;
            var failedNew = MakeJob(now, PrintJobStatus.ErrorFinal, storeId: storeId, printerId: null, attemptCount: 2);
            failedNew.UpdatedAtUtc = now.AddMinutes(-5);

            AddJobWithEvent(db, queuedOld);
            AddJobWithEvent(db, queuedNew);
            AddJobWithEvent(db, reprinted);
            AddJobWithEvent(db, failedOld);
            AddJobWithEvent(db, failedNew);
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/api/dashboard/overview?window=today&storeId={storeId}");

        response.EnsureSuccessStatusCode();
        var store = Assert.Single((await ReadAsJsonAsync<DashboardOverviewResponse>(response))!.Stores!);
        Assert.NotNull(store.OldestQueuedAtUtc);
        Assert.NotNull(store.OldestFailedWithoutRetryAtUtc);
        Assert.True((store.OldestQueuedAtUtc!.Value - oldestQueued).Duration() < TimeSpan.FromSeconds(1));
        Assert.True((store.OldestFailedWithoutRetryAtUtc!.Value - oldestFailed).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetOverview_ReturnsNullTimestampsWhenTheStoreHasNoIncident()
    {
        const int storeId = 987;
        var now = DateTimeOffset.UtcNow;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

            if (!await db.Stores.AnyAsync(s => s.StoreId == storeId))
            {
                db.Stores.Add(new Store { StoreId = storeId, Name = "No Incident Store", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.PrintedConfirmed, storeId: storeId, printerId: null));
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/api/dashboard/overview?window=today&storeId={storeId}");

        response.EnsureSuccessStatusCode();
        var store = Assert.Single((await ReadAsJsonAsync<DashboardOverviewResponse>(response))!.Stores!);
        Assert.Null(store.OldestQueuedAtUtc);
        Assert.Null(store.OldestFailedWithoutRetryAtUtc);
    }

    /// <summary>
    /// El KPI "sin reenviar" y el listado de cola que abre el operador al pulsarlo deben devolver
    /// exactamente el mismo conjunto. Antes el dashboard enlazaba a `status=8` (solo ErrorFinal),
    /// que es un subconjunto: el número mostrado no cuadraba con la lista y no había forma de
    /// auditar la diferencia. Además fija que un Cancelado tras agotar reintentos NO cuenta:
    /// cancelar cierra el trabajo, no deja nada pendiente de reenvío.
    /// </summary>
    [Fact]
    public async Task GetOverview_FailedWithoutRetryKpi_MatchesQueueFilterAndExcludesCancelled()
    {
        const int storeId = 987;
        const int printerId = 5987;
        var now = DateTimeOffset.UtcNow;
        Guid cancelledJobId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

            if (!await db.Stores.AnyAsync(s => s.StoreId == storeId))
            {
                db.Stores.Add(new Store { StoreId = storeId, Name = "Sin Reenviar Store", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            if (!await db.Printers.AnyAsync(p => p.PrinterId == printerId))
            {
                db.Printers.Add(new Printer { PrinterId = printerId, StoreId = storeId, PrinterName = "Sin Reenviar Printer", SpoolQueue = @"\\host\q987", Host = "host", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());

            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.ErrorFinal, storeId: storeId, printerId: printerId, attemptCount: 1));
            // Bloqueado en su primer intento: ya es una incidencia (D3, 27/10/2026).
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.PrinterBlocked, storeId: storeId, printerId: printerId, attemptCount: 1));
            var cancelled = MakeJob(now, PrintJobStatus.Cancelled, storeId: storeId, printerId: printerId, attemptCount: 4);
            cancelledJobId = cancelled.JobId;
            AddJobWithEvent(db, cancelled);
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: printerId));
            await db.SaveChangesAsync();
        }

        var overviewResponse = await Client.GetAsync($"/api/dashboard/overview?window=today&storeId={storeId}");
        overviewResponse.EnsureSuccessStatusCode();
        var overview = await ReadAsJsonAsync<DashboardOverviewResponse>(overviewResponse);
        var store = Assert.Single(overview!.Stores!);
        Assert.Equal(2, store.FailedWithoutRetryCurrent); // ErrorFinal + PrinterBlocked (1 intento); el Cancelado no

        var queueResponse = await Client.GetAsync($"/api/printjobs?storeId={storeId}&failedWithoutRetry=true&includeTotal=true");
        queueResponse.EnsureSuccessStatusCode();
        var page = await ReadAsJsonAsync<PagedJobs>(queueResponse);
        Assert.Equal(store.FailedWithoutRetryCurrent, page!.Count);
        Assert.DoesNotContain(page.Value!, j => j.JobId == cancelledJobId);
    }

    /// <summary>
    /// Igual que "sin reenviar": el KPI `unassignedQueueCurrent` que el dashboard enlaza tiene que
    /// poder listarse con el mismo conjunto exacto. `unassigned=true` fija sus propios estados (los
    /// de cola), asi que un trabajo sin impresora ya cerrado no cuenta ni aqui ni en el KPI.
    /// </summary>
    [Fact]
    public async Task GetQueue_UnassignedFilter_MatchesUnassignedQueueCurrentKpi()
    {
        const int storeId = 988;
        const int printerId = 5988;
        var now = DateTimeOffset.UtcNow;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

            if (!await db.Stores.AnyAsync(s => s.StoreId == storeId))
            {
                db.Stores.Add(new Store { StoreId = storeId, Name = "Unassigned Store", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            if (!await db.Printers.AnyAsync(p => p.PrinterId == printerId))
            {
                db.Printers.Add(new Printer { PrinterId = printerId, StoreId = storeId, PrinterName = "Unassigned Printer", SpoolQueue = @"\\host\q988", Host = "host", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());

            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: null));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: null));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: printerId));
            // Sin impresora pero ya cerrado: no esta en cola, no cuenta.
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.ErrorFinal, storeId: storeId, printerId: null));
            await db.SaveChangesAsync();
        }

        var overviewResponse = await Client.GetAsync($"/api/dashboard/overview?window=today&storeId={storeId}");
        overviewResponse.EnsureSuccessStatusCode();
        var store = Assert.Single((await ReadAsJsonAsync<DashboardOverviewResponse>(overviewResponse))!.Stores!);
        Assert.Equal(2, store.UnassignedQueueCurrent);

        var queueResponse = await Client.GetAsync($"/api/printjobs?storeId={storeId}&unassigned=true&includeTotal=true");
        queueResponse.EnsureSuccessStatusCode();
        var page = await ReadAsJsonAsync<PagedJobs>(queueResponse);
        Assert.Equal(store.UnassignedQueueCurrent, page!.Count);
    }

    /// <summary>
    /// La serie temporal tiene que cuadrar con los KPIs del overview: si se suman los tramos sale el
    /// mismo `received` y el mismo `printed`. Y la profundidad de cola, reconstruida hacia atras
    /// desde la foto actual, tiene que cerrar en `queueCurrent` en el ultimo tramo.
    /// </summary>
    [Fact]
    public async Task GetTimeseries_SumsToTheSameKpisAndClosesOnTheCurrentQueue()
    {
        const int storeId = 989;
        const int printerId = 5989;
        var now = DateTimeOffset.UtcNow;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

            if (!await db.Stores.AnyAsync(s => s.StoreId == storeId))
            {
                db.Stores.Add(new Store { StoreId = storeId, Name = "Timeseries Store", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            if (!await db.Printers.AnyAsync(p => p.PrinterId == printerId))
            {
                db.Printers.Add(new Printer { PrinterId = printerId, StoreId = storeId, PrinterName = "Timeseries Printer", SpoolQueue = @"\\host\q989", Host = "host", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());

            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.PrintedConfirmed, storeId: storeId, printerId: printerId));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.PrintedConfirmed, storeId: storeId, printerId: printerId));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: printerId));
            await db.SaveChangesAsync();
        }

        var overview = await ReadAsJsonAsync<DashboardOverviewResponse>(
            await Client.GetAsync($"/api/dashboard/overview?window=today&storeId={storeId}"));

        var response = await Client.GetAsync($"/api/dashboard/timeseries?window=today&storeId={storeId}");
        response.EnsureSuccessStatusCode();
        var series = await ReadAsJsonAsync<TimeseriesResponse>(response);

        Assert.NotNull(series);
        Assert.Equal("hour", series!.Bucket);
        Assert.NotEmpty(series.Points!);
        Assert.Equal(overview!.Kpis!.Received, series.Points!.Sum(p => p.Received));
        Assert.Equal(overview.Kpis.Printed, series.Points!.Sum(p => p.Printed));

        var store = Assert.Single(overview.Stores!);
        Assert.Equal(store.QueuedCurrent, series.Points!.Last().QueueDepth);
    }

    /// <summary>
    /// La cola de un trabajo dura segundos: al cerrar el tramo ya está vacía aunque dentro esperasen
    /// varios. queuePeak da el máximo del tramo. Y una reimpresión vuelve a la cola sin ser una
    /// llegada: sin contarla, los tramos anteriores arrastraban ese trabajo.
    /// </summary>
    [Fact]
    public async Task GetTimeseries_QueuePeak_CuentaLoQueEsperoDentroDelTramo()
    {
        const int storeId = 986;
        var now = DateTimeOffset.UtcNow;
        // Dentro del tramo de ahora (horas en punto también en Madrid).
        var tramo = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        var llega = tramo + (now - tramo) / 4;
        var sale = tramo + (now - tramo) / 2;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            if (!await db.Stores.AnyAsync(s => s.StoreId == storeId))
                db.Stores.Add(new Store { StoreId = storeId, Name = "Peak Store", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());

            for (var i = 0; i < 6; i++)
            {
                var job = MakeJob(now, PrintJobStatus.PrintedConfirmed, storeId: storeId, printerId: null);
                job.CreatedAtUtc = llega;
                job.UpdatedAtUtc = sale;
                AddJobWithEvent(db, job);
            }
            var reimpreso = MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: null);
            reimpreso.CreatedAtUtc = now.AddDays(-3);
            db.PrintJobs.Add(reimpreso);
            db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = reimpreso.JobId,
                EventType = "REPRINT",
                OldStatus = PrintJobStatus.PrintedConfirmed,
                NewStatus = PrintJobStatus.Pending,
                ActorType = "user",
                OccurredAtUtc = llega
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/api/dashboard/timeseries?window=today&storeId={storeId}");
        response.EnsureSuccessStatusCode();
        var points = (await ReadAsJsonAsync<TimeseriesResponse>(response))!.Points!;

        Assert.Equal(1, points.Last().QueueDepth);
        Assert.Equal(7, points.Last().QueuePeak);
        Assert.All(points.SkipLast(1), p => Assert.Equal(0, p.QueueDepth));
    }

    /// <summary>
    /// El resultado de lo que llegó en un tramo: cada trabajo una vez, por su estado de ahora. Un fallo
    /// sin resolver sigue contando como fallido en su tramo (antes la gráfica lo perdía de vista).
    /// </summary>
    [Fact]
    public async Task GetTimeseries_Outcome_RepartePorEstadoDeAhoraYNoPierdeLosFallos()
    {
        const int storeId = 985;
        var now = DateTimeOffset.UtcNow;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            if (!await db.Stores.AnyAsync(s => s.StoreId == storeId))
                db.Stores.Add(new Store { StoreId = storeId, Name = "Outcome Store", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());

            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.PrintedConfirmed, storeId: storeId, printerId: null));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.ErrorFinal, storeId: storeId, printerId: null));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.ErrorFinal, storeId: storeId, printerId: null));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.PrinterBlocked, storeId: storeId, printerId: null));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Cancelled, storeId: storeId, printerId: null));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: null));
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/api/dashboard/timeseries?window=today&storeId={storeId}");
        response.EnsureSuccessStatusCode();
        var tramo = Assert.Single((await ReadAsJsonAsync<TimeseriesResponse>(response))!.Points!, p => p.Received > 0);

        Assert.Equal(6, tramo.Received);
        Assert.Equal(1, tramo.Outcome!.Printed);
        Assert.Equal(3, tramo.Outcome.Failed);
        Assert.Equal(1, tramo.Outcome.Cancelled);
        Assert.Equal(1, tramo.Outcome.Active);
    }

    [Fact]
    public async Task GetTimeseries_UsesDailyBucketsOnTheWiderWindows()
    {
        var response = await Client.GetAsync("/api/dashboard/timeseries?window=7d");

        response.EnsureSuccessStatusCode();
        var series = await ReadAsJsonAsync<TimeseriesResponse>(response);
        Assert.Equal("day", series!.Bucket);
    }

    // Quien no es Admin y no tiene tienda no ve ninguna, nunca todas: igual que la cola y las
    // impresoras. Pedir una tienda concreta tampoco le sirve.
    [Fact]
    public async Task OverviewAndTimeseries_NonAdminWithoutStore_ReturnForbid()
    {
        using var scope = Factory.Services.CreateScope();
        var controller = ActivatorUtilities.CreateInstance<DashboardController>(scope.ServiceProvider);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Employee") }, "test"))
            }
        };

        Assert.IsType<ForbidResult>(await controller.GetOverview("today", DashboardTestStoreId, CancellationToken.None));
        Assert.IsType<ForbidResult>(await controller.GetTimeseries("today", DashboardTestStoreId, null, CancellationToken.None));
    }

    [Fact]
    public async Task GetThresholds_ReturnsDefaultsWhenMissing()
    {
        var response = await Client.GetAsync("/api/dashboard/thresholds");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<DashboardThresholdsResponse>(response);
        Assert.NotNull(body);
        Assert.Equal(10, body!.WarningQueueMin);
        Assert.Equal(30, body.CriticalQueueMin);
    }

    [Fact]
    public async Task GetHealth_ReturnsDatabaseCheck()
    {
        var response = await Client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<HealthResponse>(response);
        Assert.NotNull(body);
        Assert.Equal("ok", body!.Status);
        Assert.True(body.Checks?.ContainsKey("database"));
        Assert.Equal("Healthy", body.Checks!["database"]);
    }

    private async Task SeedDashboardScenarioAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var now = DateTimeOffset.UtcNow;

        if (!await db.Stores.AnyAsync(s => s.StoreId == DashboardTestStoreId))
        {
            db.Stores.Add(new Store
            {
                StoreId = DashboardTestStoreId,
                Name = "Dashboard Test Store",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        if (!await db.Printers.AnyAsync(p => p.PrinterId == 5880))
        {
            db.Printers.Add(new Printer
            {
                PrinterId = 5880,
                StoreId = DashboardTestStoreId,
                PrinterName = "Dashboard Test Printer",
                SpoolQueue = @"\\host\queue",
                Host = "host",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == DashboardTestStoreId).ToListAsync());

        AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Pending, attemptCount: 0));
        AddJobWithEvent(db, MakeJob(now, PrintJobStatus.PrintedConfirmed, attemptCount: 1));
        AddJobWithEvent(db, MakeJob(now, PrintJobStatus.ErrorFinal, attemptCount: 2));

        await db.SaveChangesAsync();
    }

    private async Task SeedSecondStoreWithJobAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var now = DateTimeOffset.UtcNow;
        const int otherStoreId = 881;

        if (!await db.Stores.AnyAsync(s => s.StoreId == otherStoreId))
        {
            db.Stores.Add(new Store
            {
                StoreId = otherStoreId,
                Name = "Other Dashboard Store",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == otherStoreId).ToListAsync());
        db.PrintJobs.Add(MakeJob(now, PrintJobStatus.Pending, storeId: otherStoreId, attemptCount: 0));
        await db.SaveChangesAsync();
    }

    private const int UnspecifiedPrinterId = -1;

    /// <summary>
    /// "printed"/"failed" ahora se calculan desde PrintJobEvents (KPI-P1-001/002), no solo desde
    /// el Status/UpdatedAtUtc actual del job — sembrar el evento correspondiente junto al job.
    /// </summary>
    private static void AddJobWithEvent(ImpresorasDbContext db, PrintJob job)
    {
        db.PrintJobs.Add(job);

        var isPrintedOrFailedStatus = job.Status is PrintJobStatus.SpoolAccepted or PrintJobStatus.PrintedConfirmed
            or PrintJobStatus.PrintedUnknown or PrintJobStatus.ErrorFinal or PrintJobStatus.RetryScheduled;
        if (isPrintedOrFailedStatus)
        {
            db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = job.JobId,
                EventType = "StatusChanged",
                NewStatus = job.Status,
                ActorType = "system",
                OccurredAtUtc = job.UpdatedAtUtc
            });
        }
    }

    private static PrintJob MakeJob(
        DateTimeOffset now,
        PrintJobStatus status,
        int attemptCount = 0,
        int storeId = DashboardTestStoreId,
        int? printerId = UnspecifiedPrinterId)
    {
        return new PrintJob
        {
            JobId = Guid.NewGuid(),
            SourceSystem = "TEST",
            ExternalJobId = Guid.NewGuid().ToString("N"),
            StoreId = storeId,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = [0x25, 0x50, 0x44, 0x46],
            PdfSha256 = Guid.NewGuid().ToString("N"),
            Status = status,
            PrinterId = printerId != UnspecifiedPrinterId
                ? printerId
                : (status is PrintJobStatus.Pending or PrintJobStatus.Routed ? 5880 : null),
            AttemptCount = attemptCount,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private sealed class DashboardOverviewResponse
    {
        public DashboardKpis? Kpis { get; set; }
        public List<StoreRow>? Stores { get; set; }
    }

    private sealed class DashboardKpis
    {
        public int Received { get; set; }
        public int Printed { get; set; }
        public int Failed { get; set; }
        public int QueueCurrent { get; set; }
        public int FailedWithoutRetryCurrent { get; set; }
        public int ActivePrinters { get; set; }
        public int ActiveStores { get; set; }
    }

    private sealed class StoreRow
    {
        public int StoreId { get; set; }
        public string? Health { get; set; }
        public int QueuedCurrent { get; set; }
        public int FailedWithoutRetryCurrent { get; set; }
        public int UnassignedQueueCurrent { get; set; }
        public DateTimeOffset? OldestQueuedAtUtc { get; set; }
        public DateTimeOffset? OldestFailedWithoutRetryAtUtc { get; set; }
        public List<PrinterRow>? Printers { get; set; }
    }

    private sealed class TimeseriesResponse
    {
        public string? Bucket { get; set; }
        public List<TimeseriesPoint>? Points { get; set; }
    }

    private sealed class TimeseriesPoint
    {
        public int Received { get; set; }
        public int Printed { get; set; }
        public int Failed { get; set; }
        public int QueueDepth { get; set; }
        public int QueuePeak { get; set; }
        public TimeseriesOutcome? Outcome { get; set; }
    }

    private sealed class TimeseriesOutcome
    {
        public int Printed { get; set; }
        public int Failed { get; set; }
        public int Cancelled { get; set; }
        public int Active { get; set; }
    }

    private sealed class PagedJobs
    {
        public List<QueueJob>? Value { get; set; }
        public int Count { get; set; }
    }

    private sealed class QueueJob
    {
        public Guid JobId { get; set; }
        public int AttemptCount { get; set; }
    }

    private sealed class PrinterRow
    {
        public int PrinterId { get; set; }
        public int QueueCurrent { get; set; }
        public int FailedWindow { get; set; }
        public int ReceivedWindow { get; set; }
    }

    private sealed class DashboardThresholdsResponse
    {
        public int WarningQueueMin { get; set; }
        public int CriticalQueueMin { get; set; }
    }

    private sealed class HealthResponse
    {
        public string? Status { get; set; }
        public Dictionary<string, string>? Checks { get; set; }
    }
}
