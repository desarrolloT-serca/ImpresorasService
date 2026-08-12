-- SAP HANA - esquema real de ZTEST_VICENTE_2, extraido el 2026-08-12.
-- Generado por scripts\extraer-ddl-hana.ps1 a partir del catalogo. No editar a mano:
-- si el esquema cambia en HANA, vuelve a ejecutar el script y commitea el resultado.
-- Reconstruido del catalogo: cubre columnas, tipos, defaults, PK y unicos.

CREATE COLUMN TABLE "ZTEST_VICENTE_2"."printer_print_job" (
    "job_id" VARBINARY(16) NOT NULL,
    "source_system" VARCHAR(50) NOT NULL,
    "external_job_id" VARCHAR(120) NOT NULL,
    "store_id" INTEGER NOT NULL,
    "document_type" VARCHAR(80) NOT NULL,
    "channel" VARCHAR(40) DEFAULT 'DEFAULT' NOT NULL,
    "pdf_blob" BLOB NOT NULL,
    "pdf_sha256" VARCHAR(64) NOT NULL,
    "status" VARCHAR(40) NOT NULL,
    "printer_id" INTEGER,
    "attempt_count" INTEGER DEFAULT 0 NOT NULL,
    "next_retry_at_utc" TIMESTAMP,
    "last_error_code" VARCHAR(60),
    "last_error_message" VARCHAR(1000),
    "correlation_id" VARBINARY(16) NOT NULL,
    "created_at_utc" TIMESTAMP NOT NULL,
    "updated_at_utc" TIMESTAMP NOT NULL,
    "row_version" BLOB
);

CREATE UNIQUE INDEX "ix_printer_print_job_source_external" ON "ZTEST_VICENTE_2"."printer_print_job" ("source_system", "external_job_id");
