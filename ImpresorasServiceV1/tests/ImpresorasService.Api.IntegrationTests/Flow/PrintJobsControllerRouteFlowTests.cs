using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using ImpresorasService.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Flow;

public sealed class PrintJobsControllerRouteFlowTests
{
    [Fact]
    public async Task Route_WhenPendingAndRuleExists_TransitionsToRoutedAndReturnsOkRouted()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        db.Stores.Add(new Store
        {
            StoreId = 1,
            Name = "Store 1",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var printer = new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            StoreId = 1,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Printers.Add(printer);

        db.RoutingRules.Add(new RoutingRule
        {
            RuleId = 1,
            Priority = 1,
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PrinterId = printer.PrinterId,
            IsActive = true,
            ValidFromUtc = now.AddMinutes(-1),
            ValidToUtc = null,
            CreatedBy = "test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var jobId = Guid.NewGuid();
        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "SAP-TEST",
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
            RowVersion = Array.Empty<byte>()
        });
        await db.SaveChangesAsync();

        var resolver = new RoutingResolver(db);
        var routingService = new RoutingService(db, resolver);
        var controller = new PrintJobsController(db, routingService, TimeProvider.System);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "StoreManager", storeId: "1")
            }
        };

        // Act
        var result = await controller.Route(jobId, CancellationToken.None);

        // Assert response shape
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Routed", GetAnonProp<string>(ok.Value!, "status"));
        Assert.Equal(printer.PrinterId, GetAnonProp<int>(ok.Value!, "printerId"));

        // Las escrituras van por ExecuteUpdate, que no refresca lo ya seguido por este contexto:
        // sin soltarlo, la relectura devolveria la copia en memoria de antes del UPDATE.
        db.ChangeTracker.Clear();
        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Routed, jobAfter.Status);
        Assert.Equal(printer.PrinterId, jobAfter.PrinterId);
        Assert.Equal(0, jobAfter.AttemptCount);
    }

    [Fact]
    public async Task Route_WhenPendingAndNoRule_ReturnsOkErrorFinalRouteNotFoundAndPersistsJob()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        db.Stores.Add(new Store
        {
            StoreId = 1,
            Name = "Store 1",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        db.Printers.Add(new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            StoreId = 1,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var jobId = Guid.NewGuid();
        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "SAP-TEST",
            ExternalJobId = "EXT-2",
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
            RowVersion = Array.Empty<byte>()
        });
        await db.SaveChangesAsync();

        var resolver = new RoutingResolver(db);
        var routingService = new RoutingService(db, resolver);
        var controller = new PrintJobsController(db, routingService, TimeProvider.System);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "StoreManager", storeId: "1")
            }
        };

        // Act
        var result = await controller.Route(jobId, CancellationToken.None);

        // Assert response
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("ErrorFinal", GetAnonProp<string>(ok.Value!, "status"));
        Assert.Equal(RoutingService.RouteNotFoundCode, GetAnonProp<string?>(ok.Value!, "errorCode"));

        // Assert persistence
        // Las escrituras van por ExecuteUpdate, que no refresca lo ya seguido por este contexto:
        // sin soltarlo, la relectura devolveria la copia en memoria de antes del UPDATE.
        db.ChangeTracker.Clear();
        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.ErrorFinal, jobAfter.Status);
        Assert.Null(jobAfter.PrinterId);
        Assert.Equal(RoutingService.RouteNotFoundCode, jobAfter.LastErrorCode);
    }

    [Fact]
    public async Task Route_WhenJobInPrinting_ReturnsBadRequest()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        db.Stores.Add(new Store
        {
            StoreId = 1,
            Name = "Store 1",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        db.Printers.Add(new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            StoreId = 1,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var jobId = Guid.NewGuid();
        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "SAP-TEST",
            ExternalJobId = "EXT-3",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Printing,
            PrinterId = 10,
            AttemptCount = 2,
            NextRetryAtUtc = null,
            LastErrorCode = null,
            LastErrorMessage = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        });
        await db.SaveChangesAsync();

        var resolver = new RoutingResolver(db);
        var routingService = new RoutingService(db, resolver);
        var controller = new PrintJobsController(db, routingService, TimeProvider.System);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "StoreManager", storeId: "1")
            }
        };

        // Act
        var result = await controller.Route(jobId, CancellationToken.None);

        // Assert
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(bad.Value);
        Assert.NotNull(GetAnonProp<string>(bad.Value!, "error"));
    }

    private static ClaimsPrincipal BuildPrincipal(string role, string storeId)
    {
        // Nota: PrintJobsController usa User.IsInRole("Admin") y claim "StoreId".
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

    private static T GetAnonProp<T>(object value, string propName)
    {
        var prop = value.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        Assert.NotNull(prop);
        var raw = prop!.GetValue(value);
        if (raw is null) return default!;
        return (T)raw;
    }
}

