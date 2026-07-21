# Auditoría integral — ImpresorasServiceV1

**Fecha:** 2026-07-21
**Rama:** `develop` · HEAD `3b4f025` + working tree no commiteado (F0–F2 KPI).
**Autor del informe:** revisión independiente (principal engineer).
**Método:** lectura de contrato/documentación, rastreo de cada cambio reciente hasta consumidores y tests, ejecución de suites. Suites verdes en el momento de la auditoría: **.NET 125/125** (`dotnet test tests/ImpresorasService.Api.IntegrationTests`), **PHP 12/12** (`php artisan test`). `npm run build` **no ejecutado** (recomendado antes de merge).

> Convención de estados verdad: **demostrado** (código + test/ejemplo reproducible) · **aparentemente resuelto** (código plausible, sin test que fije la semántica) · **no resuelto** · **no verificable sin HANA/staging** · **riesgo aceptado por negocio**.

---

## 1. Resumen ejecutivo (franco)

El trabajo reciente de KPIs (`4226f60`, `389d1b0`, `3b4f025` + working tree) **mejora** la corrección respecto al bug original (cohorte vs evento, timezone de servidor, tiendas inactivas, truncado a 500) y centraliza dos piezas que antes divergían (`DashboardPrintJobPredicates`, `BusinessTimeZoneClock`). Esas son decisiones **bien fundamentadas**.

Sin embargo, **la sospecha del usuario es correcta: los KPIs de período siguen teniendo defectos reales**, dos de ellos con impacto operativo directo:

1. **`failedWithoutRetryCurrent` no es "current"** — está filtrado por `UpdatedAtUtc` dentro de la ventana. Un `ErrorFinal` (estado terminal cuyo `UpdatedAtUtc` no vuelve a moverse) **desaparece del recuento al cambiar de día**, provocando que la salud de tienda "se cure sola" a las 00:00 de Madrid y que el Worker envíe una alerta **"RECUPERADA" falsa**. Es el hallazgo más grave. **P1, demostrable con ejemplo temporal.**

2. **`printed`/`failed` dependen al 100% de `PrintJobEvents`** desde la revisión v2 del contrato. No hay backfill de eventos históricos, así que tras el despliegue las ventanas **7d/30d subcontarán impresos/fallidos** (posiblemente cerca de cero para jobs anteriores al event-sourcing fiable) hasta que el log acumule historia. **P1, a verificar contra el histórico real de eventos en HANA.**

Además persisten riesgos de arquitectura ya conocidos (múltiples instancias del Worker sin lock — Fase 2 forense) y de plataforma (comparación de fechas sobre columnas potencialmente VARCHAR en HANA, **no verificable aquí**).

**Veredicto de despliegue:** los cambios de KPI **no deben ir a producción** sin (a) corregir A-KPI-01, (b) resolver el backfill de A-KPI-02, y (c) pasar el gate HANA de fechas (A-KPI-05). El resto es mejorable pero no bloqueante.

---

## 2. Veredicto por cambio reciente

| Commit / cambio | Veredicto | Nota |
|---|---|---|
| `389d1b0` Fase 1 forense (starvation watchdog, ingesta por-job, TimeProvider) | **Resuelto** | Watchdog ordena por antigüedad y empuja filtro a SQL (`SpoolAcceptedWatchdogBackgroundService.cs:75-91`); `TimeProvider` inyectado. Correcto. |
| `4226f60` semántica KPI de período | **Parcial** | `received`=cohorte, `printed`/`failed`=evento: dirección correcta. Pero introduce A-KPI-02 (dependencia total de eventos sin backfill) y no toca A-KPI-01. |
| `3b4f025` limpieza F4 (TimeProvider en PrintJobsController, Telegram off) | **Resuelto** | Verificado en código: `PrintJobsController.cs:19-21`, `Worker/appsettings.json:36 Enabled=false`. |
| Working tree: `DashboardController.cs` printed/failed por eventos | **Parcial / regresión latente** | Ver A-KPI-02 y A-KPI-03 (perf). |
| Working tree: `failedWithoutRetryCurrent` (API + Worker) | **No resuelto** | A-KPI-01: sigue windowed por `UpdatedAtUtc`. |
| Working tree: `BusinessTimeZoneClock` / `DashboardPrintJobPredicates` centralizados | **Resuelto (positivo)** | Elimina divergencia real previa API↔Worker. |
| Working tree: breakdown por impresora + `unassignedQueueCurrent` | **Resuelto** | API expone `printers[]`; PHP los usa sin depender del fetch truncado. |
| Working tree: componente `action-icon` accesible | **Resuelto (positivo)** | `role=img` + `aria-label` + `title`; SVG `aria-hidden`. |

---

## 3. KPIs de período: definición esperada vs comportamiento real

### A-KPI-01 · `failedWithoutRetryCurrent` envejece fuera de la ventana — **P1 · demostrado**

**Esperado:** "fallos sin reenvío **actuales**" = foto del estado presente. No debe depender de la ventana temporal seleccionada; un job aún en `ErrorFinal` es un problema **ahora**, se rompió hace 5 minutos o hace 5 días.

**Real:**
- API: `DashboardController.cs:92` → `jobsUpdatedInWindow.CountAsync(FailedWithoutRetryCurrent)` y `:295` `failedWindowStats` sobre `jobsUpdatedInWindow` (= `UpdatedAtUtc >= fromUtc`).
- Worker: `StoreHealthAlertBackgroundService.cs:133-136` → `UpdatedAtUtc >= windowStart` con `windowStart = TodayStartUtc(now)`.

`ErrorFinal` es terminal: `UpdatedAtUtc` queda congelado en el instante del fallo y no se vuelve a tocar. Al filtrar por `UpdatedAtUtc >= inicioDeHoy`, el job **sale del recuento en cuanto pasa la medianoche de negocio**, aunque siga fallido.

**Ejemplo temporal (reproducible):**

| Momento (Europe/Madrid) | Estado del job J (tienda S) | `windowStart` (today) | ¿Cuenta en `failedWithoutRetryCurrent`? | Salud tienda S |
|---|---|---|---|---|
| 2026-07-20 18:00 | `ErrorFinal`, `UpdatedAtUtc=18:00` | 2026-07-20 00:00 | **Sí** (18:00 ≥ 00:00) | crítica |
| 2026-07-20 23:59 | `ErrorFinal` (sin cambios) | 2026-07-20 00:00 | Sí | crítica |
| 2026-07-21 00:01 | `ErrorFinal` (sin cambios) | **2026-07-21 00:00** | **No** (18:00 < 00:00) | **healthy** |
| 2026-07-21 00:05 | `ErrorFinal` (sin cambios) | — | — | Worker envía **"🟢 RECUPERADA"** |

**Impacto:** (1) alertas de recuperación **falsas** cada medianoche; (2) fallos persistentes **ocultos** en el dashboard con ventana "today"; (3) la salud de tienda **cambia según la ventana** que elija el usuario (7d/30d muestran más fallos que today) → cifras incoherentes entre pestañas.

**Causa raíz:** aplicar una ventana temporal (`UpdatedAtUtc`) a una métrica que semánticamente es una foto de estado. El contrato (`contrato-kpi-dashboard.md:18`) racionaliza el windowing, pero la racionalización es incorrecta para estados terminales.

**Fix:** eliminar el filtro de ventana de `failedWithoutRetryCurrent` en **API y Worker** — contar el estado actual puro (`Where(FailedWithoutRetryCurrent)` sin `jobsUpdatedInWindow`). Resuelve además A-KPI-04. Test: fijar reloj, sembrar `ErrorFinal` con `UpdatedAtUtc` de ayer, consultar con `window=today`, aseverar que **sí** cuenta.

---

### A-KPI-02 · `printed`/`failed` sin backfill de eventos — **P1 · a verificar en HANA**

**Esperado:** en 7d/30d, `printed` = jobs cuya primera impresión ocurrió en la ventana; `failed` = jobs con señal de fallo en la ventana.

**Real:** `LoadPrintedAndFailedAsync` (`DashboardController.cs:200-241`) lee **solo** `PrintJobEvents`. Cualquier job cuyas transiciones a impreso/fallo ocurrieron **antes de que el event-sourcing emitiera eventos de forma fiable** no tiene filas de evento y **no cuenta**, aunque su `Status` actual sea `PrintedConfirmed`.

**Impacto:** el día del despliegue, `printed`/`failed` a 7d y 30d leen artificialmente bajos (cerca de cero para el histórico) y se recuperan gradualmente a lo largo de 30 días. Para un operador es "el dashboard dice que no imprimimos casi nada esta semana", justo el síntoma que motiva esta auditoría.

**Causa raíz:** cambio de métrica snapshot→event-sourced sin migración de datos.

**Fix:** backfill de `PrintJobEvents` — sintetizar un evento "primera impresión" (`OccurredAtUtc = UpdatedAtUtc`, `NewStatus = Status`) por cada job actualmente en estado impreso/fallido sin evento previo; o documentar y aceptar una "ventana ciega" de 30 días post-deploy. **Gate:** verificar en HANA cuántos jobs impresos carecen de evento antes de desplegar.

---

### A-KPI-03 · `firstPrintedEvents`: agregación sin prefiltro + IN-list global — **P2 · perf**

`DashboardController.cs:203-208`: `GroupBy(JobId).Min(OccurredAtUtc)` se ejecuta sobre **toda** la tabla `PrintJobEvents` (sin prefiltro por fecha ni por tienda) en cada carga del dashboard. Después (`:213-217`) `firstPrintedIds.Contains(...)` envía a HANA una lista `IN` con **todos** los JobId impresos en la ventana de **todas** las tiendas, incluso cuando el dashboard es de una sola tienda.

**Impacto:** full scan + agregación de la tabla de eventos por request; `IN` potencialmente enorme → riesgo de límite de parámetros y latencia en HANA bajo volumen.

**Nota de cuidado:** no se puede "prefiltrar eventos por `OccurredAtUtc >= from` antes del `GroupBy`" ingenuamente — rompería la semántica de "primera vez" (un job impreso por primera vez *antes* de la ventana con otro evento impreso *dentro* contaría mal). El fix correcto es **empujar el scope de tienda al `GroupBy`** y añadir índice de cobertura `(NewStatus, JobId, OccurredAtUtc)`; valorar una columna materializada `FirstPrintedAtUtc` en el job.

---

### A-KPI-04 · Dashboard(window) ≠ alerta Telegram para 7d/30d — **P2**

El Worker calcula la salud **siempre** con `TodayStartUtc` (`StoreHealthAlertBackgroundService.cs:108`), mientras la API usa la ventana seleccionada. La corrección KPI-P2-004 alineó la **timezone** pero no la **longitud de ventana**. Una tienda mostrada como warning/critical en el dashboard a 7d puede no generar alerta (el Worker solo mira hoy) y viceversa. Se **resuelve automáticamente** al aplicar A-KPI-01 (métrica sin ventana).

---

### A-KPI-05 · Comparación de fechas sobre columnas potencialmente VARCHAR en HANA — **P2 · no verificable sin HANA**

`scripts/sql/` **no contiene** el DDL de `print_printer_job` ni `print_job_events` (el DBA lo aplica manualmente). La migración baseline usa `TEXT` (SQLite, solo tests). Los filtros `CreatedAtUtc/UpdatedAtUtc/OccurredAtUtc >= @from` se traducen por EF; si en HANA esas columnas son `NVARCHAR` con ISO8601 y **offsets mixtos** (`+00:00` vs `+02:00`) o formatos legacy (`dd/MM/yyyy`), la comparación lexicográfica **rompe los filtros de ventana** y produce KPIs incorrectos **en silencio**. Es el gate F3 del roadmap KPI. **No parchear con lógica tolerante en el controller**: verificar tipo real y migrar a `TIMESTAMP` nativo.

---

### A-KPI-06 · `failed` usa `AttemptCount` actual, no el del momento de impresión — **P3**

`DashboardController.cs:235`: `printed.Where(p => p.AttemptCount > 1)`. `AttemptCount` es el valor **actual** del job, no el que tenía al imprimir. Impacto marginal (un job impreso no vuelve a reintentar), pero la semántica "impreso con reintentos" no es exacta. Decisión de negocio documentada; bajo.

### A-KPI-07 · `BusinessTimeZoneClock.Resolve` sin fallback — **P3**

`BusinessTimeZoneClock.cs:12-14`: `FindSystemTimeZoneById` lanza si el id es inválido o si falta ICU en el host. Al resolverse en el **constructor** del controller, un valor de config erróneo tumba **todo** el dashboard con 500. Añadir `try/catch` → `TimeZoneInfo.Utc` con log de warning.

---

## 4. Auditoría de arquitectura

| ID | Sev | Hallazgo | Evidencia |
|---|---|---|---|
| A-ARCH-01 | **P1** | **Sin lock de instancia única del Worker.** Ingesta usa lease/claim, pero ejecución y watchdog usan solo `RowVersion` optimista. Con 2 workers habría doble sondeo IPP y eventos duplicados → contaminaría `printed`/`failed` (que ahora cuentan eventos). Fase 2 forense (`roadmapimpresoras.md`), **pendiente**. | `PrintExecutionService.cs` (tx + RowVersion), `SpoolAcceptedWatchdogBackgroundService.cs` |
| A-ARCH-02 | P2 | **Lógica de KPI duplicada API↔PHP.** El fallback PHP reimplementa (aproximado, snapshot) la semántica de eventos. Aceptable como degradación, pero es deuda: dos definiciones que pueden divergir. Mitigado por el contrato. | `DashboardController.php:174-291` |
| A-ARCH-03 | P2 | **`QueueStatuses` y `PrintedStatuses` duplicados** en `DashboardController` y `StoreHealthAlertBackgroundService` (comentario "debe mantenerse idéntico"). Frágil; ya pasó con `IsFailedAfterRetry`. Centralizar como los predicados. | `DashboardController.cs:24-30`, `StoreHealthAlertBackgroundService.cs:26-30` |
| A-ARCH-04 | ✅ | **Positivo:** `DashboardPrintJobPredicates` y `BusinessTimeZoneClock` centralizados eliminan divergencia real previa. | — |
| A-ARCH-05 | P3 | **Observabilidad de negocio limitada.** `logKpiDiffIfAny` gated por config; sin métricas (OTel/Prometheus) de KPIs ni de duplicados de evento. | `DashboardController.php:359-361` |
| A-ARCH-06 | P3 | Dos warnings `CS8604` (posible null) en `PrintersController.cs:172,175` (`ExecuteSqlRawAsync`). Nit de build. | salida `dotnet build` |

---

## 5. Auditoría UI/UX y accesibilidad

| ID | Sev | Hallazgo | Evidencia |
|---|---|---|---|
| A-UI-01 | ✅ | **Positivo:** componente `action-icon` con `role="img"`, `aria-label`, `title` y `<svg aria-hidden focusable="false">`. Acciones CRUD icon-only ahora accesibles (impresoras/tiendas/usuarios lo usan). | `components/ui/action-icon.blade.php` |
| A-UI-02 | P2 | **Confirmado, no solo sospecha (actualizado 2026-07-21):** 55 selectores top-level definidos en ambos archivos (`dbx.css` 2334 líneas + `system.css` 5950). Muestreo (`.dbx-card`, `.dbx-pill`, `.dbx-table`, `.dbx-tabs`, `.dbx-title`, `.dbx-toolbar`, `.badge`) confirma valores **distintos** en cada archivo, no duplicados — `system.css` carga después (`layouts/app.blade.php:10-11`) y gana siempre; las reglas de `dbx.css` para esos 55 selectores son código muerto. `npm run build`: 43.7 kB + 118.6 kB. Remediación (borrar las reglas muertas) diferida a una pasada con verificación visual — ver `roadmap-integral-2026-07-21.md` G5.1. | `dbx.css`, `system.css`, `layouts/app.blade.php` |
| A-UI-03 | P2 | ✅ Corregido 2026-07-21 (G5.2): la fila "Sin reenviar" en `dashboard.blade.php` vivía sin distinguirse dentro de la tarjeta "Periodo: X" — tras el fix A-KPI-01 (esa cifra ya no depende de la ventana) la etiqueta pasó a ser engañosa. Añadida marca "(actual)". | `dashboard.blade.php` |
| A-UI-04 | P2 | **Más preciso que la sospecha original (actualizado 2026-07-21):** la Api ya expone `overview.alerts` (misma fuente que `stores[].health`, sin riesgo de divergencia interna). PHP lo **ignora por completo** y siempre recalcula con `buildPrioritizedAlerts()`, que usa su propio motor de reglas dinámico (hasta 3 niveles de severidad, `dashboard-threshold-rules.json`) — más rico que el de la Api (solo warning/critical). Tres implementaciones independientes de "qué severidad aplica" que pueden divergir. Unificar requiere decisión de producto (¿la Api adopta el motor de 3 niveles, o PHP renuncia a su granularidad multi-alerta?) — no es un fix de UI pequeño, ver `roadmap-integral-2026-07-21.md` G5.3. | `DashboardController.cs:98-111`, `DashboardController.php:1072-1184` |
| A-UI-05 | P3 | **Fallback CSS inline** (`layouts/app.blade.php:290`) admite que "los estilos completos requieren `npm run build`". Si el build no se ejecuta en deploy, la UI degrada. Documentar en pipeline. | `layouts/app.blade.php:290` |

> El análisis UI se limita a lo verificado en código. A-UI-03/04 requieren revisión visual en navegador (no ejecutada) antes de cerrarse.

---

## 6. Huecos de pruebas

- **Ninguna prueba fija la semántica de A-KPI-01.** `DashboardControllerWindowTests` (11 `[Fact]`) cubre cruces de medianoche para `received` y casos de ventana, pero **no** aserta que un `ErrorFinal` de ayer siga contando en `failedWithoutRetryCurrent` con `window=today`. Los tests validan la implementación actual, no la propiedad "current no envejece".
- **Sin test de backfill / ventana ciega** (A-KPI-02): no hay cobertura de "job impreso sin evento".
- **Sin test de duplicados de evento** (A-ARCH-01): no se verifica que dos transiciones al mismo estado impreso cuenten una sola vez bajo eventos repetidos del watchdog.
- **Sin validación HANA** de traducción LINQ→SQL de fechas y del `EXISTS` correlacionado (`Stores.Any(...)`, `DashboardController.cs:67`).
- **Sin e2e** de IPP/watchdog/conectividad (ya documentado en `CLAUDE.md`).

---

## 7. Hallazgos positivos (decisiones recientes bien fundamentadas)

1. Separación `received` (cohorte por `CreatedAtUtc`) vs `printed`/`failed` (evento) — modelo mental correcto.
2. `TimeProvider` inyectado en controllers y servicios → KPIs deterministas y testeables con reloj falso.
3. Centralización de `DashboardPrintJobPredicates` y `BusinessTimeZoneClock` — mata una divergencia API↔Worker real y previa.
4. Breakdown por impresora en el overview → el fallback PHP deja de necesitar el fetch de jobs crudos truncado a 500.
5. Watchdog Fase 1: orden por antigüedad + filtro en SQL evita starvation bajo backlog.
6. `action-icon` accesible.

---

## 8. Riesgos que bloquean despliegue (gate)

1. **A-KPI-01** (P1) — falsas recuperaciones y fallos ocultos: **corregir antes de producción**.
2. **A-KPI-02** (P1) — backfill de eventos o aceptación documentada de ventana ciega 7d/30d.
3. **A-KPI-05** (P2, gate HANA) — verificar tipo real de columnas de fecha; si son VARCHAR mixto, **bloquea F1 entero**.
4. **A-ARCH-01** (P1) — no ejecutar 2+ instancias de Worker hasta implementar lock (duplicaría eventos y por tanto KPIs).

Ver `roadmap-integral-2026-07-21.md` para el plan de ejecución.
