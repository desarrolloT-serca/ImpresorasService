using ImpresorasService.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ImpresorasService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IngestionService>();
        return services;
    }
}
