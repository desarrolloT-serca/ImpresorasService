# Spike tecnico HANA ORM (Fase 0)

## Alcance del spike
- Validar wiring de proveedor HANA en `DbContext`.
- Validar compilacion con configuracion `Database:Provider=Hana`.
- Preparar baseline de migraciones EF para quitar `EnsureCreated` y DDL manual.

## Resultado
- `GO` para continuar con migracion controlada.
- Se reemplazo el arranque con `Database.Migrate()` en API/Worker.
- Se genero baseline de migraciones EF en `Infrastructure/Persistence/Migrations`.
- Se activo configuracion HANA por defecto en `appsettings` de API y Worker.
- Se validaron fixes de Sprint 3 para rendimiento:
  - `GET /api/printjobs` con `OrderBy/Take` en SQL,
  - monitor de conectividad con guardado por lote (sin `SaveChanges` por impresora).

## Hallazgo relevante
- En este entorno el metodo `UseHana` no esta disponible en compilacion estatica.
- Mitigacion aplicada: resolucion por reflexion en runtime (`ConfigureHanaProvider`).
- Si el proveedor SAP no esta cargado/licenciado, el sistema falla rapido con mensaje explicito.

## Riesgos abiertos
- Validacion funcional contra instancia HANA real aun pendiente (conexion, permisos, rendimiento).
- Ajustes de SQL generado por proveedor HANA deben verificarse en preproduccion.
- Validar estrategia final de despliegue del runtime nativo de HANA (cliente SAP).
- El adapter `SapHana` actual opera contra `SourcePrintJobs` del ORM local; no existe aun un lector SQL remoto dedicado a tabla externa HANA.

## Criterio de salida del spike
- Solucion compila y arranca en modo migraciones sin `EnsureCreated`.
- Adapter de origen HANA tiene flujo claim/lease/ack operativo sobre modelo ORM.
- Existe baseline de esquema reproducible por migraciones.

## Decision actual (transparencia operativa)
- **Produccion objetivo:** `Database:Provider=Hana` en entornos con provider SAP correctamente instalado/licenciado.
- **Local recomendado:** `Database:Provider=Sqlite` + `Source:Mode=SqlTest`.
- **Pendiente para cerrar migracion HANA real:** decidir si se mantiene enfoque ORM actual para dual-run o se implementa adapter SQL remoto dedicado.
