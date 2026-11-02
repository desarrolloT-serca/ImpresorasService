using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using ImpresorasService.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Flow;

internal sealed class DummyRoutingService : IRoutingService
{
    public Task<RouteResult> TryRetryRouteAsync(Guid jobId, CancellationToken cancellationToken = default, string? actorId = null)
        => throw new NotImplementedException();

    public Task<RouteResult> ReprintAsync(Guid jobId, string? reason, string? note, string? actorId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task TryRouteBatchAsync(IReadOnlyList<Guid> jobIds, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

public sealed class PrintJobsControllerCancelFlowTests
{
    [Fact]
    public async Task Cancel_WhenPending_Admin_ReturnsOkAndPersistsCancelledEvent()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "ERP-TEST",
            ExternalJobId = "EXT-1",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Pending,
            PrinterId = null,
            AttemptCount = 0,
            NextRetryAtUtc = null,
            LastErrorCode = null,
            LastErrorMessage = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var controller = new PrintJobsController(db, new DummyRoutingService(), TimeProvider.System);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "Admin", storeId: "1")
            }
        };

        // Act
        var result = await controller.Cancel(jobId, CancellationToken.None);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        // Las escrituras van por ExecuteUpdate, que no refresca lo ya seguido por este contexto:
        // sin soltarlo, la relectura devolveria la copia en memoria de antes del UPDATE.
        db.ChangeTracker.Clear();
        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Cancelled, jobAfter.Status);
        Assert.Null(jobAfter.NextRetryAtUtc);

        var lastEvent = await db.PrintJobEvents
            .Where(e => e.JobId == jobId)
            .OrderByDescending(e => e.EventId)
            .FirstOrDefaultAsync();

        Assert.NotNull(lastEvent);
        Assert.Equal("CANCELLED_BY_USER", lastEvent!.EventType);
        Assert.Equal(PrintJobStatus.Pending, lastEvent.OldStatus);
        Assert.Equal(PrintJobStatus.Cancelled, lastEvent.NewStatus);
        Assert.Equal("user-1", lastEvent.ActorId);
    }

    [Fact]
    public async Task Cancel_WhenPrinting_Admin_ReturnsBadRequestAndDoesNotChangeStatus()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "ERP-TEST",
            ExternalJobId = "EXT-2",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Printing,
            PrinterId = 10,
            AttemptCount = 2,
            NextRetryAtUtc = null,
            LastErrorCode = "X",
            LastErrorMessage = "Y",
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var controller = new PrintJobsController(db, new DummyRoutingService(), TimeProvider.System);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "Admin", storeId: "1")
            }
        };

        var result = await controller.Cancel(jobId, CancellationToken.None);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(bad.Value);

        // Las escrituras van por ExecuteUpdate, que no refresca lo ya seguido por este contexto:
        // sin soltarlo, la relectura devolveria la copia en memoria de antes del UPDATE.
        db.ChangeTracker.Clear();
        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Printing, jobAfter.Status);
    }

    [Fact]
    public async Task Cancel_WhenErrorFinal_Admin_ReturnsOkAndPersistsCancelledEvent()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "ERP-TEST",
            ExternalJobId = "EXT-3",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.ErrorFinal,
            PrinterId = null,
            AttemptCount = 4,
            NextRetryAtUtc = null,
            LastErrorCode = "ROUTE_NOT_FOUND",
            LastErrorMessage = "No hay regla",
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var controller = new PrintJobsController(db, new DummyRoutingService(), TimeProvider.System);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "Admin", storeId: "1")
            }
        };

        var result = await controller.Cancel(jobId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        // Las escrituras van por ExecuteUpdate, que no refresca lo ya seguido por este contexto:
        // sin soltarlo, la relectura devolveria la copia en memoria de antes del UPDATE.
        db.ChangeTracker.Clear();
        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Cancelled, jobAfter.Status);

        var eventType = await db.PrintJobEvents
            .Where(e => e.JobId == jobId)
            .Select(e => e.EventType)
            .ToListAsync();

        Assert.Contains("CANCELLED_BY_USER", eventType);
    }

    [Fact]
    public async Task Cancel_WhenStoreManagerAndJobNotInStore_ReturnsForbid()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "ERP-TEST",
            ExternalJobId = "EXT-4",
            StoreId = 2,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Pending,
            PrinterId = null,
            AttemptCount = 0,
            NextRetryAtUtc = null,
            LastErrorCode = null,
            LastErrorMessage = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var controller = new PrintJobsController(db, new DummyRoutingService(), TimeProvider.System);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "StoreManager", storeId: "1")
            }
        };

        var result = await controller.Cancel(jobId, CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Cancel_WhenStoreManagerAndJobInStore_ReturnsOkAndPersistsCancelledEvent()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "ERP-TEST",
            ExternalJobId = "EXT-5",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Routed,
            PrinterId = 10,
            AttemptCount = 1,
            NextRetryAtUtc = null,
            LastErrorCode = null,
            LastErrorMessage = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var controller = new PrintJobsController(db, new DummyRoutingService(), TimeProvider.System);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "StoreManager", storeId: "1")
            }
        };

        // Act
        var result = await controller.Cancel(jobId, CancellationToken.None);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        // Las escrituras van por ExecuteUpdate, que no refresca lo ya seguido por este contexto:
        // sin soltarlo, la relectura devolveria la copia en memoria de antes del UPDATE.
        db.ChangeTracker.Clear();
        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Cancelled, jobAfter.Status);

        var lastEvent = await db.PrintJobEvents
            .Where(e => e.JobId == jobId)
            .OrderByDescending(e => e.EventId)
            .FirstOrDefaultAsync();

        Assert.NotNull(lastEvent);
        Assert.Equal("CANCELLED_BY_USER", lastEvent!.EventType);
        Assert.Equal("user-1", lastEvent.ActorId);
        Assert.Equal(PrintJobStatus.Routed, lastEvent.OldStatus);
    }

    [Fact]
    public async Task Cancel_WhenSpoolAccepted_ReturnsBadRequestAndDoesNotChangeStatus()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "ERP-TEST",
            ExternalJobId = "EXT-6",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.SpoolAccepted,
            PrinterId = 10,
            AttemptCount = 1,
            NextRetryAtUtc = null,
            LastErrorCode = null,
            LastErrorMessage = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var controller = new PrintJobsController(db, new DummyRoutingService(), TimeProvider.System);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "Admin", storeId: "1")
            }
        };

        var result = await controller.Cancel(jobId, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);

        // Las escrituras van por ExecuteUpdate, que no refresca lo ya seguido por este contexto:
        // sin soltarlo, la relectura devolveria la copia en memoria de antes del UPDATE.
        db.ChangeTracker.Clear();
        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.SpoolAccepted, jobAfter.Status);
    }

    /// <summary>
    /// Cancelar a mano cancela también en la cola de la impresora lo que salió por IP, con su número y
    /// el mismo nombre con que se mandó (decisión del usuario, 27/10/2026). Lo que salió sin número no se
    /// puede cancelar desde aquí: se avisa, en la respuesta y en el evento, para que lo mire alguien.
    /// </summary>
    [Theory]
    [InlineData(258, "Cancelado", "También se canceló en la impresora")]
    [InlineData(null, "SinNumero", "cola de Windows")]
    public async Task Cancel_EnLaImpresoraTambien_YQuedaConstancia(int? ippJobId, string codigo, string aviso)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;
        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();
        db.Printers.Add(new Printer { PrinterId = 10, StoreId = 1, PrinterName = "P", Host = "10.0.0.9", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId, SourceSystem = "ERP-TEST", ExternalJobId = "EXT-9", StoreId = 1, DocumentType = "FACTURA",
            Channel = "DEFAULT", PdfBlob = MinimalPdf.Bytes, PdfSha256 = "pdf-sha", Status = PrintJobStatus.PrintedUnknown,
            PrinterId = 10, IppJobId = ippJobId, AttemptCount = 1, CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now, UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();
        var ipp = new IppQueCancela();
        var controller = new PrintJobsController(db, new DummyRoutingService(), TimeProvider.System, ipp: ipp)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = BuildPrincipal(role: "Admin", storeId: "1") }
            }
        };

        var result = await controller.Cancel(jobId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var respuesta = System.Text.Json.JsonSerializer.SerializeToElement(ok.Value);
        Assert.Equal(codigo, respuesta.GetProperty("printerCancel").GetString());
        Assert.Contains(aviso, respuesta.GetProperty("printerMessage").GetString());
        Assert.Equal(ippJobId is null ? null : ("10.0.0.9", 258, "FACTURA EXT-9"), ipp.Cancelado);
        var evento = await db.PrintJobEvents.Where(e => e.JobId == jobId).OrderByDescending(e => e.EventId).FirstAsync();
        Assert.Contains(aviso, evento.Message);
    }

    /// <summary>
    /// Reimprimir algo sin confirmar cancela la copia anterior si sigue en la cola de la impresora
    /// (decisión del usuario, 27/10/2026), pero solo si la nueva va a una impresora: sin regla, el
    /// documento se quedaría sin salir y la anterior no se toca.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reprint_SinConfirmar_CancelaLaCopiaAnteriorSoloSiLaNuevaTieneImpresora(bool conRegla)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;
        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();
        db.Printers.Add(new Printer { PrinterId = 10, StoreId = 1, PrinterName = "P", Host = "10.0.0.9", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
        if (conRegla)
            db.RoutingRules.Add(new RoutingRule { Priority = 1, StoreId = 1, PrinterId = 10, IsActive = true, ValidFromUtc = now.AddDays(-1), CreatedAtUtc = now, UpdatedAtUtc = now });
        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId, SourceSystem = "ERP-TEST", ExternalJobId = "EXT-7", StoreId = 1, DocumentType = "ALBARAN",
            Channel = "DEFAULT", PdfBlob = MinimalPdf.Bytes, PdfSha256 = "pdf-sha", Status = PrintJobStatus.PrintedUnknown,
            PrinterId = 10, IppJobId = 258, AttemptCount = 1, CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now, UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();
        var ipp = new IppQueCancela();
        var controller = new PrintJobsController(db, new RoutingService(db, new RoutingResolver(db)), TimeProvider.System, ipp: ipp)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = BuildPrincipal(role: "Admin", storeId: "1") }
            }
        };

        var result = await controller.Reprint(jobId, new ReprintRequest("PERDIDO", null), CancellationToken.None);

        var respuesta = System.Text.Json.JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);
        if (conRegla)
        {
            Assert.Equal(("10.0.0.9", 258, "ALBARAN EXT-7"), ipp.Cancelado);
            Assert.Equal("Cancelado", respuesta.GetProperty("printerCancel").GetString());
            Assert.Contains("solo saldrá la nueva", respuesta.GetProperty("printerMessage").GetString());
            Assert.True(await db.PrintJobEvents.AnyAsync(e => e.JobId == jobId && e.EventType == "PREVIOUS_COPY_IN_PRINTER"));
        }
        else
        {
            Assert.Equal("ErrorFinal", respuesta.GetProperty("status").GetString());
            Assert.Null(ipp.Cancelado);
        }
    }

    private sealed class IppQueCancela : IIppConfirmationService
    {
        public (string Host, int JobId, string JobName)? Cancelado { get; private set; }

        public Task<CancelacionEnImpresora> CancelJobAsync(string printerHost, int jobId, string jobName, CancellationToken ct)
        {
            Cancelado = (printerHost, jobId, jobName);
            return Task.FromResult(CancelacionEnImpresora.Cancelado);
        }

        public Task<IppQueryResult> QueryPrinterStateAsync(string printerHost, CancellationToken ct) => throw new NotImplementedException();
        public Task<PrintSpoolResult?> PrintPdfAsync(string printerHost, byte[] pdf, string jobName, bool comoImagen, CancellationToken ct) => throw new NotImplementedException();
        public Task<IppQueryResult?> QueryJobStateAsync(string printerHost, int jobId, CancellationToken ct) => throw new NotImplementedException();
    }

    private static ClaimsPrincipal BuildPrincipal(string role, string storeId)
    {
        // Nota de decisión: PrintJobsController usa User.IsInRole("...") y lee claim "StoreId".
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role),
            new("StoreId", storeId),
            new(ClaimTypes.NameIdentifier, "user-1"),
            new(ClaimTypes.Name, "user-1"),
        };

        var identity = new ClaimsIdentity(claims, authenticationType: "test");
        return new ClaimsPrincipal(identity);
    }
}

