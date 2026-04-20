using System.IO;
using System.Text;
using ImpresorasService.Api.Security;
using ImpresorasService.Application;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

static bool IsUnsafeJwtSecret(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return true;

    var normalized = value.Trim();
    if (normalized.Length < 32)
        return true;

    return string.Equals(normalized, "ImpresorasService-V1-SecretKey-Min32Chars!!", StringComparison.Ordinal)
        || string.Equals(normalized, "ChangeMe123", StringComparison.Ordinal)
        || string.Equals(normalized, "changeme", StringComparison.OrdinalIgnoreCase);
}

static async Task NormalizeLegacyUserRolesAsync(ImpresorasDbContext db)
{
    var users = await db.Users.ToListAsync();
    var changed = false;

    foreach (var user in users)
    {
        var normalized = RoleCatalog.Normalize(user.Role);
        if (!string.Equals(user.Role, normalized, StringComparison.Ordinal))
        {
            user.Role = normalized;
            changed = true;
        }
    }

    if (changed)
        await db.SaveChangesAsync();
}

static async Task SeedDefaultUsersAsync(ImpresorasDbContext db)
{
    if (!await db.Users.AnyAsync(u => u.Login == "admin"))
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123", BCrypt.Net.BCrypt.GenerateSalt(10));
        db.Users.Add(new ImpresorasService.Domain.Entities.User
        {
            Login = "admin",
            PasswordHash = hash,
            Role = RoleCatalog.Admin,
            StoreId = null,
            DisplayName = "Administrador"
        });
    }
    if (!await db.Users.AnyAsync(u => u.Login == "supervisor"))
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("sup123", BCrypt.Net.BCrypt.GenerateSalt(10));
        db.Users.Add(new ImpresorasService.Domain.Entities.User
        {
            Login = "supervisor",
            PasswordHash = hash,
            Role = RoleCatalog.StoreManager,
            StoreId = 1,
            DisplayName = "Jefe de tienda 1"
        });
    }
    await db.SaveChangesAsync();
}

// Add services to the container.

builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader();
            return;
        }

        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
                .AllowAnyMethod()
                .AllowAnyHeader();
            return;
        }

        throw new InvalidOperationException("Cors:AllowedOrigins debe configurarse fuera de Development.");
    });
});

var jwtSecret = builder.Configuration["Jwt:Secret"];
if (IsUnsafeJwtSecret(jwtSecret))
    throw new InvalidOperationException("Jwt:Secret es obligatorio, debe estar definido por entorno y no puede usar valores inseguros/default.");
var jwtSecretValue = jwtSecret!.Trim();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretValue)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ImpresorasService",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ImpresorasService",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(RoleCatalog.Admin));
    options.AddPolicy("StoreManagerOrAdmin", policy => policy.RequireRole(RoleCatalog.Admin, RoleCatalog.StoreManager));
    options.AddPolicy("EmployeeOrAbove", policy => policy.RequireRole(RoleCatalog.Admin, RoleCatalog.StoreManager, RoleCatalog.Employee));
    options.AddPolicy(
        "SupervisorOrAdmin",
        policy => policy.RequireRole(RoleCatalog.Admin, RoleCatalog.StoreManager, RoleCatalog.LegacySupervisor));
});

builder.Services.AddControllers();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
    if (app.Environment.IsEnvironment("Testing"))
        await dbContext.Database.EnsureCreatedAsync();
    else
        await dbContext.Database.MigrateAsync();
    await NormalizeLegacyUserRolesAsync(dbContext);

    var seedDefaultUsers = app.Configuration.GetValue<bool?>("Bootstrap:SeedDefaultUsers")
        ?? app.Environment.IsDevelopment();
    if (seedDefaultUsers)
        await SeedDefaultUsersAsync(dbContext);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/diagnostics", async (ImpresorasDbContext db) =>
{
    var conn = db.Database.GetDbConnection();
    var dataSource = conn.ConnectionString?.Split(';')
        .Select(s => s.Trim())
        .FirstOrDefault(s => s.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        ?.Substring("Data Source=".Length).Trim() ?? "?";
    var fullPath = string.IsNullOrEmpty(dataSource) ? "?" : Path.GetFullPath(dataSource);
    var sourceCount = await db.SourcePrintJobs.CountAsync();
    var printCount = await db.PrintJobs.CountAsync();
    var pendingSource = await db.SourcePrintJobs.CountAsync(x => !x.IsProcessed);
    return Results.Ok(new
    {
        databasePath = fullPath,
        sourcePrintJobsTotal = sourceCount,
        sourcePrintJobsPending = pendingSource,
        printJobsTotal = printCount
    });
}).RequireAuthorization("AdminOnly");

// Redirigir raíz a Swagger para que el navegador muestre algo
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

public partial class Program { }
