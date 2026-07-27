# Roadmap integral — ImpresorasServiceV1

**Fecha:** 2026-07-21 · **Base:** `develop` `3b4f025` + working tree KPI.
**Fuente:** `docs/auditoria-integral-2026-07-21.md` (hallazgos A-KPI-*, A-ARCH-*, A-UI-*).
**Principio rector:** el dashboard tiene **una sola fuente de verdad** (la API); ninguna métrica se da por correcta sin un test que fije su semántica o, si depende de HANA, sin pasar el gate de staging.

---

## Orden de fases (por dependencia)

```
G0  Hotfix "current" no envejece            ← P1, desbloquea coherencia salud/alertas
G1  Backfill de eventos / ventana ciega      ← P1, correctitud de 7d/30d
G2  Gate HANA de fechas + EXISTS             ← P2, bloquea despliegue de F1 completo
G3  Robustez y perf (KPI)                     ← P2/P3, no bloqueante
G4  Arquitectura (lock de Worker, dedup)      ← P1 para multi-instancia; refactor lo demás
G5  UI/UX y accesibilidad                     ← P2/P3
```

**No hacer todavía (y por qué):**
- No escalar a 2+ instancias de Worker hasta G4.1 (duplicaría eventos → KPIs inflados).
- No "arreglar" fechas con parsing tolerante en el controller (G2): si HANA guarda VARCHAR mixto, la solución es DDL nativo, no lógica defensiva que enmascare corrupción.
- No retirar `logKpiDiffIfAny` hasta 48 h sin diffs en staging (G2.3).

---

## G0 — `failedWithoutRetryCurrent` deja de envejecer `[P1]`

**Problema:** A-KPI-01. Métrica de foto de estado filtrada por `UpdatedAtUtc` en ventana → estados terminales salen del recuento a medianoche → falsas "RECUPERADA" y fallos ocultos; salud depende de la ventana.

**Cambios:**
- `DashboardController.cs:92` — `failedWithoutRetryCurrent` = `jobs.CountAsync(FailedWithoutRetryCurrent)` (sobre `jobs`, **sin** `jobsUpdatedInWindow`).
- `DashboardController.cs:295-303` (`failedWindowStats`) y `:313-318` (`printerFailedWindow`) — usar `allJobs`/`jobs`, no `jobsUpdatedInWindow`.
- `StoreHealthAlertBackgroundService.cs:133-136` — quitar `j.UpdatedAtUtc >= windowStart`; contar estado actual puro.
- Contrato: `contrato-kpi-dashboard.md:18` — reescribir la fila: "foto de estado actual, **sin ventana**".

**Tests (antes del fix, deben fallar → luego verde):**
- `.NET` nuevo en `DashboardControllerWindowTests`: reloj a `2026-07-21T00:05Z`, sembrar `ErrorFinal` con `UpdatedAtUtc=2026-07-20T18:00Z`, `GET overview?window=today` → `failedWithoutRetryCurrent == 1` y `store.health != healthy`.
- `.NET` Worker (si hay proyecto de test de Worker; si no, cubrir vía predicado + integración API): misma siembra → salud crítica, sin "RECUPERADA".

**Aceptación:** un `ErrorFinal` de ayer cuenta hoy; la cifra de `failedWithoutRetryCurrent` es idéntica en `today`/`7d`/`30d`; el dashboard y la alerta coinciden. Resuelve A-KPI-01 **y** A-KPI-04.

**Riesgo/rollback:** bajo (solo lectura). Si sube el ruido de alertas, es porque ahora se ven fallos reales antes ocultos — comportamiento correcto. Rollback = revertir el commit.

---

## G1 — Correctitud de `printed`/`failed` en 7d/30d `[P1]`

**Problema:** A-KPI-02. Dependen 100% de `PrintJobEvents`; sin backfill, 7d/30d subcuentan tras el deploy.

**Paso 1 — medir (gate HANA/staging):** contar en HANA cuántos jobs en estado impreso/fallido **carecen** de evento de esa transición. Query de diagnóstico (no cambio de código). Decide entre 2A y 2B:

- **2A · Backfill** (recomendado si el hueco es grande): script en `scripts/sql/` que inserta un `PrintJobEvent` sintético (`OccurredAtUtc = updated_at_utc`, `NewStatus = status`, `EventType='Backfill'`, `ActorType='migration'`) por job impreso/fallido sin evento previo. Idempotente (no duplicar si ya existe evento de ese estado).
- **2B · Ventana ciega documentada:** si el hueco es pequeño o el histórico no importa, aceptar y anotar en el contrato "los KPIs de evento son fiables desde la fecha de activación del event-sourcing".

**Tests:**
- `.NET`: job en `PrintedConfirmed` **sin** `PrintJobEvents` → tras backfill simulado, cuenta en `printed` de la ventana correspondiente; sin backfill, no cuenta (documenta el límite).

**Aceptación:** `printed`/`failed` a 7d/30d reproducen el volumen real del histórico (2A) o el límite queda escrito y visible en UI (2B). **Gate:** no desplegar F1 sin decidir 2A/2B con datos de HANA.

**Riesgo:** 2A modifica datos → ejecutar en staging primero, con `COUNT` antes/después y backup. Rollback = borrar eventos `EventType='Backfill'`.

---

## G2 — Gate HANA: fechas y `EXISTS` `[P2 · bloquea despliegue de F1]`

**Problema:** A-KPI-05. Filtros de fecha y `Stores.Any(...)` no verificados contra HANA.

| # | Prueba (en staging con datos reales) | Verifica |
|---|---|---|
| 1 | Tipo real de `created_at_utc`/`updated_at_utc`/`occurred_at_utc` (`TIMESTAMP` vs `NVARCHAR`) | A-KPI-05 |
| 2 | `CreatedAtUtc >= @from` devuelve el conjunto correcto (incl. filas legacy si las hay) | received |
| 3 | `OccurredAtUtc >= @from` ídem sobre `PrintJobEvents` | printed/failed |
| 4 | Traducción SQL del `EXISTS` correlacionado (`DashboardController.cs:67`) | tiendas activas |
| 5 | Job creado 23:58 Madrid, consultado 00:05 → no cuenta en "today" | timezone |
| 6 | 48 h de `logKpiDiffIfAny` sin diffs legacy↔overview | fuente única |

**Regla:** si (1) revela VARCHAR con formatos/offsets mixtos → **bloquear F1** y migrar a `TIMESTAMP` nativo (DDL en `scripts/sql/`). No parchear en el controller.

**Aceptación:** pruebas 1-6 verdes en staging antes de producción.

---

## G3 — Robustez y rendimiento de KPI `[P2/P3]`

- **G3.1 (P2, A-KPI-03)** ✅ HECHO 2026-07-21 (parcial): `LoadPrintedAndFailedAsync` reescrito — `Join` contra `jobsScope` (ya filtrado por tienda activa/storeId) empuja el scope a la agregación `MIN(OccurredAtUtc)` en vez de escanear toda la tabla de eventos; elimina además el segundo roundtrip con `IN`-list de JobIds (`printed`/`errorOrRetry` ya traen `StoreId`/`AttemptCount` del join, sin consulta adicional a `jobsScope`). Igual para `errorOrRetry`. Verificado: `dotnet test` 128/128 (invariante suma tienda↔global y no-doble-conteo de `KpiP1_001` intactos). **Pendiente** (no ejecutable aquí): índice de cobertura `(NewStatus, JobId, OccurredAtUtc)` en HANA — requiere DDL en `scripts/sql/` + aplicación por el DBA; medir latencia real con volumen en staging (gate G2).
- **G3.2 (P3, A-KPI-07)** ✅ HECHO 2026-07-21: `BusinessTimeZoneClock.Resolve` con `try/catch` (`TimeZoneNotFoundException`/`InvalidTimeZoneException`) → `TimeZoneInfo.Utc` + `logger?.LogWarning`. `DashboardController` recibe `ILogger<DashboardController>` por constructor; `StoreHealthAlertBackgroundService` pasa su `_logger` ya existente. Tests: `Resolve_InvalidTimeZoneId_FallsBackToUtcInsteadOfThrowing`, `Resolve_ValidTimeZoneId_ReturnsIt` (`BusinessTimeZoneClockTests.cs`). Verificado: `dotnet test` 128/128.
- **G3.3 (P3, A-KPI-06)** ✅ HECHO 2026-07-21: documentado en `contrato-kpi-dashboard.md` ("`AttemptCount` en 'impreso con reintentos' es el valor actual, no el histórico") — sin impacto conocido porque un job impreso no vuelve a reintentar; capturar `AttemptCount` en el evento queda anotado como mejora futura si se necesitara exactitud histórica. Sin cambio de código.
- **G3.4 (P3, A-ARCH-06)** ✅ HECHO 2026-07-21: `CS8604` en `PrintersController.cs:172,175` resuelto con `!` sobre `input.Host`/`input.CapabilitiesJson` (columnas legítimamente nullable; `ExecuteSqlRawAsync` traduce `null` a `DBNull` sin cambio de comportamiento — solo silencia el warning de nullabilidad del `params object[]`). Verificado: `dotnet build` 0 advertencias, `dotnet test` 128/128.

---

## G4 — Arquitectura `[P1 lock / P2-P3 refactor]`

- **G4.1 (P1, A-ARCH-01)** ✅ HECHO 2026-07-27: lock de instancia única del Worker vía fila singleton `printer_worker_lock` (`id=1`, `holder`, `heartbeat_utc`) adquirida/renovada por `WorkerLockCoordinator.TryAcquireOrRenewAsync` con UPDATE condicional (`ExecuteUpdateAsync`, mismo patrón que `SapHanaJobSourceAdapter.RenewJobLeasesAsync`) — solo afecta la fila si el holder coincide o el lease expiró (`WorkerLock:LeaseSeconds`, 30s por defecto). Nuevo `WorkerLockBackgroundService` corre en bucle (heartbeat cada `WorkerLock:HeartbeatIntervalSeconds`, 10s) y publica el resultado en `WorkerLockState` (singleton en memoria, un `InstanceId` por proceso). Los 5 BackgroundService existentes (`IngestionBackgroundService`, `PrintExecutionBackgroundService`, `SpoolAcceptedWatchdogBackgroundService`, `PrinterConnectivityMonitorService`, `StoreHealthAlertBackgroundService`) comprueban `WorkerLockState.IsHolder` al inicio de cada ciclo y se saltan el trabajo si no son titulares — así una 2ª instancia no ingiere, no envía al spooler, no escribe eventos/estado ni duplica alertas Telegram. DDL de referencia: `scripts/sql/create_worker_lock.sql`. Tests: `WorkerLockCoordinatorTests` (siembra inicial, bloqueo del 2º holder durante el lease, renovación por el mismo holder, relevo tras expirar el lease) con `ManualTimeProvider`/SQLite en memoria. Verificado: `dotnet test` 132/132 (antes 128), `php artisan test` 12/12 (sin cambios PHP). **Pendiente** (no ejecutable aquí): aplicar el DDL en HANA/staging antes de desplegar; verificar en staging con 2 procesos Worker reales que solo uno procese (el acceptance del roadmap original: "arrancar 2 Workers → sólo uno procesa; matar al titular → el segundo toma el relevo").
- **G4.2 (P2, A-ARCH-03)** ✅ HECHO 2026-07-21: `PrintedStatuses`/`QueueStatuses` movidos a `DashboardPrintJobPredicates.cs` (Core), junto a `FailedWithoutRetryCurrent`. `DashboardController.cs` y `StoreHealthAlertBackgroundService.cs` referencian la copia única; eliminado el comentario "debe mantenerse idéntico" (ya no puede divergir, es la misma constante). Verificado: `dotnet build` ambos proyectos 0 advertencias, `dotnet test` 128/128.
- **G4.3 (P3, A-ARCH-05):** métricas de negocio (conteo de KPIs, diffs overview↔legacy, duplicados de evento) vía OTel/logs estructurados.

---

## G5 — UI/UX y accesibilidad `[P2/P3]`

- **G5.1 (P2, A-UI-02)** ⚠️ AUDITADO 2026-07-21, sin cambio de código (decisión explícita del usuario: solo documentar, no tocar CSS sin verificación visual). Confirmado con evidencia, no solo sospecha: **55 selectores de nivel superior** definidos en ambos archivos (`comm -12` sobre selectores top-level). Muestreo de los más usados en el dashboard (`.dbx-card`, `.dbx-pill`, `.dbx-table`, `.dbx-tabs`, `.dbx-title`, `.dbx-toolbar`, `.badge`) muestra que **no son duplicados inofensivos**: `dbx.css` define un valor (tamaños en `px`, colores fijos) y `system.css` (cargado después, `layouts/app.blade.php:10-11`) lo **redefine con valores distintos** (`rem`, `var(--ui-primary)`, pesos de fuente distintos) que ganan por orden de cascada. Las reglas de `dbx.css` para esos 55 selectores son código muerto en producción. Build real (`npm run build`) confirma el peso: `dbx.css` compila a 43.7 kB, `system.css` a 118.6 kB — 162 kB de CSS para el dashboard, con buena parte de `dbx.css` inerte. **Remediación futura** (no ejecutada): eliminar de `dbx.css` las 55 reglas confirmadas muertas, verificando con captura antes/después (dev server + Playwright) por tratarse de un archivo compartido por todas las páginas (dashboard, impresoras, tiendas, usuarios, cola, alertas, ajustes).
- **G5.2 (P2, A-UI-03)** ✅ HECHO 2026-07-21: la fila "Sin reenviar" en `dashboard.blade.php` vivía dentro de la tarjeta rotulada "Periodo: {{ $windowLabel }}" sin distinguirse — tras el fix de G0 (failedWithoutRetryCurrent ya no depende de la ventana), esa etiqueta pasó a ser **activamente engañosa** (antes del fix sí era coherente con "Periodo"; ahora no). Corregido: la fila añade `(actual)` + `title` explicando que no depende del periodo seleccionado arriba. `dashboard-local.blade.php` ya lo hacía bien (etiqueta "fallos activos", fuera del bloque "Flujo del periodo") — no requirió cambio. Verificado: `php artisan test` 12/12, vista sigue renderizando sin errores.
- **G5.3 (P2, A-UI-04)** ⚠️ AUDITADO 2026-07-21, sin cambio de código (mismo criterio de riesgo que G5.1: cambio de comportamiento runtime, no solo estilo, sin forma de verificar visualmente aquí). Hallazgo más preciso que el original: la Api ya devuelve `overview.alerts` (un alert por tienda no-healthy, `DashboardController.cs:98-111`, misma fuente que `stores[].health`/`healthReason` — sin riesgo de divergencia interna). **PHP ignora `overview.alerts` por completo** y siempre recalcula desde cero con `buildPrioritizedAlerts()` (`DashboardController.php:1072-1184`), que usa su **propio motor de reglas** (`dashboard-threshold-rules.json`, hasta 3 niveles de severidad por métrica) — más rico que el de la Api (`DashboardThresholds`, solo warning/critical). Son **tres implementaciones independientes** de "qué severidad aplica" (`StoreHealthEvaluator.Compute` en C#, `computeHealth()` y `buildPrioritizedAlerts()` en PHP) que pueden divergir en la prioridad con la que evalúan las reglas. Unificar exige decidir si la Api adopta el motor de reglas dinámico de PHP (3 niveles) o si PHP renuncia a su granularidad multi-alerta-por-tienda — **decisión de producto/arquitectura, no un fix de UI pequeño**. Requiere confirmación antes de implementar.
- **G5.4 (P3, A-UI-05)** ✅ HECHO 2026-07-21: `.github/workflows/impresoras-service-ci.yml` no ejecutaba `npm run build` en ningún job — un error en Vite/CSS/JS solo se habría descubierto en producción, degradando silenciosamente al fallback inline de `layouts/app.blade.php:290`. Añadidos pasos `actions/setup-node@v4` + `npm ci` + `npm run build` al job `php`, antes de `php artisan test`. Verificado localmente: `npm run build` compila limpio (57 módulos, `dbx-*.css` 43.7 kB, `system-*.css` 118.6 kB, `app-*.js` 43.5 kB); `public/build/` está en `.gitignore`, no ensucia el repo.

---

## Mapa hallazgo → acción → test → cierre

| Hallazgo | Sev | Acción | Test | Criterio de cierre |
|---|---|---|---|---|
| A-KPI-01 | P1 | G0 | `ErrorFinal` de ayer cuenta hoy | cifra estable entre ventanas; sin falsa "RECUPERADA" |
| A-KPI-02 | P1 | G1 (2A/2B) | job impreso sin evento | 7d/30d reproducen histórico o límite documentado |
| A-KPI-03 | P2 | G3.1 | dashboard 1 tienda no carga JobIds de otras | latencia aceptable con volumen |
| A-KPI-04 | P2 | (se cierra con G0) | igualdad dashboard↔alerta | coinciden en 7d/30d |
| A-KPI-05 | P2 | G2 | pruebas HANA 1-6 | fechas nativas verificadas |
| A-KPI-06 | P3 | G3.3 | — | contrato actualizado |
| A-KPI-07 | P3 | G3.2 | id TZ inválido | endpoint no 500 |
| A-ARCH-01 | P1 | G4.1 ✅ (código) | `WorkerLockCoordinatorTests` (2º holder bloqueado / relevo tras expirar) | lock verificado en staging con 2 procesos reales (pendiente) |
| A-ARCH-03 | P2 | G4.2 | — | estados en un único punto |
| A-UI-02 | P2 | G5.1 | — | sin solapes críticos |

---

## Orden recomendado de PRs (evitar PRs gigantes)

1. **PR-1 (G0):** fix `failedWithoutRetryCurrent` sin ventana (API + Worker + contrato + tests). Pequeño, alto impacto.
2. **PR-2 (G3.2 + G3.4):** robustez TZ + warnings null. Trivial, aparte para no mezclar con semántica.
3. **PR-3 (G1 paso 1):** query de diagnóstico + decisión 2A/2B documentada.
4. **PR-4 (G1 paso 2):** backfill o nota de ventana ciega + tests.
5. **PR-5 (G3.1):** perf de `firstPrintedEvents` + índice.
6. **PR-6 (G4.2):** centralizar estados.
7. **PR-7 (G5.x):** UI/UX por lotes pequeños.
8. **G2 (HANA) y G4.1 (lock)** no son PRs de código puro: gate de staging y feature mayor respectivamente, con su propio plan.

---

## Gates obligatorios antes de producción

1. G0 mergeado y verde (sin falsas recuperaciones).
2. G1 decidido con datos de HANA (backfill aplicado o ventana ciega documentada en UI).
3. G2: pruebas HANA 1-6 verdes; fechas en tipo nativo.
4. G4.1: lock implementado y con test (✅ 2026-07-27); falta aplicar `scripts/sql/create_worker_lock.sql` en HANA y verificar en staging con 2 procesos Worker reales antes de escalar a multi-instancia.
5. Suites verdes: `dotnet test` + `php artisan test` + `npm run build`.
6. 48 h de `logKpiDiffIfAny` sin diffs en staging.
