-- SAP HANA - esquema real de ZTEST_VICENTE_2, extraido el 2026-08-12.
-- Generado por scripts\extraer-ddl-hana.ps1 a partir del catalogo. No editar a mano:
-- si el esquema cambia en HANA, vuelve a ejecutar el script y commitea el resultado.
-- Reconstruido del catalogo: cubre columnas, tipos, defaults, PK y unicos.

CREATE COLUMN TABLE "ZTEST_VICENTE_2"."printer_source_print_job" (
    "id" BIGINT NOT NULL,
    "source_system" VARCHAR(50) NOT NULL,
    "external_job_id" VARCHAR(120) NOT NULL,
    "store_id" INTEGER NOT NULL,
    "document_type" VARCHAR(80) NOT NULL,
    "channel" VARCHAR(40),
    "pdf_blob" BLOB NOT NULL,
    "created_at_utc" TIMESTAMP NOT NULL,
    "is_processed" BOOLEAN DEFAULT 0 NOT NULL,
    "claimed_by" VARCHAR(200),
    "claimed_until_utc" TIMESTAMP,
    "claim_token" VARCHAR(64),
    PRIMARY KEY ("id")
);
