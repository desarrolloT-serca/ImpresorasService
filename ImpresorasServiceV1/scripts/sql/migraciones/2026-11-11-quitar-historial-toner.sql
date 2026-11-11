-- Migracion: se quita el historial del toner (printer_toner_reading, de 2026-11-10-ficha-y-toner.sql).
-- No aportaba: en las Canon manda la estimacion de la propia impresora (printer_page_counter.toner_days_left)
-- y en el resto no habia lecturas. Decision del 11/11/2026 (ERP_SERCA docs/72, mejora 7).
-- Despues de desplegar la version que ya no la usa (Api y Worker escriben en ella hasta entonces):
--   node scripts/sql/migraciones/aplicar.mjs scripts/sql/migraciones/2026-11-11-quitar-historial-toner.sql
-- Repetirla no cambia nada. Borra sus filas (5 en la copia el 11/11/2026), que no se recuperan.
-- Marcha atras: volver a aplicar 2026-11-10-ficha-y-toner.sql (crea la tabla vacia) y desplegar la version
-- anterior.
DROP TABLE IF EXISTS printer_toner_reading;
