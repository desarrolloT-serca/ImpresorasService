# Roadmap de corrección — ImpresorasService

**Base:** commit `34ff3c7` (`develop`) · auditoría + análisis previo + **decisiones de producto congeladas**.
**Fuera de alcance:** token histórico de Telegram.
**Principio:** restaurar invariantes con el menor cambio verificable; sin infraestructura nueva (ni Redis, colas, leader election); DDL manual en `scripts/sql/`; SQLite para suite rápida, HANA para garantías.

## Decisiones congeladas aplicadas (fuente de verdad)

| # | Decisión | Consecuencia en el roadmap |
|---|---|---|
| 1 | Un solo Worker + lock de 2ª instancia + **claim atómico obligatorio** | Fase 2 |
| 2 | Ambiguo → revisión manual; **no** reintento auto; priorizar evitar duplicados | Fase 2 (clasificación de fallos) + Fase 3 (estado) |
| 3 | `PrintedConfirmed` sólo con evidencia por trabajo; sin ella, estado honesto | Fase 3 |
| 4 | Bloqueo de cambio de impresora en estados activos; reimpresión manual `StoreManager`/`Admin` con auditoría | Fase 4 |
| 5 | Invalidación inmediata de acceso (IsActive / TokenVersion) | Fase 5 |
| 6 | Pruebas HANA obligatorias para garantías | Fase 0 (gates) + Fase 7 (suite) |

**Nota de roles:** los roles reales son `Admin`, `StoreManager`, `Employee`. "Supervisor" es alias legacy → `StoreManager` (`RoleCatalog.cs:8,40-45`). La política `StoreManagerOrAdmin` ya existe y es exactamente "Supervisor/Admin". No se inventa ningún rol.

---

## Estructura del roadmap

```
Fase 0  Gates de validación (HANA / Spooler / IPP)      ← desbloquean el diseño de 2,3,6
Fase 1  Correcciones confirmadas independientes          ← sin dependencia de validación
Fase 2  Claim atómico + lock de instancia + clasificación de fallos   [núcleo I1/I6/I16]
Fase 3  Estados honestos + evidencia por trabajo         [I7/I15]  (depende de S1/I-1)
Fase 4  Reimpresión manual auditada + bloqueo de impresora [I8]    (depende de Fase 2)
Fase 5  Invalidación inmediata de acceso                  (independiente, paralelizable)
Fase 6  Datos y tiempo (fechas / pdf_blob)               (depende de H2/D1)
Fase 7  Suite de garantías (HANA + concurrencia + crash) (acompaña a 2/3/6, gate de despliegue)
TRV     Observabilidad mínima (worker que procesa)       (transversal)
```

**Grafo de dependencias:**
```
Fase 0 ──┬─> Fase 2 ──┬─> Fase 4
         │            └─> Fase 3 ──> (UI honesta)
         ├─> Fase 6
         └─> Fase 7
Fase 1  (paralela, sin dependencias)
Fase 5  (paralela)
TRV     (se integra en 2 y 7)
```

Orden de ejecución recomendado: **0 → 1 (en paralelo) → 2 → {3, 4} → 6 → 7**, con 5 y TRV en paralelo desde el inicio. Complejidad relativa por tarea: **B**aja / **M**edia / **A**lta. Sin fechas: la secuenciación es por dependencia y por gate, no por calendario.

---

## Fase 0 — Gates de validación (desbloqueantes)

> Ninguna corrección del núcleo se convierte en fix hasta cerrar su gate. Resultado de cada gate = decisión de diseño.

### 0.1 Entorno HANA no productivo `[M]`
Habilitar esquema/instancia HANA aislada para tests de contrato. No requiere infraestructura permanente costosa; sirve un esquema dedicado en entorno no productivo. **Salida:** cadena de conexión de test + procedimiento de reset de datos.

### 0.2 Gate H1 — Rowcount de UPDATE condicional `[M]`
**Hipótesis:** `UPDATE printer_print_job SET status='Printing',... WHERE job_id=@id AND status IN ('Routed','RetryScheduled')` devuelve rowcount 1/0 fiable vía `ExecuteSqlRawAsync`/`ExecuteUpdateAsync`.
**Procedimiento:** dos conexiones concurrentes compiten por el mismo job. **Esperado:** sólo una obtiene 1.
**Desbloquea:** Fase 2 (claim atómico), P2-008.

### 0.3 Gate H2 — Tipos reales en HANA `[B]`
**Hipótesis:** `row_version` es BLOB (no comparable en WHERE) y las columnas de fecha son VARCHAR con formato homogéneo.
**Procedimiento:** `SYS.TABLE_COLUMNS` + muestreo de valores de fecha y de `pdf_blob` en estados avanzados.
**Desbloquea:** decisión de RowVersion, Fase 6, DIV-001.

### 0.4 Gate H3 — Claim de ingesta en HANA `[M]`
**Hipótesis:** HANA soporta `FOR UPDATE SKIP LOCKED` o hay que emular con UPDATE condicional + reintento (el ejemplo del repo `create_sap_aux_print_queue.sql` es **PostgreSQL**, no HANA).
**Desbloquea:** el mecanismo de claim de ingesta en Fase 2.

### 0.5 Gate S1/S2 — Spooler job-id y reenvío `[M]`
**S1:** ¿el envío vía SumatraPDF/Win Print API permite recuperar un identificador de trabajo del spooler correlacionable? **S2:** confirmar que reenviar el mismo PDF produce una segunda impresión (sin dedup del spooler).
**Desbloquea:** Fase 3 (spooler_job_id, evidencia por trabajo), confirma la necesidad de la política de Fase 2.

### 0.6 Gate I-1 — IPP por trabajo en el parque `[M]`
**Hipótesis:** las impresoras responden `Get-Job-Attributes` con `job-state`/`job-media-sheets-completed`; comportamiento al terminar/cancelar/eliminar el job; ventana de disponibilidad del histórico.
**Desbloquea:** Fase 3 (confirmación por job vs estado honesto), decisión #3 por modelo de impresora.

**Regla de gate:** si S1 o I-1 son negativos para parte del parque → esa parte **nunca** llega a `PrintedConfirmed`; se queda en estado honesto no verificado (decisión #3). El roadmap no bloquea por ello; refleja la limitación en estado y UI.

---

## Fase 1 — Correcciones confirmadas independientes

> Todas tienen evidencia suficiente y no dependen de gates. Ejecutables en paralelo a la Fase 0. Verificables en SQLite salvo confirmación final en HANA donde se indica.

| ID | Tarea | Hallazgo | Invariante | Archivos | Aceptación | Cmpl |
|---|---|---|---|---|---|---|
| 1.1 | `OrderBy(UpdatedAtUtc)` **antes** de `Take` en watchdog | P2-006 | I13 | `SpoolAcceptedWatchdogBackgroundService.cs:67-90` | test con backlog > windowLimit confirma que el más antiguo entra siempre | B |
| 1.2 | `OrderBy` antes de `Take` en selección de candidatos de ingesta | P2-006 | I13 | `SapHanaJobSourceAdapter.cs:53-65` | filas antiguas no quedan starved bajo backlog | B |
| 1.3 | Proyección escalar para candidatos; cargar `PdfBlob` sólo de reclamados | P2-007 | robustez | `SapHanaJobSourceAdapter.cs:53-77` | no se materializan PDFs no reclamados (medición memoria) | M |
| 1.4 | Insertar PrintJobs **por job** (try/catch por elemento) en vez de lote all-or-nothing | P2-005 | robustez ingesta | `IngestionService.cs:40-98` | un duplicado intra-lote no descarta el resto; confirmar en HANA con índice único | M |
| 1.5 | Exigir `ClaimToken` en ACK y renovación de lease | P2-005 (#18/#19) | integridad claim | `SapHanaJobSourceAdapter.cs:113-165` | ACK/renew filtran por `ClaimedBy AND ClaimToken`; test de claim robado | B |
| 1.6 | Validar `BackoffSeconds` no vacío / valores ≥0 al arrancar | P3-013 | I17 | `PrintExecutionOptions`, `DependencyInjection.cs` | arranque falla rápido con config inválida; sin `IndexOutOfRange` en runtime | B |
| 1.7 | Persistir `StoreAlertState` **antes** de enviar la alerta Telegram + histéresis | P2-011 | robustez | `StoreHealthAlertBackgroundService.cs:145-220` | crash entre envío y guardado no reenvía; test de orden | B |
| 1.8 | Actualizar `symfony/*` a versión parcheada | P3-012 | seguridad | `composer.json/lock` | `composer audit` limpio | B |
| 1.9 | Introducir `TimeProvider` inyectable en servicios con `UtcNow` | #30 | testabilidad (habilitador) | ejecución, watchdog, ingesta, alertas | tests deterministas de tiempo posibles | M |

**Salida de Fase 1:** invariante I13 e I17 restaurados; ingesta robusta a duplicados; base de tiempo testeable. Ninguna toca el efecto físico → riesgo de despliegue bajo.

---

## Fase 2 — Claim atómico + lock de instancia + clasificación de fallos `[núcleo]`

> Depende de **H1, H2, H3**. Cierra I1, I6, I16 y prepara I8. Es el corazón del roadmap.

### 2.1 Lock de instancia única del Worker `[M]` — decisión #1
Mecanismo de arranque que impide que una 2ª instancia procese. Opción simple sin infraestructura: **lock en HANA** (fila singleton con `holder`, `heartbeat_utc`, `lease`) adquirida por UPDATE condicional; la instancia que no lo obtiene:
- registra log claro y señal de monitorización,
- permanece inactiva reintentando adquirir el lock (no procesa), o finaliza con error configurable.
El titular renueva heartbeat; si expira (caída), otra instancia puede tomarlo.
**Aceptación:** arrancar 2 Workers → sólo uno procesa; matar al titular → el segundo toma el relevo tras expirar el lease. **Archivos:** nuevo servicio de lock + `Program.cs` del Worker + DDL `printer_worker_lock`.

### 2.2 Claim atómico de ejecución de `PrintJob` `[A]` — decisión #1
Sustituir el check no atómico `RowVersionSnapshotStillMatches` por **UPDATE condicional** que reclama el job en el mismo statement que lo pasa a `Printing`:
```
UPDATE printer_print_job
SET status='Printing', attempt_count=attempt_count+1, updated_at_utc=@now
WHERE job_id=@id AND status IN ('Routed','RetryScheduled')
   AND (status<>'Printing' OR updated_at_utc <= @staleThreshold)
```
Proceder al spooler **sólo si rowcount = 1**. Decidir el futuro de `RowVersion` según H2:
- si BLOB no comparable → **retirar** `RowVersion` como defensa y documentar que la exclusión la da el UPDATE condicional (status en WHERE);
- opción alternativa: introducir token comparable `INT/BIGINT` si se quiere OCC general (sólo si aporta valor demostrable).
**Aceptación (gate X1 en HANA):** 2 conexiones compiten por el mismo job → una sola imprime. **Archivos:** `PrintExecutionService.cs:136-256`.

### 2.3 Clasificación de resultados de envío `[M]` — decisión #2
Introducir taxonomía explícita de resultado, diferenciando:
- `fallo_seguro_antes_de_entrega` → reintento automático permitido (validación previa, impresora inválida, PDF ausente/ inválido, fallo antes de crear el proceso);
- `entrega_posiblemente_realizada` → **no** reintento auto → estado revisable;
- `spooler_confirmo_aceptacion` → SpoolAccepted;
- `resultado_fisico_desconocido` → estado honesto (Fase 3).
Mapear los códigos actuales (`PDF_MISSING`, `PRINTER_INVALID`, `NET_TIMEOUT`, `SPOOLER_EXCEPTION`, `SPOOLER_DOWN`, `PDF_INVALID`) a estas clases. En particular, **`NET_TIMEOUT` deja de ser reintento automático** (hoy `IsTransient=true`, `PrintExecutionService.cs:268`).
**Aceptación:** matriz de errores documentada; test que verifica que cada clase enruta al estado correcto. **Archivos:** `WindowsPrintSpooler.cs`, `PrintExecutionService.cs`, modelo `PrintSpoolResult`.

### 2.4 Recuperación de `Printing` stale → revisión (no reenvío) `[M]` — decisión #2
Un `Printing` encontrado tras reinicio/timeout sin evidencia suficiente pasa a estado revisable (Fase 3), **no** se reenvía al spooler.
**Aceptación (gate X2):** con spooler fake que cuenta envíos, una caída entre spooler y tx2 **no** incrementa el contador de impresiones físicas; el job queda revisable. **Archivos:** `PrintExecutionService.cs:214-220,237-333`.

### 2.5 Operaciones manuales condicionales `[M]` — P2-008 (#22/#23)
`cancel`, `route`/reencaminar y reintento manual pasan a UPDATE condicional por estado esperado. Si el Worker ya reclamó el trabajo, la operación afecta 0 filas y la API responde honestamente (no confirma un cambio no aplicado).
**Aceptación:** test de carrera cancelar/ejecutar → no imprime tras "Cancelado"; la API devuelve conflicto/estado real. **Archivos:** `PrintJobsController.cs:111-200`, `RoutingService.cs`.

**Salida de Fase 2:** I1, I6, I16 restaurados; I8 preparado; política de duplicados aplicada en el motor.

---

## Fase 3 — Estados honestos + evidencia por trabajo `[I7/I15]`

> Depende de **S1** y **I-1** y de la Fase 2. Implementa decisiones #2 y #3.

### 3.1 Estado honesto para resultado desconocido `[M]` — decisión #3
Definir el estado honesto para "entregado pero no verificado / resultado desconocido" respetando el dominio y **evitando estados redundantes**. Candidatos: reutilizar `PrintedUnknown` (ya existe) para el caso de resultado físico desconocido, y reservar la confirmación fuerte para evidencia por job. Sólo crear un nombre nuevo (`DeliveredUnverified`/`PrintOutcomeUnknown`) si se demuestra que `PrintedUnknown` no cubre la semántica operativa. **Decisión de nombre: técnica + revisión de dominio.**
**Aceptación:** máquina de estados actualizada sin duplicar semántica; doc `CLAUDE.md` sincronizada.

### 3.2 `SpoolAccepted` con etiqueta honesta en UI `[B]` — #12
`SpoolAccepted` = "aceptado por el spooler", no "impreso". Ajustar `StatusLabels` y vistas para que no afirmen impresión física.
**Aceptación:** revisión de `StatusLabels.php` y `cola`/`dashboard` blade; ninguna etiqueta afirma "impreso" en SpoolAccepted/PrinterBlocked/Unknown.

### 3.3 Guardar `spooler_job_id` `[M]` — sólo si **S1** positivo
Persistir el identificador del trabajo del spooler al enviar, para correlación posterior.
**Aceptación:** columna `spooler_job_id` poblada; test de persistencia. **DDL:** `ALTER TABLE printer_print_job ADD spooler_job_id`.

### 3.4 Confirmación por trabajo `[A]` — decisión #3, sólo si **I-1** positivo
Sustituir la confirmación por `printer-state` por consulta de atributos del **job concreto** (IPP `Get-Job-Attributes` y/o estado del job en el spooler). `PrintedConfirmed` sólo con evidencia correlacionada al job. Impresoras sin soporte → permanecen en estado honesto no verificado.
**Aceptación (gate I-1):** watchdog con impresora Idle y job inexistente **no** marca PrintedConfirmed; con evidencia de job sí. **Archivos:** `IppConfirmationService.cs`, `SpoolAcceptedWatchdogBackgroundService.cs:99-282`.

### 3.5 Reflejar la limitación en estado/UI `[B]` — decisión #3
Cuando el parche de impresora no permita confirmación por job, estado e interfaz lo indican explícitamente (p. ej. "entrega no verificable por la impresora").
**Aceptación:** UI muestra la limitación; no hay falsos "confirmado".

**Salida de Fase 3:** I7 e I15 restaurados; `PrintedConfirmed` fiable o ausente con honestidad.

---

## Fase 4 — Reimpresión manual auditada + bloqueo de impresora `[I8]`

> Depende de la Fase 2 (operaciones condicionales). Implementa decisión #4.

### 4.1 Bloqueo de cambio de impresora en estados activos `[M]`
Rechazar reencaminar/cambiar impresora cuando el job esté en `Printing`, `SpoolAccepted`, `PrinterBlocked` o cualquier estado de claim/envío activo. Permitido antes del claim del Worker o en estados finales/revisables, mediante acción explícita.
**Aceptación:** intento de cambio en estado activo → rechazo con mensaje claro; test por estado. **Archivos:** `PrintJobsController.cs`, `RoutingService.cs`.

### 4.2 Reimpresión manual auditada `[A]` — decisión #4
Disponible para `StoreManager`/`Admin` (política existente `StoreManagerOrAdmin`) en estados definidos: resultado desconocido, ErrorFinal, cancelado (si negocio lo permite), confirmado-no-entregado. La acción:
- pide confirmación explícita,
- registra usuario, fecha y motivo,
- crea evento de auditoría ligado al trabajo original,
- es idempotente frente a doble clic / solicitudes concurrentes (token de operación o UPDATE condicional),
- avisa de posible duplicado cuando el resultado previo era desconocido.
**Aceptación:** doble clic no genera dos reimpresiones; evento de auditoría con usuario/motivo/relación; permiso denegado a `Employee`. **Archivos:** nuevo endpoint en `PrintJobsController`, evento en `PrintJobEvent`, UI de confirmación en frontend.

### 4.3 Consistencia UI ↔ estado real `[B]`
La UI no confirma modificaciones que el UPDATE condicional no aplicó (el Worker ya reclamó).
**Aceptación:** respuesta de API refleja rowcount real; frontend muestra el estado efectivo.

**Salida de Fase 4:** I8 restaurado; reimpresión trazable y segura frente a concurrencia.

---

## Fase 5 — Invalidación inmediata de acceso `[seguridad]`

> Independiente del núcleo; paralelizable desde el inicio. Implementa decisión #5.

### 5.1 `User.IsActive` + comprobación en pipeline `[M]`
Añadir flag de estado y verificarlo en autenticación/autorización. Baja/desactivación → token deja de servir de inmediato.
**Aceptación:** usuario desactivado con token válido → 401 en siguiente petición; test de integración. **Archivos:** `User.cs`, pipeline JWT (`Program.cs`), `UsersController` (desactivar en vez de sólo borrar). **DDL:** `ADD is_active`.

### 5.2 Revocación por cambio de credencial `[M]` — decisión #5
`TokenVersion`/`SecurityStamp` en el claim; cambio de contraseña o revocación admin invalida tokens previos.
**Aceptación:** cambiar contraseña invalida tokens emitidos antes; test.

### 5.3 Expiración de token más corta (complementaria) `[B]`
Reducir la ventana de 8h como defensa en profundidad.
**Aceptación:** valor configurable; refresh si aplica.

**Nota de proporcionalidad:** sin servicio externo de sesiones. Valorar caché de estado de usuario **sólo** si se demuestra problema de rendimiento por consultar en cada petición.

---

## Fase 6 — Datos y tiempo

> Depende de **H2** y **D1**. Implementa P2-009 y DIV-001.

### 6.1 Fechas `[M]` — P2-009
Según H2: si el formato en HANA es homogéneo (`yyyy-MM-dd HH:mm:ss`), documentar y añadir guardas; si conviven formatos legacy o se necesita precisión sub-segundo para lease/orden → migrar a columna temporal nativa (TIMESTAMP) con estrategia aditiva y lectura tolerante de datos antiguos.
**Aceptación (HANA):** filtros/orden temporales correctos con datos representativos; test de rango. **DDL:** condicionado a H2.

### 6.2 `pdf_blob` en estados avanzados `[M]` — DIV-001
Resolver la divergencia: `migrate_pdf_blob_nullable.sql` documenta limpieza de `pdf_blob` al llegar a `SpoolAccepted`, pero el código no la implementa. Decidir (técnico): **implementar la limpieza** (libera espacio, pero impide reimpresión desde estados avanzados sin re-ingesta) **o retirar** la doc/DDL. Debe alinearse con la política de reimpresión de Fase 4 (si se reimprime desde "desconocido", el blob debe seguir disponible en ese estado).
**Aceptación:** comportamiento y DDL coherentes; test que verifica presencia/ausencia del blob según estado.

---

## Fase 7 — Suite de garantías (HANA + concurrencia + crash)

> Acompaña a Fases 2/3/6; actúa como **gate de despliegue**. Implementa decisión #6.

Suite de integración contra HANA no productiva (no obligatoria en cada commit; sí antes de aprobar cambios que dependan de garantías HANA). Pruebas mínimas:

| Nº | Prueba | Verifica |
|---|---|---|
| 1 | 2 conexiones reclaman el mismo `PrintJob` | I1 / 2.2 |
| 2 | UPDATE condicional y rowcount | H1 / 2.2 |
| 3 | Claim y ACK de trabajos origen | 1.5 / H3 |
| 4 | Expiración y renovación de leases | ingesta / 2.1 |
| 5 | Un duplicado dentro de un lote | 1.4 |
| 6 | Fechas y precisión temporal | 6.1 |
| 7 | Ordenación y filtros temporales | 1.1/1.2/6.1 |
| 8 | BLOB y token de concurrencia | 2.2 / H2 |
| 9 | Cancelación concurrente con el Worker | 2.5 |
| 10 | Recuperación tras interrupción de transacción | 2.4 (crash tras spooler, spooler fake con contador) |

**Integración CI:** nueva etapa/gate previo a despliegue (workflows actuales: `tests.yml`, `pull-requests.yml`). SQLite sigue en la suite rápida por commit; HANA como gate pre-release.
**Aceptación:** las 10 pruebas verdes en HANA antes de declarar cerradas las Fases 2/3/6.

---

## Transversal — Observabilidad mínima

### TRV.1 Registrar el Worker que procesa `[B]` — #39
Columna `processed_by` (worker/instancia) en `PrintJob` o en el evento, para diagnosticar duplicados y reclamos.
**Aceptación:** cada transición de ejecución registra qué instancia la hizo. **DDL:** `ADD processed_by` (o campo en evento).
Sin plataforma de métricas nueva; se apoya en `PrintJobEvent` y logs existentes.

---

## Objetos DDL nuevos (para `scripts/sql/`, aplicación manual por DBA)

| Objeto | Fase | Motivo |
|---|---|---|
| `printer_worker_lock` (tabla singleton) | 2.1 | lock de instancia única |
| `printer_print_job.spooler_job_id` | 3.3 | correlación por trabajo (si S1) |
| `printer_user.is_active` | 5.1 | invalidación inmediata |
| `printer_user` token/security stamp (columna) | 5.2 | revocación por credencial |
| `printer_print_job` fecha→TIMESTAMP | 6.1 | sólo si H2 lo exige |
| `printer_print_job.processed_by` | TRV.1 | observabilidad |

> Todo el DDL debe entregarse con el `<SCHEMA>` parametrizado y coordinado con el DBA. El modelo EF runtime debe alinearse con el DDL (evitar la divergencia actual snapshot↔runtime de `RowVersion`).

---

## Clasificación exigida por las decisiones

### Correcciones confirmadas
1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 4.1, 4.2, 4.3, 5.1, 5.2, 5.3, TRV.1.

### Validaciones contra HANA
0.1, 0.2 (H1), 0.3 (H2), 0.4 (H3), Fase 7 (1–9), 6.1.

### Validaciones contra el spooler
0.5 (S1/S2), gate X2 en 2.4/7.10, prerequisito de 3.3.

### Validaciones IPP
0.6 (I-1), prerequisito de 3.4.

### Mejoras aplazables (no bloqueantes)
- Distinguir `DeliveredUnverified` de `PrintedUnknown` (sólo si operación lo pide).
- Lease de ejecución explícito además del UPDATE condicional (innecesario con 1 worker).
- Observabilidad ampliada más allá de eventos/logs.
- Límite máximo de PDF (definir con datos reales de tamaños).
- Migración de fechas si H2 no la obliga.

### Riesgos residuales técnicamente inevitables
- **Exactly-once físico imposible:** aun con claim atómico + política de revisión, un crash exactamente entre el envío al spooler y su persistencia puede dejar un documento **impreso** marcado como "desconocido". La reimpresión manual de ese caso es un duplicado **consciente y autorizado**, no automático. Es el límite físico aceptado por la decisión #2.
- **Parque sin `Get-Job-Attributes`/job-id:** esas impresoras nunca alcanzan `PrintedConfirmed` real; se quedan en estado honesto no verificado (decisión #3). Limitación reflejada en UI, no un bug.
- **Ventana IsActive↔caché:** si se cachea el estado de usuario por rendimiento, existe una ventana mínima de acceso tras la baja; se mitiga con TTL corto (decisión #5).
- **Lock de instancia basado en heartbeat:** ante partición o reloj desincronizado, hay una ventana teórica de solapamiento al expirar el lease; el claim atómico de 2.2 la cubre (por eso ambos son obligatorios).

---

## Criterios para declarar la aplicación apta

- **APTO CON CONDICIONES** cuando estén cerradas Fases 1 y 2 (claim atómico + lock + política de ambiguos) con gates H1/H2 y X1/X2 verdes en HANA.
- **APTO** cuando además Fases 3 y 4 cierren I7/I8/I15 (estados honestos + reimpresión auditada) y la suite de Fase 7 pase como gate de despliegue.
- Fases 5 y 6 no bloquean la aptitud de impresión pero sí la de seguridad/datos y deben cerrarse antes de considerar el sistema completo.

## Trazabilidad hallazgo → fase

| Hallazgo | Fase |
|---|---|
| P1-001 (RowVersion/claim) | 2.2 + 0.2/0.3 + 7 |
| P1-002 (ventana spooler) | 2.3 + 2.4 + 0.5 + 7.10 |
| P1-003 (IPP por impresora) | 3.4 + 0.6 |
| P2-005 (claim/lote ingesta) | 1.4 + 1.5 + 0.4 + 7.3/7.5 |
| P2-006 (starvation) | 1.1 + 1.2 |
| P2-007 (PDFs en memoria) | 1.3 |
| P2-008 (cancel vs worker) | 2.5 + 7.9 |
| P2-009 (fechas) | 6.1 + 0.3 + 7.6/7.7 |
| P2-010 (JWT/baja) | 5.1 + 5.2 + 5.3 |
| P2-011 (alertas) | 1.7 |
| P3-012 (symfony) | 1.8 |
| P3-013 (BackoffSeconds) | 1.6 |
| DIV-001 (pdf_blob) | 6.2 + 0.3 |
| #39 (worker que procesa) | TRV.1 |
