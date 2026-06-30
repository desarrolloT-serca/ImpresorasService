-- M-2: pdf_blob se limpia al pasar a SpoolAccepted para liberar espacio en BD.
-- Requiere que la columna admita NULL (antes era NOT NULL).
-- Sustituir <SCHEMA> por el esquema HANA del entorno antes de ejecutar.
ALTER TABLE "<SCHEMA>"."printer_print_job" ALTER ("pdf_blob" BLOB NULL);
