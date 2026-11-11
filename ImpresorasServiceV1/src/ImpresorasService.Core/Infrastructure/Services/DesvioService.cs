using System.Text.Json;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Connectivity;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Desvío a la impresora de reserva (ERP_SERCA docs/75). Aquí están las definiciones comunes (§4) y la
/// tabla de decisión (§5): la API (botón manual, F2) y, desde F3, el Worker deciden con lo mismo.
/// </summary>
public sealed class DesvioService
{
    public const string DivertedEvent = "DIVERTED";

    /// <summary>Lo leído por el monitor hace más que esto no vale para decidir (como la guarda de envío).</summary>
    public static readonly TimeSpan DatoFresco = TimeSpan.FromSeconds(90);

    private readonly ImpresorasDbContext _db;
    private readonly IDashboardThresholdRuleStore _reglas;
    private readonly TimeProvider _reloj;
    private readonly IIppConfirmationService? _ipp;

    public DesvioService(ImpresorasDbContext db, IDashboardThresholdRuleStore reglas, TimeProvider reloj,
        IIppConfirmationService? ipp = null)
    {
        _db = db;
        _reglas = reglas;
        _reloj = reloj;
        _ipp = ipp;
    }

    public enum Disponibilidad { Puede, NoPuede, SinDato }

    /// <summary>
    /// Si la impresora puede imprimir ahora (§4). No puede: sin conexión (no «sin host») o parada por la
    /// propia impresora con un motivo <c>-error</c>. Ocupada o con avisos, sí puede. Sin dato fresco del
    /// monitor no se sabe.
    /// </summary>
    public static (Disponibilidad Estado, string Motivo, DateTimeOffset? Desde) Estado(Printer p, DateTimeOffset ahora)
    {
        if (!p.IsActive)
            return (Disponibilidad.NoPuede, "está desactivada", null);
        if (p.LastConnectionOk is null || p.LastConnectionCheckAtUtc is not { } leida || ahora - leida > DatoFresco)
            return (Disponibilidad.SinDato, "no hay un dato reciente suyo", null);
        if (p.LastConnectionOk == false && !PrinterConnectivityState.IsHostNotConfiguredError(p.LastConnectionError))
            return (Disponibilidad.NoPuede, "está sin conexión", p.ConnectionFailingSinceUtc);
        var ippFresca = p.IppCheckedAtUtc is { } ipp && ahora - ipp <= DatoFresco;
        var conError = (p.IppStateReasons ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(m => m.EndsWith("-error", StringComparison.OrdinalIgnoreCase));
        if (ippFresca && p.IppState == 5 && conError)
        {
            var texto = IppEstadoImpresora.Describir(p, ahora, UmbralToner.Default).Texto;
            return (Disponibilidad.NoPuede,
                string.IsNullOrWhiteSpace(texto) ? "está parada" : $"está parada: {texto.TrimEnd('.')}", p.IppStateSinceUtc);
        }
        return (Disponibilidad.Puede, "puede imprimir", null);
    }

    /// <summary>El estado en dos o tres palabras, para las tarjetas de la ventana: «Sin conexión», «Lista»…</summary>
    public static string Corto(Disponibilidad estado, string motivo) =>
        estado == Disponibilidad.Puede ? "Lista"
        : estado == Disponibilidad.SinDato ? "Sin dato reciente"
        : motivo.StartsWith("está ", StringComparison.Ordinal) ? char.ToUpperInvariant(motivo[5]) + motivo[6..]
        : motivo;

    /// <summary>
    /// Lo que se ha decidido de un trabajo (§5): si se puede, a qué impresora y, si no, por qué. Si se puede,
    /// también cómo está cada una, para la ventana (origen → destino): estado corto y desde cuándo.
    /// </summary>
    public sealed record Evaluacion(bool Posible, string Motivo, int? ReservaId, string? ReservaNombre, bool YaEnviado)
    {
        public string? OriginalNombre { get; init; }
        public string? OriginalEstado { get; init; }
        public DateTimeOffset? OriginalDesde { get; init; }
        public string? ReservaEstado { get; init; }
    }

    // Ya en la impresora: se cancela allí antes de desviar.
    private static readonly PrintJobStatus[] Enviados = [PrintJobStatus.SpoolAccepted, PrintJobStatus.PrinterBlocked];
    // Aún en IS: se puede mandar a otra sin cancelar nada.
    private static readonly PrintJobStatus[] SinEnviar = [PrintJobStatus.Routed, PrintJobStatus.RetryScheduled];
    // Un error final se desvía solo si fue por la impresora; por la regla o el PDF, no arregla nada.
    private static readonly string[] ErroresDeImpresora = ["PRINTER_UNREACHABLE", "IPP_PRINTER_BUSY", "IPP_NO_RESPONSE"];

    /// <summary>La tabla de decisión de docs/75 §5, para un trabajo con su impresora y su reserva leídas.</summary>
    public static Evaluacion Evaluar(PrintJob job, Printer? original, Printer? reserva, ReglaDesvio regla, DateTimeOffset ahora)
    {
        Evaluacion No(string motivo) => new(false, motivo, reserva?.PrinterId, reserva?.PrinterName, false);

        if (regla.Modo == ReglaDesvio.Desactivado)
            return No("El desvío a la reserva está desactivado en Reglas y avisos.");
        if (job.DivertedFromPrinterId is not null)
            return No("Ya se desvió una vez: no se vuelve a desviar desde la reserva.");
        if (job.Status is PrintJobStatus.PrintedConfirmed or PrintJobStatus.PrintedUnknown)
            return No("Puede haber salido ya: si hace falta otra copia, se reimprime.");
        var yaEnviado = Enviados.Contains(job.Status);
        var desviable = yaEnviado || SinEnviar.Contains(job.Status)
            || (job.Status == PrintJobStatus.ErrorFinal && ErroresDeImpresora.Contains(job.LastErrorCode));
        if (!desviable)
            return No(job.Status == PrintJobStatus.Printing
                ? "Se está enviando a la impresora ahora mismo."
                : "En este estado no se puede desviar.");
        if (original is null || job.PrinterId != original.PrinterId)
            return No("No tiene impresora asignada.");
        if (original.BackupPrinterId is null)
            return No($"«{original.PrinterName}» no tiene impresora de reserva: se pone en su ficha.");
        if (reserva is null || reserva.PrinterId != original.BackupPrinterId)
            return No("No se encuentra su impresora de reserva.");
        if (reserva.StoreId != original.StoreId || reserva.StoreId != job.StoreId)
            return No($"La reserva «{reserva.PrinterName}» es de otra tienda.");

        var (estadoOriginal, motivoOriginal, desdeOriginal) = Estado(original, ahora);
        if (estadoOriginal == Disponibilidad.SinDato)
            return No($"De «{original.PrinterName}» {motivoOriginal}: no se sabe si puede imprimir.");
        if (estadoOriginal == Disponibilidad.Puede)
            return No($"«{original.PrinterName}» puede imprimir: no hace falta desviarlo.");
        var (estadoReserva, motivoReserva, _) = Estado(reserva, ahora);
        if (estadoReserva != Disponibilidad.Puede)
            return No($"La reserva «{reserva.PrinterName}» tampoco sirve: {motivoReserva}.");

        return new Evaluacion(true, $"«{original.PrinterName}» {motivoOriginal}.", reserva.PrinterId, reserva.PrinterName, yaEnviado)
        {
            OriginalNombre = original.PrinterName,
            OriginalEstado = Corto(estadoOriginal, motivoOriginal),
            OriginalDesde = desdeOriginal,
            ReservaEstado = Corto(estadoReserva, motivoReserva)
        };
    }

    /// <summary>Evalúa un trabajo leyendo lo que hace falta (sin el PDF).</summary>
    public async Task<Evaluacion> EvaluarAsync(Guid jobId, CancellationToken ct)
    {
        var (_, evaluacion) = await LeerAsync(jobId, ct);
        return evaluacion;
    }

    private async Task<(Leido Leido, Evaluacion Evaluacion)> LeerAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.PrintJobs.AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new PrintJob
            {
                JobId = j.JobId, Status = j.Status, PrinterId = j.PrinterId, StoreId = j.StoreId, IppJobId = j.IppJobId,
                DocumentType = j.DocumentType, ExternalJobId = j.ExternalJobId, LastErrorCode = j.LastErrorCode,
                DivertedFromPrinterId = j.DivertedFromPrinterId
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Job {jobId} no encontrado.");
        var original = job.PrinterId is null
            ? null
            : await _db.Printers.AsNoTracking().FirstOrDefaultAsync(p => p.PrinterId == job.PrinterId, ct);
        var reserva = original?.BackupPrinterId is null
            ? null
            : await _db.Printers.AsNoTracking().FirstOrDefaultAsync(p => p.PrinterId == original.BackupPrinterId, ct);
        var regla = (await _reglas.LoadAsync(ct)).Desvio;
        return (new Leido(job, original, reserva), Evaluar(job, original, reserva, regla, _reloj.GetUtcNow()));
    }

    private sealed record Leido(PrintJob Job, Printer? Original, Printer? Reserva);

    /// <summary>
    /// Qué ha pasado al pedir el desvío. Codigo: «Desviado»; «YaSalio» (la original dice que lo imprimió:
    /// queda impreso y no se desvía); o «Incierta» (no se sabe si la original lo soltará: hace falta
    /// aceptar el duplicado).
    /// </summary>
    public sealed record Resultado(string Codigo, string Mensaje, int? ReservaId);

    /// <summary>
    /// Desvía el trabajo a la reserva (docs/75 F2, §5). Si ya está en la original, primero lo cancela allí:
    /// solo con cancelación segura se sigue sin más (P1). No pasa por las reglas de enrutado, que lo
    /// devolverían a la original. Lanza InvalidOperationException si la tabla no lo permite y
    /// PrintJobStateConflictException si el estado cambió entre medias.
    /// </summary>
    public async Task<Resultado> DesviarAsync(Guid jobId, string? actor, bool aceptaDuplicado, CancellationToken ct,
        string actorType = "user")
    {
        var (leido, evaluacion) = await LeerAsync(jobId, ct);
        if (!evaluacion.Posible)
            throw new InvalidOperationException(evaluacion.Motivo);
        var (job, original, reserva) = (leido.Job, leido.Original!, leido.Reserva!);
        if (!await _db.PrintJobs.AnyAsync(j => j.JobId == jobId && j.PdfBlob != null, ct))
            throw new InvalidOperationException("Este trabajo ya no tiene el PDF: el ERP tiene que volver a mandar el documento.");

        string cancelacion = "NoEnviado";
        if (evaluacion.YaEnviado)
        {
            var resultado = job.IppJobId is null || _ipp is null || string.IsNullOrWhiteSpace(original.Host)
                ? (CancelacionEnImpresora?)null
                : await _ipp.CancelJobAsync(original.Host, job.IppJobId.Value,
                    IIppConfirmationService.JobNameFor(job.DocumentType, job.ExternalJobId), CancellationToken.None);
            cancelacion = resultado?.ToString() ?? "SinNumero";
            if (resultado == CancelacionEnImpresora.YaSalio)
                return await MarcarImpresoAsync(job, original, actor, actorType);
            var segura = resultado is CancelacionEnImpresora.Cancelado or CancelacionEnImpresora.YaNoSaldra;
            if (!segura && !aceptaDuplicado)
                return new Resultado("Incierta",
                    $"No se ha podido confirmar que «{original.PrinterName}» no lo imprimirá ({TextoCancelacion(resultado)}): " +
                    "si se desvía, puede salir dos veces.", reserva.PrinterId);
        }

        var ahora = _reloj.GetUtcNow();
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var cambiado = await _db.PrintJobs
            .Where(j => j.JobId == jobId && j.Status == job.Status && j.PrinterId == original.PrinterId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, PrintJobStatus.Routed)
                .SetProperty(j => j.PrinterId, reserva.PrinterId)
                .SetProperty(j => j.DivertedFromPrinterId, original.PrinterId)
                .SetProperty(j => j.IppJobId, (int?)null)
                .SetProperty(j => j.AttemptCount, 0)
                .SetProperty(j => j.NextRetryAtUtc, (DateTimeOffset?)null)
                .SetProperty(j => j.LastErrorCode, (string?)null)
                .SetProperty(j => j.LastErrorMessage, (string?)null)
                .SetProperty(j => j.UpdatedAtUtc, ahora)
                .SetProperty(j => j.LastRequeuedAtUtc, ahora), ct);
        if (cambiado != 1)
            throw new PrintJobStateConflictException(
                $"El job {jobId} cambió de estado mientras se desviaba; vuelva a consultarlo.");

        var duplicado = evaluacion.YaEnviado && cancelacion is not ("Cancelado" or "YaNoSaldra");
        _db.PrintJobEvents.Add(new PrintJobEvent
        {
            JobId = jobId,
            EventType = DivertedEvent,
            OldStatus = job.Status,
            NewStatus = PrintJobStatus.Routed,
            ActorType = actorType,
            ActorId = actor,
            // Lo que enseña la cola al pasar el ratón por «reserva»: de dónde venía, por qué y adónde fue.
            Message = $"Iba a «{original.PrinterName}» ({evaluacion.OriginalEstado?.ToLowerInvariant()}): " +
                      $"enviado a su reserva «{reserva.PrinterName}»." +
                      (duplicado ? " Aceptado que puede salir dos veces." : ""),
            MetadataJson = JsonSerializer.Serialize(new
            {
                from = original.PrinterId,
                to = reserva.PrinterId,
                motivo = evaluacion.Motivo,
                cancelacion,
                aceptaDuplicado = duplicado
            }),
            OccurredAtUtc = ahora
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new Resultado("Desviado", $"Enviado a «{reserva.PrinterName}».", reserva.PrinterId);
    }

    // La original dice que ya lo imprimió: queda impreso, con su evento, y no se desvía (§5, fila 4).
    private async Task<Resultado> MarcarImpresoAsync(PrintJob job, Printer original, string? actor, string actorType)
    {
        var ahora = _reloj.GetUtcNow();
        var cambiado = await _db.PrintJobs
            .Where(j => j.JobId == job.JobId && j.Status == job.Status)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, PrintJobStatus.PrintedConfirmed)
                .SetProperty(j => j.LastErrorCode, (string?)null)
                .SetProperty(j => j.LastErrorMessage, (string?)null)
                .SetProperty(j => j.UpdatedAtUtc, ahora), CancellationToken.None);
        if (cambiado == 1)
        {
            _db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = job.JobId,
                EventType = "PRINTED_BEFORE_DIVERT",
                OldStatus = job.Status,
                NewStatus = PrintJobStatus.PrintedConfirmed,
                ActorType = actorType,
                ActorId = actor,
                Message = $"Al ir a desviarlo, «{original.PrinterName}» dice que ya lo había impreso: no se desvía.",
                OccurredAtUtc = ahora
            });
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        return new Resultado("YaSalio", $"«{original.PrinterName}» ya lo había impreso: no se ha desviado.", null);
    }

    private static string TextoCancelacion(CancelacionEnImpresora? r) => r switch
    {
        null => "no tiene número de trabajo en la impresora",
        CancelacionEnImpresora.NoLoTiene => "ya no lo tiene en su cola, y pudo salir antes",
        CancelacionEnImpresora.NoEsNuestro => "ese número es ahora otro documento",
        CancelacionEnImpresora.SinNombre => "no dice el nombre del trabajo",
        CancelacionEnImpresora.NoDeja => "no deja cancelarlo por la red",
        _ => "no responde"
    };
}
