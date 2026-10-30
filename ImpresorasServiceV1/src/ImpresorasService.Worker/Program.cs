using ImpresorasService.Application;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Worker;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

DotEnv.Load();
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "ImpresorasService.Worker");
// Parar espera al trabajo que se está enviando (auditoría M1): el plazo de un envío y margen para la
// consulta previa. ponytail: no cubre PDF, imagen y cola encadenados (hasta 3 plazos); eso acaba en
// PRINTING_INTERRUPTED, sin duplicar. Un plazo único para todo el envío si molesta.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout =
    TimeSpan.FromSeconds(builder.Configuration.GetValue("PrintExecution:TimeoutSeconds", 120) + 30));
var appCultureName = builder.Configuration["Globalization:Culture"] ?? "es-ES";
var appCulture = CultureInfo.GetCultureInfo(appCultureName);
CultureInfo.DefaultThreadCurrentCulture = appCulture;
CultureInfo.DefaultThreadCurrentUICulture = appCulture;

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddSingleton<WorkerLockState>();
builder.Services.AddHostedService<WorkerLockBackgroundService>();
builder.Services.AddHostedService<IngestionBackgroundService>();
builder.Services.AddHostedService<PrintExecutionBackgroundService>();
builder.Services.AddHostedService<SpoolAcceptedWatchdogBackgroundService>();
builder.Services.AddHostedService<PrinterConnectivityMonitorService>();
builder.Services.AddHostedService<StoreHealthAlertBackgroundService>();
builder.Services.AddHostedService<PdfRetentionBackgroundService>();

var host = builder.Build();

await using (var scope = host.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
    var conn = dbContext.Database.GetDbConnection();
    var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ImpresorasService.Worker.IngestionBackgroundService>>();
    logger.LogInformation("Worker usando PostgreSQL Host={Host} Database={Database}", conn.DataSource, conn.Database);

    // Sin base al arrancar no se cae (auditoría L17): los servicios esperan y se reconectan solos
    // (EstadoBd anota el corte y su vuelta), como cuando la base cae con el Worker ya en marcha.
    try
    {
        var rulesCount = await dbContext.RoutingRules.CountAsync(r => r.IsActive);
        logger.LogInformation("Reglas de enrutado activas: {Count}", rulesCount);
    }
    catch (Exception ex) when (FallosBd.EsFalloDeConexion(ex))
    {
        logger.LogWarning("La base de datos no responde al arrancar; el Worker sigue y se reconectará solo.");
    }

    // En Production esto es inalcanzable (AddInfrastructure ya habría fallado), pero fuera de
    // Production la simulación es legítima y debe verse: un "impreso" de esta instancia no es papel.
    if (!builder.Configuration.GetValue<bool>("PrintExecution:UseRealSpooler"))
        logger.LogWarning("SPOOLER EN SIMULACION: no se envia nada a impresoras reales. Los trabajos avanzaran a SpoolAccepted sin efecto fisico.");
}

await host.RunAsync();
