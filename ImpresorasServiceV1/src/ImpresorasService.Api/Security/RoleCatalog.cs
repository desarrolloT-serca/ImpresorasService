namespace ImpresorasService.Api.Security;

public static class RoleCatalog
{
    public const string Admin = "Admin";
    public const string StoreManager = "StoreManager";
    public const string Employee = "Employee";
    public const string LegacySupervisor = "Supervisor";

    public static readonly string[] AllowedRoles =
    [
        Admin,
        StoreManager,
        Employee
    ];

    // Legacy fallback: keep this for persisted/historical roles and claims. For new input validation use TryNormalize.
    public static string Normalize(string? role)
    {
        return TryNormalize(role, out var normalized)
            ? normalized
            : Employee;
    }

    public static bool TryNormalize(string? role, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(role))
            return false;

        var trimmed = role.Trim();

        if (string.Equals(trimmed, Admin, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Admin;
            return true;
        }

        if (string.Equals(trimmed, StoreManager, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, LegacySupervisor, StringComparison.OrdinalIgnoreCase))
        {
            normalized = StoreManager;
            return true;
        }

        if (string.Equals(trimmed, Employee, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Employee;
            return true;
        }

        return false;
    }

    public static bool IsValidForPersistence(string? role)
    {
        return TryNormalize(role, out _);
    }
}
