-- SAP HANA — solo lectura. G1, prueba 2/3: ejecutar como sentencia única.
-- Rango temporal del hueco: si created_at_utc reciente aparece aquí, 2B (ventana ciega) no es aceptable.
SELECT
    j."status",
    MIN(j."created_at_utc") AS created_at_utc_min,
    MAX(j."created_at_utc") AS created_at_utc_max
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
