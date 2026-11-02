-- Migracion (PLAN B9.2): numero de trabajo que da la impresora al imprimir por IP (IPP). Con el, el
-- watchdog confirma ese trabajo concreto (terminado, abortado, detenido) y no solo el estado de la
-- impresora. Antes de desplegar la version que lo usa; la anterior no lee la columna:
--   psql ... -v ON_ERROR_STOP=1 -1 -c "SET search_path TO admon" -f 2026-10-26-ipp-job-id.sql
-- Marcha atras: ALTER TABLE printer_print_job DROP COLUMN ipp_job_id;
ALTER TABLE printer_print_job ADD COLUMN IF NOT EXISTS ipp_job_id integer;
