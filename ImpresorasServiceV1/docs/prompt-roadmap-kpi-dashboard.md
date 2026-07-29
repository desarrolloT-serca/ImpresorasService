# Prompt-roadmap: reparar KPIs de periodo del dashboard

Usa este documento como prompt para una IA validadora/desarrolladora. El objetivo no es hacer cambios cosmeticos: es cerrar de verdad el bug de KPIs por periodo del dashboard operativo de `ImpresorasServiceV1`.

## Rol

Actua como ingeniero senior de producto, backend y datos. Se extremadamente riguroso con la semantica de metricas, los casos de borde temporales y la evidencia de tests. No declares un KPI arreglado si solo se ha cambiado el timestamp consultado sin demostrar que el evento de negocio se cuenta una sola vez.

## Contexto del repo

- Solucion: `ImpresorasServiceV1`
- Stack:
  - API .NET 8: `src/ImpresorasService.Api`
  - Core/Infra .NET 8: `src/ImpresorasService.Core`
  - Worker .NET 8: `src/ImpresorasService.Worker`
  - Frontend Laravel/PHP: `src/ImpresorasService.Web.PHP`
  - Tests .NET: `tests/ImpresorasService.Api.IntegrationTests`
  - Tests PHP: `src/ImpresorasService.Web.PHP/tests`
- Dominio: ingesta de trabajos desde SAP HANA, cola interna, enrutado, envio al spooler, watchdog IPP y dashboard operativo.

## Estado actual a validar

Hay commits recientes que intentan cerrar el bug:

- `389d1b0` - Fase 1 forense: starvation, ingesta por job, `TimeProvider`.
- `4226f60` - semantica de KPIs de periodo.
- `3b4f025` - limpieza F4, `TimeProvider` en `PrintJobsController`, Telegram deshabilitado por defecto, docs.

Checks observados:

- `dotnet test ImpresorasServiceV1/tests/ImpresorasService.Api.IntegrationTests/ImpresorasService.Api.IntegrationTests.csproj --no-restore` pasa con `116/116`.
- `php artisan test` en `src/ImpresorasService.Web.PHP` pasa con `9/9`.
- `composer audit` falla con advisories en Laravel/Guzzle/PSR7/CommonMark. Esto no bloquea el KPI, pero debe quedar registrado como riesgo de seguridad separado.

## Hipotesis principal

El bug de KPIs no esta completamente resuelto.

El cambio actual mueve `printed` y `failed` desde `CreatedAtUtc` hacia `UpdatedAtUtc`, pero sigue contando sobre el estado actual de `PrintJob`. Eso no equivale necesariamente a contar eventos de negocio ocurridos en el periodo.

Ejemplo critico:

1. Dia 1: job pasa a `SpoolAccepted`.
2. Dia 1: dashboard cuenta `printed = 1`, porque `SpoolAccepted` esta en `PrintedStatuses`.
3. Dia 2: watchdog cambia el mismo job a `PrintedConfirmed` o `PrintedUnknown` y actualiza `UpdatedAtUtc`.
4. Dia 2: dashboard vuelve a contar `printed = 1`.

Resultado: el mismo trabajo puede contarse como impreso en dos periodos distintos.

## Archivos clave

Revisa obligatoriamente:

- `src/ImpresorasService.Api/Controllers/DashboardController.cs`
- `src/ImpresorasService.Web.PHP/app/Http/Controllers/DashboardController.php`
- `src/ImpresorasService.Web.PHP/app/Services/DashboardOverviewService.php`
- `src/ImpresorasService.Worker/SpoolAcceptedWatchdogBackgroundService.cs`
- `src/ImpresorasService.Worker/StoreHealthAlertBackgroundService.cs`
- `src/ImpresorasService.Core/Infrastructure/Services/PrintExecutionService.cs`
- `src/ImpresorasService.Core/Infrastructure/Services/RoutingService.cs`
- `src/ImpresorasService.Core/Domain/Entities/PrintJobEvent.cs`
- `tests/ImpresorasService.Api.IntegrationTests/Controllers/DashboardControllerTests.cs`
- `tests/ImpresorasService.Api.IntegrationTests/Controllers/DashboardControllerWindowTests.cs`
- `src/ImpresorasService.Web.PHP/tests/Feature/DashboardControllerTest.php`
- `docs/contrato-kpi-dashboard.md`
- `docs/roadmap-kpi-dashboard.md`
- `docs/roadmapimpresoras.md`

## Contrato KPI deseado

Antes de tocar codigo, confirma o ajusta este contrato. Si cambia, actualiza `docs/contrato-kpi-dashboard.md` primero.

| KPI | Semantica deseada | Fuente recomendada |
|---|---|---|
| `received` | trabajos creados en la ventana | `PrintJobs.CreatedAtUtc` |
| `printed` | trabajos que entraron por primera vez en un estado considerado impreso durante la ventana | `PrintJobEvents.OccurredAtUtc` + transicion a estado impreso |
| `failed` | trabajos que entraron en fallo durante la ventana | `PrintJobEvents.OccurredAtUtc` + transicion a fallo/retry/fallo tras intento |
| `queueCurrent` | foto actual de cola activa | `PrintJobs.Status` actual |
| `failedWithoutRetryCurrent` | foto actual de fallos sin reenvio, opcionalmente acotada por ventana si producto lo mantiene asi | definir explicitamente |
| `activePrinters` | impresoras activas actuales | `Printers.IsActive` |
| `activeStores` | tiendas activas actuales | `Stores.IsActive` |

Punto clave: `printed` no debe depender solo de `PrintJobs.UpdatedAtUtc`, porque `UpdatedAtUtc` se actualiza en transiciones posteriores que no son una nueva impresion.

## Hallazgos a validar

### KPI-P1-001: `printed` puede contar el mismo job en varios periodos

Evidencia esperada:

- `DashboardController.cs` cuenta `printed` con `jobsUpdatedInWindow.CountAsync(x => PrintedStatuses.Contains(x.Status))`.
- `PrintedStatuses` incluye `SpoolAccepted`, `PrintedConfirmed`, `PrintedUnknown`.
- `SpoolAcceptedWatchdogBackgroundService` cambia `SpoolAccepted -> PrintedConfirmed` o `SpoolAccepted -> PrintedUnknown` y actualiza `UpdatedAtUtc`.

Reproduccion minima:

- Congelar reloj en dia 2.
- Sembrar job con:
  - `CreatedAtUtc = dia 1`
  - estado actual `PrintedConfirmed`
  - `UpdatedAtUtc = dia 2`
  - evento historico `Printing -> SpoolAccepted` en dia 1
  - evento `SpoolAccepted -> PrintedConfirmed` en dia 2
- Esperado si `printed` significa primera impresion/procesado: dia 2 no debe contar como nueva impresion.
- Actual probable: dia 2 cuenta `printed = 1`.

Fix recomendado:

- Calcular `printed` desde `PrintJobEvents`, no desde `PrintJobs.UpdatedAtUtc`.
- Contar una sola vez por `JobId`, usando la primera transicion a estado impreso.
- Definir si la primera transicion impresa es:
  - `NewStatus == SpoolAccepted`, si el negocio acepta spooler como "impreso/procesado"; o
  - `NewStatus == PrintedConfirmed`, si se quiere evidencia posterior; o
  - dos KPIs separados: `processed` y `confirmed`.

### KPI-P1-002: `failed` puede mezclar evento de fallo con estado actual posterior

Evidencia esperada:

- `failed` usa `UpdatedAtUtc` + estado actual + `AttemptCount`.
- Si un job con fallo previo recibe otra actualizacion posterior, puede caer en una ventana distinta.

Fix recomendado:

- Calcular `failed` con eventos:
  - `NewStatus == ErrorFinal`
  - `NewStatus == RetryScheduled`
  - o transiciones a estados impresos con `AttemptCount > 1`, si el contrato mantiene "impreso tras reintento" como senal de fallo.
- Evitar que una transicion posterior no relacionada vuelva a imputar el fallo al periodo nuevo.

### KPI-P2-003: `activePrinters` y `activeStores` no se sobrescriben desde overview en PHP

Evidencia esperada:

- `DashboardController.php::applyOverviewKpis()` solo aplica:
  - `received`
  - `printed`
  - `failed`
  - `queueCurrent`
  - `failedWithoutRetryCurrent`
- No aplica:
  - `activePrinters`
  - `activeStores`

Fix recomendado:

- Si la API es fuente unica, PHP debe aplicar tambien `activePrinters` y `activeStores`.
- Alternativa: documentar que esos dos se calculan en PHP por vista filtrada, pero entonces no vender `overview` como fuente unica.

### KPI-P2-004: alertas de tienda usan medianoche UTC

Evidencia esperada:

- `StoreHealthAlertBackgroundService` usa `var windowStart = now.Date`.
- Dashboard API usa `Dashboard:BusinessTimeZone`, por defecto `Europe/Madrid`.

Fix recomendado:

- Inyectar o reutilizar la misma timezone de negocio que la API.
- Calcular `today` de alertas igual que dashboard.
- Anadir test alrededor de medianoche Madrid/UTC.

### ING-P2-005: catch de ingesta trata todo `DbUpdateException` como duplicado

Evidencia esperada:

- `IngestionService` agrega todos los `SourceJobId` a `sourceJobIdsToMarkProcessed` antes de persistir.
- Si `SaveChangesAsync` falla por una razon no duplicada, el catch incrementa `duplicatesCount`, limpia tracking y sigue.
- Luego `MarkJobsProcessedAsync(sourceJobIdsToMarkProcessed)` puede ACKear origen aunque el job no este en cola local.

Fix recomendado:

- Solo tratar como duplicado las violaciones comprobadas del indice unico `(SourceSystem, ExternalJobId)`.
- Para otros `DbUpdateException`, no ACKear y propagar o registrar error recuperable.
- Mover el alta de `SourceJobId` a la lista de ACK solo despues de persistencia local exitosa o duplicado confirmado ya existente.

## Roadmap de ejecucion

### Fase 0: validar contrato y reproducir bug

Objetivo: demostrar el fallo antes de tocar codigo.

Tareas:

1. Leer docs actuales de KPI.
2. Confirmar con negocio si `printed` debe significar:
   - primera aceptacion por spooler,
   - confirmacion fisica,
   - o ambas separadas.
3. Crear test .NET rojo para doble conteo de `SpoolAccepted -> PrintedConfirmed` en dos dias.
4. Crear test rojo para `failed` si hay transicion posterior que no debe imputar fallo al periodo.

Criterio de salida:

- Hay al menos un test rojo que reproduce el problema real.
- El contrato KPI queda actualizado.

**✅ HECHO (2026-07-20).** Los 5 hallazgos (KPI-P1-001, KPI-P1-002, KPI-P2-003, KPI-P2-004,
ING-P2-005) se confirmaron por lectura de código antes de tocar nada — `PrintJobEvent` sí registra
`NewStatus`/`OccurredAtUtc` en todo el ciclo de vida (ingesta, enrutado, ejecución, watchdog,
cancelación), así que la Fase 1 era técnicamente viable sin migraciones nuevas. Se resolvió la
pregunta de negocio de la tarea 2 sin abrir una ronda nueva de decisión: dado que D2/D3 (sesión
previa) ya fijaron que los 3 estados cuentan como "impreso", la pregunta que quedaba era técnica
(cuándo se cuenta cada job una sola vez), no de negocio — decisión aplicada: **primera transición a
cualquiera de los 3 estados**, deduplicado por `JobId`.

Test rojo escrito y ejecutado: `KpiP1_001_Printed_DoesNotDoubleCount_WhenSpoolAcceptedYesterdayAndConfirmedToday`
(`tests/.../Controllers/DashboardControllerWindowTests.cs`). Resultado con el código anterior a la
Fase 1: `Con error: 1, Superado: 0` — confirma el bug con evidencia de ejecución, no solo lectura.

### Fase 1: mover KPIs de eventos a `PrintJobEvents`

Objetivo: que `printed` y `failed` sean metricas de eventos, no inferencias desde estado actual.

Tareas:

1. En `DashboardController.cs`, construir queries sobre `PrintJobEvents`.
2. Para `printed`, contar `JobId` unicos cuya primera transicion a estado impreso cae en la ventana.
3. Para `failed`, contar eventos de fallo ocurridos en la ventana segun contrato.
4. Mantener `received` en `PrintJobs.CreatedAtUtc`.
5. Mantener `queueCurrent` como foto actual en `PrintJobs`.
6. Revisar `BuildStoreRowsAsync` para usar la misma semantica por tienda.
7. Revisar breakdown por impresora: decidir si `totalWindow` es recibidos, procesados o eventos por impresora. No mezclar nombres.

Criterio de salida:

- El mismo job no suma dos veces en `printed` por pasar de `SpoolAccepted` a `PrintedConfirmed`.
- Las filas por tienda suman con los KPIs globales.
- Tests con reloj falso cubren dia 1/dia 2.

**✅ HECHO (2026-07-20).**

Archivos modificados:
- `src/ImpresorasService.Api/Controllers/DashboardController.cs`: nuevo `LoadPrintedAndFailedAsync`
  (consulta `PrintJobEvents`, agrupa por `JobId`, toma `MIN(OccurredAtUtc)` para `printed`; para
  `failed` une eventos ErrorFinal/RetryScheduled con los jobs de `printed` con `AttemptCount>1`,
  deduplicado por `JobId`). `BuildStoreRowsAsync` recibe `printedRows`/`failedRows` en vez de
  recalcular desde `jobsUpdatedInWindow`. Renombrado `TotalWindow`→`ReceivedWindow` en el breakdown
  por impresora (decisión: se queda como cohorte de recibidos, no eventos — evita el nombre ambiguo
  que pedía la tarea 7).
- `tests/.../Controllers/DashboardControllerTests.cs` y `DashboardControllerWindowTests.cs`: los
  seeds que crean jobs en estados impreso/fallo ahora también crean el `PrintJobEvent`
  correspondiente (antes solo importaba `Status`/`UpdatedAtUtc`).

Tests añadidos (`DashboardControllerWindowTests.cs`):
- `KpiP1_001_Printed_DoesNotDoubleCount_WhenSpoolAcceptedYesterdayAndConfirmedToday` (el test rojo
  de Fase 0, ahora en verde — progresión temporal real con dos estados de BD y dos consultas).
- `Printed_CountsFirstPrintedTransitionInsideWindow`
- `Printed_DoesNotCountFirstPrintedTransitionBeforeWindowEvenIfUpdatedToday` (prueba que `printed`
  ignora `Status`/`UpdatedAtUtc` del job, lee solo el evento).
- `Failed_CountsFailureEventInsideWindow`
- `Failed_DoesNotMoveFailureToLaterWindowOnUnrelatedUpdate` (simétrico al anterior para `failed`).
- `Overview_StoreRowsSumToGlobalKpis` (invariante: KPIs globales = suma de filas por tienda, consulta
  admin sin `storeId`).

No se implementó `Today_UsesBusinessTimezoneEuropeMadrid` (ya cubierto por `Fixture3`/`Fixture9` de
la ronda anterior) ni `Overview_ActivePrintersAndStoresMatchKpis`/`Ingestion_...` (pertenecen a
Fase 2/4, no a esta fase).

Resultado de comandos:
- `dotnet test .../ImpresorasService.Api.IntegrationTests.csproj --no-restore` → **122/122** (117
  previos + 5 nuevos).
- `php artisan test` → **9/9**, sin cambios (esta fase no tocó PHP).

Riesgos residuales:
- La traducción a SQL HANA de `GroupBy(JobId).Min(OccurredAtUtc)` y del patrón `IN` con listas
  materializadas no está verificada contra HANA real — se apoya en el mismo gate ya pendiente de
  F3 (`docs/roadmap-kpi-dashboard.md`), no se añadió una prueba nueva de traducción HANA para esto.
- El diseño de `failed` (unión deduplicada por `JobId`, no conteo de eventos) es una decisión propia
  no explícitamente especificada en este documento — ver nota "Por qué `failed` no es solo eventos
  en la ventana" en `docs/contrato-kpi-dashboard.md`.

Decisión: seguir a Fase 2 (alinear PHP con la fuente única — KPI-P2-003) cuando se indique.

### Fase 2: alinear PHP con la fuente unica

Objetivo: que el frontend no reintroduzca cifras inconsistentes.

Tareas:

1. `applyOverviewKpis()` debe aplicar todos los KPIs del overview o documentar explicitamente excepciones.
2. Revisar `dashboard.blade.php` y `dashboard-local.blade.php` para nombres honestos.
3. Si el overview no responde, fallback PHP debe mostrar aviso claro de datos parciales.
4. Si el fallback sigue existiendo, alinear su semantica con eventos solo si la API expone eventos suficientes; si no, marcarlo como aproximacion.

Criterio de salida:

- Con overview disponible, PHP no recalcula ningun KPI principal desde `api/printjobs`.
- Tests PHP verifican que `activePrinters` y `activeStores` tambien salen del overview.

**✅ HECHO (2026-07-20).**

Tarea 1 (`applyOverviewKpis`): añadidos `activePrinters`/`activeStores` al bucle que copia campos
del overview — antes se quedaban con el cálculo local (`array_sum(connectedPrinters)` /
"tiendas con ≥1 impresora conectada"), una definición distinta de la de la Api ("tiendas con
`IsActive=true`"). Con overview disponible, gana la definición de la Api.

**Bug real encontrado y corregido de paso:** el rename `totalWindow`→`receivedWindow` de Fase 1
(lado Api, decisión #2 de la sesión anterior) nunca se propagó a PHP. `applyOverviewPrinters()`
seguía leyendo la clave vieja `totalWindow` del overview, que ya no existe — el chip de "recibidos
por impresora" se habría quedado silenciosamente en 0 en producción pese a que el fix de Fase 1 ya
estaba desplegado. Corregido: las 7 referencias a `totalWindow` en `DashboardController.php` (array
de scaffold, merge de overview, docblock) renombradas a `receivedWindow`.

Tarea 2 (nombres honestos en vistas): revisado — "Impresos"/"Recibidos"/"Fallidos" son etiquetas
genéricas que no afirman confirmación física, coherente con la decisión D2/D3 ya registrada en
`docs/contrato-kpi-dashboard.md`. Sin cambios necesarios.

Tarea 3 (aviso de datos parciales): ya implementado en trabajo previo de esta sesión (F2.2).

Tarea 4 (fallback marcado como aproximación): añadido comentario explícito en
`DashboardController.php` sobre el bucle de fallback — `api/printjobs` no expone `PrintJobEvents`,
así que el fallback no puede deduplicar por evento como la Api; puede sobre-contar un job que cambió
de estado impreso más de una vez en la misma ventana. Aceptable porque solo se activa con la Api
caída.

Tests añadidos (`tests/Feature/DashboardControllerTest.php`):
- `test_dashboard_uses_all_overview_kpis`
- `test_dashboard_does_not_recalculate_kpis_when_overview_exists` (prueba que `partialData` se
  queda en `false` con 600 jobs crudos simulados — señal de que el bucle de agregación local nunca
  se ejecuta cuando el overview responde, no solo que su resultado se sobrescribe al final).

Resultado de comandos:
- `php artisan test` → **11/11** (9 previos + 2 nuevos).
- `dotnet test .../ImpresorasService.Api.IntegrationTests.csproj --no-restore` → **122/122**, sin
  cambios (esta fase no tocó C#).

Riesgos residuales: ninguno nuevo. El fallback sigue siendo una aproximación por diseño (documentado
en código); no se intentó exponer `PrintJobEvents` vía API para que PHP pueda replicar la
deduplicación exacta — sería alcance nuevo, no pedido por esta fase.

Decisión: seguir a Fase 3 (alinear alertas y salud de tienda con la timezone de negocio) cuando se
indique.

### Fase 3: alinear alertas y salud de tienda

Objetivo: que dashboard y alertas usen el mismo reloj y definicion de fallo.

Tareas:

1. Usar `Dashboard:BusinessTimeZone` o una opcion compartida en `StoreHealthAlertBackgroundService`.
2. Evitar `now.Date` UTC.
3. Decidir si alertas usan `failedWithoutRetryCurrent` como foto actual o eventos de fallo del dia.
4. Anadir test de medianoche Madrid.

Criterio de salida:

- Una tienda no aparece saludable en dashboard y critica en alerta por corte horario distinto.

**✅ HECHO (2026-07-20).**

Se encontraron **dos** divergencias reales entre dashboard y alertas, no solo la de timezone que
pedía la tarea 1/2:

1. **Reloj distinto** — `StoreHealthAlertBackgroundService` usaba `now.Date` (medianoche UTC);
   `DashboardController` ya usaba `Dashboard:BusinessTimeZone` (Europe/Madrid) desde Fase 1.
2. **Definición de fallo distinta** — el Worker mantenía su propia copia del predicado
   (`IsFailedAfterRetry`), que excluía `Printing` con reintentos; `DashboardPrintJobPredicates.FailedWithoutRetryCurrent`
   (la Api) sí lo cuenta como fallo. Un job en `Printing` con `AttemptCount>1` podía verse "healthy"
   en la alerta y contribuir a "warning/critical" en el dashboard, exactamente el síntoma del
   criterio de salida.

Fix (tarea 1+3, root-cause en vez de duplicar lógica de nuevo): ambas piezas compartidas se movieron
a `ImpresorasService.Core` para que sea estructuralmente imposible que Api y Worker vuelvan a divergir:
- `Application/Services/DashboardPrintJobPredicates.cs` (antes vivía solo en `DashboardController.cs`).
- `Application/Services/BusinessTimeZoneClock.cs` (nuevo — `Resolve()` + `TodayStartUtc()`, usado
  ahora por `DashboardController` **y** `StoreHealthAlertBackgroundService`).

`StoreHealthAlertBackgroundService` recibe `IConfiguration` por constructor y lee
`Dashboard:BusinessTimeZone` (misma clave que la Api; default `Europe/Madrid` si no está configurada
— antes tenía Madrid hardcodeado con una rama condicional por SO que ya no hacía falta, ver nota
abajo). Añadida la misma clave a `Worker/appsettings.json`.

Tarea 2 (evitar `now.Date` UTC): resuelto como consecuencia directa del fix anterior.

Tarea 4 (test de medianoche Madrid): no existe ningún test de integración para servicios
`BackgroundService` del Worker en este repo (sin precedente, y montar el DI/hosting completo solo
para esto era alcance mayor al pedido). Se testeó directamente `BusinessTimeZoneClock.TodayStartUtc`
— la función compartida por los dos llamadores — con 3 casos (invierno CET, verano CEST, minutos
antes de medianoche Madrid). Cubre a ambos consumidores sin duplicar cobertura ni levantar el host
del Worker.

Archivos modificados:
- `src/ImpresorasService.Core/Application/Services/DashboardPrintJobPredicates.cs` (nuevo, movido).
- `src/ImpresorasService.Core/Application/Services/BusinessTimeZoneClock.cs` (nuevo).
- `src/ImpresorasService.Api/Controllers/DashboardController.cs` (usa los dos helpers compartidos
  en vez de su copia local).
- `src/ImpresorasService.Worker/StoreHealthAlertBackgroundService.cs` (`IConfiguration` inyectada,
  `_spainTz`/`IsFailedAfterRetry` eliminados, usa los helpers compartidos).
- `src/ImpresorasService.Worker/appsettings.json` (`Dashboard:BusinessTimeZone`).
- `tests/.../Controllers/DashboardControllerTests.cs` (using actualizado tras el move).

Tests añadidos: `tests/ImpresorasService.Api.IntegrationTests/BusinessTimeZoneClockTests.cs` (3 casos).

Resultado de comandos:
- `dotnet build` Api + Worker (Worker compilado a carpeta temporal — dos servicios de Windows
  instalados localmente tenían el DLL bloqueado; parados y reiniciados con permiso del usuario) → 0 errores.
- `dotnet test .../ImpresorasService.Api.IntegrationTests.csproj --no-restore` → **125/125** (122
  previos + 3 nuevos).
- `php artisan test` → **11/11**, sin cambios (esta fase no tocó PHP).

Riesgos residuales:
- No hay test de integración end-to-end del Worker (`RunOnceAsync`) — la cobertura es sobre la
  función de reloj compartida, no sobre la orquestación completa del servicio. Si se quiere cerrar
  esa brecha, haría falta introducir un patrón de test para `BackgroundService` en este proyecto,
  fuera del alcance pedido aquí.
- Se simplificó la resolución de timezone del Worker quitando la rama condicional por SO
  ("Romance Standard Time" en Windows vs "Europe/Madrid" en Linux) para igualarla a la de la Api,
  que ya funciona sin esa rama en este stack (.NET 8 resuelve IDs IANA vía ICU en Windows). No
  verificado en un despliegue Linux real (el Worker de este proyecto solo se despliega en Windows
  según `docs/ejecutable.md`).

Decisión: seguir a Fase 4 (endurecer ingesta, ING-P2-005) cuando se indique.

### Fase 4: endurecer ingesta

Objetivo: no perder jobs por ACK indebido.

Tareas:

1. Clasificar `DbUpdateException`.
2. ACKear origen solo cuando:
   - el job fue insertado localmente; o
   - el duplicado local esta confirmado por indice/consulta.
3. Test con error de BD no duplicado: no debe llamar a `MarkJobsProcessedAsync`.

Criterio de salida:

- Un fallo no duplicado de persistencia no marca origen como procesado.

### Fase 5: validacion HANA/staging

Objetivo: confirmar que las queries nuevas son traducibles y correctas en HANA.

Pruebas:

1. Query de `PrintJobEvents` con `OccurredAtUtc >= from` y `<= now`.
2. Agrupacion por `JobId` para primera transicion impresa.
3. Agrupacion por `StoreId`.
4. Fechas cerca de medianoche Europe/Madrid.
5. Dataset con mas de 500 jobs.
6. Comparacion 48h dashboard vs consultas SQL manuales.

Criterio de salida:

- Queries funcionan en HANA real/staging.
- No hay divergencia visible en dashboard.

## Tests minimos que deben existir

### .NET

- `Printed_DoesNotDoubleCount_WhenSpoolAcceptedYesterdayAndConfirmedToday`
- `Printed_CountsFirstPrintedTransitionInsideWindow`
- `Printed_DoesNotCountFirstPrintedTransitionBeforeWindowEvenIfUpdatedToday`
- `Failed_CountsFailureEventInsideWindow`
- `Failed_DoesNotMoveFailureToLaterWindowOnUnrelatedUpdate`
- `Today_UsesBusinessTimezoneEuropeMadrid`
- `Overview_StoreRowsSumToGlobalKpis`
- `Overview_ActivePrintersAndStoresMatchKpis`
- `Ingestion_DoesNotAckSourceJob_WhenDbUpdateExceptionIsNotDuplicate`

### PHP

- `dashboard_uses_all_overview_kpis`
- `dashboard_does_not_recalculate_kpis_when_overview_exists`
- `dashboard_fallback_marks_partial_data`

## Comandos de verificacion

Ejecutar desde la raiz del repo:

```powershell
dotnet test ImpresorasServiceV1/tests/ImpresorasService.Api.IntegrationTests/ImpresorasService.Api.IntegrationTests.csproj --no-restore
```

Ejecutar desde `ImpresorasServiceV1/src/ImpresorasService.Web.PHP`:

```powershell
php artisan test
composer audit
```

## Criterios de cierre

El bug KPI se considera cerrado solo si:

1. `printed` no puede contar el mismo job en dos periodos por transiciones posteriores.
2. `failed` se imputa al periodo del evento de fallo, no a cualquier actualizacion posterior.
3. Dashboard global, filas por tienda y chips por impresora tienen semanticas documentadas y coherentes.
4. PHP usa la API como fuente unica para todos los KPIs principales.
5. Alertas y dashboard comparten timezone de negocio.
6. Tests .NET y PHP pasan.
7. HANA/staging valida traduccion SQL y datos temporales.
8. `composer audit` queda triageado en un ticket separado si no se arregla en este roadmap.

## Salida esperada de la IA

Entrega un informe y luego implementa por fases. Para cada fase, incluye:

- archivos modificados,
- tests anadidos,
- resultado de comandos,
- riesgos residuales,
- decision de seguir/parar.

No mezcles fixes de seguridad, IPP fisico o claim atomico del worker salvo que sean necesarios para cerrar la semantica KPI. Si aparecen, registralos como deuda relacionada, pero manten el foco.
