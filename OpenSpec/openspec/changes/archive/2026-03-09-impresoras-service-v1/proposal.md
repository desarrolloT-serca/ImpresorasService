## Why

La operacion de impresion actual depende de SAP y de PCs, lo que genera puntos unicos de fallo, diagnostico lento y riesgo de perdida/reimpresion duplicada en escenarios de red inestable entre tiendas.

Se necesita una V1 que centralice el flujo completo, permita operar por tienda con permisos de AD y garantice trazabilidad total por trabajo.

## What Changes

### 1) Plataforma centralizada de impresion
- Servicio backend en .NET 8 (Worker + API) para ingesta, enrutado, ejecucion y auditoria.
- Panel Blazor Server para operacion intranet (Admin y Supervisor).
- Base de datos interna para cola y auditoria:
  - SQL Server como objetivo productivo V1.
  - SQLite permitido en desarrollo local para pruebas rapidas sin infraestructura adicional.

### 2) Ingesta flexible de origen
- Polling cada 5 segundos sobre origen de trabajos.
- Arquitectura por adaptadores para soportar SQL de pruebas y SAP HANA sin reescribir la logica de negocio.
- Persistencia de PDF en BLOB en la base interna.

### 3) Control estricto de no duplicados
- Idempotencia por `SourceSystem + ExternalJobId`.
- Verificacion adicional por hash de contenido para reforzar casos limite.
- Reglas de concurrencia para evitar doble reimpresion manual por acciones simultaneas.

### 4) Enrutado, impresion y reintentos
- Resolucion de impresora por prioridad:
  1. Tienda + TipoDocumento + Canal
  2. Tienda + TipoDocumento
  3. Tienda
  4. Global
- Envio por Windows Print Spooler con timeout de 30 segundos por intento.
- Reintentos con backoff de 15/30/60/90 segundos.

### 5) Seguridad, operacion y auditoria
- Autenticacion AD (Negotiate) y autorizacion por roles.
- Supervisor limitado a su tienda, Admin con alcance global.
- Alertas en panel al entrar en `ErrorFinal`.
- Historial completo por job/documento y exportacion CSV.

## Capabilities

### New Capabilities
- `sap-printing-service`: capacidad principal de ingesta, enrutado, impresion, operacion y auditoria del servicio.

## Impact

### New Files
- `openspec/changes/impresoras-service-v1/specs/sap-printing-service/spec.md`
- `openspec/changes/impresoras-service-v1/design.md`
- `openspec/changes/impresoras-service-v1/tasks.md`

### Supporting project docs (already created)
- `docs/impresoras-service-v1/open-spec.md`
- `docs/impresoras-service-v1/state-machine-printjob.md`
- `docs/impresoras-service-v1/error-retry-matrix.md`
- `docs/impresoras-service-v1/v1-backlog.md`
- `docs/impresoras-service-v1/sql-inicial-v1.sql`
