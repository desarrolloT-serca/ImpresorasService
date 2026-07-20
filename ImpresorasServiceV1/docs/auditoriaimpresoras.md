# Auditoría forense — ImpresorasService

## 1. Commit exacto auditado

- **Rama:** `origin/develop`
- **Commit:** `34ff3c74df76df4bf2bba02a2cd705ca663f6326` — *Merge pull request #4 from desarrolloT-serca/IU* (2026-07-15T09:49:43+02:00)
- El SHA histórico indicado en el encargo (`619d40e…`) es el padre inmediato; está contenido tanto en `develop` como en `main`. `develop` avanzó por encima de él con los merges #3 y #4.
- `main` (`d99aa45`) y `develop` (`34ff3c7`) comparten todo el árbol de código funcional auditado; los hallazgos aplican a ambas.
- Auditoría **estática + dinámica local**. No se ha tocado producción, ni impresoras, ni HANA, ni Telegram reales.

## 2. Estado de compilación y pruebas

| Acción | Resultado |
|---|---|
| `dotnet build -c Release` | ✅ 0 errores, 2 warnings (CS8604 en `PrintersController` 172/175, `ExecuteSqlRawAsync` con posible `null`) |
| `dotnet test` (IntegrationTests) | ✅ **110/110** correctas (11,4 s) |
| `php artisan test` | ✅ 7/7 (19 asserts) |
| `composer audit` | ⚠️ **symfony CVE-2026-45133** (YAML) en versión instalada |

> **Advertencia metodológica:** los 110 tests corren sobre **SQLite en memoria**. SQLite no reproduce el aislamiento transaccional de HANA, ni el comportamiento de BLOB, ni las carreras entre procesos, ni el almacenamiento de fechas como string. Los tests verdes **no** cubren los hallazgos P1 de concurrencia y doble impresión de este informe (ver §Informe de pruebas).

## 3. Mapa de arquitectura (real, extraído del código)

```
SAP HANA  (printer_source_print_job)
   │  FetchPendingJobsAsync: SELECT candidatos → claim (ClaimedBy/Until/Token) → commit
   ▼
IngestionService.IngestBatchAsync           [Worker: IngestionBackgroundService, poll 2s]
   ├── ExistsBySourceExternalId (check-then-insert, NO atómico)
   ├── AddAsync(PrintJob{Pending}) + evento INGESTED
   ├── SaveChanges (lote entero, all-or-nothing)   ← ⚠ window A
   ├── MarkJobsProcessed (ACK: WHERE ClaimedBy==self)  ← ⚠ window B
   └── por cada job: RoutingService.TryRouteJob → Routed | ErrorFinal(ROUTE_NOT_FOUND)
          │
          ▼
PrintExecutionService.ExecuteBatchAsync     [Worker: PrintExecutionBackgroundService, poll 5s, lote 10]
   ├── barrido AsNoTracking (snapshot RowVersion)
   ├── TryProcessOneAsync (tx1): check RowVersion en memoria → Printing → SaveChanges → commit  ← ⚠ window C
   ├── SendToPrinterAsync (Sumatra → spooler Windows)   ← EFECTO EXTERNO NO TRANSACCIONAL ⚠ window D
   └── (tx2): SpoolAccepted | RetryScheduled | ErrorFinal  ← ⚠ window E
          │
          ▼
Windows Spooler → Impresora física
          │
          ▼
SpoolAcceptedWatchdog  [poll 10s]  SpoolAccepted>120s → IppConfirmationService.QueryPrinterState
   ├── PrinterIdle       → PrintedConfirmed   ← ⚠ estado de IMPRESORA, no del trabajo
   ├── PrinterProcessing → esperar (≤30 min)
   ├── PrinterStopped    → PrinterBlocked
   └── Unavailable/timeout → PrintedUnknown

PrinterConnectivityMonitor [30s]  puertos 515/9100/631, TCP, actualiza LastConnectionOk/IppSupported
StoreHealthAlert [5min]  StoreHealthEvaluator → Telegram (envío ANTES de persistir StoreAlertState) ⚠
```

**Ventanas donde una caída/carrera produce daño:**

| Ventana | Riesgo |
|---|---|
| A (crash tras SaveChanges, antes de ACK) | fila origen reingerida → dedup por unique index (OK) |
| B (ACK falla / lease perdido) | reingesta; PrintJob ya existe → duplicado descartado (OK) |
| C (dos workers pasan el check RowVersion) | **doble Printing → doble envío → DOBLE IMPRESIÓN** (P1-001) |
| D (crash tras enviar al spooler, antes de tx2) | job queda en Printing; a los 40 s se reenvía → **DOBLE IMPRESIÓN** (P1-002) |
| E (tx2 falla) | reintento; si D ya imprimió, duplicado |

## 4. Inventario de hallazgos

---

### [PRINT-P1-001] `RowVersion` no es token de concurrencia EF: doble impresión con 2 Workers

**Severidad:** P1 · **Confianza:** Confirmado · **Categoría:** Concurrencia
**Archivos:** `ImpresorasDbContext.cs:158,168-170`; `PrintExecutionService.cs:136-256,430-438`
**Evidencia:** El mapeo de `RowVersion` sólo hace `HasColumnName("row_version")`. El comentario 168-170 es explícito: *"Mantenemos el valor, pero sin token de concurrencia a nivel EF"*. No hay `IsConcurrencyToken()`/`IsRowVersion()` en el modelo runtime. En cambio, `ImpresorasDbContextModelSnapshot.cs:173` y la migración baseline **sí** llevan `.IsConcurrencyToken()` → el modelo runtime **divergió** de la migración.

**Comportamiento esperado:** El `UPDATE` que pasa un job a `Printing` debe llevar `WHERE row_version = @anterior`; si otro worker ya lo cambió, `SaveChanges` afecta 0 filas y se aborta.

**Comportamiento real:** `SaveChangesAsync` genera `UPDATE … WHERE job_id = @id` (sin cláusula de versión). `BumpPrintJobRowVersionsForConcurrency()` reescribe el valor en cada guardado, pero **nadie lo usa en un WHERE**. La única defensa es `RowVersionSnapshotStillMatches`, un compare-and-set **no atómico**: lectura → comparación en memoria → escritura, sin `FOR UPDATE` ni lock. En READ COMMITTED (HANA) dos transacciones de dos procesos leen la misma versión, ambas validan, ambas escriben `Printing`, ambas hacen commit, ambas envían al spooler.

**Secuencia:**
```
Worker A lee job v=X (Routed)   Worker B lee job v=X (Routed)
A: SnapshotMatches(X,X)=true    B: SnapshotMatches(X,X)=true
A: Status=Printing, SaveChanges (WHERE job_id) → 1 fila, commit
B: Status=Printing, SaveChanges (WHERE job_id) → 1 fila, commit
A: SendToPrinter → imprime      B: SendToPrinter → imprime   ← DOBLE
```
`var rows = SaveChangesAsync(); if (rows==0) return false;` nunca protege porque sin token siempre devuelve ≥1.

**Impacto:** Cualquier documento, sistemáticamente, si arrancan 2 instancias del Worker (despliegue accidental, servicio duplicado, actualización solapada). La impresión física no admite rollback.
**Frecuencia:** Constante con ≥2 workers; nula con 1 worker perfectamente único.
**Detectabilidad:** Nula hoy — no hay campo "worker que procesó", ni idempotency key, ni alerta de doble instancia.

**Corrección mínima:** Reemplazar el paso a `Printing` por un `UPDATE` condicional atómico:
`UPDATE printer_print_job SET status='Printing', attempt_count=attempt_count+1, updated_at=@now WHERE job_id=@id AND status IN ('Routed','RetryScheduled')` vía `ExecuteSqlRaw`, y proceder al spooler **solo si afectó 1 fila**. Esto da claim atómico sin depender del BLOB.
**Corrección robusta:** Columna de lease de ejecución (`exec_claimed_by`, `exec_claimed_until`) reclamada con el mismo UPDATE condicional; single-consumer por impresora; o token de concurrencia comparable (INT/BIGINT en vez de BLOB) que HANA sí puede comparar en WHERE.
**Test de regresión:** dos `DbContext` concurrentes sobre el mismo job compitiendo por pasarlo a Printing; sólo uno debe conseguir enviar. (No reproducible en SQLite en memoria compartida; usar dos conexiones a archivo SQLite con `busy_timeout` o, idealmente, HANA de staging.)
**Riesgo residual:** El UPDATE condicional cierra la ventana C, pero **no** la ventana D (P1-002).

---

### [PRINT-P1-002] Ventana no transaccional del spooler: doble impresión en crash/reinicio

**Severidad:** P1 · **Confianza:** Confirmado · **Categoría:** Concurrencia/Datos
**Archivos:** `PrintExecutionService.cs:237-333`; recuperación de stale en `44-53,214-220`
**Evidencia:** El envío físico ocurre **entre** dos transacciones: tx1 persiste `Printing` y hace commit (253-256); luego `SendToPrinterAsync` (264); luego tx2 persiste `SpoolAccepted` (278-332). Si el proceso cae después de enviar y antes de tx2, el job queda en `Printing`. El barrido considera recuperable todo `Printing` con `UpdatedAtUtc <= now - (TimeoutSeconds+10)` = 40 s (44-53, 214-220) y **lo reenvía al spooler**.

**Comportamiento real:** Un documento que **sí se imprimió** pero cuya confirmación se perdió (crash, kill del servicio durante despliegue, timeout que en realidad imprimió) se reimprime en el siguiente ciclo. Windows Spooler no recibe ninguna idempotency key, de modo que acepta el reenvío como trabajo nuevo. `NET_TIMEOUT` se marca `IsTransient=true` (268) → consume intento **y** reimprime.

**Secuencia:**
```
tx1: Printing (commit)
SendToPrinter → Sumatra encola → impresora imprime página
--- CRASH / kill / timeout ---
+40s: barrido ve Printing stale → reenvía → SEGUNDA IMPRESIÓN
```
**Impacto:** Duplicado por cada job en vuelo en el momento de un crash/despliegue. Con 1 worker es el modo de doble impresión más probable en operación normal (los reinicios ocurren).
**Frecuencia:** Ocasional (ligado a reinicios/timeouts), pero garantizado en cada despliegue con trabajos en vuelo.
**Detectabilidad:** Baja. `AttemptCount` sube, pero no distingue "reintento tras fallo real" de "reintento tras impresión ya realizada".

**Corrección mínima:** No tratar `NET_TIMEOUT` como transitorio reintenable automático; enviar los `Printing` stale a `PrintedUnknown` (revisión manual) en vez de reimprimir a ciegas.
**Corrección robusta:** Idempotencia end-to-end: registrar el `JobId` del spooler de Windows y, antes de reenviar un `Printing` stale, consultar el spooler/IPP por ese trabajo concreto; sólo reenviar si se confirma que nunca entró. Definir política de negocio explícita (§Preguntas de negocio: ¿duplicado o pérdida?).
**Test de regresión:** simular crash entre `SendToPrinterAsync` y tx2 (spooler fake que registra nº de envíos) y verificar que la recuperación no incrementa el contador de impresiones físicas.
**Riesgo residual:** Sin idempotency key reconocida por el spooler, el *exactly-once físico* es inalcanzable; queda elegir entre riesgo de duplicado o de pérdida.

---

### [PRINT-P1-003] IPP confirma estado de la impresora, no del trabajo → `PrintedConfirmed` falso

**Severidad:** P1 · **Confianza:** Confirmado · **Categoría:** Datos/UX operativa
**Archivos:** `IppConfirmationService.cs:33-118` (petición `Get-Printer-Attributes`, sólo `printer-state`); `SpoolAcceptedWatchdogBackgroundService.cs:217-226`
**Evidencia:** La consulta IPP pide `printer-state` a nivel de **impresora** (`ipp://host:631/ipp/printer`), no atributos de un `job-id`. El watchdog mapea `PrinterIdle → PrintedConfirmed` (219-226). No hay correlación con el trabajo concreto: no se guarda ni consulta `job-id`, ni contador de páginas, ni historial.

**Comportamiento real:** `PrinterIdle` sólo significa "la impresora no está procesando **nada ahora**". Se marca `PrintedConfirmed` cuando:
- La impresora nunca recibió el trabajo (spooler aceptó, dispositivo estaba apagado y volvió a Idle).
- Terminó **otro** documento distinto.
- Varios jobs de la app comparten impresora: **una** lectura Idle confirma **todos** a la vez.
- El documento fue descartado/cancelado en el propio dispositivo.

`PrintedConfirmed` promete una certeza que el protocolo, tal como se usa, no da. `PrintedUnknown` (default/timeout) puede a su vez ocultar impresiones realmente realizadas.

**Impacto:** Estado falso que induce decisiones operativas (no reimprimir algo que no salió; dar por bueno lo que no se imprimió). Afecta a la métrica central del sistema.
**Frecuencia:** Frecuente en tiendas con varias colas por impresora o con impresoras que entran/salen de suspensión.
**Corrección mínima:** Renombrar el estado a algo honesto (p.ej. `PrinterIdleAfterSpool`) y en la UI no afirmar "impreso confirmado" sin evidencia de trabajo.
**Corrección robusta:** Consultar `Get-Job-Attributes` con el `job-id` devuelto por el spooler y/o `job-media-sheets-completed`; correlacionar por trabajo, no por impresora. Definir con negocio qué evidencia habilita "PrintedConfirmed".
**Test de regresión:** watchdog con impresora Idle y trabajo que nunca entró → no debe pasar a PrintedConfirmed.

---

### [PRINT-P1-004] Token de bot de Telegram expuesto en el historial de Git

**Severidad:** P1 · **Confianza:** Confirmado · **Categoría:** Seguridad
**Evidencia:** `git log -S` localiza `BotToken: "8401567379:AAFa2mpwCzy-o8Hzl4fxyS3mb8dciOcbRNY"` introducido en `f22e304` y "eliminado" en `d33029d`. Sigue **recuperable en el historial**. El estado actual del árbol ya no lo contiene (correcto), pero el secreto quedó comprometido.
**Impacto:** Cualquiera con acceso al repo (o a un fork/clon) puede controlar el bot: leer chats, enviar mensajes suplantando alertas.
**Corrección mínima:** **Revocar/regenerar el token** en @BotFather de inmediato (la rotación es obligatoria; borrarlo del código no basta).
**Corrección robusta:** Token sólo por variable de entorno (`Telegram__BotToken`), nunca en `appsettings*`; escaneo de secretos (gitleaks/git-secrets) en CI; considerar reescritura de historial si el repo se hará público.
**Riesgo residual:** Si el repo ya fue clonado por terceros, el token viejo debe considerarse quemado para siempre.

---

### [PRINT-P2-005] Ingesta HANA: claim sin bloqueo real y lote all-or-nothing

**Severidad:** P2 · **Confianza:** Confirmado (lógica) · **Categoría:** Concurrencia/Datos
**Archivos:** `SapHanaJobSourceAdapter.cs:50-110`; `IngestionService.cs:40-98`
El claim es check-then-update sin lock (ni `SourcePrintJobRecord` tiene token de concurrencia): dos workers pueden reclamar las mismas filas (last-write-wins en las columnas de claim) y ambos devolverlas. La deduplicación posterior depende del índice único `(SourceSystem, ExternalJobId)`, pero `IngestionService` inserta **todo el lote en un único `SaveChanges`** (98): si **un** job viola el índice, **el lote entero** aborta y no se persiste ninguno (además el `MarkJobsProcessed` posterior no se ejecuta por la excepción). Semántica real: **at-least-once con dedup**, no exactly-once. `ClaimToken` se genera (45,89) pero **nunca se exige** en ACK ni en renovación (sólo se filtra por `ClaimedBy`), es una salvaguarda muerta.
**Corrección:** insertar por job (o `try/catch` por elemento) para que un duplicado no tire el lote; exigir `ClaimToken` en ACK/renovación; o claim atómico con UPDATE condicional.

---

### [PRINT-P2-006] Watchdog: `Take(windowLimit)` sin orden determinista → starvation

**Severidad:** P2 · **Confianza:** Confirmado · **Categoría:** Rendimiento/Datos
**Archivos:** `SpoolAcceptedWatchdogBackgroundService.cs:67-90`
`windowLimit = max(200, batchSize*50)` (≈2500). Se hace `.Take(windowLimit)` **sin `OrderBy`** y luego se filtra/ordena por `UpdatedAtUtc` **en memoria**. Con backlog sostenido > windowLimit de trabajos en `SpoolAccepted`/`PrinterBlocked`, el subconjunto que EF devuelve es no determinista (típicamente orden de PK); trabajos fuera de la ventana pueden no confirmarse **nunca**. Mismo patrón en la ingesta (`Take(batchSize*5)` antes de filtrar claims: si las primeras filas están reclamadas, se pierde el ciclo aunque haya libres después).
**Corrección:** `OrderBy(UpdatedAtUtc)` **antes** de `Take`, empujado a SQL.

---

### [PRINT-P2-007] Ingesta carga PDFs completos en memoria dos veces sólo para elegir IDs

**Severidad:** P2 · **Confianza:** Confirmado · **Categoría:** Rendimiento/Memoria
**Archivos:** `SapHanaJobSourceAdapter.cs:53-77`
`preCandidates` materializa `Take(batchSize*5)` **entidades completas** de `SourcePrintJobs`, incluido `PdfBlob`, sólo para calcular `candidateIds`. Luego `claimedRows` vuelve a cargarlas. Con `batchSize=20` son 100 PDFs en RAM (x2). Un PDF grande o un pico de cola dispara el consumo. Proyectar sólo columnas escalares para la selección de candidatos y cargar el blob únicamente de las filas ya reclamadas.

---

### [PRINT-P2-008] Cancelación de usuario vs Worker sin guardia de concurrencia

**Severidad:** P2 · **Confianza:** Confirmado (ventana estrecha) · **Categoría:** Concurrencia/UX
**Archivos:** `PrintJobsController.cs:150-200`; `PrintExecutionService.cs:237-256`
`Cancel` lee el job (tracked), lo pasa a `Cancelled` y `SaveChanges` sin transacción ni chequeo de versión. En READ COMMITTED, si el worker aún no ha commiteado su tx1, el cancel ve `Routed`, procede y confirma "Cancelled" al usuario; acto seguido el worker commitea `Printing` (sin guardia) y **envía al spooler** → el documento se imprime pese al "Cancelado", y el log de eventos queda incoherente (Cancelled → Printing). Cerrar con el mismo UPDATE condicional de P1-001.

---

### [PRINT-P2-009] Fechas como string truncado a segundos + formatos legacy mixtos

**Severidad:** P2 · **Confianza:** Parcial (requiere HANA real) · **Categoría:** Datos
**Archivos:** `ImpresorasDbContext.cs:12-36,403-418`
Los `DateTimeOffset` se persisten como `"yyyy-MM-dd HH:mm:ss"` (sin milisegundos) y se leen aceptando también `dd/MM/yyyy` (legacy). Se pierde precisión sub-segundo, crítica porque `UpdatedAtUtc` hace de lease, timeout y orden a la vez (dos actualizaciones en el mismo segundo son indistinguibles). Además, las consultas que filtran en SQL (`PrintExecutionService.cs:48-50`: `UpdatedAtUtc <= now-stale`, `NextRetryAtUtc <= now`) comparan **strings** en HANA: con datos homogéneos en `yyyy-MM-dd` el orden lexicográfico funciona, pero cualquier fila legacy en `dd/MM/yyyy` rompe el orden y los rangos. Verificar contra HANA de staging con datos migrados.

---

### [PRINT-P2-010] JWT sin revocación y usuario sin flag de baja

**Severidad:** P2 · **Confianza:** Confirmado · **Categoría:** Seguridad
**Archivos:** `AuthController.cs:52-122`; `User.cs`; `UsersController.cs:149-175`
El token vive 8 h con `ClockSkew=0`. No hay lista de revocación ni versión de credenciales. La entidad `User` **no tiene** campo activo/deshabilitado; sólo se puede **borrar**. Un usuario borrado conserva acceso hasta 8 h (su token sigue validando). Cambiar contraseña tampoco invalida tokens vivos. Añadir `IsActive`/`TokenVersion` y comprobarlo, o reducir expiración + refresh.

---

### [PRINT-P2-011] Alerta Telegram enviada antes de persistir estado + sin guardia multi-instancia

**Severidad:** P2 · **Confianza:** Confirmado · **Categoría:** Operación
**Archivos:** `StoreHealthAlertBackgroundService.cs:145-220`
`SendAlertAsync` se llama y se fija `NotifiedHealth` en memoria, pero `SaveChanges` se hace al final del ciclo (149). Un crash entre envío y guardado → re-alerta el próximo ciclo (spam). Con 2 workers, ambos evalúan y ambos envían la misma alerta (sin lock ni histéresis). El estado de tienda se calcula además con `failedWindow` desde `now.Date` (medianoche UTC), lo que puede diferir de la ventana local mostrada en el dashboard.

---

### [PRINT-P3-012] Symfony CVE-2026-45133 en dependencias del frontend

**Severidad:** P3 · **Confianza:** Confirmado · **Categoría:** Seguridad
`composer audit` reporta `symfony/yaml` (u otro componente) vulnerable. Actualizar a la versión parcheada (≥7.4.12 / rama correspondiente).

---

### [PRINT-P3-013] `BackoffSeconds` vacío/mal configurado provoca excepción

**Severidad:** P3 · **Confianza:** Confirmado · **Categoría:** Operación
**Archivos:** `PrintExecutionService.cs:176,303`
`BackoffSeconds[Math.Min(idx, Length-1)]`: si el array se configura vacío, `Length-1 = -1` → `IndexOutOfRangeException` en cada reintento. Validar en el arranque (`DependencyInjection`) que el array no está vacío y tiene valores ≥0.

---

## 5. Verificación de invariantes

| # | Invariante | Estado | Evidencia / contraejemplo |
|---|---|---|---|
| I1 | Terminal no vuelve a no-terminal | Parcial | `PrinterBlocked→SpoolAccepted` (watchdog) es un retroceso intencionado; `RETRY_ROUTE` saca de ErrorFinal (manual, aceptable) |
| I4 | No doble envío por dos workers | **Incumplido** | PRINT-P1-001 |
| I5 | Ningún job desaparece sin estado consultable | Cumplido | rescate de Pending huérfano (`ExecuteBatch` 78-84) |
| I6 | Fallo Telegram no detiene impresión | Cumplido | `SendAlertAsync` captura por chat, no propaga |
| I9 | AttemptCount == envíos reales | **Incumplido** | PRINT-P1-002: un timeout que imprimió consume intento y reimprime |
| I10 | ACK sólo si persistido durablemente | Cumplido | ACK tras `SaveChanges`; pero I11↓ |
| I11 | Perder el ACK no genera 2ª impresión | Cumplido | dedup por unique index en reingesta |
| I12 | PrintedConfirmed con evidencia del trabajo | **Incumplido** | PRINT-P1-003 |
| I13 | Enrutado determinista | Cumplido | `RoutingResolver` ordena por Priority, RuleId; desempate estable |
| I14 | Ninguna consulta por lotes deja registros bloqueados | **Incumplido** | PRINT-P2-006 (starvation) |
| I15 | Sin escalada de rol por llamada directa | Cumplido | políticas `[Authorize]` por endpoint verificadas |
| I17 | Secretos no en repo/logs/respuestas | **Incumplido** | PRINT-P1-004 (historial Git) |
| I18 | Reinicio en cualquier punto sin pérdida silenciosa | Parcial | sin pérdida, pero con **duplicado** (P1-002) |
| I19 | Política explícita ante imposibilidad de exactly-once | **Ausente** | no hay decisión documentada duplicado-vs-pérdida |
| I20 | Frontend refleja estado real de API | Parcial | `PrintedConfirmed` engañoso se propaga a UI (I12) |

## 6. Matriz de fallos (operaciones externas)

| Operación | Falla antes | Falla durante | Falla después | Duplicado | Pérdida |
|---|---|---|---|---|---|
| SAP claim | sin efecto | tx rollback | lease expira, re-claim | no | no |
| Persistencia local | — | lote abortado entero | ACK no corre | no | lote entero re-ingesta |
| SAP ACK | — | filas quedan claimed | reingesta→dedup | no | no |
| Windows Spooler | job en Routed | Printing stale→**reenvío** | tx2 no persiste→**reenvío** | **sí (P1-002)** | no |
| IPP | Unavailable→PrintedUnknown | timeout→Unknown | confirma por impresora no por job | — | falso confirmado |
| Telegram | — | captura por chat | estado no persiste→re-spam | alerta dup | alerta perdida si crash |

## 7. Informe de pruebas

- **110 tests .NET verdes**, pero todos sobre **SQLite en memoria**: no ejercitan aislamiento HANA, BLOB, carreras entre procesos, ni fechas-string. Los P1-001/002/003 y P2-005/009 **no están cubiertos**.
- No hay ningún test con **dos consumidores concurrentes** del mismo job.
- No hay test que simule **crash entre spooler y tx2**.
- El watchdog se prueba con pocos jobs → la **starvation** (P2-006) no aflora.
- **Batería mínima propuesta (prioridad):** (1) claim atómico bajo 2 conexiones; (2) recuperación de `Printing` stale sin doble envío (spooler fake con contador); (3) IPP Idle con trabajo inexistente ⇒ no PrintedConfirmed; (4) ingesta con duplicado intra-lote ⇒ el resto persiste; (5) suite de contrato contra **HANA de staging** para fechas/BLOB/rowcount.

## 8. Plan de corrección

**Bloqueante antes de producción con 2 workers / o garantizar 1 worker único**
- P1-001 claim atómico por UPDATE condicional. P1-002 política duplicado-vs-pérdida + no reenvío ciego de stale. P1-004 **revocar token Telegram ya**.

**7 días**
- P1-003 confirmación IPP por job-id o renombrar estado. P2-010 flag IsActive + revocación. P2-005 inserción por-job en ingesta. P2-011 persistir estado antes de enviar alerta.

**30 días**
- P2-006 orden antes de Take. P2-007 proyección escalar en ingesta. P2-009 validar fechas contra HANA (o migrar a columna TIMESTAMP nativa). P3-012 actualizar symfony. P3-013 validar BackoffSeconds.

## 9. Preguntas de negocio (requieren decisión)

1. Ante imposibilidad de exactly-once físico: **¿se prefiere riesgo de duplicado o de pérdida?** (decide P1-002).
2. **¿Se permite más de una instancia del Worker?** Si no, ¿cómo se garantiza técnicamente (lock de arranque, servicio único)?
3. **¿Qué evidencia habilita "PrintedConfirmed"?** (contador de páginas IPP, job-id, o se acepta la ambigüedad actual).
4. ¿Puede un empleado reimprimir manualmente un `PrintedUnknown`?
5. SLA máximo de una impresión y tiempo de retención del `PdfBlob`.

## Decisión de aptitud

> **NO APTO PARA PRODUCCIÓN** tal cual, **salvo** que se garantice de forma verificable **una única instancia del Worker** y se asuma por escrito el riesgo de duplicado en reinicios (P1-002).
>
> Justificación: con ≥2 workers la doble impresión es **sistemática** (P1-001, sin optimistic locking real). Incluso con 1 worker, un reinicio/timeout con trabajos en vuelo **reimprime** (P1-002). El estado central `PrintedConfirmed` **no es fiable** (P1-003). Y hay un **secreto comprometido** en el historial (P1-004, acción inmediata).
>
> Con las correcciones "bloqueantes" aplicadas (claim atómico + política de stale + rotación de token) el sistema pasa a **APTO CON CONDICIONES**.
