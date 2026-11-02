-- Migracion: la cola se ordena por la ultima vez que se mando imprimir (reintento o reimpresion) y
-- no por la llegada, para que lo reimpreso no se pierda entre lo que llego despues (27/10/2026).
-- Antes de desplegar la version que la usa (Api y Worker leen la columna en toda consulta de trabajos):
--   node scripts/sql/migraciones/aplicar.mjs scripts/sql/migraciones/2026-10-27-orden-cola.sql
-- Rellena las filas ya reintentadas o reimpresas desde su ultimo evento; las demas quedan en null
-- (se ordenan por created_at_utc). Repetirla no cambia nada.
-- Marcha atras: DROP INDEX ix_printer_print_job_orden_cola; ALTER TABLE printer_print_job DROP COLUMN last_requeued_at_utc;
ALTER TABLE printer_print_job ADD COLUMN IF NOT EXISTS last_requeued_at_utc timestamptz;

UPDATE printer_print_job j
SET last_requeued_at_utc = e.ultima
FROM (
    SELECT job_id, max(occurred_at_utc) AS ultima
    FROM printer_print_job_event
    WHERE event_type IN ('REPRINT', 'RETRY_ROUTE')
    GROUP BY job_id
) e
WHERE e.job_id = j.job_id
  AND j.last_requeued_at_utc IS DISTINCT FROM e.ultima;

CREATE INDEX IF NOT EXISTS ix_printer_print_job_orden_cola
    ON printer_print_job ((coalesce(last_requeued_at_utc, created_at_utc)));
