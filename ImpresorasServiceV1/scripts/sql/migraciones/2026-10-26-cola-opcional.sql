-- Migracion (PLAN B9): la cola de Windows pasa a ser opcional. Se imprime primero por IP (IPP) y la
-- cola es la alternativa; una impresora puede tener solo IP. El indice unico (store_id, spool_queue)
-- se queda: en PostgreSQL varios NULL no chocan.
-- Antes de desplegar la version nueva (la anterior sigue funcionando con la columna nullable):
--   psql ... -v ON_ERROR_STOP=1 -1 -c "SET search_path TO admon" -f 2026-10-26-cola-opcional.sql
-- Marcha atras (solo si no hay impresoras sin cola):
--   ALTER TABLE printer_printer ALTER COLUMN spool_queue SET NOT NULL;
ALTER TABLE printer_printer ALTER COLUMN spool_queue DROP NOT NULL;
