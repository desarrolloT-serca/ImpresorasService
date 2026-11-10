extern alias worker;

using System.Text;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
using ImpresorasService.Infrastructure.Services;
using Xunit;
using Vigilante = worker::ImpresorasService.Worker.SpoolAcceptedWatchdogBackgroundService;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// ERP_SERCA docs/72, 10/11/2026: el trabajo de prueba sin papel. La impresora lo aceptó, se quedó parado,
/// a los 10 min pasó a «Sin confirmar» y, al reponer el papel, salió, pero nadie lo volvió a mirar.
/// </summary>
public sealed class VigilanteTrabajosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 10, 11, 30, 0, TimeSpan.Zero);

    private static Vigilante.WatchdogJob SinConfirmarPorParada() => new()
    {
        JobId = Guid.NewGuid(),
        PrinterId = 1,
        Status = PrintJobStatus.PrintedUnknown,
        UpdatedAtUtc = Ahora.AddMinutes(-5),
        LastErrorCode = Vigilante.StoppedTimeout,
        LastErrorMessage = "Impresora parada más de 10 min: puede salir aún cuando se recupere.",
        IppJobId = 160
    };

    [Fact]
    public void Sin_confirmar_por_parada_se_confirma_cuando_la_impresora_lo_termina()
    {
        var job = SinConfirmarPorParada();

        Assert.True(Vigilante.ApplyOutcome(job, new IppQueryResult(IppOutcome.PrinterIdle), 120, Ahora));

        Assert.Equal(PrintJobStatus.PrintedConfirmed, job.Status);
        Assert.Null(job.LastErrorCode);   // ya no sale en rojo ni con «Cancelar» en la cola
        Assert.Null(job.LastErrorMessage);
    }

    [Fact]
    public void Sin_confirmar_por_parada_pasa_a_error_si_la_impresora_lo_aborta()
    {
        var job = SinConfirmarPorParada();

        Assert.True(Vigilante.ApplyOutcome(job,
            new IppQueryResult(IppOutcome.JobAborted, "IPP_JOB_CANCELED", "El trabajo se canceló en la impresora."), 120, Ahora));

        Assert.Equal(PrintJobStatus.ErrorFinal, job.Status);
        Assert.Equal("IPP_JOB_CANCELED", job.LastErrorCode);
    }

    [Theory]
    [InlineData(IppOutcome.PrinterStopped)]
    [InlineData(IppOutcome.PrinterProcessing)]
    [InlineData(IppOutcome.Unavailable)]
    public void Sin_confirmar_por_parada_se_queda_igual_mientras_siga_sin_salir(IppOutcome outcome)
    {
        var job = SinConfirmarPorParada();
        var antes = job.UpdatedAtUtc;

        Assert.False(Vigilante.ApplyOutcome(job, new IppQueryResult(outcome), 120, Ahora));
        Assert.False(Vigilante.ApplyOutcome(job, null, 120, Ahora));

        Assert.Equal(PrintJobStatus.PrintedUnknown, job.Status);
        Assert.Equal(Vigilante.StoppedTimeout, job.LastErrorCode);
        Assert.Equal(antes, job.UpdatedAtUtc);
    }

    [Fact]
    public void Lo_de_antes_sigue_igual_bloqueado_diez_minutos_pasa_a_sin_confirmar()
    {
        var job = new Vigilante.WatchdogJob
        {
            JobId = Guid.NewGuid(), PrinterId = 1, Status = PrintJobStatus.PrinterBlocked,
            UpdatedAtUtc = Ahora.AddMinutes(-11), IppJobId = 160
        };

        Assert.True(Vigilante.ApplyOutcome(job, new IppQueryResult(IppOutcome.PrinterStopped), 120, Ahora));

        Assert.Equal(PrintJobStatus.PrintedUnknown, job.Status);
        Assert.Equal(Vigilante.StoppedTimeout, job.LastErrorCode);
    }

    [Fact]
    public void El_trabajo_parado_lleva_el_motivo_de_la_impresora()
    {
        var porTrabajo = new IppQueryResult(IppOutcome.PrinterStopped, "IPP_JOB_STOPPED", "La impresora tiene el trabajo detenido.");
        var impresora = new IppQueryResult(IppOutcome.PrinterStopped, "media-empty-error", "Sin papel en la bandeja.");

        var r = Vigilante.ConMotivoDeLaImpresora(porTrabajo, impresora)!;

        Assert.Equal(("media-empty-error", "Sin papel en la bandeja."), (r.ErrorCode, r.ErrorMessage));
        // Sin respuesta por el trabajo, vale la de la impresora (la decide el que llama).
        Assert.Null(Vigilante.ConMotivoDeLaImpresora(null, impresora));
        // Si la impresora no dice por qué, se queda el del trabajo.
        Assert.Equal(porTrabajo, Vigilante.ConMotivoDeLaImpresora(porTrabajo, new IppQueryResult(IppOutcome.PrinterStopped)));
    }

    // 10/11/2026: 5 trabajos a la Brother sin papel. El primero se paró; los otros 4, «en espera» detrás,
    // seguían como aceptados hasta 30 min. Con la impresora parada, también están parados.
    [Fact]
    public void Los_que_esperan_detras_de_una_impresora_parada_estan_parados()
    {
        var enEspera = new IppQueryResult(IppOutcome.PrinterProcessing);
        var impresora = new IppQueryResult(IppOutcome.PrinterStopped, "media-empty-error", "Sin papel en la bandeja.");

        var r = Vigilante.ConMotivoDeLaImpresora(enEspera, impresora)!;
        Assert.Equal((IppOutcome.PrinterStopped, "media-empty-error", "Sin papel en la bandeja."), (r.Outcome, r.ErrorCode, r.ErrorMessage));

        var job = new Vigilante.WatchdogJob
        {
            JobId = Guid.NewGuid(), PrinterId = 1, Status = PrintJobStatus.SpoolAccepted,
            UpdatedAtUtc = Ahora.AddSeconds(-20), IppJobId = 163, PorTrabajo = true
        };
        Assert.True(Vigilante.ApplyOutcome(job, r, 120, Ahora));
        Assert.Equal(PrintJobStatus.PrinterBlocked, job.Status);
        Assert.Equal("media-empty-error", job.LastErrorCode);

        // Con la impresora libre, «en espera» es solo eso.
        Assert.Same(enEspera, Vigilante.ConMotivoDeLaImpresora(enEspera, new IppQueryResult(IppOutcome.PrinterIdle)));
    }

    // Primera mirada a los 15 s: antes del plazo de 2 min solo cuenta lo que la impresora afirma.
    [Theory]
    [InlineData(IppOutcome.PrinterStopped, true, PrintJobStatus.PrinterBlocked)]
    [InlineData(IppOutcome.JobAborted, true, PrintJobStatus.ErrorFinal)]
    [InlineData(IppOutcome.PrinterIdle, true, PrintJobStatus.PrintedConfirmed)]   // terminado, por su número
    [InlineData(IppOutcome.PrinterIdle, false, PrintJobStatus.SpoolAccepted)]     // libre sin el trabajo: al plazo
    [InlineData(IppOutcome.PrinterProcessing, true, PrintJobStatus.SpoolAccepted)]
    [InlineData(IppOutcome.Unavailable, false, PrintJobStatus.SpoolAccepted)]     // sin respuesta: al plazo
    public void Antes_del_plazo_solo_cuenta_lo_que_la_impresora_afirma(IppOutcome outcome, bool porTrabajo, PrintJobStatus esperado)
    {
        var job = new Vigilante.WatchdogJob
        {
            JobId = Guid.NewGuid(), PrinterId = 1, Status = PrintJobStatus.SpoolAccepted,
            UpdatedAtUtc = Ahora.AddSeconds(-20), IppJobId = 162, PorTrabajo = porTrabajo
        };

        Vigilante.ApplyOutcome(job, new IppQueryResult(outcome), 120, Ahora);

        Assert.Equal(esperado, job.Status);
    }

    [Fact]
    public void Sin_respuesta_al_plazo_sigue_pasando_a_sin_confirmar_como_antes()
    {
        var job = new Vigilante.WatchdogJob
        {
            JobId = Guid.NewGuid(), PrinterId = 1, Status = PrintJobStatus.SpoolAccepted,
            UpdatedAtUtc = Ahora.AddSeconds(-130), IppJobId = 162
        };

        Assert.True(Vigilante.ApplyOutcome(job, null, 120, Ahora));
        Assert.Equal(PrintJobStatus.PrintedUnknown, job.Status);
    }

    [Fact]
    public void El_evento_dice_como_se_confirmo()
    {
        var job = SinConfirmarPorParada();
        Vigilante.ApplyOutcome(job, new IppQueryResult(IppOutcome.PrinterIdle), 120, Ahora);

        job.PorTrabajo = false;
        Assert.Contains("estado de la impresora", Vigilante.BuildEvent(job, PrintJobStatus.PrintedUnknown, Ahora).Message);
        job.PorTrabajo = true;
        Assert.Contains("por su número", Vigilante.BuildEvent(job, PrintJobStatus.PrintedUnknown, Ahora).Message);
    }

    // Lo que contestó la Brother por el trabajo 160 parado sin papel: job-state 6 con motivos de fase.
    [Fact]
    public void Los_motivos_de_fase_del_trabajo_no_salen_como_errores()
    {
        var r = IppConfirmationService.ParseResponse(200, Respuesta(
            (0x23, "job-state", [0, 0, 0, 6]),
            (0x44, "job-state-reasons", Encoding.UTF8.GetBytes("job-incoming")),
            (0x44, "", Encoding.UTF8.GetBytes("job-printing"))));

        var estado = IppConfirmationService.EstadoDelTrabajo(r)!;

        Assert.Equal(IppOutcome.PrinterStopped, estado.Outcome);
        Assert.DoesNotContain("Error de impresora", estado.ErrorMessage);
        Assert.DoesNotContain("job-", estado.ErrorMessage);
    }

    private static byte[] Respuesta(params (byte Tag, string Nombre, byte[] Valor)[] atributos)
    {
        var ms = new MemoryStream();
        ms.Write([0x01, 0x01, 0x00, 0x00, 0, 0, 0, 1, 0x02]); // job-attributes
        foreach (var (tag, nombre, valor) in atributos)
        {
            var n = Encoding.UTF8.GetBytes(nombre);
            ms.WriteByte(tag);
            ms.Write([(byte)(n.Length >> 8), (byte)n.Length]); ms.Write(n);
            ms.Write([(byte)(valor.Length >> 8), (byte)valor.Length]); ms.Write(valor);
        }
        ms.WriteByte(0x03);
        return ms.ToArray();
    }
}
