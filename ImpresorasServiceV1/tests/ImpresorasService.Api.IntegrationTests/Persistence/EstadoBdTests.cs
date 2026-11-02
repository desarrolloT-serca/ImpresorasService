using ImpresorasService.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Persistence;

public sealed class EstadoBdTests
{
    // Como llega de EF: «transient failure» envolviendo el fallo de Npgsql.
    private static Exception SinConexion() => new InvalidOperationException(
        "An exception has been raised that is likely due to a transient failure.",
        new NpgsqlException("Failed to connect to 10.110.46.121:5433", new TimeoutException("The operation has timed out.")));

    [Fact]
    public void FallosBd_DistingueLaBaseCaidaDeLosErroresDeConsulta()
    {
        Assert.True(FallosBd.EsFalloDeConexion(SinConexion()));
        Assert.True(FallosBd.EsFalloDeConexion(new PostgresException("the database system is starting up", "FATAL", "FATAL", "57P03")));
        Assert.True(FallosBd.EsFalloDeConexion(new PostgresException("sorry, too many clients already", "FATAL", "FATAL", "53300")));
        Assert.False(FallosBd.EsFalloDeConexion(new PostgresException("duplicate key value", "ERROR", "ERROR", "23505")));
        Assert.False(FallosBd.EsFalloDeConexion(new PostgresException("could not serialize access", "ERROR", "ERROR", "40001")));
        Assert.False(FallosBd.EsFalloDeConexion(new InvalidOperationException("otra cosa")));
    }

    [Fact]
    public void FallosBd_DaElMotivoLegible()
    {
        Assert.Equal("Failed to connect to 10.110.46.121:5433 (The operation has timed out.)", FallosBd.Motivo(SinConexion()));
        Assert.Equal("57P03: the database system is starting up",
            FallosBd.Motivo(new PostgresException("the database system is starting up", "FATAL", "FATAL", "57P03")));
    }

    [Fact]
    public void EstadoBd_AnotaElCorteUnaVezRecuerdaCada5MinYAnotaLaVuelta()
    {
        var reloj = new ManualTimeProvider();
        reloj.SetUtcNow(new DateTimeOffset(2026, 10, 26, 7, 53, 0, TimeSpan.Zero));
        var log = new LogEnMemoria();
        var estado = new EstadoBd(log, reloj);

        Assert.False(estado.RegistrarFallo(new InvalidOperationException("no es de la base")));
        Assert.True(estado.RegistrarFallo(SinConexion()));
        Assert.True(estado.RegistrarFallo(SinConexion()));
        reloj.SetUtcNow(reloj.GetUtcNow().AddMinutes(2));
        estado.RegistrarFallo(SinConexion());

        Assert.True(estado.Caida);
        Assert.Equal("Failed to connect to 10.110.46.121:5433 (The operation has timed out.)", estado.Motivo);
        var corte = Assert.Single(log.Mensajes);
        Assert.Equal(LogLevel.Error, corte.Nivel);
        Assert.StartsWith("Base de datos no disponible: Failed to connect", corte.Texto);

        reloj.SetUtcNow(reloj.GetUtcNow().AddMinutes(4));
        estado.RegistrarFallo(SinConexion());
        Assert.Equal(2, log.Mensajes.Count);
        Assert.Contains("sigue sin responder desde hace 6 min (4 intentos fallidos)", log.Mensajes[1].Texto);

        reloj.SetUtcNow(reloj.GetUtcNow().AddSeconds(30));
        estado.RegistrarExito();
        estado.RegistrarExito();
        Assert.False(estado.Caida);
        Assert.Null(estado.Motivo);
        Assert.Equal(3, log.Mensajes.Count);
        Assert.Equal(LogLevel.Warning, log.Mensajes[2].Nivel);
        Assert.StartsWith("Base de datos recuperada tras 6 min 30 s (4 intentos fallidos)", log.Mensajes[2].Texto);
    }

    private sealed class LogEnMemoria : ILogger<EstadoBd>
    {
        public List<(LogLevel Nivel, string Texto)> Mensajes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Mensajes.Add((logLevel, formatter(state, exception)));
    }
}
