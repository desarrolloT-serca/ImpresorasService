using System.Data.Odbc;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.RateLimiting;
using ImpresorasService.Api.Security;
using ImpresorasService.Application;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var appCultureName = builder.Configuration["Globalization:Culture"] ?? "es-ES";
var appCulture = CultureInfo.GetCultureInfo(appCultureName);
CultureInfo.DefaultThreadCurrentCulture = appCulture;
CultureInfo.DefaultThreadCurrentUICulture = appCulture;

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
    var adminExists = await db.Users
        .Where(u => u.Login == "admin")
        .Select(u => u.UserId)
        .Take(1)
        .CountAsync() > 0;
    if (!adminExists)
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
    var supervisorExists = await db.Users
        .Where(u => u.Login == "supervisor")
        .Select(u => u.UserId)
        .Take(1)
        .CountAsync() > 0;
    if (!supervisorExists)
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
});

builder.Services.AddControllers();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ImpresorasDbContext>("database");

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.ContentType = "application/json";
            await context.HttpContext.Response.WriteAsJsonAsync(
                new { error = "Demasiados intentos. Espera un momento e inténtalo de nuevo." },
                cancellationToken);
        };
        options.AddFixedWindowLimiter("auth", limiter =>
        {
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.PermitLimit = 10;
            limiter.QueueLimit = 0;
        });
    });
}

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "JWT Authorization header. Ejemplo: Bearer {token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    options.AddSecurityDefinition("Bearer", securityScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
    var applyMigrations = app.Configuration.GetValue<bool>("Database:ApplyMigrations");
    if (app.Environment.IsEnvironment("Testing"))
    {
        await dbContext.Database.EnsureCreatedAsync();
    }
    else if (applyMigrations)
    {
        await dbContext.Database.MigrateAsync();
    }
    await NormalizeLegacyUserRolesAsync(dbContext);

    var seedDefaultUsers = app.Configuration.GetValue<bool>("Bootstrap:SeedDefaultUsers");
    if (seedDefaultUsers)
    {
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "Bootstrap:SeedDefaultUsers solo está permitido en Development o Testing.");
        }

        await SeedDefaultUsersAsync(dbContext);
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSecurityHeaders();

app.UseHttpsRedirection();

app.UseCors();

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseRateLimiter();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var status = report.Status == HealthStatus.Healthy ? "ok" : "degraded";
        await context.Response.WriteAsJsonAsync(new
        {
            status,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Status.ToString())
        });
    }
});
app.MapPost("/bootstrap/first-admin", async (
    BootstrapAdminRequest request,
    ImpresorasDbContext db,
    IWebHostEnvironment env,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!env.IsDevelopment())
        return Results.NotFound();

    var enabled = configuration.GetValue<bool>("Bootstrap:EnableFirstAdminEndpoint");
    if (!enabled)
        return Results.NotFound();

    if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { error = "Login y password son obligatorios." });

    var login = request.Login.Trim();
    var password = request.Password.Trim();
    if (login.Length < 3)
        return Results.BadRequest(new { error = "El login debe tener al menos 3 caracteres." });
    if (password.Length < 6)
        return Results.BadRequest(new { error = "La password debe tener al menos 6 caracteres." });

    // Workaround para SAP EF provider: AnyAsync puede fallar con "Sequence contains no elements"
    // en ciertos modelos, por eso usamos un Count acotado a 1 fila.
    var anyUsers = await db.Users
        .Select(u => u.UserId)
        .Take(1)
        .CountAsync(cancellationToken) > 0;
    if (anyUsers)
        return Results.Conflict(new { error = "Ya existen usuarios. Este endpoint solo sirve para bootstrap inicial." });

    var user = new ImpresorasService.Domain.Entities.User
    {
        Login = login,
        DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? "Administrador" : request.DisplayName.Trim(),
        Role = RoleCatalog.Admin,
        StoreId = null,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(10))
    };

    db.Users.Add(user);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(new
    {
        ok = true,
        message = "Usuario administrador inicial creado. Ya puedes pedir token en /api/auth/token.",
        user = new { user.UserId, user.Login, user.DisplayName, user.Role }
    });
});

app.MapGet("/diagnostics", async (ImpresorasDbContext db) =>
{
    var conn = db.Database.GetDbConnection();
    var provider = db.Database.ProviderName ?? "unknown";
    var serverNode = conn.ConnectionString?.Split(';')
        .Select(s => s.Trim())
        .FirstOrDefault(s => s.StartsWith("ServerNode=", StringComparison.OrdinalIgnoreCase))
        ?.Substring("ServerNode=".Length).Trim() ?? "?";
    var database = conn.ConnectionString?.Split(';')
        .Select(s => s.Trim())
        .FirstOrDefault(s => s.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
        ?.Substring("Database=".Length).Trim() ?? "?";
    var sourceCount = await db.SourcePrintJobs.CountAsync();
    var printCount = await db.PrintJobs.CountAsync();
    var pendingOnly = false;
    var pendingSource = await db.SourcePrintJobs.CountAsync(x => x.IsProcessed == pendingOnly);
    return Results.Ok(new
    {
        provider,
        serverNode,
        database,
        sourcePrintJobsTotal = sourceCount,
        sourcePrintJobsPending = pendingSource,
        printJobsTotal = printCount
    });
}).RequireAuthorization("AdminOnly");

app.MapGet("/diagnostics/hana", async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    static string NormalizeIdentifier(string value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return Regex.IsMatch(candidate, "^[A-Za-z0-9_]+$") ? candidate : fallback;
    }

    var connectionString = configuration["SapHana:ConnectionString"];
    if (string.IsNullOrWhiteSpace(connectionString))
        return Results.BadRequest(new { ok = false, error = "SapHana:ConnectionString no está configurado." });

    var schema = NormalizeIdentifier(configuration["SapHana:Schema"] ?? "ZTEST_VICENTE_2", "ZTEST_VICENTE_2");
    var table = NormalizeIdentifier(configuration["SapHana:Table"] ?? "printer_source_print_job", "printer_source_print_job");

    try
    {
        await using var connection = new OdbcConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var pingCmd = connection.CreateCommand();
        pingCmd.CommandText = "SELECT CURRENT_UTCTIMESTAMP FROM DUMMY";
        var serverUtc = await pingCmd.ExecuteScalarAsync(cancellationToken);

        await using var columnsCmd = connection.CreateCommand();
        columnsCmd.CommandText = @"
SELECT COLUMN_NAME, DATA_TYPE_NAME, LENGTH, SCALE, IS_NULLABLE
FROM SYS.TABLE_COLUMNS
WHERE SCHEMA_NAME = ? AND TABLE_NAME = ?
ORDER BY POSITION";
        columnsCmd.Parameters.Add(new OdbcParameter { Value = schema });
        columnsCmd.Parameters.Add(new OdbcParameter { Value = table });

        var columns = new List<object>();
        await using (var reader = await columnsCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(new
                {
                    name = reader["COLUMN_NAME"]?.ToString(),
                    type = reader["DATA_TYPE_NAME"]?.ToString(),
                    length = reader["LENGTH"] == DBNull.Value ? null : reader["LENGTH"],
                    scale = reader["SCALE"] == DBNull.Value ? null : reader["SCALE"],
                    nullable = reader["IS_NULLABLE"]?.ToString()
                });
            }
        }

        return Results.Ok(new
        {
            ok = true,
            schema,
            table,
            serverUtc,
            columnCount = columns.Count,
            columns
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Fallo al conectar con HANA por ODBC",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireAuthorization("AdminOnly");

// Raíz: Swagger en desarrollo; health en el resto de entornos
if (app.Environment.IsDevelopment())
    app.MapGet("/", () => Results.Redirect("/swagger"));
else
    app.MapGet("/", () => Results.Redirect("/health"));

app.Run();

public record BootstrapAdminRequest(string Login, string Password, string? DisplayName = null);

public partial class Program { }
