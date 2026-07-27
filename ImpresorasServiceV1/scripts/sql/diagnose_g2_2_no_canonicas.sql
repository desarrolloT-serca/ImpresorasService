-- SAP HANA — solo lectura. G2, prueba 2/3: ejecutar como sentencia única.
-- Cuántas filas NO están en formato canónico 'yyyy-MM-dd HH:mm:ss' (19 chars, sin '/'):
-- esas filas ordenarían mal en una comparación SQL >= / <= sobre la columna string.
SELECT
    'printer_print_job.created_at_utc' AS columna,
    COUNT(*) AS filas_no_canonicas
FROM "printer_print_job"
WHERE LENGTH("created_at_utc") <> 19 OR "created_at_utc" LIKE '%/%'
UNION ALL
SELECT
    'printer_print_job.updated_at_utc',
    COUNT(*)
FROM "printer_print_job"
WHERE LENGTH("updated_at_utc") <> 19 OR "updated_at_utc" LIKE '%/%'
UNION ALL
SELECT
    'printer_print_job_event.occurred_at_utc',
    COUNT(*)
FROM "printer_print_job_event"
WHERE LENGTH("occurred_at_utc") <> 19 OR "occurred_at_utc" LIKE '%/%'
