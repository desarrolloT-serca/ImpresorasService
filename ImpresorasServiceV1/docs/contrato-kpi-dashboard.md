# Contrato KPI — Dashboard operativo

Fuente de verdad única para los KPIs de `GET /api/dashboard/overview`. Cualquier cambio a estas
definiciones pasa por actualizar este documento primero. Decisiones de negocio registradas en
`docs/roadmap-kpi-dashboard.md` (D1-D6), aprobadas 2026-07-20. Revisión 2026-07-20 (v2): `printed`
y `failed` dejan de leer `Status`/`UpdatedAtUtc` del job y pasan a leer `PrintJobEvents` — ver
`docs/prompt-roadmap-kpi-dashboard.md` (KPI-P1-001/002). La v1 (snapshot por `UpdatedAtUtc`) contaba
el mismo trabajo en dos ventanas distintas cada vez que el watchdog lo tocaba de nuevo (p. ej.
`SpoolAccepted` un día y `PrintedConfirmed` al siguiente); quedó demostrado con un test reproducible
antes de corregirlo, no solo por inspección de código.

| KPI | Definición | Fuente / timestamp | Filtros |
|---|---|---|---|
| `received` | Trabajos **creados** en la ventana | `CreatedAtUtc >= from` | tienda efectiva; **solo tiendas activas** |
| `printed` | Trabajos cuya **primera** transición a un estado impreso ocurre en la ventana (cuenta una sola vez por `JobId`, sin importar cuántas veces se actualice después) | `MIN(PrintJobEvents.OccurredAtUtc)` agrupado por `JobId`, donde `NewStatus IN (SpoolAccepted, PrintedConfirmed, PrintedUnknown)`, filtrado a `[from, now]` | ídem |
| `failed` | Trabajos distintos con **al menos una** señal de fallo en la ventana (no se re-imputa un job que ya cuenta por otra señal en la misma ventana) | Unión, deduplicada por `JobId`: eventos con `NewStatus IN (ErrorFinal, RetryScheduled)` en `[from, now]`; **más** los jobs de `printed` (mismo cálculo) cuyo `AttemptCount > 1` | ídem |
| `queueCurrent` | Foto actual, sin ventana temporal | `Status IN (Pending, Routed, Printing, RetryScheduled)` | ídem |
| `failedWithoutRetryCurrent` | Fallidos sin reenvío pendiente — foto de estado actual, **sin ventana temporal** (corregido 2026-07-21, A-KPI-01: filtrar por `UpdatedAtUtc` hacía que un `ErrorFinal` terminal "desapareciera" al cruzar medianoche, ya que su `UpdatedAtUtc` no vuelve a moverse — falsas alertas de recuperación y fallos ocultos). Idéntico en `today`/`7d`/`30d` por diseño. **`Cancelled` queda fuera** (corregido 2026-08-17): cancelar es una decisión explícita del operador que cierra el trabajo, así que un cancelado tras agotar reintentos no está pendiente de reenvío — mientras contaba inflaba el KPI de forma permanente (nunca se resuelve solo) y mantenía viva la alerta de la tienda por trabajos ya cerrados. | `DashboardPrintJobPredicates.FailedWithoutRetryCurrent` (sin filtro de fecha) | ídem |
| `activePrinters` | Impresoras activas actuales | foto actual | tienda efectiva |
| `activeStores` | Tiendas activas actuales | foto actual | tienda efectiva |

## Antigüedad de las incidencias: `oldestQueuedAtUtc` / `oldestFailedWithoutRetryAtUtc`

Añadidos 2026-08-24 en `stores[]` de `GET /api/dashboard/overview`, para la columna «Desde» de las
incidencias priorizadas del dashboard. No son KPIs de periodo: siguen la misma regla **sin ventana**
que `queueCurrent` y `failedWithoutRetryCurrent` (A-KPI-01), porque una incidencia abierta hace tres
días sigue abierta hoy y filtrarla por la ventana la haría desaparecer del panel sin haberse resuelto.

| Campo | Definición | Fuente / timestamp |
|---|---|---|
| `oldestQueuedAtUtc` | Instante de creación del trabajo **en cola** más antiguo de la tienda. `null` si no hay cola. | `MIN(CreatedAtUtc)` sobre `Status IN (Pending, Routed, Printing, RetryScheduled)`, agrupado por tienda |
| `oldestFailedWithoutRetryAtUtc` | Última actualización del **fallo sin reenviar** más antiguo de la tienda. `null` si no hay ninguno. | `MIN(UpdatedAtUtc)` sobre `DashboardPrintJobPredicates.FailedWithoutRetryCurrent`, agrupado por tienda |

Se usa `CreatedAtUtc` para la cola (cuándo entró el trabajo, que es lo que el operador percibe como
espera) y `UpdatedAtUtc` para el fallo (cuándo quedó en estado terminal, no cuándo se creó).

La antigüedad de una incidencia de **conectividad** o de **host sin configurar** no sale de aquí: la
da `Printer.LastConnectionCheckAtUtc`, que ya viajaba en `GET /api/printers`. Sin chequeo previo el
dashboard muestra «sin comprobar», no un cero.

## Porcentajes: solo dentro del mismo periodo

Los KPIs sin ventana (`queueCurrent`, `failedWithoutRetryCurrent`) **no se expresan como porcentaje**
de `received`. Mezclar ambas escalas producía cifras imposibles — 32 fallos acumulados sobre 1
recibido en el día daba «3200%» — o un 0% que escondía 32 trabajos rotos. El dashboard los muestra en
absoluto, bajo el rótulo «Estado actual de la operación», separado del bloque «Rendimiento del
periodo», que es el único que lleva porcentajes y declara sobre qué base se calculan. Sin trabajos
recibidos no hay tasa de éxito: se pinta «—», nunca un 100% ficticio.

## `unassignedQueueCurrent` debe ser auditable desde la cola

Misma regla que `failedWithoutRetryCurrent`: `GET /api/printjobs?unassigned=true` devuelve
exactamente el conjunto que cuenta el KPI — en cola (`QueueStatuses`) **y** sin `PrinterId`. Fija sus
propios estados, así que entra en la misma cascada excluyente que `failedWithoutRetry` y no se
combina con `status`. Un trabajo sin impresora ya cerrado no cuenta ni en el KPI ni en el listado.

## `failedWithoutRetryCurrent` debe ser auditable desde la cola

El número que muestra el dashboard tiene que poder abrirse y listarse: `GET /api/printjobs?failedWithoutRetry=true`
aplica el mismo `DashboardPrintJobPredicates.FailedWithoutRetryCurrent`, no una copia, y es a donde
enlazan el dashboard y el botón "Sin reenviar" de la cola. Antes se enlazaba a `status=8`, que es solo
un subconjunto (`ErrorFinal`), así que el KPI mostraba más trabajos de los que el operador podía ver
—y la diferencia no era diagnosticable desde la UI. Si en el futuro cambia el predicado, no hay nada
que sincronizar: ambos lados leen la misma expresión.

## Por qué `failed` no es solo "eventos en la ventana"

Contar cada evento de `RetryScheduled`/`ErrorFinal` tal cual llevaría a que un mismo job sume varias
veces dentro de una única ventana (p. ej. dos reintentos el mismo día), y a que un job pudiera contar
dos veces en la misma consulta por caer a la vez en la rama de eventos y en la de "impreso con
reintento". `failed` cuenta **jobs distintos**, no eventos — un job con problemas ese día cuenta una
vez, sin importar cuántas señales de fallo tuvo.

## `AttemptCount` en "impreso con reintentos" es el valor actual, no el histórico

La rama de `failed` que añade "jobs de `printed` cuyo `AttemptCount > 1`" (A-KPI-06,
`docs/auditoria-integral-2026-07-21.md`) lee el `AttemptCount` **actual** del job en `PrintJobs`,
no el que tenía en el momento de esa primera impresión. Como un job impreso no vuelve a
reintentar, en la práctica el valor no cambia después de imprimirse — pero la semántica exacta es
"impreso, y hoy tiene más de un intento registrado", no "impreso tras al menos un reintento previo
a esa impresión". Diferencia sin impacto conocido hoy; si se necesitara exactitud histórica, capturar
`AttemptCount` en el propio evento de impresión en vez de leerlo del job.

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
