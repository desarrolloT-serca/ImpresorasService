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

    public static string Normalize(string? role)
    {
        if (string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase))
            return Admin;

        if (string.Equals(role, StoreManager, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, LegacySupervisor, StringComparison.OrdinalIgnoreCase))
            return StoreManager;

        if (string.Equals(role, Employee, StringComparison.OrdinalIgnoreCase))
            return Employee;

        return Employee;
    }

    public static bool IsValidForPersistence(string? role)
    {
        return AllowedRoles.Contains(Normalize(role), StringComparer.OrdinalIgnoreCase);
    }
}
