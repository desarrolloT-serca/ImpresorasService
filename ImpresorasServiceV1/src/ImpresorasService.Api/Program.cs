using ImpresorasService.Application;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
                Role TEXT NOT NULL DEFAULT 'Supervisor',
                StoreId INTEGER NULL,
                DisplayName TEXT NULL
            );
            CREATE UNIQUE INDEX IX_Users_Login ON Users(Login);";
        await cmd.ExecuteNonQueryAsync();
    }
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
            Role = "Admin",
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
            Role = "Supervisor",
            StoreId = 1,
            DisplayName = "Supervisor Tienda 1"
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

// Add services to the container.

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
    await EnsurePrintJobPrinterIdColumnAsync(dbContext);
    await SeedDefaultUsersAsync(dbContext);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Redirigir raíz a Swagger para que el navegador muestre algo
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

public partial class Program { }
