-- Permite liberar el PDF de los trabajos ya cerrados (PdfRetentionBackgroundService).
-- El corte es ESTADO TERMINAL + ventana de retencion (PdfRetention:RetentionDays), no SpoolAccepted:
-- limpiar en SpoolAccepted romperia la reimpresion manual desde PrintedUnknown, que pasa por ese
-- estado y devolveria PDF_MISSING. Se conservan la fila, pdf_sha256 y el resto de metadatos.
-- Sin este ALTER, PdfRetention falla contra la restriccion NOT NULL y solo deja un warning.
-- Sustituir <SCHEMA> por el esquema HANA del entorno antes de ejecutar.
ALTER TABLE "<SCHEMA>"."printer_print_job" ALTER ("pdf_blob" BLOB NULL);
