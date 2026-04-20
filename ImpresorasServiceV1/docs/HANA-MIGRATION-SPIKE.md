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

## Hallazgo relevante
- En este entorno el metodo `UseHana` no esta disponible en compilacion estatica.
- Mitigacion aplicada: resolucion por reflexion en runtime (`ConfigureHanaProvider`).
- Si el proveedor SAP no esta cargado/licenciado, el sistema falla rapido con mensaje explicito.

## Riesgos abiertos
- Validacion funcional contra instancia HANA real aun pendiente (conexion, permisos, rendimiento).
- Ajustes de SQL generado por proveedor HANA deben verificarse en preproduccion.
- Validar estrategia final de despliegue del runtime nativo de HANA (cliente SAP).

## Criterio de salida del spike
- Solucion compila y arranca en modo migraciones sin `EnsureCreated`.
- Adapter de origen HANA tiene flujo claim/lease/ack operativo sobre modelo ORM.
- Existe baseline de esquema reproducible por migraciones.
