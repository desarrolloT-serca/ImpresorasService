using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ImpresorasService.Api.Security;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
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
/// Con la base caída la Api responde 503 con el motivo —no 500 ni 401— y vuelve sola en cuanto
/// responde, sin reiniciar nada.
/// </summary>
public sealed class BdCaidaTests : IClassFixture<BdCaidaApiFactory>
{
    private readonly BdCaidaApiFactory _factory;

    public BdCaidaTests(BdCaidaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ConLaBaseCaida_Responde503ConElMotivoYSeRecuperaSola()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PaseDeAdministrador());

        _factory.Base.Caida = true;
        try
        {
            await AssertBdNoDisponible(await client.GetAsync("/api/printjobs"));
            await AssertBdNoDisponible(await client.GetAsync("/api/stores"));
        }
        finally
        {
            _factory.Base.Caida = false;
        }

        // La base vuelve: el mismo pase funciona sin reiniciar nada.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/printjobs")).StatusCode);
    }

    private static string PaseDeAdministrador() => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = PaseErp.Emisor,
        Audience = PaseErp.Audiencia,
        Expires = DateTime.UtcNow.AddMinutes(2),
        SigningCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiWebApplicationFactory.SecretoErp)), SecurityAlgorithms.HmacSha256),
        Claims = new Dictionary<string, object>
        {
            ["sub"] = "1",
            ["alm"] = 1,
            ["perm"] = new[] { "impresion.leer", "impresion.administrar", "impresion.tiendas.todas" }
        }
    });

    private static async Task AssertBdNoDisponible(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("10", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
        var cuerpo = await response.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.StartsWith("La base de datos no responde", cuerpo!.Error);
    }

    private sealed record RespuestaError(string Error);
}
