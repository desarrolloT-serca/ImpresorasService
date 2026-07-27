-- SAP HANA — solo lectura. G1, prueba 1/3: ejecutar como sentencia única.
-- Resumen: cuántos jobs por status carecen del evento de su transición actual.
SELECT
    j."status",
    COUNT(*) AS jobs_sin_evento_de_su_status
FROM "printer_print_job" j
WHERE j."status" IN ('SpoolAccepted', 'PrintedConfirmed', 'PrintedUnknown', 'ErrorFinal', 'RetryScheduled')
  AND NOT EXISTS (
        SELECT 1
        FROM "printer_print_job_event" e
        WHERE e."job_id" = j."job_id"
          AND e."new_status" = j."status"
      )
GROUP BY j."status"
ORDER BY j."status"
