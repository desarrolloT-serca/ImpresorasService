using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// Como <see cref="ApiWebApplicationFactory"/>, pero autentica con el JWT real (el pase del ERP) en vez del
/// usuario de prueba: aquí se prueba justo cómo llega el pase.
/// </summary>
public class JwtApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.PostConfigure<AuthenticationOptions>(o =>
        {
            o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }));
    }
}
