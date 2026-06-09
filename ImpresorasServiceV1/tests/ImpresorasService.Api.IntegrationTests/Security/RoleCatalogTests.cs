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

    [Theory]
    [InlineData("Admin", "Admin")]
    [InlineData("StoreManager", "StoreManager")]
    [InlineData("Supervisor", "StoreManager")]
    [InlineData("Employee", "Employee")]
    [InlineData(" admin ", "Admin")]
    public void TryNormalize_ReturnsSafeKnownRole(string role, string expected)
    {
        var success = RoleCatalog.TryNormalize(role, out var normalized);

        Assert.True(success);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("root")]
    public void TryNormalize_RejectsUnknownRole(string? role)
    {
        var success = RoleCatalog.TryNormalize(role, out var normalized);

        Assert.False(success);
        Assert.Equal(string.Empty, normalized);
    }
}
