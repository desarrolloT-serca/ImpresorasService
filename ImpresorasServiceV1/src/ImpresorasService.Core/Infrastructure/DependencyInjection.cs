using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Options;
using ImpresorasService.Infrastructure.Adapters;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Repositories;
using ImpresorasService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

namespace ImpresorasService.Infrastructure;

public static class DependencyInjection
{
    /// <param name="environmentName">
    /// Entorno del host. Solo lo pasa el Worker: activa el fail-fast que impide arrancar en
    /// Production con el spooler simulado. La API no imprime y lo omite.
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string? environmentName = null)
    {
        // Fase 1.9: TimeProvider inyectable en los servicios de ejecución/watchdog/ingesta/alertas
        // en vez de DateTimeOffset.UtcNow directo, para habilitar tests deterministas de tiempo.
        services.AddSingleton(TimeProvider.System);

        services.Configure<SourceOptions>(configuration.GetSection(SourceOptions.SectionName));
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));
        services.AddOptions<PrintExecutionOptions>()
            .Bind(configuration.GetSection(PrintExecutionOptions.SectionName))
            // Fase 1.6: fallar rápido al arrancar en vez de IndexOutOfRangeException en el
            // primer reintento (BackoffSeconds[Math.Min(intento-1, Length-1)] con Length=0).
            .Validate(o => o.BackoffSeconds is { Length: > 0 }, "PrintExecution:BackoffSeconds no puede estar vacío.")
            .Validate(o => o.BackoffSeconds is null || o.BackoffSeconds.All(s => s >= 0), "PrintExecution:BackoffSeconds no admite valores negativos.")
            .Validate(o => o.MaxAttempts > 0, "PrintExecution:MaxAttempts debe ser mayor que 0.")
            .ValidateOnStart();

        string connectionString = configuration.GetConnectionString("PrintQueue") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:PrintQueue es obligatorio (PostgreSQL: Host, Port, Database, Username, Search Path).");

        services.AddSingleton<EstadoBd>();
        services.AddDbContext<ImpresorasDbContext>((sp, options) => options
            .UseNpgsql(connectionString)
            .AddInterceptors(new EstadoBdInterceptor(sp.GetRequiredService<EstadoBd>()))
            // Los cortes de la base los anota EstadoBd una vez, con su motivo; estos avisos de EF
            // repetían la traza en cada petición y en cada ciclo del Worker. Los errores que no son
            // de conexión siguen apareciendo: los registran Kestrel y los bucles del Worker.
            .ConfigureWarnings(w => w.Log(
                (RelationalEventId.ConnectionError, LogLevel.Debug),
                (RelationalEventId.CommandError, LogLevel.Debug),
                (CoreEventId.QueryIterationFailed, LogLevel.Debug),
                (CoreEventId.SaveChangesFailed, LogLevel.Debug))));

        services.AddScoped<IPrintJobRepository, PrintJobRepository>();
        services.AddScoped<IJobSourceAdapter, ErpJobSourceAdapter>();
        services.AddScoped<IRoutingResolver, RoutingResolver>();
        services.AddScoped<IRoutingService, RoutingService>();

        // El spooler simulado devuelve éxito siempre: un job simulado avanza a SpoolAccepted y de
        // ahí a PrintedConfirmed sin que salga una hoja. Nunca debe activarse por inferencia
        // (plataforma equivocada, sección de config ausente); solo por decisión explícita.
        var wantsRealSpooler = configuration.GetValue<bool>("PrintExecution:UseRealSpooler");
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var isProduction = string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);

        if (wantsRealSpooler && !isWindows)
            throw new InvalidOperationException(
                "PrintExecution:UseRealSpooler=true pero el host no es Windows. El spooler real requiere Windows; " +
                "arrancar aquí degradaría a simulación y marcaría como impresos trabajos que nunca salieron.");

        if (isProduction && !wantsRealSpooler)
            throw new InvalidOperationException(
                "PrintExecution:UseRealSpooler=false en Production. La simulación marca como impresos trabajos " +
                "que nunca salen por papel. Active el spooler real o despliegue fuera de Production.");

        if (wantsRealSpooler)
            services.AddScoped<IPrinterSpooler, WindowsPrintSpooler>();
        else
            services.AddSingleton<IPrinterSpooler>(new NoOpPrintSpooler(simulateSuccess: true));

        services.AddScoped<IPrintExecutionService, PrintExecutionService>();
        services.AddSingleton<IIppConfirmationService, IppConfirmationService>();

        services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.SectionName));
        services.AddSingleton<ITelegramNotifier, TelegramNotifierService>();

        services.Configure<PdfRetentionOptions>(configuration.GetSection(PdfRetentionOptions.SectionName));

        services.Configure<WorkerLockOptions>(configuration.GetSection(WorkerLockOptions.SectionName));
        services.AddScoped<IWorkerLockCoordinator, WorkerLockCoordinator>();

        services.Configure<DashboardThresholdRulesOptions>(configuration.GetSection(DashboardThresholdRulesOptions.SectionName));
        services.AddSingleton<IDashboardThresholdRuleStore, DashboardThresholdRuleStore>();

        return services;
    }
}
