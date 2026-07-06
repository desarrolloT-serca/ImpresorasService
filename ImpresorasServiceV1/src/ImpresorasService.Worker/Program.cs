using ImpresorasService.Application;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Worker;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

var builder = Host.CreateApplicationBuilder(args);
var appCultureName = builder.Configuration["Globalization:Culture"] ?? "es-ES";
var appCulture = CultureInfo.GetCultureInfo(appCultureName);
CultureInfo.DefaultThreadCurrentCulture = appCulture;
CultureInfo.DefaultThreadCurrentUICulture = appCulture;

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<IngestionBackgroundService>();
builder.Services.AddHostedService<PrintExecutionBackgroundService>();
builder.Services.AddHostedService<SpoolAcceptedWatchdogBackgroundService>();
builder.Services.AddHostedService<PrinterConnectivityMonitorService>();
builder.Services.AddHostedService<StoreHealthAlertBackgroundService>();

var host = builder.Build();

await using (var scope = host.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
    var conn = dbContext.Database.GetDbConnection();
    var server = conn.ConnectionString?.Split(';').Select(s => s.Trim())
        .FirstOrDefault(s => s.StartsWith("Server=", StringComparison.OrdinalIgnoreCase))
        ?.Substring("Server=".Length).Trim() ?? "?";
    var schema = conn.ConnectionString?.Split(';').Select(s => s.Trim())
        .FirstOrDefault(s => s.StartsWith("Current Schema=", StringComparison.OrdinalIgnoreCase))
        ?.Substring("Current Schema=".Length).Trim() ?? "?";
    var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ImpresorasService.Worker.IngestionBackgroundService>>();
    logger.LogInformation("Worker usando HANA Server={Server} Schema={Schema}", server, schema);

    var applyMigrations = host.Services.GetRequiredService<IConfiguration>().GetValue<bool>("Database:ApplyMigrations");
    if (applyMigrations)
        await dbContext.Database.MigrateAsync();

    var activeOnly = true;
    var rulesCount = await dbContext.RoutingRules.CountAsync(r => r.IsActive == activeOnly);
    logger.LogInformation("Reglas de enrutado activas: {Count}", rulesCount);
}

await host.RunAsync();
