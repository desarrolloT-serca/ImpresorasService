-- Migracion (auditoria M9, docs/auditoria-2026-10-26.md): el KPI «Reimpresiones» del dashboard y el
-- registro de reimpresiones buscan los eventos REPRINT por fecha; sin este indice recorren toda la
-- tabla de eventos en cada carga. Parcial: solo las filas REPRINT, asi que ocupa poco. Se puede aplicar
-- antes o despues de desplegar (la aplicacion no depende de el):
--   psql ... -v ON_ERROR_STOP=1 -1 -c "SET search_path TO admon" -f 2026-10-27-indice-reimpresiones.sql
-- Dentro de una transaccion no vale CONCURRENTLY: bloquea las escrituras de la tabla mientras se crea
-- (segundos con el volumen actual).
-- Marcha atras: DROP INDEX ix_printer_print_job_event_reprint;
CREATE INDEX IF NOT EXISTS ix_printer_print_job_event_reprint
    ON printer_print_job_event (occurred_at_utc) WHERE event_type = 'REPRINT';
