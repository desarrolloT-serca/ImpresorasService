using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Options;
using ImpresorasService.Infrastructure.Adapters;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Repositories;
using ImpresorasService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ImpresorasService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
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
        services.Configure<SapHanaOptions>(configuration.GetSection(SapHanaOptions.SectionName));

        string provider = configuration.GetValue<string>("Database:Provider") ?? "Hana";
        string connectionString = configuration.GetConnectionString("PrintQueue") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:PrintQueue es obligatorio para el proveedor de base de datos.");

        services.AddDbContext<ImpresorasDbContext>(options =>
        {
            if (string.Equals(provider, "Hana", StringComparison.OrdinalIgnoreCase))
            {
                ConfigureHanaProvider(options, connectionString);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Proveedor de base de datos no soportado: '{provider}'. Use 'Hana'.");
            }
        });

        services.AddScoped<IPrintJobRepository, PrintJobRepository>();
        services.AddScoped<SapHanaJobSourceAdapter>();
        services.AddScoped<IJobSourceAdapter, ConfigurableJobSourceAdapter>();
        services.AddScoped<IRoutingResolver, RoutingResolver>();
        services.AddScoped<IRoutingService, RoutingService>();

        var useRealSpooler = configuration.GetValue<bool>("PrintExecution:UseRealSpooler")
            && RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (useRealSpooler)
            services.AddScoped<IPrinterSpooler, WindowsPrintSpooler>();
        else
            services.AddSingleton<IPrinterSpooler>(new NoOpPrintSpooler(simulateSuccess: true));

        services.AddScoped<IPrintExecutionService, PrintExecutionService>();
        services.AddSingleton<IIppConfirmationService, IppConfirmationService>();

        services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.SectionName));
        services.AddSingleton<ITelegramNotifier, TelegramNotifierService>();

        services.Configure<WorkerLockOptions>(configuration.GetSection(WorkerLockOptions.SectionName));
        services.AddScoped<IWorkerLockCoordinator, WorkerLockCoordinator>();

        services.Configure<DashboardThresholdRulesOptions>(configuration.GetSection(DashboardThresholdRulesOptions.SectionName));
        services.AddSingleton<IDashboardThresholdRuleStore, DashboardThresholdRuleStore>();

        return services;
    }

    private static void ConfigureHanaProvider(DbContextOptionsBuilder options, string connectionString)
    {
        var hanaAssembly = Assembly.Load("Sap.EntityFrameworkCore.Hana.v8.0");
        var extensionTypeNames = new[]
        {
            "Microsoft.EntityFrameworkCore.HanaDbContextOptionsBuilderExtensions",
            "Sap.EntityFrameworkCore.Hana.Infrastructure.HanaDbContextOptionsBuilderExtensions"
        };

        Type? extensionsType = extensionTypeNames
            .Select(hanaAssembly.GetType)
            .FirstOrDefault(t => t is not null);

        if (extensionsType is null)
        {
            extensionsType = hanaAssembly
                .GetTypes()
                .FirstOrDefault(t => t.Name == "HanaDbContextOptionsBuilderExtensions");
        }

        var hanaMethods = extensionsType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m =>
            {
                if (!string.Equals(m.Name, "UseHana", StringComparison.Ordinal)
                    && !string.Equals(m.Name, "AddHana", StringComparison.Ordinal))
                    return false;

                var parameters = m.GetParameters();
                return parameters.Length >= 1
                       && typeof(DbContextOptionsBuilder).IsAssignableFrom(parameters[0].ParameterType);
            })
            .OrderByDescending(m => m.GetParameters().Any(p => p.ParameterType == typeof(string)))
            .ThenByDescending(m => m.GetParameters().Length)
            .ToArray();

        var hanaUseMethod = hanaMethods?.FirstOrDefault();

        if (hanaUseMethod is null)
            throw new InvalidOperationException(
                "No se encontró el proveedor EF de SAP HANA en runtime. Revise instalación/licencia del provider SAP.");

        var invokeArgs = hanaUseMethod.GetParameters()
            .Select((p, idx) =>
            {
                if (idx == 0)
                    return (object)options;

                if (p.ParameterType == typeof(string))
                    return (object)connectionString;

                if (p.HasDefaultValue)
                    return p.DefaultValue;

                if (!p.ParameterType.IsValueType)
                    return null;

                return Activator.CreateInstance(p.ParameterType);
            })
            .ToArray();

        hanaUseMethod.Invoke(null, invokeArgs);
    }
}
