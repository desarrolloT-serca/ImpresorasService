-- SAP HANA — solo lectura. G2, prueba 1/3: ejecutar como sentencia única.
-- Tipo de columna declarado en el catálogo de HANA (TIMESTAMP vs NVARCHAR).
SELECT
    TABLE_NAME,
    COLUMN_NAME,
    DATA_TYPE_NAME,
    LENGTH,
    SCALE
FROM SYS.TABLE_COLUMNS
WHERE SCHEMA_NAME = CURRENT_SCHEMA
  AND TABLE_NAME IN ('printer_print_job', 'printer_print_job_event')
  AND COLUMN_NAME IN ('created_at_utc', 'updated_at_utc', 'next_retry_at_utc', 'occurred_at_utc')
ORDER BY TABLE_NAME, COLUMN_NAME
