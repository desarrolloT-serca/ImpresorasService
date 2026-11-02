using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ImpresorasService.Api.Security;

/// <summary>
/// Pase del ERP nuevo. Su pasarela <c>/api/impresion</c> firma en cada petición un JWT de vida corta
/// (HS256, secreto <c>Erp:Secret</c>) con el operador que ha entrado y sus claves de impresión, que el
/// ERP da por departamento (<c>ACCESO_IMPRESION</c> en sus contratos). Aquí se traducen al rol y la
/// tienda que ya entienden los controladores. No hay usuario de <c>printer_user</c> detrás: nada que
/// revocar en la base; el ERP comprueba al operador en cada petición.
/// </summary>
public static class PaseErp
{
    public const string Emisor = "erp-serca";
    public const string Audiencia = "impresoras";
    public const string TiendasClaim = "Tiendas";

    // PERMISOS_IMPRESION de los contratos del ERP.
    private const string Leer = "impresion.leer";
    private const string Reimprimir = "impresion.reimprimir";
    private const string Administrar = "impresion.administrar";
    private const string Todas = "impresion.tiendas.todas";

    /// <summary>Administrador o pase con todas las tiendas: decide qué tiendas ve, no qué puede hacer.</summary>
    public static bool VeTodasLasTiendas(this ClaimsPrincipal user)
        => user.IsInRole(RoleCatalog.Admin) || user.HasClaim(TiendasClaim, "todas");

    public static Task ValidarAsync(TokenValidatedContext context)
    {
        var pase = (JsonWebToken)context.SecurityToken;
        var claves = pase.Claims.Where(c => c.Type == "perm").Select(c => c.Value).ToHashSet();
        var rol = claves.Contains(Administrar) ? RoleCatalog.Admin
            : claves.Contains(Reimprimir) ? RoleCatalog.StoreManager
            : claves.Contains(Leer) ? RoleCatalog.Employee
            : null;
        if (rol is null || !pase.Audiences.Contains(Audiencia)
            || !pase.TryGetPayloadValue<string>("sub", out var operador)
            || !pase.TryGetPayloadValue<int>("alm", out var almacen))
        {
            context.Fail("El pase del ERP no da acceso a impresión.");
            return Task.CompletedTask;
        }

        pase.TryGetPayloadValue<string>("name", out var nombre);
        // Autor en el historial de cada trabajo (actor_id, 120 caracteres).
        var autor = $"ERP {operador} {nombre}".Trim();
        // Sin NameIdentifier a propósito: no es un usuario de printer_user (auth/me, usuarios).
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, autor.Length > 120 ? autor[..120] : autor),
            new(ClaimTypes.Role, rol)
        };
        claims.Add(claves.Contains(Todas)
            ? new Claim(TiendasClaim, "todas")
            : new Claim("StoreId", almacen.ToString()));

        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Emisor, ClaimTypes.Name, ClaimTypes.Role));
        return Task.CompletedTask;
    }
}
