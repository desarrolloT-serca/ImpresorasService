using ImpresorasService.Domain.Entities;

namespace ImpresorasService.Application.Abstractions;

/// <summary>
/// Contador de páginas acumulado de una impresora y de dónde sale. <see cref="DiasToner"/>: los días que la
/// propia impresora estima que le quedan al tóner hasta agotarse (solo las Canon, por su web; más de un año
/// = <see cref="MasDeUnAno"/>); null si no lo da.
/// </summary>
public sealed record LecturaContador(long Total, string Origen, int? DiasToner = null)
{
    public const int MasDeUnAno = 366;
}

/// <summary>
/// Lee el contador de páginas acumulado de una impresora (ERP_SERCA docs/73, opción E). Interfaz para
/// poder probar las lecturas diarias sin impresoras.
/// </summary>
public interface ILectorContador
{
    /// <summary>null si la impresora no responde o no da contador.</summary>
    Task<LecturaContador?> LeerAsync(Printer impresora, CancellationToken ct);
}
