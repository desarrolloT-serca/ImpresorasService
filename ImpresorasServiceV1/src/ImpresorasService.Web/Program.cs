using System.Security.Claims;
using ImpresorasService.Web.Components;
using ImpresorasService.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddMudServices();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Impresoras.Auth";
        options.LoginPath = "/login";
        options.LogoutPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5105";
builder.Services.AddHttpClient("Api", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<ApiClient>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Endpoint para login por POST tradicional (evita "Headers are read-only" en Blazor Server)
app.MapPost("/login/submit", async (HttpContext ctx, IHttpClientFactory httpFactory) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var login = form["Login"].ToString();
    var password = form["Password"].ToString();
    if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
    {
        ctx.Response.Redirect("/login?error=" + Uri.EscapeDataString("Usuario y contraseña son obligatorios"));
        return;
    }
    var client = httpFactory.CreateClient("Api");
    var response = await client.PostAsJsonAsync("api/auth/login", new { Login = login, Password = password });
    if (!response.IsSuccessStatusCode)
    {
        var msg = response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "Credenciales inválidas" : "Error al conectar con la API";
        ctx.Response.Redirect("/login?error=" + Uri.EscapeDataString(msg));
        return;
    }
    var user = await response.Content.ReadFromJsonAsync<LoginResponse>();
    if (user == null)
    {
        ctx.Response.Redirect("/login?error=" + Uri.EscapeDataString("Respuesta inválida"));
        return;
    }
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new(ClaimTypes.Name, user.Login),
        new(ClaimTypes.GivenName, user.DisplayName),
        new(ClaimTypes.Role, user.Role)
    };
    if (user.StoreId.HasValue)
        claims.Add(new Claim("StoreId", user.StoreId.Value.ToString()));
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
    });
    ctx.Response.Redirect("/");
}).DisableAntiforgery();

app.Run();

record LoginResponse(int UserId, string Login, string DisplayName, string Role, int? StoreId);
