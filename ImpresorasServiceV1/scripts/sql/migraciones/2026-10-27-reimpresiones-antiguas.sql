-- Migracion OPCIONAL (auditoria L5, docs/auditoria-2026-10-26.md): hasta el 26/10/2026 reimprimir algo
-- «Sin confirmar» (PrintedUnknown) se apuntaba como RETRY_ROUTE, sin motivo. Desde el 26/10 es REPRINT.
-- Esto pasa las antiguas a REPRINT para que salgan en el registro de reimpresiones y en su KPI (sin
-- motivo: no se pidio). No hace falta para desplegar:
--   psql ... -v ON_ERROR_STOP=1 -1 -c "SET search_path TO admon" -f 2026-10-27-reimpresiones-antiguas.sql
-- Marcha atras (solo las que no tienen metadata_json, que es lo que distingue a las antiguas):
--   UPDATE printer_print_job_event SET event_type = 'RETRY_ROUTE'
--   WHERE event_type = 'REPRINT' AND old_status = 'PrintedUnknown' AND metadata_json IS NULL;
UPDATE printer_print_job_event
SET event_type = 'REPRINT'
WHERE event_type = 'RETRY_ROUTE' AND old_status = 'PrintedUnknown';
