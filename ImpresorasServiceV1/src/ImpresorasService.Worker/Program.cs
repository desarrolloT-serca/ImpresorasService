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
    await dbContext.Database.MigrateAsync();
    var rulesCount = await dbContext.RoutingRules.CountAsync(r => r.IsActive);
    logger.LogInformation("Reglas de enrutado activas: {Count}", rulesCount);
}

await host.RunAsync();
