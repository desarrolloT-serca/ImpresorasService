using System.Globalization;
using System.Security.Claims;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Security;

/// <summary>
/// Cierra la ventana entre revocar un acceso y que deje de funcionar.
///
/// Un JWT es válido hasta que caduca, y aquí caduca a las 8 horas: sin esta comprobación, borrar o
/// desactivar un usuario —o cambiarle la contraseña porque se sospecha que está comprometida— no le
/// quitaba el acceso hasta ese plazo. Fase 5 de docs/roadmapimpresoras.md.
///
/// Se consulta la base de datos en cada petición autenticada, proyectando dos columnas de una fila
/// por clave primaria. Sin caché a propósito: una caché reintroduce exactamente la ventana que esto
/// viene a cerrar, y solo merece la pena si se demuestra un problema de rendimiento real.
/// </summary>
public static class UserRevocationValidator
{
    /// <summary>Versión de token con la que se emitió el JWT.</summary>
    public const string TokenVersionClaim = "tv";

    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null)
        {
            context.Fail("Token sin identidad.");
            return;
        }

        var rawUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(rawUserId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
        {
            context.Fail("Token sin identificador de usuario utilizable.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<ImpresorasDbContext>();

        var current = await db.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new { u.IsActive, u.TokenVersion })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        // Usuario borrado: el token sigue estando bien firmado, pero ya no representa a nadie.
        if (current is null)
        {
            context.Fail("El usuario del token ya no existe.");
            return;
        }

        if (!current.IsActive)
        {
            context.Fail("El usuario del token está desactivado.");
            return;
        }

        // Un token emitido antes de que existiera el claim se trata como versión 0, que es el valor
        // por defecto de la columna: aplicar el DDL no cierra las sesiones abiertas.
        var rawTokenVersion = principal.FindFirstValue(TokenVersionClaim);
        var tokenVersion = int.TryParse(rawTokenVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

        if (tokenVersion != current.TokenVersion)
            context.Fail("El token se emitió antes del último cambio de credenciales.");
    }
}
