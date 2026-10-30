using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ImpresorasService.Api.IntegrationTests.Controllers;
using ImpresorasService.Api.Security;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Security;

public sealed class ErpApiWebApplicationFactory : JwtApiWebApplicationFactory
{
    public const string Secreto = "secreto-del-erp-para-las-pruebas-32+";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Erp:Secret", Secreto);
    }
}

/// <summary>
/// Pase del ERP (PaseErp): la pasarela del ERP firma el operador y sus claves de impresión; la API
/// los traduce a rol y tienda sin usuario propio.
/// </summary>
public sealed class PaseErpTests : IClassFixture<ErpApiWebApplicationFactory>
{
    private const string Leer = "impresion.leer";
    private const string Reimprimir = "impresion.reimprimir";
    private const string Administrar = "impresion.administrar";
    private const string Todas = "impresion.tiendas.todas";

    private readonly ErpApiWebApplicationFactory _factory;

    public PaseErpTests(ErpApiWebApplicationFactory factory) => _factory = factory;

    private static string Pase(
        string[] claves,
        int almacen = 1,
        string secreto = ErpApiWebApplicationFactory.Secreto,
        string emisor = PaseErp.Emisor,
        string audiencia = PaseErp.Audiencia,
        DateTime? caduca = null)
    {
        var ahora = DateTime.UtcNow;
        var hasta = caduca ?? ahora.AddMinutes(2);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = emisor,
            Audience = audiencia,
            IssuedAt = hasta.AddMinutes(-2),
            NotBefore = hasta.AddMinutes(-2),
            Expires = hasta,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secreto)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "123",
                ["name"] = "OPERADOR DE PRUEBA",
                ["dep"] = 113,
                ["alm"] = almacen,
                ["perm"] = claves
            }
        });
    }

    private async Task<HttpResponseMessage> GetAsync(string ruta, string pase)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pase);
        return await client.GetAsync(ruta);
    }

    [Theory]
    [InlineData(Administrar, HttpStatusCode.OK)]
    [InlineData(Reimprimir, HttpStatusCode.Forbidden)]
    [InlineData(Leer, HttpStatusCode.Forbidden)]
    public async Task ElRolSaleDeLaClaveMasAlta(string clave, HttpStatusCode reglas)
    {
        string[] claves = clave == Leer ? [Leer] : [Leer, clave];

        Assert.Equal(reglas, (await GetAsync("/api/routingrules", Pase(claves))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/printers", Pase(claves))).StatusCode);
    }

    // Un pase interceptado no puede crear un admin permanente (auditoría M11).
    [Fact]
    public async Task ConPaseDeAdministradorNoSeGestionanUsuarios()
        => Assert.Equal(HttpStatusCode.Forbidden,
            (await GetAsync("/api/users", Pase([Leer, Administrar, Todas]))).StatusCode);

    [Fact]
    public async Task SinTodasLasTiendasSoloVeLaDeSuAlmacen()
    {
        await SembrarImpresorasAsync();

        var suya = await (await GetAsync("/api/printers?storeId=2", Pase([Leer], almacen: 1)))
            .Content.ReadFromJsonAsync<List<ImpresoraDto>>();
        var todas = await (await GetAsync("/api/printers", Pase([Leer, Todas], almacen: 1)))
            .Content.ReadFromJsonAsync<List<ImpresoraDto>>();

        Assert.All(suya!, i => Assert.Equal(1, i.StoreId));
        Assert.Contains(todas!, i => i.StoreId == 2);
    }

    public static TheoryData<string> PasesNoValidos() => new()
    {
        Pase([Leer], secreto: "otro-secreto-que-no-es-el-del-erp-32+"),
        Pase([Leer], caduca: DateTime.UtcNow.AddMinutes(-1)),
        Pase([]),
        Pase([Leer], audiencia: "ImpresorasService"),
        // Firmado con la clave del ERP pero haciéndose pasar por un token propio.
        Pase([Leer], emisor: "ImpresorasService", audiencia: "ImpresorasService"),
    };

    [Theory]
    [MemberData(nameof(PasesNoValidos))]
    public async Task UnPaseNoValidoNoEntra(string pase)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/printers", pase)).StatusCode);

    [Fact]
    public async Task SinErpSecretNoSeAceptanPases()
    {
        using var factory = new JwtApiWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Pase([Leer, Administrar]));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/printers")).StatusCode);
    }

    private async Task SembrarImpresorasAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        if (db.Printers.Any())
            return;
        var ahora = DateTimeOffset.UtcNow;
        db.Printers.AddRange(
            new Printer { PrinterName = "T1", SpoolQueue = "COLA-T1", StoreId = 1, CreatedAtUtc = ahora, UpdatedAtUtc = ahora },
            new Printer { PrinterName = "T2", SpoolQueue = "COLA-T2", StoreId = 2, CreatedAtUtc = ahora, UpdatedAtUtc = ahora });
        await db.SaveChangesAsync();
    }

    private sealed record ImpresoraDto(int PrinterId, int StoreId);
}
