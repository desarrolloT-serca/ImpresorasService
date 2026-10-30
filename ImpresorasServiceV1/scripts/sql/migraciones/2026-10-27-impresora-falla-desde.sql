-- Migracion: desde cuando falla una impresora sin interrupcion (sin conexion o sin host), para el
-- «Desde» de las incidencias del dashboard; antes salia la ultima comprobacion, «ahora mismo» (27/10/2026).
-- La pone el monitor de conectividad al primer fallo y la quita cuando responde.
-- Antes de desplegar la version que la usa (Api y Worker leen la columna en toda consulta de impresoras):
--   node scripts/sql/migraciones/aplicar.mjs scripts/sql/migraciones/2026-10-27-impresora-falla-desde.sql
-- Rellena las que ya fallan con una estimacion: ultima comprobacion menos la racha (una cada 30 s; la
-- racha se corta en 999, asi que una caida de mas de ~8 h queda corta). Repetirla no cambia nada.
-- Marcha atras: ALTER TABLE printer_printer DROP COLUMN connection_failing_since_utc;
ALTER TABLE printer_printer ADD COLUMN IF NOT EXISTS connection_failing_since_utc timestamptz;

UPDATE printer_printer
SET connection_failing_since_utc = last_connection_check_at_utc - connection_failures_streak * interval '30 seconds'
WHERE last_connection_ok = false
  AND last_connection_check_at_utc IS NOT NULL
  AND connection_failing_since_utc IS NULL;
