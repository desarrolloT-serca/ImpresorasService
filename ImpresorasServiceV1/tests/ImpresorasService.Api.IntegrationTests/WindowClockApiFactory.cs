using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// Factory con reloj controlable (<see cref="ManualTimeProvider"/>) para tests deterministas
/// de ventanas temporales del dashboard (F1.4, docs/roadmap-kpi-dashboard.md).
/// </summary>
public sealed class WindowClockApiFactory : ApiWebApplicationFactory
{
    public ManualTimeProvider Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}
