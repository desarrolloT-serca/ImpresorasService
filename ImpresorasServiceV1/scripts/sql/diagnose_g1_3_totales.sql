-- SAP HANA — solo lectura. G1, prueba 3/3: ejecutar como sentencia única.
-- Denominador: total de jobs impresos/fallidos, para calcular el % del hueco.
SELECT
    j."status",
    COUNT(*) AS total_jobs
FROM "printer_print_job" j
WHERE j."status" IN ('SpoolAccepted', 'PrintedConfirmed', 'PrintedUnknown', 'ErrorFinal', 'RetryScheduled')
GROUP BY j."status"
ORDER BY j."status"
