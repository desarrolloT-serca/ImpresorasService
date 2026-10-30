using System.Net;
using System.Net.Http.Json;
using ImpresorasService.Api.Controllers;
using ImpresorasService.Api.Security;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

/// <summary>
/// Como <see cref="ApiWebApplicationFactory"/>, pero autentica con el JWT real en vez del usuario de
/// prueba: aquí se prueba justo cómo llega el token.
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

/// <summary>Sesión del frontend React: el JWT viaja en una cookie httpOnly, no en el cuerpo.</summary>
public sealed class AuthSessionCookieTests : IClassFixture<JwtApiWebApplicationFactory>
{
    private const string Password = "secret123";
    private readonly JwtApiWebApplicationFactory _factory;

    public AuthSessionCookieTests(JwtApiWebApplicationFactory factory) => _factory = factory;

    // https: fuera de Development la cookie es Secure y el cliente no la devolvería por http.
    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    [Fact]
    public async Task Login_OpensACookieSessionThatAuthenticatesUntilLogout()
    {
        await SeedUserAsync("cookie-user");
        var client = NewClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { login = "cookie-user", password = Password });

        login.EnsureSuccessStatusCode();
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith(AuthController.SessionCookie + "="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Equal("cookie-user", me!.Login);
        Assert.Equal(RoleCatalog.StoreManager, me.Role);
        Assert.Equal(1, me.StoreId);
        (await client.GetAsync("/api/printjobs")).EnsureSuccessStatusCode();

        (await client.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Me_WithoutSession_Returns401()
    {
        var response = await NewClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
            Role = RoleCatalog.StoreManager,
            StoreId = 1,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
        });
        await db.SaveChangesAsync();
    }

    private sealed record MeResponse(int UserId, string Login, string DisplayName, string Role, int? StoreId);
}
