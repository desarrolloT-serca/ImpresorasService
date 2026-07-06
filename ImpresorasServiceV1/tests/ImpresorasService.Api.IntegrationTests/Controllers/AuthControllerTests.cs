using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ImpresorasService.Api.Security;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

public sealed class AuthControllerTests : IntegrationTestBase
{
    private const string TestPassword = "secret123";

    public AuthControllerTests(ApiWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Token_ValidCredentials_ReturnsJwtWithUserClaims()
    {
        await SeedAuthUserAsync("auth-user", RoleCatalog.Admin);

        var response = await Client.PostAsJsonAsync("/api/auth/token", new { login = "auth-user", password = TestPassword });

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<TokenResponse>(response);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.NotNull(body.User);
        Assert.Equal("auth-user", body.User!.Login);
        Assert.Equal(RoleCatalog.Admin, body.User.Role);

        var principal = ValidateJwt(body.Token);
        Assert.Equal("auth-user", principal.Identity?.Name);
        Assert.Contains(principal.Claims, c => c.Type == ClaimTypes.Role && c.Value == RoleCatalog.Admin);
    }

    [Fact]
    public async Task Token_InvalidPassword_ReturnsUnauthorized()
    {
        await SeedAuthUserAsync("auth-user-bad-pass", RoleCatalog.Employee, storeId: 1);

        var response = await Client.PostAsJsonAsync("/api/auth/token", new { login = "auth-user-bad-pass", password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await ReadAsJsonAsync<ErrorResponse>(response);
        Assert.NotNull(body?.Error);
    }

    [Fact]
    public async Task Token_MissingCredentials_ReturnsUnauthorized()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/token", new { login = "", password = "" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsUserWithoutToken()
    {
        await SeedAuthUserAsync("login-only-user", RoleCatalog.Employee, storeId: 1);

        var response = await Client.PostAsJsonAsync("/api/auth/login", new { login = "login-only-user", password = TestPassword });

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<LoginResponse>(response);
        Assert.NotNull(body);
        Assert.Equal("login-only-user", body!.Login);
        Assert.Equal(RoleCatalog.Employee, body.Role);
    }

    private async Task SeedAuthUserAsync(string login, string role, int? storeId = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

        if (await db.Users.AnyAsync(u => u.Login == login))
        {
            return;
        }

        var userId = Math.Abs(login.GetHashCode() % 900_000) + 10_000;
        while (await db.Users.AnyAsync(u => u.UserId == userId))
        {
            userId++;
        }

        db.Users.Add(new User
        {
            UserId = userId,
            Login = login,
            DisplayName = login,
            Role = role,
            StoreId = storeId,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestPassword),
        });
        await db.SaveChangesAsync();
    }

    private static ClaimsPrincipal ValidateJwt(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("integration-tests-secret-32-chars-minimum!!")),
            ValidateIssuer = true,
            ValidIssuer = "ImpresorasService",
            ValidateAudience = true,
            ValidAudience = "ImpresorasService",
            ValidateLifetime = false,
        };

        return handler.ValidateToken(token, parameters, out _);
    }

    private sealed class TokenResponse
    {
        public string? Token { get; set; }
        public LoginResponse? User { get; set; }
    }

    private sealed class LoginResponse
    {
        public int UserId { get; set; }
        public string? Login { get; set; }
        public string? Role { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }
}
