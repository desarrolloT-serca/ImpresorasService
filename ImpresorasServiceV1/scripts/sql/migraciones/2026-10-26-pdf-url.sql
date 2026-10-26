-- Migracion (PLAN B0.1): el ERP deja una URL del PDF en vez del PDF; el Worker lo descarga al ingerir.
-- Antes de desplegar la version nueva (la anterior ignora la columna):
--   node scripts/sql/migraciones/aplicar.mjs scripts/sql/migraciones/2026-10-26-pdf-url.sql
-- Marcha atras:
--   ALTER TABLE printer_source_print_job DROP COLUMN pdf_url;
ALTER TABLE printer_source_print_job ADD COLUMN pdf_url text;
