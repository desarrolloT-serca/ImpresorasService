using ImpresorasService.Application.Services;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// KPI-P2-004 (docs/prompt-roadmap-kpi-dashboard.md, Fase 3): dashboard y alertas de tienda deben
/// compartir el mismo reloj de negocio. Ambos llaman a <see cref="BusinessTimeZoneClock.TodayStartUtc"/>
/// — probar la función compartida cubre a los dos llamadores (DashboardController y
/// StoreHealthAlertBackgroundService) sin necesitar levantar el host del Worker.
/// </summary>
public sealed class BusinessTimeZoneClockTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    [Fact]
    public void TodayStartUtc_WinterCet_ReturnsMidnightMadridNotMidnightUtc()
    {
        // 2026-01-15 00:30 UTC = 01:30 CET (Madrid, UTC+1 en invierno). El "hoy" de negocio ya
        // empezó, pero la medianoche UTC (el bug: now.Date) daría 2026-01-15T00:00:00Z, un resultado
        // distinto. La medianoche Madrid real es 2026-01-14T23:00:00Z.
        var nowUtc = new DateTimeOffset(2026, 1, 15, 0, 30, 0, TimeSpan.Zero);

        var result = BusinessTimeZoneClock.TodayStartUtc(nowUtc, Madrid);

        Assert.Equal(new DateTimeOffset(2026, 1, 14, 23, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void TodayStartUtc_SummerCest_UsesTwoHourOffset()
    {
        // 2026-07-15 00:30 UTC = 02:30 CEST (verano, UTC+2). Medianoche Madrid = 2026-07-14T22:00:00Z.
        var nowUtc = new DateTimeOffset(2026, 7, 15, 0, 30, 0, TimeSpan.Zero);

        var result = BusinessTimeZoneClock.TodayStartUtc(nowUtc, Madrid);

        Assert.Equal(new DateTimeOffset(2026, 7, 14, 22, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void TodayStartUtc_JustBeforeMadridMidnight_BelongsToPreviousBusinessDay()
    {
        // 2026-01-20 23:58 CET = 22:58Z, un job "de ayer" en Madrid aunque falten pocos minutos
        // para la medianoche UTC del mismo día calendario.
        var nowUtc = new DateTimeOffset(2026, 1, 20, 22, 58, 0, TimeSpan.Zero);

        var result = BusinessTimeZoneClock.TodayStartUtc(nowUtc, Madrid);

        Assert.Equal(new DateTimeOffset(2026, 1, 19, 23, 0, 0, TimeSpan.Zero), result);
    }

    /// <summary>
    /// A-KPI-07 (docs/auditoria-integral-2026-07-21.md): un id de timezone inválido en
    /// Dashboard:BusinessTimeZone no debe tumbar el dashboard/Worker con una excepción sin capturar
    /// desde el constructor del controller — debe degradar a UTC.
    /// </summary>
    [Fact]
    public void Resolve_InvalidTimeZoneId_FallsBackToUtcInsteadOfThrowing()
    {
        var result = BusinessTimeZoneClock.Resolve("Not/A_Real_TimeZone");

        Assert.Equal(TimeZoneInfo.Utc, result);
    }

    [Fact]
    public void Resolve_ValidTimeZoneId_ReturnsIt()
    {
        var result = BusinessTimeZoneClock.Resolve("Europe/Madrid");

        Assert.Equal(Madrid, result);
    }
}
