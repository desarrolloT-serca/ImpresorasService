using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ImpresorasService.Infrastructure.Persistence;

/// <summary>Qué fallos son «la base de datos no responde», y por qué.</summary>
public static class FallosBd
{
    /// <summary>
    /// La base no responde: red, tiempo agotado, servidor caído o reiniciándose, o sin conexiones
    /// libres. No incluye los errores de la propia consulta (sintaxis, restricciones, bloqueos):
    /// esos son del programa y se tratan como siempre.
    /// </summary>
    public static bool EsFalloDeConexion(Exception ex) => Cadena(ex).Any(EsDeConexion);

    /// <summary>
    /// Motivo para el registro: el de Npgsql y la causa de debajo, por ejemplo
    /// «Failed to connect to 10.110.46.121:5433 (The operation has timed out.)».
    /// </summary>
    public static string Motivo(Exception ex)
    {
        var npgsql = Cadena(ex).OfType<NpgsqlException>().FirstOrDefault() ?? ex;
        if (npgsql is PostgresException pg)
            return $"{pg.SqlState}: {pg.MessageText}";
        return npgsql.InnerException is { } causa ? $"{npgsql.Message} ({causa.Message})" : npgsql.Message;
    }

    private static bool EsDeConexion(Exception e) => e switch
    {
        // 08: sin conexión; 57P01-03: el servidor se apaga o está arrancando; 53300: sin conexiones libres.
        PostgresException pg => pg.SqlState.StartsWith("08", StringComparison.Ordinal)
            || pg.SqlState is "57P01" or "57P02" or "57P03" or "53300",
        // Red, E/S y tiempo agotado.
        NpgsqlException n => n.IsTransient,
        _ => false
    };

    private static IEnumerable<Exception> Cadena(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
            yield return e;
    }
}

/// <summary>
/// Si la base de datos responde, para todo el proceso (Api o Worker). Lo alimenta
/// <see cref="EstadoBdInterceptor"/> con cada comando que sale bien o falla, así que nadie más tiene
/// que registrar estos fallos: se anota el corte una vez con su motivo, se recuerda cada
/// <see cref="Recordatorio"/> mientras dura y se anota la vuelta con lo que duró. Antes se
/// registraba una traza completa en cada petición y en cada ciclo del Worker.
///
/// La reconexión es automática: Npgsql abre una conexión nueva en el siguiente intento, y el Worker
/// retoma el trabajo en cuanto renueva su lock.
/// </summary>
public sealed class EstadoBd(ILogger<EstadoBd> logger, TimeProvider reloj)
{
    public static readonly TimeSpan Recordatorio = TimeSpan.FromMinutes(5);

    private readonly object _cerrojo = new();
    private volatile bool _caida;
    private DateTimeOffset _desde;
    private DateTimeOffset _ultimoAviso;
    private int _fallos;
    private string _motivo = string.Empty;

    public bool Caida => _caida;

    public string? Motivo
    {
        get { lock (_cerrojo) return _caida ? _motivo : null; }
    }

    /// <summary>Anota el fallo si es de la base; devuelve si lo era.</summary>
    public bool RegistrarFallo(Exception ex)
    {
        if (!FallosBd.EsFalloDeConexion(ex))
            return false;

        var motivo = FallosBd.Motivo(ex);
        var ahora = reloj.GetUtcNow();
        lock (_cerrojo)
        {
            _fallos++;
            _motivo = motivo;
            if (!_caida)
            {
                _caida = true;
                _desde = ahora;
                _ultimoAviso = ahora;
                logger.LogError(
                    "Base de datos no disponible: {Motivo}. Se reconecta sola en cuanto responda; mientras tanto no se procesa nada.",
                    motivo);
            }
            else if (ahora - _ultimoAviso >= Recordatorio)
            {
                _ultimoAviso = ahora;
                logger.LogError(
                    "La base de datos sigue sin responder desde hace {Minutos} min ({Fallos} intentos fallidos). Último motivo: {Motivo}",
                    (int)(ahora - _desde).TotalMinutes, _fallos, motivo);
            }
        }
        return true;
    }

    /// <summary>Un comando ha salido bien: si la base estaba caída, ha vuelto.</summary>
    public void RegistrarExito()
    {
        // Sin cerrojo en el caso normal: pasa por aquí cada comando.
        if (!_caida)
            return;

        lock (_cerrojo)
        {
            if (!_caida)
                return;
            var duracion = reloj.GetUtcNow() - _desde;
            // Warning y no Information: el registro de eventos de Windows solo guarda desde Warning.
            logger.LogWarning(
                "Base de datos recuperada tras {Minutos} min {Segundos} s ({Fallos} intentos fallidos). Motivo del corte: {Motivo}",
                (int)duracion.TotalMinutes, duracion.Seconds, _fallos, _motivo);
            _caida = false;
            _fallos = 0;
            _motivo = string.Empty;
        }
    }
}

/// <summary>
/// Cuenta a <see cref="EstadoBd"/> cada fallo de conexión o de comando y cada comando que sale
/// bien. Se mira el comando y no la apertura de la conexión: del pool puede salir una conexión que
/// ya estaba muerta, y abrirla no prueba que la base responda.
/// </summary>
public sealed class EstadoBdInterceptor(EstadoBd estado) : IDbConnectionInterceptor, IDbCommandInterceptor
{
    public void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) =>
        estado.RegistrarFallo(eventData.Exception);

    public Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        estado.RegistrarFallo(eventData.Exception);
        return Task.CompletedTask;
    }

    public void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        estado.RegistrarFallo(eventData.Exception);

    public Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        estado.RegistrarFallo(eventData.Exception);
        return Task.CompletedTask;
    }

    public DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        estado.RegistrarExito();
        return result;
    }

    public ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        estado.RegistrarExito();
        return ValueTask.FromResult(result);
    }

    public int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        estado.RegistrarExito();
        return result;
    }

    public ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        estado.RegistrarExito();
        return ValueTask.FromResult(result);
    }

    public object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        estado.RegistrarExito();
        return result;
    }

    public ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        estado.RegistrarExito();
        return ValueTask.FromResult(result);
    }
}
