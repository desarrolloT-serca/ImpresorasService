using System.IO;
using ImpresorasService.Application;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<IngestionBackgroundService>();
builder.Services.AddHostedService<PrintExecutionBackgroundService>();
builder.Services.AddHostedService<SpoolAcceptedWatchdogBackgroundService>();
builder.Services.AddHostedService<PrinterConnectivityMonitorService>();

var host = builder.Build();

await using (var scope = host.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
    var conn = dbContext.Database.GetDbConnection();
    var ds = conn.ConnectionString?.Split(';').Select(s => s.Trim())
        .FirstOrDefault(s => s.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        ?.Substring("Data Source=".Length).Trim();
    var fullPath = string.IsNullOrEmpty(ds) ? "?" : Path.GetFullPath(ds);
    var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ImpresorasService.Worker.IngestionBackgroundService>>();
    logger.LogInformation("Worker usando BD: {Path}", fullPath);
    dbContext.Database.EnsureCreated();
    var rulesCount = await dbContext.RoutingRules.CountAsync(r => r.IsActive);
    logger.LogInformation("Reglas de enrutado activas: {Count}", rulesCount);
    await EnsurePrintersTableExistsAsync(dbContext);
    await EnsureRoutingRulesTableExistsAsync(dbContext);
    await EnsurePrintJobPrinterIdColumnAsync(dbContext);
    await SqliteSchemaPatches.EnsurePrintJobsRowVersionColumnAsync(dbContext);
    await SqliteSchemaPatches.EnsureSourcePrintJobsClaimColumnsAsync(dbContext);
    await EnsurePrinterHostColumnAsync(dbContext);
    await EnsurePrinterConnectivityColumnsAsync(dbContext);
}

await host.RunAsync();

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
