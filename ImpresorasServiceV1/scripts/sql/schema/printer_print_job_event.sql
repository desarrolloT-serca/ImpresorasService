-- SAP HANA - esquema real de ZTEST_VICENTE_2, extraido el 2026-08-12.
-- Generado por scripts\extraer-ddl-hana.ps1 a partir del catalogo. No editar a mano:
-- si el esquema cambia en HANA, vuelve a ejecutar el script y commitea el resultado.
-- Reconstruido del catalogo: cubre columnas, tipos, defaults, PK y unicos.

CREATE COLUMN TABLE "ZTEST_VICENTE_2"."printer_print_job_event" (
    "event_id" BIGINT NOT NULL,
    "job_id" VARBINARY(16) NOT NULL,
    "event_type" VARCHAR(60) NOT NULL,
    "old_status" VARCHAR(40),
    "new_status" VARCHAR(40),
    "error_code" VARCHAR(60),
    "message" VARCHAR(1000),
    "actor_type" VARCHAR(30) NOT NULL,
    "actor_id" VARCHAR(120),
    "metadata_json" TEXT,
    "occurred_at_utc" TIMESTAMP NOT NULL,
    PRIMARY KEY ("event_id")
);
