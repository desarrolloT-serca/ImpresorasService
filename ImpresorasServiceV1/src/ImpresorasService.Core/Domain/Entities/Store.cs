namespace ImpresorasService.Domain.Entities;

/// <summary>
/// Tienda: un almacén del ERP (<c>gesserca.fdalmacen</c>), leído por la vista <c>printer_store</c>
/// (scripts/sql/crear-esquema.sql). Solo lectura: las tiendas se dan de alta y de baja en el ERP.
/// </summary>
public class Store
{
    /// <summary>cod_alm del ERP.</summary>
    public int StoreId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Sin fecha de baja en el ERP.</summary>
    public bool IsActive { get; set; } = true;
}
