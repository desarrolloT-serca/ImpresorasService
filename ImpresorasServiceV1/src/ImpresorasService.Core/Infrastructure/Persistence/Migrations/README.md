# Migraciones EF Core (referencia histórica)

Las migraciones de esta carpeta (`BaselineOrmHana`) fueron generadas con anotaciones **SQLite** (`INTEGER`, `TEXT`, `Sqlite:Autoincrement`) y nombres de tabla del modelo ORM interno, **no** con el DDL HANA de producción (`printer_*`).

## Operación en HANA

- En producción y entornos HANA reales: **`Database:ApplyMigrations=false`** (valor por defecto en `appsettings.json`).
- El esquema HANA se aplica con scripts SQL externos (`scripts/sql/`) y procedimientos acordados con SAP.
- **No activar** `ApplyMigrations` por defecto.

## Tests

Los tests de integración usan SQLite en memoria con `EnsureCreated`, no estas migraciones.

## Tarea pendiente

Regenerar un baseline EF real alineado con HANA solo si el equipo decide gestionar el esquema ORM interno vía EF (fuera del alcance de la limpieza estructural).
