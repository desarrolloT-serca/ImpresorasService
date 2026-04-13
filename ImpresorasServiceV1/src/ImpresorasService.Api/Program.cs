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

static async Task EnsurePrintersTableExistsAsync(ImpresorasDbContext db)
{
    if (!db.Database.IsSqlite()) return;
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Printers'";
    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    if (count == 0)
    {
        cmd.CommandText = @"
            CREATE TABLE Printers (
                PrinterId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                PrinterName TEXT NOT NULL,
                SpoolQueue TEXT NOT NULL,
                Host TEXT NULL,
                StoreId INTEGER NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CapabilitiesJson TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IX_Printers_StoreId_SpoolQueue ON Printers(StoreId, SpoolQueue);";
        await cmd.ExecuteNonQueryAsync();
    }
}

static async Task EnsureRoutingRulesTableExistsAsync(ImpresorasDbContext db)
{
    if (!db.Database.IsSqlite()) return;
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='RoutingRules'";
    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    if (count == 0)
    {
        cmd.CommandText = @"
            CREATE TABLE RoutingRules (
                RuleId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Priority INTEGER NOT NULL,
                StoreId INTEGER NULL,
                DocumentType TEXT NULL,
                Channel TEXT NULL,
                PrinterId INTEGER NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                ValidFromUtc TEXT NOT NULL,
                ValidToUtc TEXT NULL,
                CreatedBy TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (PrinterId) REFERENCES Printers(PrinterId)
            );
            CREATE INDEX IX_RoutingRules_Resolve ON RoutingRules(IsActive, Priority, StoreId, DocumentType, Channel);";
        await cmd.ExecuteNonQueryAsync();
    }
}

static async Task EnsureUsersTableExistsAsync(ImpresorasDbContext db)
{
    if (!db.Database.IsSqlite()) return;
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Users'";
    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    if (count == 0)
    {
        cmd.CommandText = @"
            CREATE TABLE Users (
                UserId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Login TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                Role TEXT NOT NULL DEFAULT 'Employee',
                StoreId INTEGER NULL,
                DisplayName TEXT NULL
            );
            CREATE UNIQUE INDEX IX_Users_Login ON Users(Login);";
        await cmd.ExecuteNonQueryAsync();
    }
}

static async Task EnsureStoresTableExistsAsync(ImpresorasDbContext db)
{
    if (!db.Database.IsSqlite()) return;
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Stores'";
    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    if (count == 0)
    {
        cmd.CommandText = @"
            CREATE TABLE Stores (
                StoreId INTEGER NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE INDEX IX_Stores_Name ON Stores(Name);";
        await cmd.ExecuteNonQueryAsync();
    }
}

static async Task EnsureDashboardThresholdsTableExistsAsync(ImpresorasDbContext db)
{
    if (!db.Database.IsSqlite()) return;
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DashboardThresholds'";
    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    if (count == 0)
    {
        cmd.CommandText = @"
            CREATE TABLE DashboardThresholds (
                Id INTEGER NOT NULL PRIMARY KEY,
                WarningQueueMin INTEGER NOT NULL DEFAULT 10,
                CriticalQueueMin INTEGER NOT NULL DEFAULT 30,
                QueueWarningSeverity TEXT NOT NULL DEFAULT 'warning',
                QueueCriticalSeverity TEXT NOT NULL DEFAULT 'critical',
                WarningFailedWithoutRetryMin INTEGER NOT NULL DEFAULT 1,
                CriticalFailedWithoutRetryMin INTEGER NOT NULL DEFAULT 5,
                FailedWarningSeverity TEXT NOT NULL DEFAULT 'warning',
                FailedCriticalSeverity TEXT NOT NULL DEFAULT 'critical',
                MissingHostMin INTEGER NOT NULL DEFAULT 1,
                MissingHostSeverity TEXT NOT NULL DEFAULT 'warning',
                ConnWarningFailuresMin INTEGER NOT NULL DEFAULT 2,
                ConnCriticalFailuresMin INTEGER NOT NULL DEFAULT 3,
                ConnWarningSeverity TEXT NOT NULL DEFAULT 'warning',
                ConnCriticalSeverity TEXT NOT NULL DEFAULT 'critical',
                UpdatedAtUtc TEXT NOT NULL
            );
            INSERT INTO DashboardThresholds
                (Id, WarningQueueMin, CriticalQueueMin, QueueWarningSeverity, QueueCriticalSeverity,
                 WarningFailedWithoutRetryMin, CriticalFailedWithoutRetryMin, FailedWarningSeverity, FailedCriticalSeverity,
                 MissingHostMin, MissingHostSeverity, ConnWarningFailuresMin, ConnCriticalFailuresMin, ConnWarningSeverity, ConnCriticalSeverity,
                 UpdatedAtUtc)
            VALUES
                (1, 10, 30, 'warning', 'critical',
                 1, 5, 'warning', 'critical',
                 1, 'warning', 2, 3, 'warning', 'critical',
                 CURRENT_TIMESTAMP);";
        await cmd.ExecuteNonQueryAsync();
    }
}

static async Task EnsureDashboardThresholdsColumnsAsync(ImpresorasDbContext db)
{
    if (!db.Database.IsSqlite()) return;
    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();

    async Task EnsureColumnAsync(string columnName, string ddl)
    {
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('DashboardThresholds') WHERE name='{columnName}'";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        if (count == 0)
        {
            cmd.CommandText = ddl;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    await EnsureColumnAsync("QueueWarningSeverity", "ALTER TABLE DashboardThresholds ADD COLUMN QueueWarningSeverity TEXT NOT NULL DEFAULT 'warning'");
    await EnsureColumnAsync("QueueCriticalSeverity", "ALTER TABLE DashboardThresholds ADD COLUMN QueueCriticalSeverity TEXT NOT NULL DEFAULT 'critical'");
    await EnsureColumnAsync("FailedWarningSeverity", "ALTER TABLE DashboardThresholds ADD COLUMN FailedWarningSeverity TEXT NOT NULL DEFAULT 'warning'");
    await EnsureColumnAsync("FailedCriticalSeverity", "ALTER TABLE DashboardThresholds ADD COLUMN FailedCriticalSeverity TEXT NOT NULL DEFAULT 'critical'");
    await EnsureColumnAsync("MissingHostMin", "ALTER TABLE DashboardThresholds ADD COLUMN MissingHostMin INTEGER NOT NULL DEFAULT 1");
    await EnsureColumnAsync("MissingHostSeverity", "ALTER TABLE DashboardThresholds ADD COLUMN MissingHostSeverity TEXT NOT NULL DEFAULT 'warning'");
    await EnsureColumnAsync("ConnWarningFailuresMin", "ALTER TABLE DashboardThresholds ADD COLUMN ConnWarningFailuresMin INTEGER NOT NULL DEFAULT 2");
    await EnsureColumnAsync("ConnCriticalFailuresMin", "ALTER TABLE DashboardThresholds ADD COLUMN ConnCriticalFailuresMin INTEGER NOT NULL DEFAULT 3");
    await EnsureColumnAsync("ConnWarningSeverity", "ALTER TABLE DashboardThresholds ADD COLUMN ConnWarningSeverity TEXT NOT NULL DEFAULT 'warning'");
    await EnsureColumnAsync("ConnCriticalSeverity", "ALTER TABLE DashboardThresholds ADD COLUMN ConnCriticalSeverity TEXT NOT NULL DEFAULT 'critical'");
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

static async Task EnsurePrintJobPrinterIdColumnAsync(ImpresorasDbContext db)
{
    if (!db.Database.IsSqlite()) return;
    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('PrintJobs') WHERE name='PrinterId'";
    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    if (count == 0)
    {
        cmd.CommandText = "ALTER TABLE PrintJobs ADD COLUMN PrinterId INTEGER NULL";
        await cmd.ExecuteNonQueryAsync();
    }
}

static async Task EnsurePrinterHostColumnAsync(ImpresorasDbContext db)
{
    if (!db.Database.IsSqlite()) return;
    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Printers') WHERE name='Host'";
    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    if (count == 0)
    {
        cmd.CommandText = "ALTER TABLE Printers ADD COLUMN Host TEXT NULL";
        await cmd.ExecuteNonQueryAsync();
    }
}

static async Task EnsurePrinterConnectivityColumnsAsync(ImpresorasDbContext db)
{
    if (!db.Database.IsSqlite()) return;
    var conn = db.Database.GetDbConnection();
    if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();

    async Task EnsureColumnAsync(string columnName, string ddl)
    {
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Printers') WHERE name='{columnName}'";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        if (count == 0)
        {
            cmd.CommandText = ddl;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    await EnsureColumnAsync("ConnectionFailuresStreak",
        "ALTER TABLE Printers ADD COLUMN ConnectionFailuresStreak INTEGER NOT NULL DEFAULT 0");
    await EnsureColumnAsync("LastConnectionOk",
        "ALTER TABLE Printers ADD COLUMN LastConnectionOk INTEGER NULL");
    await EnsureColumnAsync("LastConnectionCheckAtUtc",
        "ALTER TABLE Printers ADD COLUMN LastConnectionCheckAtUtc TEXT NULL");
    await EnsureColumnAsync("LastConnectionTransport",
        "ALTER TABLE Printers ADD COLUMN LastConnectionTransport TEXT NULL");
    await EnsureColumnAsync("LastConnectionError",
        "ALTER TABLE Printers ADD COLUMN LastConnectionError TEXT NULL");
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
if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new InvalidOperationException("Jwt:Secret es obligatorio.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
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
    dbContext.Database.EnsureCreated();
    await EnsurePrintersTableExistsAsync(dbContext);
    await EnsureRoutingRulesTableExistsAsync(dbContext);
    await EnsureUsersTableExistsAsync(dbContext);
    await EnsureStoresTableExistsAsync(dbContext);
    await EnsureDashboardThresholdsTableExistsAsync(dbContext);
    await EnsureDashboardThresholdsColumnsAsync(dbContext);
    await EnsurePrintJobPrinterIdColumnAsync(dbContext);
    await SqliteSchemaPatches.EnsurePrintJobsRowVersionColumnAsync(dbContext);
    await SqliteSchemaPatches.EnsureSourcePrintJobsClaimColumnsAsync(dbContext);
    await EnsurePrinterHostColumnAsync(dbContext);
    await EnsurePrinterConnectivityColumnsAsync(dbContext);
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
