using System.Linq.Expressions;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;

namespace ImpresorasService.Application.Services;

/// <summary>
/// Predicados de dominio compartidos entre la Api (dashboard) y el Worker (alertas de tienda) para
/// que "fallo sin reenvío" signifique exactamente lo mismo en ambos — antes vivía solo en la Api y
/// el Worker mantenía su propia copia (<c>IsFailedAfterRetry</c>) que había divergido silenciosamente
/// (excluía <see cref="PrintJobStatus.Printing"/> con reintentos, la Api sí lo cuenta).
/// </summary>
public static class DashboardPrintJobPredicates
{
    /// <remarks>
    /// <see cref="PrintJobStatus.Cancelled"/> queda FUERA: cancelar es una decisión explícita del
    /// operador que cierra el trabajo, así que un cancelado tras agotar reintentos no está pendiente
    /// de reenvío. Mientras contaba, inflaba el KPI de forma permanente (nunca se resuelve solo),
    /// degradaba la salud de la tienda y mantenía viva la alerta de Telegram por trabajos ya cerrados.
    /// </remarks>
    public static readonly Expression<Func<PrintJob, bool>> FailedWithoutRetryCurrent =
        x => x.Status == PrintJobStatus.ErrorFinal
             || ((x.Status == PrintJobStatus.Pending
                  || x.Status == PrintJobStatus.Routed
                  || x.Status == PrintJobStatus.Printing
                  || x.Status == PrintJobStatus.PrinterBlocked)
                 && x.AttemptCount > 1);

    /// <summary>
    /// Estados que cuentan como "impreso" para el KPI `printed` (dashboard). Antes vivía como
    /// copia privada en DashboardController; centralizado aquí para que cualquier futuro
    /// consumidor (Worker, otro controller) no tenga que mantener una segunda copia sincronizada —
    /// A-ARCH-03, docs/auditoria-integral-2026-07-21.md.
    /// </summary>
    public static readonly PrintJobStatus[] PrintedStatuses =
    [
        PrintJobStatus.SpoolAccepted,
        PrintJobStatus.PrintedConfirmed,
        PrintJobStatus.PrintedUnknown
    ];

    /// <summary>
    /// Estados que cuentan como "en cola" (queueCurrent). Antes vivía duplicado, con comentario
    /// "debe mantenerse idéntico", en DashboardController y StoreHealthAlertBackgroundService —
    /// A-ARCH-03, docs/auditoria-integral-2026-07-21.md.
    /// </summary>
    public static readonly PrintJobStatus[] QueueStatuses =
    [
        PrintJobStatus.Pending,
        PrintJobStatus.Routed,
        PrintJobStatus.Printing,
        PrintJobStatus.RetryScheduled
    ];
}
