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
using System.Reflection;
using System.Runtime.InteropServices;

namespace ImpresorasService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SourceOptions>(configuration.GetSection(SourceOptions.SectionName));
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));
        services.Configure<PrintExecutionOptions>(configuration.GetSection(PrintExecutionOptions.SectionName));
        services.Configure<SapPostgresOptions>(configuration.GetSection(SapPostgresOptions.SectionName));
        services.Configure<SapHanaOptions>(configuration.GetSection(SapHanaOptions.SectionName));

        string provider = configuration.GetValue<string>("Database:Provider") ?? "Hana";
        string connectionString = configuration.GetConnectionString("PrintQueue")
            ?? "Data Source=impresoras-local.db";

        services.AddDbContext<ImpresorasDbContext>(options =>
        {
            if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connectionString);
            }
            else if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connectionString);
            }
            else
            {
                ConfigureHanaProvider(options, connectionString);
            }
        });

        services.AddScoped<IPrintJobRepository, PrintJobRepository>();
        services.AddScoped<SqlTestJobSourceAdapter>();
        services.AddScoped<SapHanaJobSourceAdapter>();
        services.AddScoped<SapPostgresJobSourceAdapter>();
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

        return services;
    }

    private static void ConfigureHanaProvider(DbContextOptionsBuilder options, string connectionString)
    {
        var hanaUseMethod = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsSealed && t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(m =>
            {
                if (!string.Equals(m.Name, "UseHana", StringComparison.Ordinal))
                    return false;

                var parameters = m.GetParameters();
                return parameters.Length >= 2
                       && parameters[0].ParameterType == typeof(DbContextOptionsBuilder)
                       && parameters[1].ParameterType == typeof(string);
            });

        if (hanaUseMethod is null)
            throw new InvalidOperationException(
                "No se encontró el proveedor EF de SAP HANA en runtime. Revise instalación/licencia del provider SAP.");

        hanaUseMethod.Invoke(null, [options, connectionString]);
    }
}
