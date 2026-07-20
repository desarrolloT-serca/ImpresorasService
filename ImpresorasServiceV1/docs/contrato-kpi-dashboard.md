# Contrato KPI — Dashboard operativo

Fuente de verdad única para los KPIs de `GET /api/dashboard/overview`. Cualquier cambio a estas
definiciones pasa por actualizar este documento primero. Decisiones de negocio registradas en
`docs/roadmap-kpi-dashboard.md` (D1-D6), aprobadas 2026-07-20.

| KPI | Definición | Fuente / timestamp | Filtros |
|---|---|---|---|
| `received` | Trabajos **creados** en la ventana | `CreatedAtUtc >= from` | tienda efectiva; **solo tiendas activas** |
| `printed` | Trabajos que **pasaron a estado impreso** durante la ventana (throughput, no cohorte) | `UpdatedAtUtc >= from` + `Status IN (SpoolAccepted, PrintedConfirmed, PrintedUnknown)` | ídem |
| `failed` | Trabajos que **entraron en fallo** durante la ventana | `UpdatedAtUtc >= from` + (`Status == ErrorFinal` OR `Status == RetryScheduled` OR (estado impreso AND `AttemptCount > 1`)) | ídem |
| `queueCurrent` | Foto actual, sin ventana temporal | `Status IN (Pending, Routed, Printing, RetryScheduled)` | ídem |
| `failedWithoutRetryCurrent` | Fallidos sin reenvío pendiente, actualizados en la ventana (misma ventana que `failed`) | `UpdatedAtUtc >= from` + `DashboardPrintJobPredicates.FailedWithoutRetryCurrent` | ídem |
| `activePrinters` | Impresoras activas actuales | foto actual | tienda efectiva |
| `activeStores` | Tiendas activas actuales | foto actual | tienda efectiva |

## Ventana temporal

- `today` = medianoche → ahora en **Europe/Madrid** (configurable vía `Dashboard:BusinessTimeZone`,
  default `Europe/Madrid`), independiente de la timezone del servidor donde corre la Api.
- `7d` / `30d` = ventanas **rodantes** (`now - 7 días` / `now - 30 días`), no días de calendario.
- Límite superior: implícito en "ahora" (`TimeProvider`), sin cota superior explícita salvo F4.1.

## Estados considerados "impreso"

`SpoolAccepted`, `PrintedConfirmed`, `PrintedUnknown` — los tres cuentan. Ninguno es hoy evidencia
física verificada por trabajo (ver `roadmapimpresoras.md` Fase 3 / hallazgo P1-003). Revisar esta
decisión cuando exista confirmación IPP por job.

## Fuente única

La Api (`DashboardController.cs`) es la única que calcula estos valores. El frontend PHP los
muestra tal cual (`overview.kpis`); el cálculo legacy sobre `api/printjobs` es solo fallback si el
overview no responde (ver F2 del roadmap).
