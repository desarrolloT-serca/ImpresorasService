-- Migracion (PLAN B6.6): quitar printer_print_job.row_version, columna que no usaba nadie.
-- Con los servicios PARADOS (la version anterior la lee):
--   psql ... -v ON_ERROR_STOP=1 -1 -c "SET search_path TO admon" -f 2026-10-23-quitar-row-version.sql
ALTER TABLE printer_print_job DROP COLUMN IF EXISTS row_version;
