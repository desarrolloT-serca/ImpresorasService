-- SAP HANA — solo lectura. G2, prueba 3/3: ejecutar como sentencia única.
-- Muestra de valores no canónicos, para ver qué formatos legacy hay realmente.
SELECT DISTINCT "created_at_utc"
FROM "printer_print_job"
WHERE LENGTH("created_at_utc") <> 19 OR "created_at_utc" LIKE '%/%'
LIMIT 20
