# Resumen rapido del proyecto

Este documento explica el proyecto de forma corta y practica para poder orientarte sin leer todo el codigo.

## 1) Que es este proyecto

`ImpresorasServiceV1` es una plataforma para:
- recibir trabajos de impresion desde SAP (polling con claim/ack sobre PostgreSQL),
- guardarlos en la cola interna (`printer_print_job`, eventos),
- decidir a que impresora enviarlos (enrutado),
- y ejecutarlos con reintentos/control de estado.

La solucion esta separada por capas en `ImpresorasService.Core` y por procesos (API, Worker, UI Laravel).

## 2) Mapa mental en 30 segundos

- **Origen**: tabla `printer_source_print_job`, donde SAP deja los trabajos.
- **Cola interna**: tablas `printer_*` en el mismo esquema PostgreSQL (`admon`, provisional; ver `docs/PLAN.md` B0.5).
- **Decision**: reglas que eligen impresora segun tienda/tipo de documento/canal.
- **Ejecucion**: worker que manda el PDF al spooler y actualiza estado.

## 3) Estructura principal

- `src/ImpresorasService.Web.PHP`: UI oficial Laravel.
- `src/ImpresorasService.Api`: API REST (auth, cola, impresoras, reglas, health, diagnostics).
- `src/ImpresorasService.Worker`: ingesta + ejecucion + watchdog + monitorizacion.
- `src/ImpresorasService.Core`: dominio + aplicacion + infraestructura.
- `tests/ImpresorasService.Api.IntegrationTests`: pruebas (SQLite en memoria solo aqui).

## 4) Configuracion que mas impacta

- `ConnectionStrings:PrintQueue` (servidor, usuario y esquema: única configuración de BD)
- `Source:Mode` = `SapHana`
- `SapHana:*` (sistema origen, lease)
- `Ingestion:*`, `PrintExecution:*`
- `Jwt:Secret`
- `Database:ApplyMigrations` = `false` (el DDL se aplica con `scripts/crear-esquema.ps1`)

Documentacion historica (Sqlite/SqlTest/Postgres): `docs/archive/`.

## 5) Flujo end-to-end

1. Trabajo en la tabla origen.
2. `IngestionBackgroundService` → `SapHanaJobSourceAdapter` (claim/ack).
3. `IngestionService` → cola interna con idempotencia.
4. `RoutingService` → impresora.
5. `PrintExecutionBackgroundService` → spooler.
6. Estado final: `SpoolAccepted`, `RetryScheduled`, `ErrorFinal`, etc.

## 6) Puntos de entrada recomendados

1. `README.md`
2. `src/ImpresorasService.Api/Program.cs`
3. `src/ImpresorasService.Worker/Program.cs`
4. `Infrastructure/Adapters/SapHanaJobSourceAdapter.cs` (sin modificar claim/ack en limpiezas)
5. `Infrastructure/Services/PrintExecutionService.cs`
6. `Controllers/PrintJobsController.cs`

## 7) Scripts utiles

- `scripts/crear-esquema.ps1` — crea el esquema PostgreSQL desde `ConnectionStrings:PrintQueue`
- `scripts/probar-impresion.ps1`
- `scripts/verificar-estado.ps1`
- `scripts/archive/verificar-bd-sqlite-historico.ps1` — solo referencia historica

## 8) Frontend PHP

Ver `docs/FRONTEND-WEB-PHP-GIT.md` para el estado del gitlink/submodulo.
