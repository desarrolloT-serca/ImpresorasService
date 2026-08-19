namespace ImpresorasService.Application.Abstractions;

/// <summary>
/// El trabajo cambió de estado entre que se leyó y se intentó escribir, así que la operación no se
/// aplicó a ninguna fila (Fase 2.5).
///
/// <para>Es distinto de "la operación no es válida para este estado": aquí la petición era legítima
/// cuando se recibió, y lo que ha pasado es que alguien —normalmente el Worker reclamando el
/// trabajo— llegó antes. Merece un 409 y no un 400, porque reintentarla tras releer el estado puede
/// perfectamente funcionar.</para>
///
/// <para>Existe para que la API pueda distinguir ambos casos sin mirar el texto del mensaje.</para>
/// </summary>
public sealed class PrintJobStateConflictException : InvalidOperationException
{
    public PrintJobStateConflictException(string message) : base(message)
    {
    }
}
