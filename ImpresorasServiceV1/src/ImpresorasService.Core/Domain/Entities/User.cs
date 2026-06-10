namespace ImpresorasService.Domain.Entities;

public class User
{
    public int UserId { get; set; }
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "Supervisor";
    public int? StoreId { get; set; }
    public string? DisplayName { get; set; }
}
