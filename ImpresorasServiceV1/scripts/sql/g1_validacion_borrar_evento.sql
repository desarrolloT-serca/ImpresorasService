-- SAP HANA — control positivo de la query G1 (diagnose_g1_1_resumen.sql).
-- Sustituye 'TU-EXTERNAL-JOB-ID' por el external_job_id de un job YA impreso/fallido
-- creado por la app (vía SourcePrintJobsController/test). Borra su evento más reciente
-- para simular el hueco legacy, sin tocar binarios a mano.
--
-- Después de ejecutar esto, vuelve a correr diagnose_g1_1_resumen.sql: debe pasar de 0 a 1
-- para el status de ese job. Si no cambia, la query tiene un bug real (no un problema de datos).

DELETE FROM "printer_print_job_event"
WHERE "event_id" = (
    SELECT TOP 1 e."event_id"
    FROM "printer_print_job_event" e
    JOIN "printer_print_job" j ON j."job_id" = e."job_id"
    WHERE j."external_job_id" = 'TU-EXTERNAL-JOB-ID'
      AND e."new_status" = j."status"
    ORDER BY e."occurred_at_utc" DESC
)
