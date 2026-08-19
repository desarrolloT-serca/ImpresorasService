namespace ImpresorasService.Domain.Entities;

public class User
{
    public int UserId { get; set; }
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "Supervisor";
    public int? StoreId { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>
    /// Un usuario desactivado deja de servir en la siguiente petición, sin esperar a que caduque su
    /// token. Antes de esto, la única forma de cortar el acceso era borrarlo y aun así el token
    /// emitido seguía valiendo hasta 8 horas.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Se incrementa al cambiar la contraseña. El token lleva el valor con el que se emitió (claim
    /// <c>tv</c>) y deja de validar en cuanto no coincide: cambiar la contraseña cierra las sesiones
    /// abiertas, que es lo que se espera cuando se cambia porque se sospecha que estaba comprometida.
    /// </summary>
    public int TokenVersion { get; set; }
}
