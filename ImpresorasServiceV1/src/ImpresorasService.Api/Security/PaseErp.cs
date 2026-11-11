using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ImpresorasService.Api.Security;

/// <summary>
/// Pase del ERP nuevo. Su pasarela <c>/api/impresion</c> firma en cada petición un JWT de vida corta
/// (HS256, secreto <c>Erp:Secret</c>) con el operador que ha entrado y sus claves de impresión, que el
/// ERP da por departamento (<c>ACCESO_IMPRESION</c> en sus contratos). Aquí se traducen al rol y la
/// tienda que ya entienden los controladores. Es la única forma de entrar (desde el 11/11/2026 no hay
/// usuarios propios): nada que revocar en la base; el ERP comprueba al operador en cada petición.
/// </summary>
public static class PaseErp
{
    public const string Emisor = "erp-serca";
    public const string Audiencia = "impresoras";
    public const string TiendasClaim = "Tiendas";
    // Perfil de tienda, que comparten todos sus operadores (reimprime identificándose).
    public const string CompartidoClaim = "Compartido";

    // PERMISOS_IMPRESION de los contratos del ERP.
    private const string Leer = "impresion.leer";
    private const string Reimprimir = "impresion.reimprimir";
    private const string Administrar = "impresion.administrar";
    private const string Todas = "impresion.tiendas.todas";
    private const string Identificarse = "impresion.reimprimir.identificarse";

    /// <summary>Administrador o pase con todas las tiendas: decide qué tiendas ve, no qué puede hacer.</summary>
    public static bool VeTodasLasTiendas(this ClaimsPrincipal user)
        => user.IsInRole(RoleCatalog.Admin) || user.HasClaim(TiendasClaim, "todas");

    /// <summary>
    /// Quién hizo cada cosa y el motivo y la nota de las reimpresiones: administración y quien puede reimprimir con
    /// su perfil personal (jefe de tienda). El perfil de tienda, que comparten todos sus operadores, no: reimprime
    /// identificándose, pero no ve quién fue (ERP_SERCA, usuario, 11/11/2026).
    /// </summary>
    public static bool VeQuien(this ClaimsPrincipal user)
        => (user.IsInRole(RoleCatalog.Admin) || user.IsInRole(RoleCatalog.StoreManager))
            && !user.HasClaim(CompartidoClaim, "si");

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
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, autor.Length > 120 ? autor[..120] : autor),
            new(ClaimTypes.Role, rol)
        };
        claims.Add(claves.Contains(Todas)
            ? new Claim(TiendasClaim, "todas")
            : new Claim("StoreId", almacen.ToString()));
        if (claves.Contains(Identificarse))
            claims.Add(new Claim(CompartidoClaim, "si"));

        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Emisor, ClaimTypes.Name, ClaimTypes.Role));
        return Task.CompletedTask;
    }
}
