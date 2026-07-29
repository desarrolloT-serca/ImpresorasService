-- SAP HANA — control positivo de G2 prueba #5 (limite "today" en Europe/Madrid).
-- Sustituye 'TU-EXTERNAL-JOB-ID' por el external_job_id de un job creado por la app.
-- Lo deja como "creado 23:58 Madrid de AYER" (CEST = UTC+2 en julio -> 21:58 UTC).
--
-- Después de ejecutar esto, llama a GET /api/dashboard/overview?window=today (hoy, después
-- de medianoche Madrid) y confirma que ese job NO aparece en "received" ni en printed/failed
-- de la ventana "today" -- si aparece, el filtro de timezone está mal.
-- Ajusta las fechas si hoy no es 2026-07-27 o si la fecha cae fuera de horario CEST.

UPDATE "printer_print_job"
SET "created_at_utc" = '2026-07-26 21:58:00', "updated_at_utc" = '2026-07-26 21:58:00'
WHERE "external_job_id" = 'TU-EXTERNAL-JOB-ID'
