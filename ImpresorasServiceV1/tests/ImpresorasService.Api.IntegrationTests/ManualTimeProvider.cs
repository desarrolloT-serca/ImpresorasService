namespace ImpresorasService.Api.IntegrationTests;

/// <summary>Reloj controlable para tests que necesitan fijar "ahora" de forma determinista.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;

    public override DateTimeOffset GetUtcNow() => _utcNow;
}
