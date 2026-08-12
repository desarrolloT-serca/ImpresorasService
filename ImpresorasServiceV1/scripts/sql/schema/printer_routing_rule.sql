-- SAP HANA - esquema real de ZTEST_VICENTE_2, extraido el 2026-08-12.
-- Generado por scripts\extraer-ddl-hana.ps1 a partir del catalogo. No editar a mano:
-- si el esquema cambia en HANA, vuelve a ejecutar el script y commitea el resultado.
-- Reconstruido del catalogo: cubre columnas, tipos, defaults, PK y unicos.

CREATE COLUMN TABLE "ZTEST_VICENTE_2"."printer_routing_rule" (
    "rule_id" INTEGER NOT NULL,
    "priority" INTEGER NOT NULL,
    "store_id" INTEGER,
    "document_type" VARCHAR(80),
    "channel" VARCHAR(40),
    "printer_id" INTEGER NOT NULL,
    "is_active" BOOLEAN DEFAULT 1 NOT NULL,
    "valid_from_utc" TIMESTAMP NOT NULL,
    "valid_to_utc" TIMESTAMP,
    "created_by" VARCHAR(120) NOT NULL,
    "created_at_utc" TIMESTAMP NOT NULL,
    "updated_at_utc" TIMESTAMP NOT NULL
);
