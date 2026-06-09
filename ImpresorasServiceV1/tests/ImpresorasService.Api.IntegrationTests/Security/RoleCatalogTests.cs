using ImpresorasService.Api.Security;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Security;

public sealed class RoleCatalogTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("StoreManager")]
    [InlineData("Employee")]
    [InlineData("Supervisor")]
    [InlineData("admin")]
    public void IsValidForPersistence_AcceptsKnownRolesOnly(string role)
    {
        Assert.True(RoleCatalog.IsValidForPersistence(role));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("asdf")]
    [InlineData("root")]
    [InlineData("manager")]
    public void IsValidForPersistence_RejectsUnknownRoles(string? role)
    {
        Assert.False(RoleCatalog.IsValidForPersistence(role));
    }
}
