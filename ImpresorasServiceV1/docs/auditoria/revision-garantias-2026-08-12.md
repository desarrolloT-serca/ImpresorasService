# Revisión de garantías — ImpresorasService

**Snapshot revisado:** `main` @ `2d3e193e` (verificado: HEAD no ha avanzado respecto al documento de handoff).
**Entrada:** `Auditoria_Contexto_Revision_ImpresorasService_2026-08-12.md` (mapa de investigación).
**Método:** lectura estática del código del snapshot. Sin HANA, sin Windows Service, sin impresora, sin bot.

Etiquetas: **DEMOSTRADO** (leído en el código de este SHA) · **HIPÓTESIS** · **STAGING** (solo verificable con HANA/Windows/dispositivo real) · **NEGOCIO** (decisión, no defecto).

---

## 1. Resumen ejecutivo

La garantía real que ofrece hoy el sistema es **at-least-once sobre el spooler, con confirmación por impresora y no por trabajo**. Ninguna de las dos mitades está declarada como tal en el código ni en la UI, y ambas producen afirmaciones más fuertes que su evidencia:

- `PrintedConfirmed` se asigna a **todos** los trabajos pendientes de una impresora con una sola lectura de `printer-state`. No es una confirmación por documento.
- Un `Printing` stale se reenvía al spooler sin ningún identificador de spool que permita saber si el envío anterior llegó a salir por papel.

A eso se suman tres cosas que no estaban en el mapa de investigación y que considero de la misma gravedad:

- La ingesta puede **acusar recibo de un trabajo que nunca se persistió**, perdiendo el PDF de forma definitiva y silenciosa (H-05).
- El esquema HANA de las tablas principales **no existe en el repositorio** en ninguna forma versionada — ni DDL ni migraciones EF (H-06).
- Todas las fechas se persisten como **cadena de texto**, y todos los filtros y ordenaciones temporales del Worker dependen de cómo HANA compare esa columna (H-07).

Lo que el mapa daba por maduro, lo es: el lock de instancia está implementado y es un CAS correcto, la idempotencia por índice único está bien planteada, y el modelo de estados de incertidumbre (`SpoolAccepted` / `PrinterBlocked` / `PrintedUnknown`) es conceptualmente sano. El problema no es que falten piezas, es que **tres de ellas prometen más de lo que demuestran**.

---

## 2. Hallazgos P0

### H-01 · Confirmación IPP colectiva por impresora, no por trabajo — **DEMOSTRADO**
*(cubre P0-02)*

`SpoolAcceptedWatchdogBackgroundService.cs:183-202` consulta IPP **una vez por host único** y guarda el resultado en un diccionario `host → IppQueryResult`. Después, `ResolveIppResult` (L204-214) devuelve **ese mismo resultado** a cada job del lote que apunte a esa impresora, y `ApplyOutcome` (L230-237) lo convierte en `PrintedConfirmed`.

Consecuencia directa: si hay 8 trabajos `SpoolAccepted` de la tienda 12 y la impresora responde `idle` una vez, los 8 pasan a `PrintedConfirmed` en la misma iteración. El batch es de 50 (`SpoolAcceptedWatchBatchSize`).

Peor caso realista: la cola de Windows está mal configurada y Sumatra devuelve 0 sin que nada llegue al dispositivo. La impresora está `idle` porque no tiene nada que hacer. Todos los trabajos se marcan confirmados. **`PrintedConfirmed` es hoy indistinguible de "la impresora no estaba ocupada cuando miramos".**

Un `idle` es evidencia de ausencia de trabajo en curso, nunca de trabajo completado.

**Mínimo:** renombrar la semántica y dejar de emitir `PrintedConfirmed` desde una señal global. `PrinterIdleAfterSpool` describe exactamente lo que se sabe. El coste es un rename + la UI; el beneficio es que el KPI deja de mentir.
**Robusto:** correlacionar con `Get-Jobs` / `job-id`. Requiere STAGING para saber qué modelos lo soportan.
**Aceptación:** con dos trabajos encolados y la impresora imprimiendo solo el primero, el segundo no debe quedar `PrintedConfirmed`.

---

### H-02 · Simulación silenciosa en producción — **DEMOSTRADO**
*(cubre P0-05)*

`DependencyInjection.cs:63-68`:

```csharp
var useRealSpooler = configuration.GetValue<bool>("PrintExecution:UseRealSpooler")
    && RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
if (useRealSpooler) services.AddScoped<IPrinterSpooler, WindowsPrintSpooler>();
else services.AddSingleton<IPrinterSpooler>(new NoOpPrintSpooler(simulateSuccess: true));
```

No hay rama de error. Un `UseRealSpooler=false` por descuido, un despliegue en un host no-Windows, o una sección `PrintExecution` que no se cargue, degradan a `NoOpPrintSpooler` que **devuelve éxito siempre** (`NoOpPrintSpooler.cs:23-24`). Los trabajos avanzan a `SpoolAccepted` y de ahí, vía H-01, a `PrintedConfirmed`. El sistema reporta una jornada perfecta sin haber impreso una hoja.

`Worker/Program.cs` no comprueba nada de esto al arrancar: valida `BackoffSeconds` y `MaxAttempts` (`DependencyInjection.cs:29-36`) pero no el spooler.

**Mínimo (ladder rung 7, ~10 líneas):** en `AddInfrastructure`, si el entorno es Production y `useRealSpooler` es false → `throw`. Modo simulación solo con `PrintExecution:Mode=Simulation` explícito.
**Aceptación:** arrancar el Worker en Production con `UseRealSpooler=false` debe fallar el arranque, no imprimir en el vacío.

---

### H-03 · `RowVersion` no es control de concurrencia — **DEMOSTRADO Y CONFIRMADO CONTRA HANA**
*(cubre P0-03; confirma la hipótesis del documento)*

> **Verificado el 12/08/2026 contra `ZTEST_VICENTE_2`:** `printer_print_job.row_version` es **`BLOB`**
> (`scripts/sql/schema/printer_print_job.sql`). Queda descartada la opción de activar el token de
> concurrencia de EF: HANA no puede comparar BLOB en un `WHERE`. **La vía es la propuesta mínima de
> abajo** — claims con `ExecuteUpdateAsync` condicional sobre `status`. La alternativa "robusta"
> (migrar a VARBINARY) exigiría un `ALTER` sobre una columna en uso y no aporta nada que el claim
> condicional no dé.

Tres capas distintas, ninguna es un compare-and-swap:

1. `ImpresorasDbContext.cs:176-197` genera 8 bytes aleatorios en cada `Added`/`Modified`. No hay `.IsConcurrencyToken()` ni `.IsRowVersion()` en el mapeo (L237, y el comentario L247-249 lo declara explícitamente por la limitación BLOB de HANA). **El `UPDATE` emitido no lleva `WHERE row_version = @old`.**
2. `PrintExecutionService.RowVersionSnapshotStillMatches` (L433-441) compara el snapshot del lote contra lo leído dentro de la transacción. Es un *read-then-check*: con READ COMMITTED, dos lectores pueden ver el mismo valor y ambos escribir.
3. `DbUpdateConcurrencyException` se captura en el watchdog (L152-155) para un evento que **este mapeo no puede producir**. Es código muerto que da falsa confianza.

Además, dos rutas escriben con `Attach` + `IsModified` sin pasar siquiera por el chequeo en memoria: `PrintExecutionService.RescuePendingJobAsync` (L356-370) y el watchdog (L123-134).

El lock global mitiga esto **entre réplicas**, no entre el Worker y la API: `PrintJobsController.Cancel` (L185) y `Route` escriben `Status` sin ninguna coordinación con el ciclo de ejecución.

**Mínimo:** convertir los cambios de estado críticos en `ExecuteUpdateAsync` con `Where(status == esperado)` y comprobar `rows == 1` — el mismo patrón que ya usa correctamente `WorkerLockCoordinator.cs:47-53`. La pieza ya está en la casa; solo hay que reusarla.
**Robusto:** migrar `row_version` a VARBINARY comparable y activar el token EF. Requiere STAGING + DDL.
**Aceptación:** dos `ExecuteBatchAsync` concurrentes sobre el mismo job deben producir exactamente un envío al spooler.

---

### H-04 · Ventana de crash entre commit y efecto físico — **DEMOSTRADO**
*(cubre P0-01; confirma la tesis central del documento)*

`PrintExecutionService.cs:256-267`: se persiste `Printing`, se hace `tx.CommitAsync`, y **después** se llama al spooler. Es la ordenación correcta (mejor un `Printing` huérfano que una impresión sin registro), pero deja tres ventanas:

| Ventana | Estado en BD | Realidad física | Qué hace el sistema |
|---|---|---|---|
| A · muere tras el commit, antes de Sumatra | `Printing` | nada impreso | reintenta a los 40 s. Correcto. |
| B · muere durante Sumatra | `Printing` | **indeterminado** | reintenta a los 40 s. **Puede duplicar.** |
| C · muere tras Sumatra, antes de `SpoolAccepted` | `Printing` | impreso | reintenta a los 40 s. **Duplica.** |

El rescate está en `ExecuteBatchAsync` L52: `Status == Printing && UpdatedAtUtc <= now - (TimeoutSeconds + 10)`. Con `TimeoutSeconds=30`, cualquier `Printing` de más de 40 segundos se reenvía. **No existe ningún identificador de spool persistido** — `PrintSpoolResult` (usado en `WindowsPrintSpooler.cs:146`) devuelve solo `(bool, errorCode, errorMessage, isTransient)`. No hay nada con lo que reconciliar tras un reinicio.

Esto no es un bug: es una garantía que el sistema tiene y no declara. La transacción única BD+impresora no existe y no puede existir.

**Mínimo:** que el rescate de `Printing` stale no sea automático. Enviarlo a un estado `PrintingUnknown` visible en la cola de excepciones, y que el reenvío sea una decisión humana con el riesgo de duplicado escrito en la pantalla. Cambia el reparto de riesgo, no la física.
**Robusto:** capturar el job-id del spooler de Windows tras el envío y reconciliar contra la cola al arrancar. STAGING.
**Aceptación:** matar el Worker entre `Process.Start` y el commit de `SpoolAccepted` no debe producir un segundo papel sin intervención humana.

---

### H-05 · La ingesta acusa recibo de trabajos que no persistió — **DEMOSTRADO** · *no estaba en el mapa*
*(afecta a P0-04)*

`IngestionService.cs:96-114`:

```csharp
try { await _printJobRepository.SaveChangesAsync(ct); insertedCount++; }
catch (DbUpdateException ex) { duplicatesCount++; /* log "Duplicado intra-lote" */ }
```

`DbUpdateException` **no significa "violación de índice único"**. Cubre también timeouts de HANA, pérdida de conexión, desbordes de longitud y cualquier fallo de constraint. El `catch` los clasifica todos como duplicado.

Y a continuación, L117-122, **fuera del try**:

```csharp
await _jobSourceAdapter.MarkJobsProcessedAsync(sourceJobIdsToMarkProcessed, ct);
```

`sourceJobIdsToMarkProcessed` se rellena en L46, **antes** de intentar el insert, con todos los ids del fetch. `MarkJobsProcessedAsync` pone `IsProcessed = true` y libera el claim (`SapHanaJobSourceAdapter.cs:145-151`).

Secuencia completa del fallo: HANA tiene un hipo de 2 s durante el insert → el job no se persiste → se cuenta como duplicado → el source row se marca procesado → **el PDF desaparece y nadie lo sabe**. La única traza es un `LogWarning` que dice "Duplicado intra-lote descartado", que es exactamente lo contrario de lo que ocurrió.

**Mínimo:** solo hacer ack de los ids realmente resueltos. Distinguir violación de unicidad (→ ack, es un duplicado real) de cualquier otro `DbUpdateException` (→ no hacer ack, dejar que el lease expire y se reintente).
**Aceptación:** inyectar un fallo de conexión en el `SaveChanges` de un job y comprobar que su source row sigue con `is_processed = false`.

---

### H-06 · No existe esquema versionado de las tablas principales — **DEMOSTRADO · resuelto el 12/08/2026**
*(cubre P0-06)*

> **Resuelto.** `scripts/extraer-ddl-hana.ps1` reconstruye el DDL desde el catálogo de HANA y lo
> escribe en `scripts/sql/schema/`, un fichero por tabla, más `_inventario.sql` con columnas, claves
> e índices. Ejecutado contra `ZTEST_VICENTE_2`: **11 de las 12 tablas del modelo extraídas**.
>
> `GET_OBJECT_DEFINITION` no sirve aquí — normaliza los nombres a mayúsculas y estas tablas están
> creadas en minúsculas —, de ahí la reconstrucción desde `SYS.TABLE_COLUMNS` / `SYS.CONSTRAINTS` /
> `SYS.INDEXES`. Cubre columnas, tipos, defaults, PK y únicos; no cubre claves ajenas ni triggers
> (hoy no hay).
>
> **Lo que reveló la extracción, y es buena noticia:** todos los índices que el código da por
> supuestos existen realmente — el único de ingesta `ix_printer_print_job_source_external`, el de
> `(status, next_retry_at_utc)`, el de `(job_id, occurred_at_utc)`, el de `(store_id, spool_queue)`,
> el de resolución de routing y los dos de claim de la fuente. La deriva de esquema es mucho menor
> de lo que este hallazgo temía.

---

### H-14 · `printer_worker_lock` no existe en la base de datos — **DEMOSTRADO** · *no estaba en el mapa*

La extracción de H-06 encontró **once tablas de doce**. La ausente es `printer_worker_lock`, la que
sostiene el lock de instancia única. `scripts/sql/create_worker_lock.sql` existe en el repositorio
desde hace tiempo, pero **nunca se ha aplicado** a este esquema (el `CLAUDE.md` del proyecto ya lo
listaba como pendiente; ahora está demostrado).

La consecuencia no es "el Worker corre sin lock". Es peor, y se sigue del código:

1. `WorkerLockCoordinator.TryAcquireOrRenewAsync` (L27) hace `WorkerLocks.AnyAsync(x => x.Id == 1)`
   contra una tabla inexistente → excepción del provider.
2. `WorkerLockBackgroundService` (L55-59) la captura, registra un warning y fija `acquired = false`.
3. `IsHolder` queda en false, y **los cinco BackgroundService comprueban ese flag antes de
   trabajar** (`PrintExecutionBackgroundService.cs:33`, watchdog L46, alertas L57, ingesta,
   conectividad): todos entran en el `Task.Delay(5s); continue;`.

Con `WorkerLock:Enabled=true` (el valor por defecto de `WorkerLockOptions`, y el efectivo porque
`Worker/appsettings.json` no lo declara), **el Worker arranca correctamente y no procesa
absolutamente nada**: ni ingiere, ni imprime, ni confirma, ni monitoriza, ni alerta. El único
síntoma es un warning por heartbeat.

**Corrección:** aplicar `scripts/sql/create_worker_lock.sql` al esquema. Es la creación de una tabla
singleton, sin impacto sobre datos existentes.

> **Resuelto el 19/08/2026.** La tabla ya existe en `ZTEST_VICENTE_2` y `IMPRESION` tiene
> SELECT/INSERT/UPDATE/DELETE sobre ella. El síntoma se dio tal cual describe el hallazgo (el 17/08
> el Worker estuvo horas en Running sin procesar nada), aunque por privilegios y no por ausencia de
> tabla: entre el 12 y el 17 se creó, pero sin GRANT, así que el provider seguía lanzando —error 258
> en vez de 259— y el efecto era idéntico. Se sorteó temporalmente con `WorkerLock__Enabled=false`
> en el entorno del servicio; ese apaño ya está retirado y el lock real está activo y renovando.
> De aquel episodio quedan dos defensas: el fallo del lock escala a `Error` en
> `WorkerLockBackgroundService`, y el health check `worker` de la Api delata al Worker inerte.

**Comprobación pendiente y más importante:** esta extracción se hizo contra `ZTEST_VICENTE_2`. **Hay
que repetirla contra el esquema de producción** (`.\scripts\extraer-ddl-hana.ps1 -Schema <prod>`) y
ver si allí la tabla existe. Si tampoco existe y el Worker de producción tiene el lock habilitado,
explica por sí solo cualquier síntoma de "el servicio está arrancado pero no imprime".

`scripts/sql/` contiene 13 ficheros: `create_worker_lock.sql`, `create_sap_aux_print_queue.sql`, un seed, `migrate_pdf_blob_nullable.sql` y nueve scripts de diagnóstico puntual (`diagnose_g1_*`, `diagnose_g2_*`, `g1_validacion_borrar_evento.sql`, `g2_5_backdatar_timezone.sql`).

**No hay ningún `CREATE TABLE` de:** `printer_print_job`, `printer_print_job_event`, `printer_printer`, `printer_routing_rule`, `printer_store`, `printer_user`, `printer_dashboard_threshold`, `printer_telegram_config`, `printer_telegram_chat`, `printer_alert_state`.

Tampoco existe carpeta `Migrations/` en `ImpresorasService.Core` — el CLAUDE.md del proyecto afirma que "EF Migrations son solo referencia histórica", pero **no quedan migraciones**. Con `Database:ApplyMigrations=false` (`Worker/appsettings.json`), la afirmación operativa es que el esquema vive **solo en la cabeza del DBA y en la instancia de producción**.

Esto convierte varios hallazgos de este informe en no-verificables: no se puede saber si `row_version` es BLOB o VARBINARY (H-03), ni si `updated_at_utc` es NVARCHAR o TIMESTAMP (H-07), leyendo el repositorio.

Nota adicional: `create_worker_lock.sql:10` declara `heartbeat_utc TIMESTAMP`, pero el converter global del `DbContext` (L493-506) escribe esa propiedad como **cadena**. Funciona por conversión implícita de HANA, pero es un ejemplo de la divergencia que nadie está vigilando.

**Mínimo:** extraer el DDL real de producción a `scripts/sql/schema/` y congelarlo como línea base. Un día de trabajo, y desbloquea todo lo demás.
**Robusto:** *schema compatibility check* al arrancar, que valide columnas y tipos críticos contra el modelo EF y falle rápido.
**Aceptación:** poder recrear un entorno HANA vacío desde `scripts/sql/` y que el Worker arranque.

---

## 3. Hallazgos P1

### H-07 · Modelo temporal híbrido y converter global innecesario — **RESUELTO EN STAGING · degradado de P1 a P2**

> **Corrección (verificado contra HANA el 12/08/2026).** La hipótesis original de este hallazgo era
> que podía haber filas *almacenadas* en formato día-primero, lo que haría lexicográficamente
> incorrectas las comparaciones de rango. **Es falsa y la retiro.** La inspección del catálogo
> muestra que las siete tablas operativas (`printer_print_job`, `printer_print_job_event`,
> `printer_printer`, `printer_routing_rule`, `printer_source_print_job`, `printer_store`,
> `printer_dashboard_threshold`) usan `TIMESTAMP(7)`. HANA convierte el literal del converter a
> TIMESTAMP, así que **todas las comparaciones de la tabla de abajo son temporales, no textuales**.
> El formato `"17/6/2026 6:58:40"` que documenta el `DbContext` es un artefacto de *lectura* — el
> driver formateando un TIMESTAMP con la cultura del proceso — no de almacenamiento. El riesgo de
> reproceso en bucle que describía este hallazgo no existe.
>
> Quedan cuatro columnas legacy en `NVARCHAR(26)`: `printer_alert_state.notified_at_utc` y
> `.checked_at_utc`, `printer_telegram_chat.created_at_utc`, `printer_telegram_config.updated_at_utc`.
> **Ninguna participa en un filtro de rango ni en una ordenación** — verificado sobre el código: solo
> se escriben y se leen por clave primaria (`StoreHealthAlertBackgroundService.cs:196-242`). Su
> riesgo funcional es cero, así que las consultas de validación de formato sobre ellas son
> opcionales.
>
> Lo que sí queda, y es real: el converter global escribe `yyyy-MM-dd HH:mm:ss` en columnas
> `TIMESTAMP(7)`, de modo que **toda escritura de la aplicación pierde la precisión subsegundo**
> (confirmado: fracciones `.000` sistemáticas en `created_at_utc` y `occurred_at_utc`). El único
> punto donde eso tiene consecuencia práctica es la **ordenación de `PrintJobEvent` dentro del mismo
> segundo**, que queda indeterminada por tiempo; el resto de la lógica temporal opera en ventanas de
> segundos o minutos (backoff, lease de 30 s, stale de 40 s, ventanas de dashboard) y no lo nota.
>
> **Acción propuesta (P2, no urgente):** migrar esas cuatro columnas a `TIMESTAMP` y después
> restringir el converter a las propiedades que aún lo necesiten, o eliminarlo. Mientras tanto,
> ordenar los eventos por `EventId` y no por `OccurredAtUtc` donde el orden importe.

---

<details>
<summary>Redacción original del hallazgo (refutada, se conserva como registro)</summary>

#### Fechas persistidas como cadena: toda comparación temporal es sospechosa — ~~DEMOSTRADO~~

`ImpresorasDbContext.cs:493-506` aplica `DateTimeOffsetToStringConverter` a **toda** propiedad `DateTimeOffset` de **todas** las entidades. El formato de escritura es `"yyyy-MM-dd HH:mm:ss"` (L46).

Toda la lógica temporal del Worker se traduce entonces a una comparación sobre esa columna:

| Ubicación | Predicado |
|---|---|
| `PrintExecutionService.cs:51-53` | `NextRetryAtUtc <= now`, `UpdatedAtUtc <= now - stale` |
| `PrintExecutionService.cs:54` | `OrderBy(NextRetryAtUtc ?? CreatedAtUtc)` |
| `SpoolAcceptedWatchdog.cs:88,89` | `UpdatedAtUtc <= thresholdUtc`, `OrderBy(UpdatedAtUtc)` |
| `SapHanaJobSourceAdapter.cs:69,70` | `ClaimedUntilUtc <= now`, `OrderBy(CreatedAtUtc)` |
| `WorkerLockCoordinator.cs:48` | `HeartbeatAtUtc <= staleThreshold` |

En formato ISO el orden lexicográfico coincide con el cronológico, así que **si todas las filas están en ISO, esto funciona**. El problema es que el propio código documenta que no lo están: el comentario de `SupportedDateFormats` (L18-27) describe filas legacy en formato día-primero (`"17/6/2026 6:58:40"`), y el fallback de parseo existe precisamente para leerlas.

Una fila con `updated_at_utc = "17/6/2026 6:58:40"` comparada lexicográficamente contra `"2026-08-12 09:00:00"`: `'1' < '2'`, luego siempre es "más antigua". El watchdog la reprocesaría en cada ciclo; el rescate de `Printing` la reenviaría al spooler indefinidamente.

Es exactamente la misma clase de bug que ya mordió a este proyecto en el dashboard (el comentario de L86-89 narra el episodio del día ≤ 12), pero esta vez del lado del `WHERE`, no del parseo.

**Verificación pendiente (STAGING, 5 minutos):**
```sql
SELECT COUNT(*) FROM printer_print_job WHERE updated_at_utc LIKE '%/%';
SELECT data_type_name FROM sys.table_columns
 WHERE table_name = 'PRINTER_PRINT_JOB' AND column_name LIKE '%_UTC';
```
Si la primera devuelve > 0, hay que normalizar con un script de un solo uso. Si el tipo es `TIMESTAMP`, HANA convierte y el riesgo cae mucho — pero entonces `H-06` sigue siendo necesario para saberlo sin preguntar.

**Resultado de esa verificación: el tipo es `TIMESTAMP(7)` en las siete tablas operativas.** Ver la
corrección al principio de esta sección.

</details>

---

### H-08 · Con `NotifyOnRecovery = false`, una tienda deja de alertar para siempre — **DEMOSTRADO**

`StoreHealthAlertBackgroundService.cs:207-238`. `NotifiedHealth` **solo** se actualiza dentro de `if (message is not null)`.

Con `NotifyOnRecovery = false` (L221 exige `notifyOnRecovery` para construir el mensaje de recuperación), la traza es:

1. Tienda 7 → `critical`. Se envía la alerta. `NotifiedHealth = "critical"`.
2. Tienda 7 se recupera → `healthy`. `isAlertLevel=false`, `wasAlertLevel=true`, pero `notifyOnRecovery=false` → `message = null` → **`NotifiedHealth` sigue siendo `"critical"`**.
3. Tienda 7 vuelve a caer → `critical`. `wasAlertLevel = true`, no es escalada → `message = null`. **Silencio.**

La tienda queda permanentemente muda. `NotifyOnRecovery` es una opción de UI (`TelegramConfig`), así que un operador puede desactivar todas las alertas del sistema creyendo que solo apaga los mensajes verdes.

**Corrección (1 línea):** mover `alertState.NotifiedHealth = health;` fuera del `if`, junto a `LastHealth` (L231). El estado notificado debe seguir a la realidad aunque no se emita mensaje.
**Aceptación:** con `NotifyOnRecovery=false`, un ciclo critical → healthy → critical debe emitir dos alertas.

---

### H-09 · `PrintedUnknown` y `PrinterBlocked` no tienen ninguna salida operativa — **DEMOSTRADO**
*(cubre P1-12)*

La API expone exactamente dos acciones manuales sobre un job (`PrintJobsController.cs`): `route` (L113) y `cancel` (L152).

- `cancel` admite `Pending, Routed, RetryScheduled, ErrorFinal` (L167-173).
- `route` admite `Pending` o `ErrorFinal` (docstring L110).
- El "Reintentar" del frontend es un `POST .../route` (`ColaController.php:231`).

Ni `PrintedUnknown` ni `PrinterBlocked` están en ninguna de las dos listas. Un trabajo que llega ahí **no se puede cancelar, ni reintentar, ni confirmar, ni archivar** desde ninguna interfaz. La única vía es un `UPDATE` manual en HANA — exactamente lo que el documento de handoff pide evitar.

Y son estados a los que el sistema envía trabajos de forma rutinaria: `ApplyOutcome` (L280-291) manda a `PrintedUnknown` **cualquier** job cuya impresora no responda IPP en 120 s. Con `IppConfirmationEnabled=false` o una flota sin IPP, *todos* los trabajos acaban ahí.

**Mínimo:** añadir `PrintedUnknown` y `PrinterBlocked` a `cancellableStates`, y un `POST /{id}/confirm` para Admin que registre `ActorType="user"` y el riesgo asumido. Dos cambios pequeños que devuelven el control a operaciones.
**Aceptación:** un `PrintedUnknown` debe poder resolverse desde la UI sin tocar la base de datos.

> **Revisado el 19/08/2026.** La parte de cancelar/reimprimir sigue en pie y cumple la aceptación.
> La de confirmar manualmente se revirtió por decisión de producto: cerrar un trabajo como impreso
> por afirmación del operador dejaba en el historial una impresión confirmada que el sistema nunca
> comprobó. Retirados el botón, la ruta de la web y el `POST /api/printjobs/{id}/confirm`. Un
> `PrintedUnknown` se resuelve reimprimiendo o cancelando; no volver a añadir la confirmación manual
> sin acordarlo antes.

---

### H-10 · El lock global tiene dos ventanas de doble holder — **DEMOSTRADO** / magnitud **STAGING**

El CAS de `WorkerLockCoordinator.cs:47-53` es correcto y bien construido. Las ventanas están alrededor:

**(a) Deriva de reloj.** `now` (L25) es el reloj **local de cada instancia**, y `staleThreshold = now - LeaseSeconds` se compara contra un `heartbeat_utc` escrito por otra máquina. Con `LeaseSeconds = 30`, una instancia con el reloj 40 s adelantado considera expirado un lease vivo y se lo lleva. Dos holders, ambos convencidos.
Corrección: derivar el umbral del reloj de HANA (`CURRENT_UTCTIMESTAMP`), no del proceso.

**(b) Suspensión del proceso.** `WorkerLockState.IsHolder` solo se actualiza cuando el bucle de `WorkerLockBackgroundService` (L69) completa una vuelta. Una pausa GC larga, una suspensión de VM o un `Task.Delay` retrasado dejan `IsHolder = true` mientras otra instancia ya adquirió el lease. Los consumidores (`PrintExecutionBackgroundService.cs:33`, watchdog L46, alertas L57) leen ese booleano cacheado y **envían al spooler**.
Corrección: guardar el instante de la última renovación exitosa y que `IsHolder` devuelva false si ha pasado más de `LeaseSeconds` — que la caducidad sea del dato, no del bucle que lo refresca.

**Aceptación:** dos Workers reales contra el mismo HANA, suspendiendo el holder con `Ctrl+Break`, no deben solapar envíos.

> **(a) resuelto el 19/08/2026.** `WorkerLockCoordinator.GetSharedNowAsync` lee
> `CURRENT_UTCTIMESTAMP FROM DUMMY` cuando el proveedor es HANA, así que el umbral del lease sale
> del reloj donde vive el dato y la deriva entre servidores deja de importar. Si la consulta falla
> se cae al reloj local con warning: quedarse sin lock por no poder leer la hora dejaría al Worker
> inerte, que es peor que el riesgo que se está evitando. En SQLite se sigue usando el
> `TimeProvider` inyectado — es lo que permite simular la expiración del lease en los tests.
> **Pendiente de verificar contra HANA:** no hay entorno en esta pasada.
>
> **(b) resuelto** antes, con la caducidad en `WorkerLockState.IsHolder`.

---

### H-11 · CI nunca ejercita la ruta de producción — **DEMOSTRADO**

`.github/workflows/impresoras-service-ci.yml:20` → `runs-on: ubuntu-latest`. Con eso, `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` es false y **todo el CI corre con `NoOpPrintSpooler`** (H-02). `WindowsPrintSpooler` no se ejecuta en ninguna prueba.

Los tests son SQLite (`SqliteTestDbHelper.cs`), un solo proyecto, 8 ficheros de prueba. No cubren el watchdog, ni IPP, ni conectividad, ni las alertas.

Los tres componentes de los que depende la corrección física del producto — provider HANA, spooler de Windows, IPP — tienen **cobertura cero**. "Tests verdes" no dice nada sobre producción; la hipótesis del documento se confirma en su forma más fuerte.

> **Mitigado a medias el 19/08/2026.** El job .NET del CI pasa a `windows-latest`, así que
> `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` ya es true y deja de ejercitarse solo la
> rama `NoOpPrintSpooler` de `AddInfrastructure`. **El hallazgo sigue abierto:** no hay ni una
> prueba de `WindowsPrintSpooler`, de IPP ni contra HANA; lo único que cambia es qué rama de la
> selección de dependencias se recorre.

---

### H-12 · `RoutingResolver` se salta `TimeProvider` — **DEMOSTRADO** (menor)

`RoutingResolver.cs:42`: `var now = DateTimeOffset.UtcNow;`. Es el único servicio del flujo que no inyecta `TimeProvider`, y se usa para filtrar `ValidFromUtc` / `ValidToUtc`. Hace no determinista cualquier test de reglas con vigencia. Un parámetro en el constructor.

---

### H-13 · Contadores del watchdog mal calculados — **DEMOSTRADO** (menor, pero contamina la operación)

`SpoolAcceptedWatchdogBackgroundService.cs:143-146` cuenta sobre `candidates` completo, incluyendo los que `ApplyOutcome` devolvió `false` y **no se modificaron**. `recovered` (L145) cuenta todos los que siguen en `SpoolAccepted`, que son mayoritariamente los ignorados, no los recuperados.

Además se emite como `LogWarning` (L148) en cada ciclo con candidatos, es decir, cada 10 segundos en operación normal. El resultado es un log de warnings continuo con números que no significan lo que dicen — el ruido que hace que nadie mire los warnings de verdad.

---

### H-15 · Ciclo de vida del PDF: cada documento se guarda dos veces y no se borra nunca — **DEMOSTRADO Y MEDIDO**
*(cubre P1-06)*

**Medido contra `ZTEST_VICENTE_2` el 12/08/2026** (entorno de pruebas; las cifras absolutas no
extrapolan a producción, la estructura sí):

| | Filas | Conservan PDF | Tamaño |
|---|---|---|---|
| `printer_source_print_job` | 245 | **245 (100 %)**, todas ya procesadas | 0,67 MB |
| `printer_print_job` | 72 | **72 (100 %)**, todas en estado terminal | 0,27 MB |
| `printer_print_job_event` | 315 | — | 4,4 eventos por trabajo |

**No existe ninguna política de retención.** `grep` sobre todo `src/`: `PdfBlob` no se asigna a
`null` en ningún punto del código, y el único borrado de trabajos del sistema es
`StoresController` cuando se elimina una tienda con `purgeHistory` — una acción administrativa
puntual, no una política. Un documento entra por la ingesta y se queda en las dos tablas para
siempre.

**Hay media solución escrita y abandonada.** `scripts/sql/migrate_pdf_blob_nullable.sql` dice
literalmente: *"M-2: pdf_blob se limpia al pasar a SpoolAccepted para liberar espacio en BD"*. Pero
(a) el script **no está aplicado** — en el esquema real `pdf_blob` sigue siendo `BLOB NOT NULL`, así
que ni siquiera se podría poner a null — y (b) **el código que haría esa limpieza no existe**. Queda
una migración preparada para una funcionalidad que nunca se implementó.

**Y esa política, tal como está descrita, ya no sirve.** Limpiar el blob en `SpoolAccepted` rompería
la reimpresión manual desde `PrintedUnknown` habilitada en H-04/H-09: el flujo pasa justamente por
`SpoolAccepted`, y `PrintExecutionService.cs:285` devolvería `PDF_MISSING`. Si se retoma, el corte
debe ser **estado terminal + ventana de retención**, no `SpoolAccepted`.

**Lo que sí está bien resuelto** (y conviene no romperlo):

- **El PDF no se expone por ninguna API.** Ni `PrintJobsController.GetQueue` ni
  `SourcePrintJobsController.GetPending` proyectan `PdfBlob`, y no hay un solo `return File(` ni
  `application/pdf` en toda la API. Los blobs no salen por HTTP.
- **No comen memoria de HANA.** El catálogo muestra `MEMORY_THRESHOLD = 1000` en las columnas LOB:
  los blobs por encima de 1 KB se almacenan en disco, no en la memoria de la column store. El coste
  es de disco y de backup, no de RAM.
- **Los temporales del spooler se limpian.** `WindowsPrintSpooler.cs:70-77` borra el fichero salvo
  con `KeepTempFileOnFailure` (hoy `false`). Sin huérfanos `impresoras-*.pdf` en el TEMP revisado.
  Quedan solo los de un proceso muerto por `kill`, sin barrido posterior que los recoja.

**Dimensionar antes de decidir.** El tamaño medio medido (3,9 KB) corresponde a PDFs de prueba; una
factura real está en decenas o cientos de KB. La fórmula es:

```
crecimiento diario ≈ documentos/día × tamaño medio × 2   (source + print_job)
```

Con 10.000 documentos/día a 100 KB serían ~2 GB/día, unos 730 GB/año que nadie borra. Con 500
documentos/día a 50 KB son 18 GB/año. El orden de magnitud cambia la urgencia por completo, y hoy
nadie tiene ese dato.

**Privacidad.** Son facturas y documentos de cliente conservados sin plazo definido. Esto excede lo
técnico: la limitación del plazo de conservación es una decisión que corresponde a quien lleve
protección de datos en la organización, no al equipo de desarrollo. Lo que sí es técnico es que hoy
**no existe la capacidad de borrar**, así que cualquier plazo que se fije no podría cumplirse sin el
trabajo de abajo.

**Propuesta mínima:** aplicar `migrate_pdf_blob_nullable.sql`, y un barrido periódico que ponga
`pdf_blob = NULL` en trabajos en estado terminal con más de N días, conservando `pdf_sha256` y todos
los metadatos — la trazabilidad y los KPI no dependen del blob. Hacer lo propio con
`printer_source_print_job` para las filas ya procesadas, que es donde más volumen hay. N debe ser
mayor que la ventana en la que un operador aún puede querer reimprimir.
**Aceptación:** un trabajo terminal de hace N+1 días conserva su fila y su hash, y no su PDF.

> **Resuelto en código el 19/08/2026, pendiente de DDL.** `PdfRetentionBackgroundService` barre
> las dos tablas: `printer_print_job` por estado terminal + ventana (12/08) y
> `printer_source_print_job` por `is_processed` + la misma ventana (19/08), que es donde estaba el
> grueso del volumen medido aquí. Una fila de origen **sin procesar** no se toca nunca: su PDF es
> el único ejemplar que existe todavía, y hay un test que lo fija.
>
> Sigue **apagado por defecto** (`PdfRetention:Enabled=false`) y **no puede funcionar** hasta que
> se aplique `scripts/sql/migrate_pdf_blob_nullable.sql`, que ahora lleva los dos `ALTER`: en
> ambas tablas `pdf_blob` es `BLOB NOT NULL`. El plazo de conservación sigue siendo la decisión
> de protección de datos que este hallazgo señalaba.

---

## 4. Hipótesis del documento: veredicto

| Hipótesis (§8 del handoff) | Veredicto |
|---|---|
| "Lock único ⇒ no hay duplicados" | **Refutada.** H-04 (crash window) y H-10 (doble holder). |
| "Exit code 0 ⇒ salió el papel" | **Confirmada como riesgo.** `WindowsPrintSpooler.cs:145` solo sabe que Sumatra terminó bien. |
| "IPP idle confirma este job" | **Refutada, y es peor de lo descrito:** el resultado se aplica a *todo el lote* de esa impresora (H-01). |
| "RowVersion da concurrencia optimista" | **Refutada.** H-03, confirmado en el mapeo y en el comentario del propio código. |
| "Puerto abierto = impresora operativa" | **Confirmada como riesgo.** El guard-rail de `PrintExecutionService.cs:171` usa `LastConnectionOk` de un sondeo TCP. |
| "Tests verdes = producción cubierta" | **Refutada.** H-11. |
| "NoOp es inocuo" | **Refutada.** H-02, agravado por H-01. |
| "Doc del repo = estado actual" | **Confirmada.** El CLAUDE.md dice "rama activa `IU`" y "EF Migrations como referencia histórica" cuando no queda ninguna (H-06). |
| "`[]` en Laravel = cero resultados" | **Parcialmente refutada.** `ApiClient::get()` **sí** distingue: marca `SESSION_API_ERROR_KEY` (L91) y la UI puede leerlo. `getQuiet()` (L101-125) **no**: solo escribe un log y devuelve `[]` indistinguible. La hipótesis vale solo para `getQuiet`. |
| "El estado actual basta para KPIs históricos" | No verificado en esta pasada. Queda abierto. |

---

## 5. Orden de trabajo propuesto

Ordenado por (riesgo evitado ÷ tamaño del diff), no por número de prioridad.

| # | Acción | Diff | Hallazgo |
|---|---|---|---|
| 1 | `NotifiedHealth` fuera del `if` | 1 línea | H-08 |
| 2 | Fail-fast si Production + spooler simulado | ~10 líneas | H-02 |
| 3 | Ack solo de los ids realmente persistidos | ~15 líneas | H-05 |
| 4 | `PrintedUnknown`/`PrinterBlocked` cancelables + `POST /confirm` | ~40 líneas | H-09 |
| 5 | Extraer el DDL real de producción a `scripts/sql/schema/` | 1 día, sin código | H-06 |
| ~~6~~ | ~~Consulta de diagnóstico de fechas legacy en HANA~~ — **hecho 12/08: sin incidencia, H-07 baja a P2** | — | H-07 |
| 7 | Caducidad local de `IsHolder` + reloj de HANA en el lease | ~20 líneas | H-10 |
| 8 | Cambios de estado críticos vía `ExecuteUpdateAsync` condicional | ~60 líneas | H-03 |
| 9 | Renombrar la semántica de `PrintedConfirmed` | rename + UI | H-01 |
| 10 | `Printing` stale → estado de excepción, no reenvío automático | ~30 líneas | H-04 |

Los puntos 1-4 son correcciones locales de defectos concretos. Los 5-7 son diagnóstico que desbloquea decisiones. Los 8-10 tocan el contrato del producto y necesitan una decisión de negocio previa sobre qué garantía se quiere ofrecer.

**Ninguno de ellos justifica una reescritura.** El problema de este sistema no es su arquitectura, es que tres de sus etiquetas afirman más de lo que su evidencia sostiene.
