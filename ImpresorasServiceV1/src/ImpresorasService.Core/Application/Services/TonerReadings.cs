using System.Text.Json;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain.Entities;

namespace ImpresorasService.Application.Services;

/// <summary>
/// Historial del tóner y previsión de cuándo llega al umbral crítico (ERP_SERCA docs/72, mejora 7).
/// Solo se guarda una lectura cuando cambia el nivel de un consumible: unas pocas filas por cartucho.
/// </summary>
public static class TonerReadings
{
    /// <summary>Lo que se guarda: un año largo, para ver cuánto dura un cartucho.</summary>
    public static readonly TimeSpan Retencion = TimeSpan.FromDays(400);

    // ponytail: hace falta al menos este tramo bajando para prever; con menos, una lectura suelta
    // (la impresora redondea el %) daría fechas absurdas.
    private static readonly TimeSpan TramoMinimo = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Lecturas nuevas: los consumibles cuyo % cambia respecto a lo que había guardado.</summary>
    public static List<TonerReading> Nuevas(int printerId, string? anteriorJson, IppEstadoImpresora estado, DateTimeOffset ahora)
    {
        var antes = anteriorJson is null ? [] : JsonSerializer.Deserialize<List<IppConsumible>>(anteriorJson, Json) ?? [];
        return estado.Consumibles
            .Select((c, i) => (c.Nivel, i))
            .Where(x => x.Nivel is >= 0 and <= 100 && antes.ElementAtOrDefault(x.i)?.Nivel != x.Nivel)
            .Select(x => new TonerReading { PrinterId = printerId, Marker = x.i, Level = x.Nivel!.Value, ReadAtUtc = ahora })
            .ToList();
    }

    /// <summary>
    /// Días que faltan para llegar a <paramref name="umbral"/> (%), por recta de mínimos cuadrados sobre las
    /// lecturas desde el último cambio de cartucho (cuando el nivel sube). 0 si ya está por debajo; null si
    /// no se puede saber: pocas lecturas, menos de un día bajando o no baja.
    /// </summary>
    public static int? DiasHasta(IReadOnlyList<(DateTimeOffset At, int Level)> lecturas, int umbral, DateTimeOffset ahora)
    {
        if (lecturas.Count == 0) return null;
        if (lecturas[^1].Level <= umbral) return 0;

        var desde = 0;
        for (var i = 1; i < lecturas.Count; i++)
            if (lecturas[i].Level > lecturas[i - 1].Level) desde = i; // cartucho nuevo
        var tramo = lecturas.Skip(desde).ToList();
        if (tramo.Count < 2 || tramo[^1].At - tramo[0].At < TramoMinimo) return null;

        var t0 = tramo[0].At;
        var xs = tramo.Select(l => (l.At - t0).TotalDays).ToList();
        var ys = tramo.Select(l => (double)l.Level).ToList();
        var mx = xs.Average();
        var my = ys.Average();
        var den = xs.Sum(x => (x - mx) * (x - mx));
        if (den <= 0) return null;
        var pendiente = xs.Zip(ys, (x, y) => (x - mx) * (y - my)).Sum() / den; // % por día
        if (pendiente >= 0) return null;

        // Desde el último nivel leído, al ritmo de la recta; descontado lo que ya ha pasado desde entonces.
        var dias = (lecturas[^1].Level - umbral) / -pendiente - (ahora - lecturas[^1].At).TotalDays;
        return Math.Max(0, (int)Math.Round(dias));
    }
}
