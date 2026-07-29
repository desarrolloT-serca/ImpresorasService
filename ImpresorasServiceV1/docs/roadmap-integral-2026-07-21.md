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

## G1 — Correctitud de `printed`/`failed` en 7d/30d `[P1]` ✅ CERRADO (no aplica) 2026-07-27

**Problema original:** A-KPI-02. `printed`/`failed` dependen 100% de `PrintJobEvents`; si hubiera jobs históricos migrados de otro sistema sin evento propio, 7d/30d subcontarían.

**Cierre:** confirmado con el usuario (2026-07-27) que no habrá migración de histórico hacia esta base — arranque limpio en producción. El pipeline actual es 100% event-sourced desde el primer commit (`IngestionService`, `RoutingService`, `PrintExecutionService` generan evento en cada transición de estado, verificado por lectura de código). Sin datos heredados que backfillear, la decisión 2A/2B no aplica: todo job que exista en `PrintJob` en estado impreso/fallido tendrá su evento correspondiente por construcción.

**Validación de la query de diagnóstico** (control positivo, no mide producción real, valida que el SQL detecta huecos si algún día existieran): en `ZTEST_VICENTE_2` (que será la base de producción), se sembraron ~25 jobs vía `/pruebas`, se borró manualmente el evento de un job en `PrintedConfirmed` (`scripts/sql/g1_validacion_borrar_evento.sql`) y `scripts/sql/diagnose_g1_1_resumen.sql` pasó de 0 a 1 fila correctamente. Confirma que si algún bug futuro rompiera el event-sourcing, la query lo detectaría.

**Nota:** si en el futuro se decide importar histórico de otro sistema, este cierre queda invalidado y hay que reabrir G1 con la decisión 2A/2B original (scripts `scripts/sql/diagnose_g1_*.sql` ya preparados).

---

## G2 — Gate HANA: fechas y `EXISTS` `[P2 · bloquea despliegue de F1]`

**Problema:** A-KPI-05. Filtros de fecha y `Stores.Any(...)` no verificados contra HANA.

`ZTEST_VICENTE_2` es la base que será de producción (sin migración de histórico, ver G1) — los resultados obtenidos ahí valen como definitivos, no como "solo sandbox".

| # | Prueba | Verifica | Estado |
|---|---|---|---|
| 1 | Tipo real de `created_at_utc`/`updated_at_utc`/`occurred_at_utc` (`TIMESTAMP` vs `NVARCHAR`) | A-KPI-05 | ✅ VERDE 2026-07-27: `TIMESTAMP` nativo confirmado (`SYS.TABLE_COLUMNS`, `scripts/sql/diagnose_g2_1_tipos_columna.sql`) en `created_at_utc`, `updated_at_utc`, `next_retry_at_utc`, `occurred_at_utc`. El comentario legacy de `ImpresorasDbContext.cs:413-414` sobre "formato string no homogéneo" no aplica a esta base — puede ser de un entorno anterior o defensivo por adelantado; sin impacto real detectado. |
| 2 | `CreatedAtUtc >= @from` devuelve el conjunto correcto | received | ⏳ pendiente — validar con más volumen real (tráfico de producción) |
| 3 | `OccurredAtUtc >= @from` ídem sobre `PrintJobEvents` | printed/failed | ⏳ pendiente |
| 4 | Traducción SQL del `EXISTS` correlacionado (`DashboardController.cs:50-55`) | tiendas activas | ⏳ pendiente — activar logging SQL de EF y comparar contra el plan de HANA |
| 5 | Job creado 23:58 Madrid, consultado 00:05 → no cuenta en "today" | timezone | ⏳ pendiente — control positivo preparado (`scripts/sql/g2_5_backdatar_timezone.sql`), no ejecutado aún |
| 6 | 48 h de `logKpiDiffIfAny` sin diffs legacy↔overview | fuente única | ⏳ pendiente — requiere tráfico real sostenido, no aplicable con datos de prueba puntuales |

**Regla (ya no aplica):** el punto de bloqueo por VARCHAR queda descartado — test #1 confirmó tipo nativo.

**Aceptación:** pruebas 2-6 verdes antes de dar por cerrado el gate completo.

**Diferido 2026-07-27** (decisión del usuario): pruebas #2-6 son verificación operativa (requieren tráfico real o pasos manuales de bajo riesgo — `EXISTS` estándar de EF, timezone ya cubierto por unit tests de `BusinessTimeZoneClockTests`), no bloquean trabajo de código. Se retoman cuando haya tráfico real de producción o antes del despliegue final. Scripts ya preparados: `scripts/sql/g2_5_backdatar_timezone.sql` (test #5).

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
- **G4.3 (P3, A-ARCH-05)** ✅ HECHO 2026-07-27: sin backend de métricas (OTel/Prometheus/App Insights) en el proyecto — se usa logging estructurado, mismo patrón que `IngestionService`/`StoreHealthAlertBackgroundService`. Los "diffs overview↔legacy" ya se logueaban (`DashboardOverviewService.php:69`, `Log::warning` con contexto); faltaban dos piezas:
  - **Conteo de KPIs**: `DashboardController.cs` (Api, tras calcular `kpis`) loguea `received/printed/failed/queueCurrent/failedWithoutRetryCurrent/activePrinters/activeStores` con propiedades nombradas en cada `GET overview`.
  - **Duplicados de evento**: `ImpresorasDbContext.SaveChanges`/`SaveChangesAsync` detectan, antes de persistir un `PrintJobEvent` nuevo, si ya existe otro con el mismo `JobId`+`OldStatus`+`NewStatus` en una ventana de 5s (`DuplicateEventDetectionWindow`) — indicio de doble procesamiento concurrente (el escenario que G4.1 ya bloquea por diseño; esto es la red de seguridad observacional). Heurística por tiempo, no unicidad real — anotado con `ponytail:` el límite (reintento legítimo muy rápido podría dar falso positivo). No bloquea el insert, solo loguea warning.
  Verificado: `dotnet build` (Core+Api) 0 advertencias, `dotnet test` 132/132.

---

## G5 — UI/UX y accesibilidad `[P2/P3]`

- **G5.1 (P2, A-UI-02)** ✅ HECHO 2026-07-27: auditoría inicial (2026-07-21) había estimado 55 selectores duplicados por nombre; análisis riguroso posterior (comparación propiedad-por-propiedad, no solo por nombre de selector) sobre los 117 selectores top-level presentes en ambos archivos identificó **46 selectores 100% muertos** (`system.css`, cargado después, redefine *todas* sus propiedades, sin `!important` en `dbx.css` que invirtiera el orden de cascada) y **71 parcialmente vivos** (aportan propiedades que `system.css` no cubre — esos se dejaron intactos). Eliminadas las 46 reglas muertas de `dbx.css` (bloques completos borrados, o solo el selector muerto podado de grupos `selector-a, selector-b` compartidos con selectores aún vivos, preservando el resto). `dbx.css`: 2334 → 2163 líneas. Build (`npm run build`): `dbx-*.css` 43.7 kB → 40.02 kB. Verificado: `php artisan test` 12/12; verificación visual manual del usuario en dashboard/impresoras/tiendas/cola tras hard-refresh — "todo igual, ningún cambio apreciable".
- **G5.2 (P2, A-UI-03)** ✅ HECHO 2026-07-21: la fila "Sin reenviar" en `dashboard.blade.php` vivía dentro de la tarjeta rotulada "Periodo: {{ $windowLabel }}" sin distinguirse — tras el fix de G0 (failedWithoutRetryCurrent ya no depende de la ventana), esa etiqueta pasó a ser **activamente engañosa** (antes del fix sí era coherente con "Periodo"; ahora no). Corregido: la fila añade `(actual)` + `title` explicando que no depende del periodo seleccionado arriba. `dashboard-local.blade.php` ya lo hacía bien (etiqueta "fallos activos", fuera del bloque "Flujo del periodo") — no requirió cambio. Verificado: `php artisan test` 12/12, vista sigue renderizando sin errores.
- **G5.3 (P2, A-UI-04)** ✅ HECHO 2026-07-27: la Api adopta el motor de reglas de 3 niveles de PHP — ya no hay tres implementaciones independientes de severidad, una sola (`StoreHealthEvaluator`, Core), consumida por `DashboardController.cs` (Api) y `StoreHealthAlertBackgroundService.cs` (Worker).
  - **Motor**: `StoreHealthEvaluator.Compute`/`BuildAlerts` (Core) — puerto exacto de `computeHealth()`/`buildPrioritizedAlerts()` de `DashboardController.php` (mismos textos de motivo en español, misma cascada de prioridad, misma peculiaridad de `severity="info"` colapsando a `health="healthy"` pero conservando el prefijo "Info:" en el motivo — cubierto por test).
  - **Reglas**: fichero JSON (`ThresholdRuleEngine`/`DashboardThresholdRuleStore`, Core) en vez de BD — no se tocó `printer_dashboard_threshold` (sin acceso a DDL en este momento). Compartido por Api y Worker vía `Dashboard:ThresholdRulesFilePath` (mismo valor en ambos `appsettings.json`, `C:\ImpresorasService\dashboard-threshold-rules.json`, sembrado con el contenido real que ya usaba PHP para no cambiar comportamiento en el cutover). El endpoint viejo `GET/PUT api/dashboard/thresholds` (2 niveles, BD) queda intacto pero sin usarse en el cálculo de salud — huérfano, no se borra.
  - **Conectividad**: `DashboardController.cs`/`StoreHealthAlertBackgroundService.cs` reutilizan `PrinterConnectivityState` (ya existía, Core) para clasificar cada impresora, en vez de que la Api contara solo por streak — hallazgo colateral: PHP también reimplementaba esta clasificación (`printerConnectivityStatus()`/`printerConnectivitySeverity()`) porque la Api no la exponía; ahora la Api la usa internamente para los conteos `connWarning`/`connCritical`/`connMaxStreak`/`missingHost` con el mismo criterio OR que PHP.
  - **Api**: nuevos `GET/PUT api/dashboard/threshold-rules` (validación 1-3 reglas/métrica, severidad/min únicos, orden ascendente — puerto de `normalizeThresholdRules`/`hasAscendingSeverityThresholds`).
  - **PHP**: `resolveHealthThresholds()` prefiere la Api sobre el fichero local (que pasa a ser solo caché de fallback); `updateThresholds()` guarda directo en `api/dashboard/threshold-rules` (se eliminó `deriveLegacyThresholdPayload`/`deriveTwoLevelLegacy`/`deriveSingleLevelLegacy`, quedaban muertos); `index()` usa `overview.alerts` cuando la Api responde, con `buildPrioritizedAlerts()` como fallback solo si la Api está caída (mismo patrón ya existente para kpis/stores vía `applyOverviewKpis`/`applyOverviewStores` — no se tocó ese fallback).
  - Verificado: `dotnet test` 142/142 (10 tests nuevos, `StoreHealthEvaluatorTests.cs`), `php artisan test` 12/12, `dotnet build` Core+Api+Worker sin advertencias.
- **G5.4 (P3, A-UI-05)** ✅ HECHO 2026-07-21: `.github/workflows/impresoras-service-ci.yml` no ejecutaba `npm run build` en ningún job — un error en Vite/CSS/JS solo se habría descubierto en producción, degradando silenciosamente al fallback inline de `layouts/app.blade.php:290`. Añadidos pasos `actions/setup-node@v4` + `npm ci` + `npm run build` al job `php`, antes de `php artisan test`. Verificado localmente: `npm run build` compila limpio (57 módulos, `dbx-*.css` 43.7 kB, `system-*.css` 118.6 kB, `app-*.js` 43.5 kB); `public/build/` está en `.gitignore`, no ensucia el repo.

---

## Mapa hallazgo → acción → test → cierre

| Hallazgo | Sev | Acción | Test | Criterio de cierre |
|---|---|---|---|---|
| A-KPI-01 | P1 | G0 | `ErrorFinal` de ayer cuenta hoy | cifra estable entre ventanas; sin falsa "RECUPERADA" |
| A-KPI-02 | P1 | G1 ✅ cerrado (no aplica) | job impreso sin evento (control positivo verde) | sin migración de histórico, no hay hueco posible por diseño |
| A-KPI-03 | P2 | G3.1 | dashboard 1 tienda no carga JobIds de otras | latencia aceptable con volumen |
| A-KPI-04 | P2 | (se cierra con G0) | igualdad dashboard↔alerta | coinciden en 7d/30d |
| A-KPI-05 | P2 | G2 (parcial ✅) | pruebas 1-6, #1 verde | fechas nativas verificadas (✅); #2-6 pendientes |
| A-KPI-06 | P3 | G3.3 | — | contrato actualizado |
| A-KPI-07 | P3 | G3.2 | id TZ inválido | endpoint no 500 |
| A-ARCH-01 | P1 | G4.1 ✅ (código) | `WorkerLockCoordinatorTests` (2º holder bloqueado / relevo tras expirar) | lock verificado en staging con 2 procesos reales (pendiente) |
| A-ARCH-03 | P2 | G4.2 | — | estados en un único punto |
| A-UI-02 | P2 | G5.1 | — | sin solapes críticos |

---

## Orden recomendado de PRs (evitar PRs gigantes)

1. **PR-1 (G0):** fix `failedWithoutRetryCurrent` sin ventana (API + Worker + contrato + tests). Pequeño, alto impacto.
2. **PR-2 (G3.2 + G3.4):** robustez TZ + warnings null. Trivial, aparte para no mezclar con semántica.
3. ~~**PR-3 (G1 paso 1)**~~ y ~~**PR-4 (G1 paso 2)**: no aplican — G1 cerrado sin cambio de código (2026-07-27, ver G1).
4. **PR-5 (G3.1):** perf de `firstPrintedEvents` + índice.
5. **PR-6 (G4.2):** centralizar estados.
6. **PR-7 (G5.x):** UI/UX por lotes pequeños.
7. **G2 (pruebas 2-6) y G4.1 (verificación en staging con 2 procesos)** no son PRs de código puro: verificación operativa con datos/tráfico real, con su propio plan.

---

## Gates obligatorios antes de producción

1. G0 mergeado y verde (sin falsas recuperaciones).
2. ~~G1 decidido~~ ✅ cerrado 2026-07-27 (no aplica, sin migración de histórico).
3. G2: test #1 verde (✅ TIMESTAMP nativo); pendientes #2-6 con tráfico real.
4. G4.1: lock implementado y con test (✅ 2026-07-27); falta aplicar `scripts/sql/create_worker_lock.sql` en HANA y verificar con 2 procesos Worker reales antes de escalar a multi-instancia.
5. Suites verdes: `dotnet test` + `php artisan test` + `npm run build`.
6. 48 h de `logKpiDiffIfAny` sin diffs, con tráfico real de producción.
