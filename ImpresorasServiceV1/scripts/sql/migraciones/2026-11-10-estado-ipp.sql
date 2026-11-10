-- Migracion: ultimo estado IPP de cada impresora (ERP_SERCA docs/72, R1, 10/11/2026).
-- Lo escribe el monitor de conectividad cada 30 s con la misma consulta Get-Printer-Attributes que ya
-- hacia (estado, motivos, mensaje, trabajos en cola, consumibles, modelo y su web); antes se tiraba.
-- Solo la ultima lectura, sin historia. Columnas nullable, sin valores por defecto ni indices: el codigo
-- anterior no las lee. Antes de desplegar la version que las usa (Api y Worker leen las columnas en toda
-- consulta de impresoras):
--   node scripts/sql/migraciones/aplicar.mjs scripts/sql/migraciones/2026-11-10-estado-ipp.sql
-- Repetirla no cambia nada.
-- Marcha atras:
--   ALTER TABLE printer_printer DROP COLUMN ipp_state, DROP COLUMN ipp_state_reasons,
--     DROP COLUMN ipp_state_message, DROP COLUMN ipp_queued_jobs, DROP COLUMN ipp_markers_json,
--     DROP COLUMN ipp_make_and_model, DROP COLUMN ipp_more_info_url, DROP COLUMN ipp_checked_at_utc;
ALTER TABLE printer_printer
    ADD COLUMN IF NOT EXISTS ipp_state integer,
    ADD COLUMN IF NOT EXISTS ipp_state_reasons varchar(400),
    ADD COLUMN IF NOT EXISTS ipp_state_message varchar(400),
    ADD COLUMN IF NOT EXISTS ipp_queued_jobs integer,
    ADD COLUMN IF NOT EXISTS ipp_markers_json text,
    ADD COLUMN IF NOT EXISTS ipp_make_and_model varchar(200),
    ADD COLUMN IF NOT EXISTS ipp_more_info_url varchar(400),
    ADD COLUMN IF NOT EXISTS ipp_checked_at_utc timestamptz;
