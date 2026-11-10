using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Páginas impresas por impresora (ERP_SERCA docs/73, opción E). El contador de la impresora es acumulado:
/// a las 00:00 de cada día se guarda su valor y las páginas de un día son la lectura del día siguiente menos
/// la suya. El hosting está en el Worker (PageCounterBackgroundService); aquí, qué se lee y cómo se cuenta.
/// </summary>
public static class ContadorPaginas
{
    // ponytail: margen en el que se busca la lectura anterior al rango o la siguiente cuando falta la del
    // extremo. Un hueco más largo deja ese extremo sin cubrir (la pantalla lo avisa: el periodo empieza o
    // acaba en otro día).
    private const int MargenDias = 62;

    /// <summary>Activas con IP que aún no tienen la lectura de <paramref name="dia"/>.</summary>
    public static Task<List<Printer>> PendientesAsync(ImpresorasDbContext db, DateOnly dia, CancellationToken ct) =>
        db.Printers.AsNoTracking()
            .Where(p => p.IsActive && p.Host != null && p.Host != ""
                        && !db.PageCounterReadings.Any(r => r.PrinterId == p.PrinterId && r.BusinessDay == dia))
            .OrderBy(p => p.PrinterId)
            .ToListAsync(ct);

    /// <summary>
    /// Lee y guarda el contador de las que faltan en <paramref name="dia"/>; si una no responde, lo intenta
    /// otra vez al momento. Devuelve las que siguen sin lectura (la siguiente pasada lo vuelve a intentar).
    /// </summary>
    public static async Task<List<Printer>> TomarAsync(
        ImpresorasDbContext db, ILectorContador lector, DateOnly dia, TimeProvider reloj, CancellationToken ct)
    {
        var sinLectura = new List<Printer>();
        foreach (var impresora in await PendientesAsync(db, dia, ct))
        {
            var lectura = await lector.LeerAsync(impresora, ct) ?? await lector.LeerAsync(impresora, ct);
            if (lectura is null)
            {
                sinLectura.Add(impresora);
                continue;
            }
            db.PageCounterReadings.Add(new PageCounterReading
            {
                PrinterId = impresora.PrinterId,
                BusinessDay = dia,
                TotalPages = lectura.Total,
                Source = lectura.Origen,
                TonerDaysLeft = lectura.DiasToner,
                ReadAtUtc = reloj.GetUtcNow()
            });
            await db.SaveChangesAsync(ct);
        }
        return sinLectura;
    }

    /// <summary>Lecturas de esas impresoras que pueden cubrir del <paramref name="desde"/> al <paramref name="hasta"/>.</summary>
    public static async Task<ILookup<int, PageCounterReading>> LecturasAsync(
        ImpresorasDbContext db, IReadOnlyCollection<int> impresoras, DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var inicio = desde.AddDays(-MargenDias);
        var fin = hasta.AddDays(1 + MargenDias);
        var lecturas = await db.PageCounterReadings.AsNoTracking()
            .Where(r => impresoras.Contains(r.PrinterId) && r.BusinessDay >= inicio && r.BusinessDay <= fin)
            .ToListAsync(ct);
        return lecturas.ToLookup(r => r.PrinterId);
    }

    /// <summary>
    /// Periodos entre lecturas consecutivas que cubren del <paramref name="desde"/> al <paramref name="hasta"/>,
    /// ambos incluidos: las lecturas de esos días y la del día siguiente al último. Si falta la de un extremo,
    /// se toma la anterior al primero o la siguiente al último, y ese periodo abarca más días (se ve en
    /// <see cref="Periodo.Desde"/> y <see cref="Periodo.Hasta"/>). Si el contador baja (impresora cambiada o
    /// reiniciada), las páginas de ese periodo no se saben.
    /// </summary>
    public static List<Periodo> Periodos(IEnumerable<PageCounterReading> lecturas, DateOnly desde, DateOnly hasta)
    {
        var fin = hasta.AddDays(1);
        var todas = lecturas.OrderBy(r => r.BusinessDay).ToList();
        var dentro = todas.Where(r => r.BusinessDay >= desde && r.BusinessDay <= fin).ToList();
        if (dentro.FirstOrDefault()?.BusinessDay != desde && todas.LastOrDefault(r => r.BusinessDay < desde) is { } antes)
            dentro.Insert(0, antes);
        if (dentro.LastOrDefault()?.BusinessDay != fin && todas.FirstOrDefault(r => r.BusinessDay > fin) is { } despues)
            dentro.Add(despues);
        return dentro.Zip(dentro.Skip(1), (a, b) => new Periodo(
                a.BusinessDay, b.BusinessDay,
                b.TotalPages >= a.TotalPages ? b.TotalPages - a.TotalPages : null,
                a.ReadAtUtc, b.ReadAtUtc))
            .ToList();
    }

    /// <summary>
    /// Páginas de <see cref="Desde"/> (incluido) a <see cref="Hasta"/> (excluido): entre la lectura de las
    /// 00:00 de un día y la del otro. null si el contador bajó.
    /// </summary>
    public sealed record Periodo(DateOnly Desde, DateOnly Hasta, long? Paginas, DateTimeOffset LeidaDesdeUtc, DateTimeOffset LeidaHastaUtc);
}
