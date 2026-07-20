# Roadmap — Corrección de KPIs de periodo del Dashboard

**Base:** rama `develop`, HEAD `34ff3c7` + worktree con Fase 1 del roadmap forense sin commitear.
**Fuente:** Reporte de Validación (hallazgos VAL-P0-001 … VAL-P3-009).
**Alcance:** build de tests, semántica de KPIs por periodo, timezone, fuente de verdad única del dashboard.
**Fuera de alcance:** claim atómico / lock de instancia (Fase 2 del roadmap forense, `roadmapimpresoras.md`), confirmación IPP por job.

---

## Estructura

```
F0  Hotfix build + congelar contrato KPI     ← bloquea todo lo demás
F1  Fix semántica periodo/tiempo (API)       ← depende de F0 (contrato aprobado)
F2  Fuente de verdad única del dashboard     ← depende de F1
F3  Validación HANA/staging                  ← acompaña a F1/F2, gate de despliegue
F4  Limpieza y deuda                         ← no bloqueante
```

**Regla:** ningún cambio de semántica KPI se implementa hasta que las preguntas de negocio (§Decisiones) estén respondidas y registradas en este documento.

---

## Decisiones de negocio bloqueantes

Registrar la respuesta aquí antes de arrancar F1:

| # | Pregunta | Decisión | Fecha |
|---|---|---|---|
| D1 | "Impresos del periodo": ¿eventos ocurridos en el periodo (throughput, recomendado) o cohorte de recibidos en el periodo? | **Eventos del periodo** (por `UpdatedAtUtc`) | 2026-07-20 |
| D2 | ¿`SpoolAccepted` cuenta como impreso, o el KPI pasa a llamarse "Procesados" con sub-métrica "Confirmados"? | **Mantener los 3 estados** bajo "Impresos" por ahora | 2026-07-20 |
| D3 | ¿`PrintedUnknown` cuenta como impreso o como no verificable? | **Cuenta como impreso** (incluido en la decisión D2) | 2026-07-20 |
| D4 | ¿`RetryScheduled` cuenta en "Fallidos"? | **Sí, mantener** | 2026-07-20 |
| D5 | ¿"today" = Europe/Madrid siempre, sea cual sea la TZ del servidor API? | **Sí**, configurable, no depende del SO del servidor | 2026-07-20 |
| D6 | ¿7d/30d rodantes o días de calendario completos? | **Rodantes** (`now - 7d` / `now - 30d`) | 2026-07-20 |

**Nota de riesgo aceptado (D2/D3):** ningún nivel de "Impresos" es hoy evidencia física 100% fiable — `SpoolAccepted` solo confirma aceptación del spooler y `PrintedConfirmed` tampoco tiene evidencia por trabajo (hallazgo P1-003 de `roadmapimpresoras.md`, Fase 3 sin implementar). Se revisará esta decisión cuando la Fase 3 forense entregue confirmación IPP por job.

---

## F0 — Hotfix build + contrato KPI `[hoy]`

### F0.1 Recuperar suite verde `[B]` — VAL-P0-001 ✅ HECHO
Añadido `TimeProvider.System` en los 11 constructores de tests rotos:
- `tests/.../PrintExecutionServiceTests.cs:56`
- `tests/.../Flow/PrintExecutionServiceFlowTests.cs:77,163,241,308,379,445,514`
- `tests/.../Flow/IngestionServiceFlowTests.cs:148,243,306`

Se usó `TimeProvider.System` (no se añadió `Microsoft.Extensions.TimeProvider.Testing`, no estaba instalado y no aporta nada a estos tests). El `FakeTimeProvider` para controlar el reloj queda para F1.4, donde sí hace falta.
**Aceptación:** `dotnet test` compila y 110/110 verdes. **Verificado:** `dotnet test` → `Superado: 110, Total: 110`. **Riesgo:** nulo (solo tests).

### F0.2 Congelar contrato KPI `[B]` ✅ HECHO
Documento `docs/contrato-kpi-dashboard.md` creado con la tabla de definiciones (KPI, timestamp, filtros) según D1-D6.
**Aceptación:** contrato aprobado por negocio/operación; D1-D6 rellenadas arriba. **Verificado:** decisiones registradas 2026-07-20, todas las recomendadas.

**Salida F0:** ✅ CI verde (110/110), contrato firmado. Los cambios del worktree (Fase 1 forense) pueden commitearse. **F0 completa — arranca F1.**

---

## F1 — Fix semántica periodo/tiempo en la API

> Depende de F0.2. Todos los cambios en `src/ImpresorasService.Api/Controllers/DashboardController.cs` salvo indicación.

### F1.1 KPIs por evento, no por cohorte `[M]` — VAL-P1-002 + VAL-P2-005 ✅ HECHO
- `received` → `CreatedAtUtc >= from` (sin cambio).
- `printed` → `UpdatedAtUtc >= from` + estado impreso.
- `failed` → `UpdatedAtUtc >= from` + señal de fallo.
- `failedWithoutRetryCurrent` → sin cambio (`UpdatedAtUtc`), ahora coherente con `failed`.
- `BuildStoreRowsAsync`: `receivedStats` (de `jobsInWindow`) separado de `printedFailedStats` (de `jobsUpdatedInWindow`).

**Archivo:** `DashboardController.cs` (`GetOverview`, `BuildStoreRowsAsync`). **Verificado:** fixtures 1 y 4 (F1.4) en verde.

### F1.2 Timezone de negocio explícita `[B]` — VAL-P1-003 ✅ HECHO
- `Dashboard:BusinessTimeZone` = `"Europe/Madrid"` en `appsettings.json` de la Api.
- `DashboardController` recibe `TimeProvider` + `IConfiguration` por constructor; `_businessTimeZone` resuelto una vez con `TimeZoneInfo.FindSystemTimeZoneById`.
- `ResolveWindowStartUtc`/`ResolveTodayStart` usan `_timeProvider.GetUtcNow()` + `TimeZoneInfo.ConvertTime(..., _businessTimeZone)` en vez de `TimeZoneInfo.Local`/`DateTime.Today`.
- `generatedAtUtc` y `UpdatedAtUtc` (thresholds) también pasan a `_timeProvider.GetUtcNow()`.

**Archivo:** `DashboardController.cs`, `appsettings.json`. **Verificado:** fixtures 3 y 9 (F1.4) en verde — cruce de medianoche UTC≠Madrid en ambos sentidos.

### F1.3 Excluir tiendas inactivas `[B]` — VAL-P2-007 ✅ HECHO
`jobs` en `GetOverview` filtrado con `Where(x => _dbContext.Stores.Any(s => s.StoreId == x.StoreId && s.IsActive))`, antes de aplicar el filtro de `storeId` explícito.
**Archivo:** `DashboardController.cs:76-78`. **Verificado:** fixture 7 (F1.4) en verde.

### F1.4 Tests de ventana con reloj falso `[M]` ✅ HECHO
- `ManualTimeProvider.cs` (TimeProvider controlable manualmente, sin dependencia nueva).
- `WindowClockApiFactory.cs` (factory de test que sustituye el `TimeProvider` singleton). Requirió quitar `sealed` de `ApiWebApplicationFactory` (era una clase de test infra sin motivo real para sellarla).
- `Controllers/DashboardControllerWindowTests.cs`: 5 fixtures (1, 3, 4, 7, 9 del Reporte de Validación §5).

**Aceptación:** los 5 fixtures pasan. **Verificado:** `dotnet test` → 115/115 (110 previos + 5 nuevos).

**Salida F1:** ✅ el overview de la API devuelve KPIs correctos y deterministas. La UI (que ya prioriza el overview) muestra números correctos sin tocar PHP. **F1 completa — arranca F2.**

**Hallazgo colateral (fuera de alcance de este roadmap):** `composer audit` en `Web.PHP` reporta CVEs en `laravel/framework` (CVE-2026-48019, CRLF injection) y `league/commonmark` (CVE-2026-33347, CVE-2026-30838). No relacionado con KPIs; requiere su propio triage de actualización de dependencias.

---

## F2 — Fuente de verdad única del dashboard

> Depende de F1. Objetivo: PHP deja de recalcular KPIs a partir de jobs crudos.

### F2.1 Breakdown por impresora en el overview `[M]` — raíz de VAL-P1-004 ✅ HECHO
- `GetOverview`/`BuildStoreRowsAsync` calculan `printerQueueCurrent`, `printerFailedWindow`, `printerTotalWindow` y `unassignedQueueStats` con `GroupBy(StoreId, PrinterId)`, se combinan en `printerAcc` y se exponen como `StoreDashboardRow.Printers[]` (`PrinterDashboardRow{PrinterId,QueueCurrent,FailedWindow,TotalWindow}`) + `UnassignedQueueCurrent`.
- PHP: `applyOverviewStores` ahora también llama a `applyOverviewPrinters`, que sustituye los chips por impresora y `unassignedQueueCurrent` del store por los del overview (antes solo sobrescribía los KPIs agregados de tienda, dejando los chips atados al fetch truncado a 500).

**Archivos:** `DashboardController.cs` (Api), `DashboardController.php` (`applyOverviewStores`, nuevo `applyOverviewPrinters`).
**Tests nuevos:** `DashboardControllerTests.GetOverview_ReturnsPerPrinterBreakdown` (.NET); `DashboardControllerTest::test_dashboard_uses_overview_printer_chips_when_printjobs_list_is_partial` (PHP, simula `api/printjobs` vacío con 600 jobs reales en el overview).
**Verificado:** `dotnet test` 116/116, `php artisan test` 8/8.

### F2.2 Degradar el cálculo legacy PHP a fallback explícito `[M]` — VAL-P1-004 + VAL-P2-006 ✅ HECHO
- El bucle de agregación sobre `$jobs` (`DashboardController.php`) ahora corre solo dentro de `if ($overview === null)`; con overview disponible el trabajo se descarta antes de ejecutarse.
- `$jobsPath` pide `limit=500` (antes 5000), honesto con el clamp real de la Api.
- `$partialData = count($jobs) >= 500` cuando el fallback está activo; se pasa a ambas vistas.
- `dashboard.blade.php`: banner "Datos parciales" cuando `$partialData`. `dashboard-local.blade.php`: mismo aviso integrado en `$attentionItems` (patrón ya existente); se corrigió `str_contains($item['href'], ...)` para tolerar `href: null` (el nuevo item de aviso no enlaza a ningún sitio).
- `$isFailedWithoutRetryStatus` (VAL-P2-006): predicado del fallback realineado con el contrato — `ErrorFinal OR (Pending/Routed/Printing/Cancelled/PrinterBlocked AND AttemptCount>1)` — independiente de `$hasFailureSignal`, igual que `DashboardPrintJobPredicates.FailedWithoutRetryCurrent` en la Api.

**Archivos:** `DashboardController.php`, `dashboard.blade.php`, `dashboard-local.blade.php`.
**Verificado:** `dotnet test` 116/116 (sin cambios en Api), `php artisan test` 8/8.
**Aceptación pendiente de staging:** `logKpiDiffIfAny` sin diffs 48h (ver F2.3); con la Api caída, banner visible con >=500 jobs.

### F2.3 Retirar o mantener `logKpiDiffIfAny` `[B]` — pendiente
Requiere 48 h de observación en staging (no ejecutable desde aquí). Decidir entonces: retirar el diagnóstico o dejarlo en nivel debug.

**Salida F2:** ✅ un solo lugar calcula KPIs (la API); el fallback PHP es explícito y se anuncia como tal. Pendiente solo F2.3 (requiere staging real).

---

## F3 — Validación HANA/staging `[gate de despliegue]`

> El fix de F1 filtra por `UpdatedAtUtc >= @from` sobre columnas de fecha **VARCHAR** en HANA (hallazgo P2-009 forense). Hereda ese riesgo.

| # | Prueba | Verifica |
|---|---|---|
| 1 | Filtro `CreatedAtUtc >= @from` con datos reales (incluye filas legacy `dd/MM/yyyy` si existen) | F1.1 |
| 2 | Filtro `UpdatedAtUtc >= @from` ídem | F1.1 |
| 3 | Overview con >500 jobs vivos: chips = BD | F2.1 |
| 4 | 48 h de `logKpiDiffIfAny` sin diffs | F2.2 |
| 5 | Job creado 23:58 Madrid / consultado 00:05: no cuenta en "today" | F1.2 |
| 6 | Traducción a SQL HANA del filtro `Stores.Any(s => s.StoreId == x.StoreId && s.IsActive)` (EXISTS correlacionado) en `GetOverview` | F1.3 |

**Regla:** si la prueba 1/2 revela formatos de fecha mixtos, se bloquea el despliegue de F1 y se escala al gate H2 del roadmap forense (migración a TIMESTAMP nativa) — no parchear con lógica tolerante en el controller.

---

## F4 — Limpieza y deuda (no bloqueante)

- **F4.1** ✅ Acotar ventana por arriba en API (`<= now`) para simetría con PHP — VAL-P3-009. `GetOverview` calcula `now` una vez (reutilizado en `ResolveWindowStartUtc`, `generatedAtUtc` y como límite superior de `jobsInWindow`/`jobsUpdatedInWindow`).
- **F4.2** ✅ `TimeProvider` inyectado en `PrintJobsController` (constructor + campo `_timeProvider`), sustituye `DateTimeOffset.UtcNow` en `Cancel` (`UpdatedAtUtc`, `OccurredAtUtc`). Requirió actualizar 9 sitios de `new PrintJobsController(...)` en `PrintJobsControllerRouteFlowTests.cs`/`PrintJobsControllerCancelFlowTests.cs` (mismo patrón que F0.1).
- **F4.3** Diferido intencionalmente — D2/D3 decidió mantener "Impresos" con los 3 estados actuales; renombrar/dividir solo tiene sentido cuando la Fase 3 de `roadmapimpresoras.md` entregue confirmación IPP por job. No hay nada que hacer aquí todavía.
- **F4.4** ✅ `Worker/appsettings.json`: `Telegram.Enabled` → `false` por defecto (coincide con lo documentado en `CLAUDE.md`; `SendAlertAsync` ya era no-op seguro con `BotToken` vacío, así que era solo inconsistencia documental, no bug funcional).
- **F4.5** ✅ `CLAUDE.md` actualizado: sección "En desarrollo" (obsoleta, databa de la rama `IU`) sustituida por el estado real verificado — IPP/Telegram implementados, pendientes reales listados (confirmación IPP por job, claim atómico/lock de instancia, tests e2e).

**Verificado:** `dotnet test` 116/116, `php artisan test` 9/9.

**Salida F4:** ✅ completa (F4.3 diferida por decisión de negocio, no por falta de trabajo).

---

## Trazabilidad hallazgo → fase

| Hallazgo | Fase |
|---|---|
| VAL-P0-001 (build tests) | F0.1 |
| VAL-P1-002 (cohorte vs evento) | F1.1 |
| VAL-P1-003 (timezone servidor) | F1.2 |
| VAL-P1-004 (limit 5000 vs 500) | F2.1 + F2.2 |
| VAL-P2-005 (failed vs failedNoRetry) | F1.1 |
| VAL-P2-006 (predicado PHP distinto) | F2.2 |
| VAL-P2-007 (tiendas inactivas) | F1.3 |
| VAL-P2-008 (SpoolAccepted como impreso) | D2/D3 → F4.3 |
| VAL-P3-009 (testabilidad/bound) | F1.4 + F4.1 + F4.2 |

## Criterios de cierre global

1. CI verde (`dotnet test` + `php artisan test`) con el worktree commiteado.
2. D1-D6 decididas y contrato KPI publicado.
3. Fixtures críticos automatizados y verdes con reloj falso.
4. 48 h sin diffs legacy↔overview en staging.
5. Pruebas HANA 1-5 de F3 verdes antes de producción.
