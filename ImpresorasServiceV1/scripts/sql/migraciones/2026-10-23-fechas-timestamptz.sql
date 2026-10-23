-- Migracion (PLAN B6.1): fechas de texto a timestamptz en las 21 columnas de fecha de las tablas
-- printer_*. Solo toca tablas de la aplicacion de impresion; nada fuera depende de ellas
-- (comprobado el 23/10/2026: sin vistas, claves ajenas, triggers ni funciones que las usen).
--
-- El texto guardado es "yyyy-MM-dd HH:mm:ss" en UTC, asi que se interpreta como UTC.
-- Sin nombre de esquema: se aplica en el search_path de la sesion. Con los servicios PARADOS y en
-- UNA transaccion:
--   psql ... -v ON_ERROR_STOP=1 -1 -c "SET search_path TO admon" -f 2026-10-23-fechas-timestamptz.sql

-- Guarda: relanzarla sobre columnas ya convertidas desplazaria las horas.
DO $$
BEGIN
    IF (SELECT data_type FROM information_schema.columns
        WHERE table_schema = current_schema() AND table_name = 'printer_store' AND column_name = 'created_at_utc')
       IS DISTINCT FROM 'character varying' THEN
        RAISE EXCEPTION 'Las fechas de % ya no son texto: esta migracion ya se aplico (o el search_path es otro).', current_schema();
    END IF;
END $$;

ALTER TABLE printer_store
    ALTER COLUMN created_at_utc TYPE timestamptz USING (NULLIF(created_at_utc, '')::timestamp AT TIME ZONE 'UTC'),
    ALTER COLUMN updated_at_utc TYPE timestamptz USING (NULLIF(updated_at_utc, '')::timestamp AT TIME ZONE 'UTC');

ALTER TABLE printer_printer
    ALTER COLUMN created_at_utc TYPE timestamptz USING (NULLIF(created_at_utc, '')::timestamp AT TIME ZONE 'UTC'),
    ALTER COLUMN updated_at_utc TYPE timestamptz USING (NULLIF(updated_at_utc, '')::timestamp AT TIME ZONE 'UTC'),
    ALTER COLUMN last_connection_check_at_utc TYPE timestamptz USING (NULLIF(last_connection_check_at_utc, '')::timestamp AT TIME ZONE 'UTC');

ALTER TABLE printer_routing_rule
    ALTER COLUMN valid_from_utc TYPE timestamptz USING (NULLIF(valid_from_utc, '')::timestamp AT TIME ZONE 'UTC'),
    ALTER COLUMN valid_to_utc TYPE timestamptz USING (NULLIF(valid_to_utc, '')::timestamp AT TIME ZONE 'UTC'),
    ALTER COLUMN created_at_utc TYPE timestamptz USING (NULLIF(created_at_utc, '')::timestamp AT TIME ZONE 'UTC'),
    ALTER COLUMN updated_at_utc TYPE timestamptz USING (NULLIF(updated_at_utc, '')::timestamp AT TIME ZONE 'UTC');

-- El DEFAULT de texto no se puede convertir: se quita, se cambia el tipo y se pone now().
ALTER TABLE printer_source_print_job ALTER COLUMN created_at_utc DROP DEFAULT;
ALTER TABLE printer_source_print_job
    ALTER COLUMN created_at_utc TYPE timestamptz USING (NULLIF(created_at_utc, '')::timestamp AT TIME ZONE 'UTC'),
    ALTER COLUMN claimed_until_utc TYPE timestamptz USING (NULLIF(claimed_until_utc, '')::timestamp AT TIME ZONE 'UTC');
ALTER TABLE printer_source_print_job ALTER COLUMN created_at_utc SET DEFAULT now();

ALTER TABLE printer_print_job
    ALTER COLUMN next_retry_at_utc TYPE timestamptz USING (NULLIF(next_retry_at_utc, '')::timestamp AT TIME ZONE 'UTC'),
    ALTER COLUMN created_at_utc TYPE timestamptz USING (NULLIF(created_at_utc, '')::timestamp AT TIME ZONE 'UTC'),
    ALTER COLUMN updated_at_utc TYPE timestamptz USING (NULLIF(updated_at_utc, '')::timestamp AT TIME ZONE 'UTC');

ALTER TABLE printer_print_job_event
    ALTER COLUMN occurred_at_utc TYPE timestamptz USING (NULLIF(occurred_at_utc, '')::timestamp AT TIME ZONE 'UTC');

ALTER TABLE printer_dashboard_threshold
    ALTER COLUMN updated_at_utc TYPE timestamptz USING (NULLIF(updated_at_utc, '')::timestamp AT TIME ZONE 'UTC');

ALTER TABLE printer_telegram_config
    ALTER COLUMN updated_at_utc TYPE timestamptz USING (NULLIF(updated_at_utc, '')::timestamp AT TIME ZONE 'UTC');

ALTER TABLE printer_telegram_chat
    ALTER COLUMN created_at_utc TYPE timestamptz USING (NULLIF(created_at_utc, '')::timestamp AT TIME ZONE 'UTC');

ALTER TABLE printer_alert_state
    ALTER COLUMN notified_at_utc TYPE timestamptz USING (NULLIF(notified_at_utc, '')::timestamp AT TIME ZONE 'UTC'),
    ALTER COLUMN checked_at_utc TYPE timestamptz USING (NULLIF(checked_at_utc, '')::timestamp AT TIME ZONE 'UTC');

ALTER TABLE printer_worker_lock
    ALTER COLUMN heartbeat_utc TYPE timestamptz USING (NULLIF(heartbeat_utc, '')::timestamp AT TIME ZONE 'UTC');
