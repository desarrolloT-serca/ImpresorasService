using System.Globalization;
using System.Text;
using ImpresorasService.Api.HealthChecks;
using ImpresorasService.Api.Security;
using ImpresorasService.Application;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

DotEnv.Load();
var builder = WebApplication.CreateBuilder(args);
// Cada uno solo actúa en su sistema: servicio de Windows o unidad de systemd (Linux).
builder.Services.AddWindowsService(options => options.ServiceName = "ImpresorasService.Api");
builder.Services.AddSystemd();
var appCultureName = builder.Configuration["Globalization:Culture"] ?? "es-ES";
var appCulture = CultureInfo.GetCultureInfo(appCultureName);
CultureInfo.DefaultThreadCurrentCulture = appCulture;
CultureInfo.DefaultThreadCurrentUICulture = appCulture;

static bool IsUnsafeSecret(string? value)
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

// La base caída no es un fallo del programa: 503 con el motivo y cuándo reintentar. EstadoBd ya
// lo anota en el registro, una vez.
static Task EscribirBdNoDisponible(HttpResponse response)
{
    response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    response.Headers.RetryAfter = "10";
    return response.WriteAsJsonAsync(new
    {
        error = "La base de datos no responde. Se reconecta sola: vuelve a probar en unos segundos."
    });
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

// Solo se entra con el pase del ERP nuevo (PaseErp): quién es y qué puede hacer lo decide el ERP con sus
// operadores y permisos. Sin usuarios propios desde el 11/11/2026 (ERP_SERCA docs/72).
var erpSecret = builder.Configuration["Erp:Secret"];
if (IsUnsafeSecret(erpSecret))
    throw new InvalidOperationException("Erp:Secret es obligatorio, debe estar definido por entorno, tener al menos 32 caracteres y no usar valores inseguros/default.");
var erpKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(erpSecret!.Trim()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = erpKey,
            ValidateIssuer = true,
            ValidIssuer = PaseErp.Emisor,
            ValidateAudience = true,
            ValidAudience = PaseErp.Audiencia,
            ValidateLifetime = true,
            // El pase lo firma otro servidor (el del ERP): sin margen, unos segundos de diferencia de
            // hora lo rechazarían como caducado o «aún no válido».
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents { OnTokenValidated = PaseErp.ValidarAsync };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(RoleCatalog.Admin));
    options.AddPolicy("StoreManagerOrAdmin", policy => policy.RequireRole(RoleCatalog.Admin, RoleCatalog.StoreManager));
    options.AddPolicy("EmployeeOrAbove", policy => policy.RequireRole(RoleCatalog.Admin, RoleCatalog.StoreManager, RoleCatalog.Employee));
});

builder.Services.AddControllers();
builder.Services.AddApplication();
// Sin environmentName a propósito: el fail-fast del spooler es del Worker, que es quien imprime.
// La API no resuelve IPrinterSpooler y no debe negarse a arrancar por la config de impresión.
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ImpresorasDbContext>("database", tags: ["ready"])
    // Sin tag "ready" a propósito: un Worker parado no debe sacar a la Api del balanceador.
    .AddCheck<WorkerActivityHealthCheck>(WorkerActivityHealthCheck.Name);

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
    // El esquema lo crea scripts/crear-esquema.ps1; solo los tests lo generan desde el modelo.
    if (app.Environment.IsEnvironment("Testing"))
        await dbContext.Database.EnsureCreatedAsync();
    // Fuera de las pruebas, arrancar no toca la base: con ella caída la Api arranca, responde 503 y
    // se recupera sola.
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSecurityHeaders();

// Lee o genera X-Correlation-Id, lo devuelve en respuesta y abre scope de log (AUD-22).
// Debe ir antes de UseHttpsRedirection para que las respuestas 301 también lleven el header.
app.Use(async (context, next) =>
{
    if (!context.Request.Headers.TryGetValue("X-Correlation-Id", out var cid) || string.IsNullOrWhiteSpace(cid))
        cid = Guid.NewGuid().ToString("N");
    context.Response.Headers.Append("X-Correlation-Id", cid.ToString());
    using (app.Logger.BeginScope("CorrelationId:{CorrelationId}", cid.ToString()))
        await next(context);
});

// Base caída durante una petición: 503 con el motivo, sin traza. En cuanto responde, la siguiente
// petición funciona: no hay que reiniciar nada.
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception ex) when (FallosBd.EsFalloDeConexion(ex) && !context.Response.HasStarted)
    {
        await EscribirBdNoDisponible(context.Response);
    }
});

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
static Task WriteHealthJson(HttpContext ctx, HealthReport report)
{
    ctx.Response.ContentType = "application/json";
    return ctx.Response.WriteAsJsonAsync(new
    {
        status = report.Status == HealthStatus.Healthy ? "ok" : "degraded",
        checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
    });
}

// /health/live — solo "¿está vivo el proceso?" (sin deps externas, ideal para reinicios)
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
// /health/ready — comprueba BD; usar como señal de tráfico en balanceador
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready"),
    ResponseWriter = WriteHealthJson
});
// /health — mantiene compatibilidad hacia atrás (mismo comportamiento que antes)
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteHealthJson });
app.MapGet("/diagnostics", async (ImpresorasDbContext db) =>
{
    var conn = db.Database.GetDbConnection();
    var provider = db.Database.ProviderName ?? "unknown";
    var serverNode = conn.DataSource;
    var database = conn.Database;
    var sourceCount = await db.SourcePrintJobs.CountAsync();
    var printCount = await db.PrintJobs.CountAsync();
    var pendingSource = await db.SourcePrintJobs.CountAsync(x => !x.IsProcessed);
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

app.MapGet("/diagnostics/db", async (ImpresorasDbContext db, CancellationToken cancellationToken) =>
{
    // Misma conexión que la aplicación (ConnectionStrings:PrintQueue); el esquema es su Search Path.
    const string table = "printer_source_print_job";

    try
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);

        object? serverUtc;
        string? schema;
        await using (var pingCmd = connection.CreateCommand())
        {
            pingCmd.CommandText = "SELECT now() AT TIME ZONE 'utc', current_schema()";
            await using var pingReader = await pingCmd.ExecuteReaderAsync(cancellationToken);
            await pingReader.ReadAsync(cancellationToken);
            serverUtc = pingReader.GetValue(0);
            schema = pingReader.GetValue(1)?.ToString();
        }

        await using var columnsCmd = connection.CreateCommand();
        columnsCmd.CommandText = $@"
SELECT column_name, data_type, character_maximum_length, numeric_scale, is_nullable
FROM information_schema.columns
WHERE table_schema = current_schema() AND table_name = '{table}'
ORDER BY ordinal_position";

        var columns = new List<object>();
        await using (var reader = await columnsCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(new
                {
                    name = reader["column_name"]?.ToString(),
                    type = reader["data_type"]?.ToString(),
                    length = reader["character_maximum_length"] == DBNull.Value ? null : reader["character_maximum_length"],
                    scale = reader["numeric_scale"] == DBNull.Value ? null : reader["numeric_scale"],
                    nullable = reader["is_nullable"]?.ToString()
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
            title: "Fallo al conectar con la base de datos",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
    finally
    {
        await db.Database.CloseConnectionAsync();
    }
}).RequireAuthorization("AdminOnly");

// Raíz: Swagger en desarrollo; health en el resto de entornos
if (app.Environment.IsDevelopment())
    app.MapGet("/", () => Results.Redirect("/swagger"));
else
    app.MapGet("/", () => Results.Redirect("/health"));

app.Run();

public partial class Program { }
