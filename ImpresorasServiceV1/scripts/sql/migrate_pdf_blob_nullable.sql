-- Permite liberar el PDF ya conservado sin proposito (PdfRetentionBackgroundService).
--
-- printer_print_job: el corte es ESTADO TERMINAL + ventana de retencion
-- (PdfRetention:RetentionDays), no SpoolAccepted: limpiar en SpoolAccepted romperia la
-- reimpresion manual desde PrintedUnknown, que pasa por ese estado y devolveria PDF_MISSING.
--
-- printer_source_print_job: el corte es is_processed = TRUE + la misma ventana. Es donde esta
-- el grueso del volumen (medicion del 12/08/2026: el 100 % de las filas conservaba su PDF, todas
-- ya procesadas). Una fila sin procesar nunca se toca: su PDF es el unico ejemplar que existe.
--
-- En ambos casos se conservan la fila y todos los metadatos; en printer_print_job tambien
-- pdf_sha256. Sin estos ALTER, la retencion falla contra la restriccion NOT NULL y solo deja un
-- warning en el log.
-- Sustituir <SCHEMA> por el esquema HANA del entorno antes de ejecutar.
ALTER TABLE "<SCHEMA>"."printer_print_job" ALTER ("pdf_blob" BLOB NULL);
ALTER TABLE "<SCHEMA>"."printer_source_print_job" ALTER ("pdf_blob" BLOB NULL);
