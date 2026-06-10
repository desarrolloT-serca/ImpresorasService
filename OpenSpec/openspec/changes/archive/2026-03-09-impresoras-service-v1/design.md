## Context

La V1 se implementa como servicio centralizado intranet para multi-tienda con SQL Server como fuente de verdad operativa en produccion.
Para desarrollo local se permite SQLite con el mismo modelo de dominio, para acelerar pruebas funcionales sin dependencia de instancia SQL Server.

Objetivo tecnico: priorizar robustez, trazabilidad y no duplicidad por encima de optimizaciones prematuras.

## Technical Approach

### Componentes
- `Ingestion Worker`:
  - Polling cada 5 segundos.
  - Lectura desde adaptador de origen (`IJobSourceAdapter`).
  - Normalizacion e insercion idempotente en `PrintJobs`.
- `Routing Service`:
  - Resolucion por prioridad de reglas activas.
  - Registro de eventos de enrutado.
- `Print Execution Worker`:
  - Toma jobs elegibles y ejecuta impresion por spooler.
  - Timeout por intento de 30s.
  - Gestion de reintentos y estado final.
- `Web API + Blazor`:
  - Consulta de cola y auditoria.
  - Acciones manuales (reintento, cancelacion logica, test print).

### Modelo de consistencia
- Idempotencia en base de datos con indice unico por `SourceSystem + ExternalJobId`.
- Concurrencia optimista en cambios de estado con `RowVersion`.
- Transiciones de estado atomicas para evitar dobles acciones.

### Seguridad
- Autenticacion Negotiate (Windows/AD).
- Autorizacion por rol:
  - Admin global.
  - Supervisor limitado por `StoreId`.

### Observabilidad
- Logs estructurados con `CorrelationId`, `JobId`, `StoreId`, `ErrorCode`, `LatencyMs`.
- Auditoria inmutable en `PrintJobEvents`.

## State Model

Estados operativos:
- `Pending`, `Routed`, `Printing`, `SpoolAccepted`, `PrintedConfirmed`, `PrintedUnknown`, `RetryScheduled`, `Cancelled`, `ErrorFinal`.

Regla de cancelacion V1:
- Permitida solo en `Pending`, `Routed` y `RetryScheduled`.

## Retry and Error Strategy

- Reintentos: 4 intentos maximos.
- Backoff: 15s, 30s, 60s, 90s.
- Errores transitorios reintentables: red/spooler/cola temporal.
- Errores no transitorios: sin regla, impresora invalida, PDF invalido.
- Al agotar reintentos: `ErrorFinal` + alerta para Admin y Supervisor de la tienda.

## Data Design

Tablas base:
- `PrintJobs`
- `PrintJobEvents`
- `Printers`
- `RoutingRules`
- `OperationalAlerts`

El SQL de referencia para V1 se mantiene en `docs/impresoras-service-v1/sql-inicial-v1.sql`.

### Estrategia por entorno de base de datos
- DEV local:
  - SQLite para pruebas de flujo rapido (ingesta, idempotencia, API y worker).
- QA/PROD:
  - SQL Server como objetivo operativo de V1.
- Regla:
  - El comportamiento funcional (estados, idempotencia, auditoria) SHALL mantenerse consistente entre proveedores.

## Risks and Mitigations

- Confirmacion fisica de impresion no estandar entre modelos:
  - Mitigacion: `PrintedConfirmed` y `PrintedUnknown` en V1.
- Acciones manuales simultaneas:
  - Mitigacion: `RowVersion` + actualizaciones atomicas.
- Cambios en origen SAP HANA:
  - Mitigacion: adaptadores y normalizacion desacoplada.
