using System.Security.Claims;
using ImpresorasService.Api.Security;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Security;

/// <summary>
/// Fase 5: un JWT bien firmado y sin caducar no basta. Hasta este validador, borrar o desactivar un
/// usuario —o cambiarle la contraseña porque se sospechaba comprometida— no le quitaba el acceso
/// hasta 8 horas después.
/// </summary>
public sealed class UserRevocationValidatorTests
{
    [Fact]
    public async Task ActiveUserWithMatchingTokenVersion_Passes()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        await SeedUserAsync(setup.Db, isActive: true, tokenVersion: 3);

        var context = await RunAsync(setup.Db, userId: 7, tokenVersion: "3");

        Assert.Null(context.Result);
    }

    [Fact]
    public async Task DeactivatedUser_Fails()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        await SeedUserAsync(setup.Db, isActive: false, tokenVersion: 0);

        var context = await RunAsync(setup.Db, userId: 7, tokenVersion: "0");

        Assert.NotNull(context.Result?.Failure);
    }

    [Fact]
    public async Task DeletedUser_Fails()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();

        // Sin sembrar: el token está bien firmado pero ya no representa a nadie.
        var context = await RunAsync(setup.Db, userId: 7, tokenVersion: "0");

        Assert.NotNull(context.Result?.Failure);
    }

    [Fact]
    public async Task TokenIssuedBeforePasswordChange_Fails()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        await SeedUserAsync(setup.Db, isActive: true, tokenVersion: 4);

        // El token se emitió con la versión 3; la contraseña cambió y la base de datos ya va por la 4.
        var context = await RunAsync(setup.Db, userId: 7, tokenVersion: "3");

        Assert.NotNull(context.Result?.Failure);
    }

    /// <summary>
    /// Un token emitido antes de que existiera el claim se trata como versión 0, igual que el DEFAULT
    /// de la columna: aplicar el DDL no debe echar a nadie que estuviera dentro.
    /// </summary>
    [Fact]
    public async Task TokenWithoutVersionClaim_PassesWhenDatabaseIsAtZero()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        await SeedUserAsync(setup.Db, isActive: true, tokenVersion: 0);

        var context = await RunAsync(setup.Db, userId: 7, tokenVersion: null);

        Assert.Null(context.Result);
    }

    private static async Task SeedUserAsync(ImpresorasDbContext db, bool isActive, int tokenVersion)
    {
        db.Users.Add(new User
        {
            UserId = 7,
            Login = "operario",
            PasswordHash = "hash",
            Role = "Admin",
            IsActive = isActive,
            TokenVersion = tokenVersion
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<TokenValidatedContext> RunAsync(ImpresorasDbContext db, int userId, string? tokenVersion)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (tokenVersion is not null)
            claims.Add(new Claim(UserRevocationValidator.TokenVersionClaim, tokenVersion));

        var services = new ServiceCollection();
        services.AddSingleton(db);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));

        var context = new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestJwt"))
        };

        await UserRevocationValidator.ValidateAsync(context);
        return context;
    }
}
