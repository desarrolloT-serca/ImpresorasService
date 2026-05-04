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
        string connectionString = configuration.GetConnectionString("PrintQueue") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:PrintQueue es obligatorio para el proveedor de base de datos.");

        services.AddDbContext<ImpresorasDbContext>(options =>
        {
            if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connectionString);
            }
            else if (string.Equals(provider, "Hana", StringComparison.OrdinalIgnoreCase))
            {
                ConfigureHanaProvider(options, connectionString);
            }
            else
            {
                throw new InvalidOperationException($"Proveedor de base de datos no soportado: '{provider}'. Use 'Hana' o 'SqlServer'.");
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
