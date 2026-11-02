using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using ImpresorasService.Api.Security;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

/// <summary>Hace fallar cada comando como lo haría Npgsql con la base caída.</summary>
public sealed class BdCaidaInterceptor : DbCommandInterceptor
{
    public volatile bool Caida;

    private static NpgsqlException Fallo() =>
        new("Failed to connect to 10.110.46.121:5433", new TimeoutException("The operation has timed out."));

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) =>
        Caida ? throw Fallo() : result;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
        Caida ? throw Fallo() : ValueTask.FromResult(result);

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) =>
        Caida ? throw Fallo() : result;

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        Caida ? throw Fallo() : ValueTask.FromResult(result);
}

/// <summary>Como <see cref="JwtApiWebApplicationFactory"/>, con un interruptor para tirar la base.</summary>
public sealed class BdCaidaApiFactory : JwtApiWebApplicationFactory
{
    public BdCaidaInterceptor Base { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            var original = services.Single(d => d.ServiceType == typeof(DbContextOptions<ImpresorasDbContext>));
            services.Remove(original);
            services.AddScoped(sp => new DbContextOptionsBuilder<ImpresorasDbContext>(
                    (DbContextOptions<ImpresorasDbContext>)original.ImplementationFactory!(sp))
                .AddInterceptors(Base)
                .Options);
        });
    }
}

/// <summary>
/// Con la base caída la Api responde 503 con el motivo —no 500, ni 401 que mandaría al login— y
/// vuelve sola en cuanto responde, sin reiniciar nada.
/// </summary>
public sealed class BdCaidaTests : IClassFixture<BdCaidaApiFactory>
{
    private const string Password = "secret123";
    private readonly BdCaidaApiFactory _factory;

    public BdCaidaTests(BdCaidaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ConLaBaseCaida_Responde503ConElMotivoYSeRecuperaSola()
    {
        await SeedUserAsync("bd-caida");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        (await client.PostAsJsonAsync("/api/auth/login", new { login = "bd-caida", password = Password })).EnsureSuccessStatusCode();

        _factory.Base.Caida = true;
        try
        {
            // Con sesión: la comprobación del usuario falla en JwtBearer; antes daba 401.
            await AssertBdNoDisponible(await client.GetAsync("/api/auth/me"));
            await AssertBdNoDisponible(await client.GetAsync("/api/printjobs"));
            // Sin sesión: el controlador falla al consultar.
            await AssertBdNoDisponible(await client.PostAsJsonAsync("/api/auth/login", new { login = "bd-caida", password = Password }));
        }
        finally
        {
            _factory.Base.Caida = false;
        }

        // La base vuelve: la misma sesión funciona sin reiniciar nada.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    private static async Task AssertBdNoDisponible(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("10", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
        var cuerpo = await response.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.StartsWith("La base de datos no responde", cuerpo!.Error);
    }

    private async Task SeedUserAsync(string login)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        if (await db.Users.AnyAsync(u => u.Login == login))
            return;
        db.Users.Add(new User
        {
            Login = login,
            DisplayName = login,
            Role = RoleCatalog.Admin,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
        });
        await db.SaveChangesAsync();
    }

    private sealed record RespuestaError(string Error);
}
