using System.Net.Http.Json;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

/// <summary>El listado de la cola manda el error para la gente (ErroresTrabajo), además del código.</summary>
public sealed class ColaErroresTests : IntegrationTestBase
{
    public ColaErroresTests(ApiWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Cada_trabajo_lleva_el_texto_y_que_hacer_de_su_error()
    {
        await Factory.ResetDatabaseAsync();
        var conCodigo = await SembrarAsync("ROUTE_NOT_FOUND", PrintJobStatus.ErrorFinal);
        var conMotivoIpp = await SembrarAsync("media-jam-error", PrintJobStatus.PrinterBlocked);
        var sinError = await SembrarAsync(null, PrintJobStatus.PrintedConfirmed);

        var r = await Client.GetFromJsonAsync<Pagina>("/api/printjobs?includeTotal=true&limit=50");

        var porId = r!.Value.ToDictionary(t => t.JobId);
        Assert.Equal("Sin impresora asignada", porId[conCodigo].LastErrorText);
        Assert.Contains("reglas de enrutado", porId[conCodigo].LastErrorHint);
        Assert.Equal("Atasco de papel", porId[conMotivoIpp].LastErrorText);
        Assert.Null(porId[sinError].LastErrorText);
    }

    private async Task<Guid> SembrarAsync(string? codigo, PrintJobStatus estado)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var ahora = DateTimeOffset.UtcNow;
        var job = new PrintJob
        {
            JobId = Guid.NewGuid(), SourceSystem = "TEST", ExternalJobId = $"E-{Guid.NewGuid():N}"[..20], StoreId = 1,
            DocumentType = "A", Channel = "DEFAULT", PdfSha256 = "x", Status = estado, LastErrorCode = codigo,
            CorrelationId = Guid.NewGuid(), CreatedAtUtc = ahora, UpdatedAtUtc = ahora
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();
        return job.JobId;
    }

    private sealed record Pagina(List<Trabajo> Value);
    private sealed record Trabajo(Guid JobId, string? LastErrorText, string? LastErrorHint);
}
