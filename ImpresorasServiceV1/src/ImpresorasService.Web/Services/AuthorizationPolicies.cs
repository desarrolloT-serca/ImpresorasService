using Microsoft.AspNetCore.Authorization;

namespace ImpresorasService.Web.Services;

/// <summary>
/// Políticas de autorización por rol (Admin, Supervisor).
/// Se implementará en la fase 5.5.
/// </summary>
public static class AuthorizationPolicies
{
    public const string Admin = "Admin";
    public const string Supervisor = "Supervisor";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(Admin, policy => policy.RequireRole("Admin"));
        options.AddPolicy(Supervisor, policy => policy.RequireRole("Admin", "Supervisor"));
    }
}
