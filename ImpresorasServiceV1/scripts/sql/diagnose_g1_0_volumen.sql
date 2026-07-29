-- SAP HANA — solo lectura. Verifica que hay volumen suficiente para que g1/g2 sean concluyentes.
SELECT
    (SELECT COUNT(*) FROM "printer_print_job") AS total_jobs,
    (SELECT COUNT(*) FROM "printer_print_job_event") AS total_eventos,
    (SELECT COUNT(DISTINCT "status") FROM "printer_print_job") AS status_distintos_presentes
FROM DUMMY
