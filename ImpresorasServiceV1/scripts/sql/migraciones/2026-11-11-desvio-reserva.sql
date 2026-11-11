-- Migracion: impresora de reserva y trabajos desviados (ERP_SERCA docs/75, F1).
-- printer_printer.backup_printer_id: la reserva de cada impresora (misma tienda, lo comprueba la API).
--   Clave ajena a la propia tabla: si se borra la reserva, la columna queda vacia. No puede ser ella misma.
-- printer_print_job.diverted_from_printer_id: de que impresora se desvio el trabajo (sin clave ajena, como
--   printer_id). Las dos nullable y vacias: hasta F2 nadie las usa para imprimir.
-- Antes de desplegar la version que las usa (Api y Worker las leen en toda consulta de impresoras y trabajos):
--   node scripts/sql/migraciones/aplicar.mjs scripts/sql/migraciones/2026-11-11-desvio-reserva.sql
-- Repetirla no cambia nada.
-- Marcha atras:
--   ALTER TABLE printer_printer DROP COLUMN backup_printer_id;
--   ALTER TABLE printer_print_job DROP COLUMN diverted_from_printer_id;
ALTER TABLE printer_printer
    ADD COLUMN IF NOT EXISTS backup_printer_id integer
        REFERENCES printer_printer (printer_id) ON DELETE SET NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_printer_printer_backup_no_self') THEN
        ALTER TABLE printer_printer
            ADD CONSTRAINT ck_printer_printer_backup_no_self CHECK (backup_printer_id <> printer_id);
    END IF;
END $$;

ALTER TABLE printer_print_job
    ADD COLUMN IF NOT EXISTS diverted_from_printer_id integer;
