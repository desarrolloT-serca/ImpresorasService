> **Documento histórico archivado.** No describe la arquitectura activa (HANA + `Source:Mode=SapHana`). Ver `../README.md` y `../../README.md`.

# Plan: Cola de impresión desde SAP (PostgreSQL) hacia Worker

## Contexto del sistema (actual)

La aplicación `ImpresorasServiceV1` está compuesta por:

- `ImpresorasService.Api` (API REST + UI Laravel en `ImpresorasService.Web.PHP`)
- `ImpresorasService.Worker` (workers en background)
- `ImpresorasService.Core` (dominio y casos de uso)

La lógica operativa de impresión existe ya y define la semántica de estados y reintentos:

- El *routing* mueve trabajos desde `Pending` -> `Routed`.
- El *print execution* mueve trabajos desde `Routed` / `RetryScheduled` -> `Printing` -> `SpoolAccepted` / `RetryScheduled` / `ErrorFinal`.
- La UI muestra el estado usando `PrintJobStatus` (enum) y mapea cada código a etiquetas.

Estados soportados por la aplicación (códigos 0..8):

- `Pending = 0`
- `Routed = 1`
- `Printing = 2`
- `SpoolAccepted = 3`
- `PrintedConfirmed = 4` 
- `PrintedUnknown = 5`
- `RetryScheduled = 6` 
- `Cancelled = 7` 
- `ErrorFinal = 8`

## Objetivo de esta integración

Sustituir la forma manual de probar impresión desde UI (creando jobs de prueba) por una cola alimentada por una tabla auxiliar en SAP (remoto) en PostgreSQL.

El worker debe:

1. Leer “trabajos entrantes” desde la tabla auxiliar remota.
2. Confiar en una clave idempotente para evitar duplicados.
3. Encolar en el sistema local existente (`PrintJobs`) para que el resto del flujo (routing + impresión + reintentos) permanezca igual.
4. Mantener “semántica exacta” de estados y reintentos (sin inventar estados nuevos).

## Decisiones MVP cerradas (para evitar ambigüedades)

1. **Idempotencia / unicidad**
  - `external_id` (tu “número de albarán/factura/devolución”) será `UNIQUE` en la tabla auxiliar remota.
  - En local, se mantiene la idempotencia existente basada en `SourceSystem + ExternalJobId` (ya implementada en `PrintJobRepository.ExistsBySourceExternalIdAsync`).
2. **Polling**
  - Se usará polling (no `LISTEN/NOTIFY`) por robustez.
  - `Ingestion.PollIntervalSeconds` inicial recomendado: `2`.
  - `PrintExecution.PollIntervalSeconds` se deja al valor actual o se ajusta de forma conservadora.
3. **Formato de payload (MVP)**
  - MVP soporta únicamente **PDF contenido** (`pdf_blob` en remota -> `PdfBlob` local).
  - No se implementa todavía `pdf_path` (rutas) ni su accesibilidad.
  - Esto reduce incertidumbre y mantiene el spooler funcionando igual que hoy.
4. **Normalización de `store_code*`*
  - `store_code` llega con ceros a la izquierda en ocasiones (ej. `01` -> `1`).
  - La ingesta debe normalizar: eliminar ceros a la izquierda y tratar `"0"` como caso explícito si existiera.
5. **Lease / claim en la tabla remota**
  - Existe concurrencia potencial (o reintentos) y puede existir más de un worker en el futuro.
  - Se requiere un mecanismo “claim lease” para evitar que el mismo `external_id` sea reclamado por otro actor mientras hay un lease vigente.
  - Lease recomendado inicialmente: **tope conservador** (la prioridad es garantizar que claim->insert->ack se complete de forma fiable con tu entorno; si no, habrá re-claims, pero no debe haber pérdida).

## Invariantes (no negociables para criticidad)

1. **No pérdida de trabajos**
  - Si el worker falla entre *claim remoto* e *insert local*, el trabajo debe recuperarse cuando expire el lease.
2. **No duplicar impresión**
  - La idempotencia local (basada en `SourceSystem + ExternalJobId`) debe evitar que dos “ingestas” acaben en impresión duplicada.
3. **Semántica de estados igual a la aplicación**
  - Los estados 0..8 y los reintentos deben comportarse igual que hoy.
4. **Ack remoto SOLO tras el insert local exitoso**
  - No marcar `processed=true` en remoto hasta que el job exista localmente y el `SaveChanges` local haya completado sin error.

## Arquitectura objetivo (alto nivel)

### Flujo propuesto

1. **Ingestion worker (polling)**
  - Consulta remota: “reclamar” (claim batch) un conjunto de trabajos candidatos.
  - Para cada trabajo, construye `IncomingPrintJob`.
  - Inserta en local (`PrintJobs` + `PrintJobEvents`) siguiendo la semántica actual.
  - Solo después del `SaveChanges` local, hace ack remoto (marca processed=true en remoto) para el batch reclamado.
2. **Routing service (local)**
  - Ejecuta reglas y transiciona a `Routed` con `PrinterId`.
3. **Print execution (local)**
  - Envía al spooler y actualiza estados `Printing -> SpoolAccepted/RetryScheduled/ErrorFinal`.
4. **UI**
  - Se apoya en local `PrintJobs` para visibilidad operativa.

## Contrato de la tabla auxiliar remota (Postgres SAP) – MVP

> Ajustar tipos exactos cuando se defina el DDL real en SAP; este documento especifica el “contrato lógico”.

Campos mínimos:

- `external_id` (UNIQUE, not null)
- `document_type` (CALLCENTER/ALBARAN/FACTURA/AD360)
- `store_code` (texto o int; normalizar en ingesta)
- `created_at_utc` (timestamptz)
- `pdf_blob` (bytea)  // MVP
- `processed` (bool default false)
- `claimed_by` (text nullable)
- `lease_expires_at_utc` (timestamptz nullable)
- `updated_at_utc` (timestamptz o timestamp with time zone, opcional pero recomendado)

Índices mínimos:

- UNIQUE: `external_id`
- Índice para claim: `(processed, lease_expires_at_utc, created_at_utc)`

### Semántica de SQL recomendada (conceptual)

1. **Claim batch atómico**
  - Seleccionar N filas con `processed=false` y `lease_expires_at_utc` vencido o null.
  - Actualizar en la misma operación: asignar `claimed_by=workerId` y `lease_expires_at_utc=now()+lease`.
  - Devolver `external_id`, `document_type`, `store_code`, `pdf_blob`, `created_at_utc`.
2. **Ack processed**
  - Actualizar a `processed=true` y limpiar lease fields, condicionado a que el lease pertenezca a `workerId` (para evitar ack erróneo tras expiración y re-claim).

> Nota: La implementación exacta depende de cómo se construya el query con Npgsql y de si preferimos CTEs (`WITH ... UPDATE ... RETURNING ...`).

## Cambios esperados en el código (a nivel de responsabilidad)

### Worker / Core

- Se añade un `SapPostgresJobSourceAdapter` con:
  - `FetchPendingJobsAsync` = claim batch remoto + mapeo a `IncomingPrintJob`
  - `MarkJobsProcessedAsync` = ack remoto tras insert local

### Core services

- `IngestionService` debe usar el adapter para ack remoto (en vez de marcar processed directamente sobre tabla local).

### Web / UI

- La UI de cola (`/cola`) se alimenta del estado local `PrintJobs` y se mantiene.
- Se añadirá una vista admin “debug” para observabilidad del worker (solo admin).

## Plan de implementación por hitos (con tareas y criterios de aceptación)

### Hito 1 — Esquema remota + contrato SQL de claim/ack

Tareas:

1. Definir DDL de la tabla remota (campos mínimos y tipos).
2. Asegurar índices y constraints (UNIQUE external_id + índices de claim).
3. Preparar y validar queries de claim y ack en entorno de prueba.

Criterio de aceptación:

- Se puede ejecutar claim con concurrencia: dos workers no reclaman el mismo `external_id` con lease vigente.
- Tras ack, el `external_id` no vuelve a salir en claim.

### Hito 2 — Extender `IJobSourceAdapter` para ack remoto y actualizar `IngestionService`

Tareas:

1. Añadir a la interfaz un método de ack remoto (por ejemplo `MarkJobsProcessedAsync(...)`).
2. Actualizar `IngestionService` para:
  - insertar local,
  - ack remoto solamente tras `SaveChangesAsync` local OK.
3. Actualizar `SqlTestJobSourceAdapter` para mantener compatibilidad.

Problemas a vigilar:

- Orden transaccional: claim/insert/ack debe ser robusto ante fallos parciales.
- Conflictos de idempotencia local (dos claims del mismo external_id por lease corto) deben ser tolerados.

Criterio de aceptación:

- En `SqlTest`, el comportamiento actual no se rompe.
- Con modo SAP (si existe un entorno de staging), no se ack `processed=true` si el insert local falla.

### Hito 3 — Implementar `SapPostgresJobSourceAdapter` (polling + claim/ack)

Tareas:

1. Implementar conexión Npgsql a `10.110.46.14:5432`.
2. Implementar claim batch:
  - normalizar `store_code`,
  - mapear `document_type`,
  - devolver `pdf_blob` (bytea) + metadatos.
3. Implementar ack remoto condicionado al `workerId` y lease.
4. Integrar en `ConfigurableJobSourceAdapter` mediante `Source:Mode`.

Criterio de aceptación:

- Cada `external_id` aparece en local como un solo `PrintJob` (idempotencia).
- Si el worker cae tras claim y antes de insert local, eventualmente el job termina en local cuando expire el lease.

### Hito 4 — Payload MVP (pdf_blob) end-to-end

Tareas:

1. Confirmar que `pdf_blob` remota -> `PdfBlob` local alimenta el spooler (`WindowsPrintSpooler`) igual que UI/SQL test.
2. Asegurar que `PdfSha256` local se calcula como hoy (cuando aplica).
3. Asegurar que errores de spooler siguen clasificándose igual (transitorio vs definitivo) para activar reintentos.

Criterio de aceptación:

- Spooler acepta y devuelve estados de la máquina de estados (0..8) sin cambios.

### Hito 5 — Admin debug en UI (solo admin)

Tareas:

1. Agregar endpoints (admin only) para métricas:
  - ingest: claimed/inserted/ackOk/ackFail,
  - impresión: contadores por `PrintJobStatus`.
2. Crear vista Blade (admin) para visualizar colas por tienda y estados.
3. Añadir switches seguros (habilitar/deshabilitar ingest/print) y límites a cambios de polling/batch.

Criterio de aceptación:

- Un admin puede diagnosticar rápidamente si el cuello de botella es `ingest` o `print execution`.

### Hito 6 — Pruebas de criticidad (no opcional)

Tareas:

1. Tests de re-claim:
  - simular lease expiring y asegurar que no hay duplicados en impresión.
2. Tests de fallo parcial:
  - caída del worker entre claim y insert local,
  - caída entre insert local y ack remoto.
3. Pruebas de carga equivalentes al volumen:
  - objetivo medio 6k–9k albaranes/día.
4. Validación invariantes:
  - no pérdida eventual,
  - no duplicación de impresión.

Criterio de aceptación:

- Tras series de fallos simulados, el sistema recupera y llega a estado final (impreso o ErrorFinal) sin duplicar impresión.

## Qué NO se implementa aún (fuera de alcance MVP)

- `pdf_path` / resolución por ruta (se deja para una fase posterior).
- Espejado completo de estados de impresión en SAP remoto (MVP: estado operativo vive en local `PrintJobs`).

## Referencias dentro del repositorio (para aceleración de otros agentes)

- Estados: `src/ImpresorasService.Core/Domain/PrintJobStatus.cs`
- Ejecución de impresión: `src/ImpresorasService.Core/Infrastructure/Services/PrintExecutionService.cs`
- Ingesta: `src/ImpresorasService.Core/Application/Services/IngestionService.cs`
- Worker: `src/ImpresorasService.Worker/IngestionBackgroundService.cs`, `src/ImpresorasService.Worker/PrintExecutionBackgroundService.cs`
- Routing local: `src/ImpresorasService.Core/Infrastructure/Services/RoutingService.cs`
- UI/labels: `src/ImpresorasService.Web.PHP/app/Helpers/StatusLabels.php` y cola `/cola`

## Checklist de “ready-to-code” (anti-errores)

- DDL remoto acordado (mínimos + índices + UNIQUE external_id)
- Contrato claim/ack definido (lease + ack condicionado)
- Decidido payload MVP = `pdf_blob`
- `store_code` normalizado (ceros a la izquierda)
- Ack remoto tras insert local OK
- Semántica states/retries intacta (mismo `PrintJobStatus` y reintentos)
- Pruebas de fallo parcial definidas y automatizadas

