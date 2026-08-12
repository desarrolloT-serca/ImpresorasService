# Rendimiento y optimización

**Corte:** 2026-07-29 · **Commit:** `49a0b9691e484472fb1da23417de172f1e60473f`

No se dispuso de HANA, impresoras ni datos de producción; por tanto, las complejidades están confirmadas por el flujo del código, pero su latencia real requiere baseline en staging.

## Baseline disponible

| Medición | Resultado local |
| --- | --- |
| Tests .NET | 142 casos en ~7 s |
| Vite build | Correcto |
| CSS generado | `app` 43,47 kB; `dbx` 40,09 kB; `system` 122,91 kB |
| JavaScript generado | 43,45 kB |
| HANA/API p95, CPU, memoria | No disponible |
| Worker throughput, spool/IPP latency | No disponible |

## Hallazgos priorizados

### PERF-01 — Resolución de reglas repite tablas completas por trabajo

**Estado:** Confirmado · **Gravedad:** Media · **Confianza:** Alta

`RoutingResolver.cs:29-48` materializa todas las reglas activas y todos los IDs de impresora activa. `IngestionService.cs:131+` resuelve secuencialmente cada alta. Para lote `B`, reglas `R` e impresoras `P`, el coste aproximado es `O(B × (R + P))`, dos consultas y asignaciones completas por trabajo.

**Solución mínima:** proporcionar una `Interface` de resolución por lote que precargue una instantánea una vez y aplique la prioridad en memoria. Si el volumen de reglas es alto, profundizar el `Module` de enrutado: consulta filtrada por `StoreId`, tipo, canal, vigencia y pertenencia de impresora, con índices que soporten ese orden. No añadir caché distribuida hasta medir necesidad.

**Objetivo:** ≤3 consultas por lote y p95 de resolución <1 s para 100 jobs/10k reglas. **Riesgo:** preservar exactamente precedencia y desempate.

### PERF-02 — Dashboard transfiere y materializa trabajo/evento para agregar

**Estado:** Confirmado · **Gravedad:** Media · **Confianza:** Alta

El overview de `Api/DashboardController.cs` ejecuta múltiples roundtrips y materializa filas de impresos/fallidos de la ventana para agrupar después. Con ventana de 30 días, tiempo y memoria crecen `O(J)`. La búsqueda `ExternalJobId.Contains` se traduce previsiblemente a comodín inicial/final y no aprovecha un índice B-tree convencional.

**Solución mínima:** agrupar en HANA por tienda/estado/ventana y devolver agregados; conservar consultas separadas cuando mejoren la legibilidad del plan. Añadir un índice/estrategia de búsqueda solo tras capturar `EXPLAIN PLAN`. Validar el `Skip` con `long` y limitar `page`.

**Objetivo:** p95 <2 s con 1M jobs en 30 días; <150 MB de working set incremental; filas transferidas proporcionales a tiendas, no a jobs.

### PERF-03 — Alertas ejecutan N+1 por tienda

**Estado:** Confirmado · **Gravedad:** Media · **Confianza:** Alta

`StoreHealthAlertBackgroundService` consulta por cada tienda impresoras, cola, fallos y estado de alerta, además de guardar cambios. El ciclo crece aproximadamente `O(S)` roundtrips y puede tardar más que su intervalo.

**Solución mínima:** leer tiendas/impresoras/estados en tres consultas y dos agregaciones agrupadas; evaluar en memoria y guardar estados modificados una vez. Es un `Module` con alto `Leverage`: el mismo snapshot puede alimentar métricas y alertas.

**Objetivo:** número de consultas acotado independientemente de tiendas y ciclo <50 % del intervalo con 500 tiendas.

### PERF-04 — Purga carga todos los IDs y construye `IN`

**Estado:** Confirmado · **Gravedad:** Media · **Confianza:** Alta

`StoresController.cs:166-202` carga todos los `JobId`, después elimina eventos con `Contains`. Para históricos grandes usa memoria `O(J)` y puede exceder parámetros/tamaño SQL o bloquear durante demasiado tiempo.

**Solución mínima:** borrado set-based por subconsulta/relación si HANA provider lo traduce, o lotes reanudables de tamaño fijo. La purga debe ser trabajo administrativo con progreso, no una petición HTTP larga.

**Objetivo:** memoria estable y transacciones <5 s por lote; el objetivo total depende de ventana operativa.

### PERF-05 — PDF duplicado y retenido amplifica BD, red y backups

**Estado:** Confirmado · **Gravedad:** Alta por privacidad/capacidad · **Confianza:** Alta

La ingesta copia `PdfBlob` desde `SourcePrintJobs` a `PrintJobs` (`IngestionService.cs:65-79`). El éxito en `PrintExecutionService.cs:283-300` no limpia el blob, aunque `migrate_pdf_blob_nullable.sql:1` documenta esa intención. Cada PDF queda al menos duplicado y se incorpora indefinidamente a backup, replicación y consultas accidentales.

**Solución mínima:** acordar retención; limpiar la copia de trabajo tras el hito elegido y expirar/purgar origen por lotes. Mantener hash y metadatos para auditoría. Validar recuperación antes de borrar.

**Métrica:** bytes BLOB activos, edad p95/máxima, tamaño y tiempo de backup/restore.

### PERF-06 — PHP mantiene un segundo motor de dashboard y fallback truncado

**Estado:** Confirmado · **Gravedad:** Media · **Confianza:** Alta

`Web.PHP/app/Http/Controllers/DashboardController.php` supera 1.100 líneas y reconstruye KPIs/salud con listados de hasta 500 registros. Duplica lógica de API/Worker y añade CPU/red en degradación; los resultados pueden ser parciales.

**Solución mínima:** hacer del overview de API el `Interface` canónico y que el BFF presente “datos no disponibles” con último snapshot conocido y timestamp, no que reimplemente el dominio. Mantener un fallback solo si el negocio acepta formalmente su precisión y límites.

### PERF-07 — Store/usuario y configuración hacen trabajo no acotado o no atómico

- `Program.cs` normaliza todos los usuarios al arrancar, coste `O(U)` y escritura inesperada. Convertir en migración operativa idempotente.
- `DashboardThresholdRuleStore.SaveAsync` escribe con `File.Create`; lectores pueden observar JSON parcial. Usar archivo temporal en el mismo volumen + flush + replace atómico y lock local, o mover el singleton a HANA si hay varios hosts.

### PERF-08 — CSS y módulos frontend grandes

`system.css` tiene ~5.369 líneas, `dbx.css` ~1.839 y `app.css` ~1.192; `dashboard.blade.php` supera 1.100. Ya se eliminaron 46 reglas totalmente muertas según el roadmap previo, por lo que no se repite la estimación antigua de “55 selectores muertos”. Aún hay solapamiento y un CSS total bruto de ~206 kB antes de gzip.

**Acción:** medir cobertura CSS por rutas/estados y extraer solo módulos con límites claros; no borrar selectores por nombre sin comparar propiedades, estados responsive y verificación visual. Objetivo inicial: evitar crecimiento y retirar reglas 100 % no usadas demostradas.

## Índices/planes a verificar en HANA

No se recomienda crear índices a ciegas. Capturar plan, cardinalidad y frecuencia para:

1. unicidad `(source_system, external_job_id)`;
2. selección de cola por `status`, `next_retry_at_utc`, `updated_at_utc`;
3. eventos por `job_id`, `occurred_at_utc`, `new_status`;
4. reglas por `is_active`, vigencia, `store_id`, `document_type`, `channel`, prioridad;
5. origen por `is_processed`, lease y claim token;
6. impresoras por `store_id`, `is_active`;
7. alertas por `store_id`;
8. búsqueda de `external_job_id` si el caso `%texto%` es necesario.

Comprobar índices ya existentes y write amplification antes de añadirlos.

## Orden de optimización

1. Detener retención de BLOB y definir lifecycle.
2. Corregir semántica/integridad antes de acelerar (enrutado cross-store, ACK, concurrencia).
3. Resolver por lote y agrupar alertas.
4. Mover agregaciones dashboard a HANA con planes medidos.
5. Hacer purgas por lotes y configuración atómica.
6. Optimizar CSS/frontend con cobertura visual.

## Instrumentación mínima

- Histogramas: ingesta, resolución, spool, watchdog, dashboard y ciclo de alertas.
- Contadores: fetched/inserted/duplicate-confirmed/DB-error/ACK; estados; reintentos; resultado Telegram por chat.
- Gauges: profundidad/edad de cola, BLOB bytes/edad, lease restante, trabajos ambiguos, último ciclo correcto.
- Correlación: `CorrelationId`, `JobId`, `SourceJobId` y `WorkerInstanceId` en scope; nunca PDF, token ni contraseña.
- Alertas: cola/edad, Worker sin lease, error rate, alertas no entregadas, almacenamiento y backup.

## Benchmark reproducible

Usar HANA de staging con datos sintéticos y planes capturados. Calentar consultas, separar primera ejecución, ejecutar al menos 30 iteraciones y reportar p50/p95/p99, CPU DB/app, memoria, filas leídas/devueltas y volumen de red. Comparar antes/después con misma cardinalidad y configuración; la mejora no se acepta si cambia la semántica KPI o de impresión.

