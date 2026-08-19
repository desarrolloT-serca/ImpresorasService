-- SAP HANA - esquema real de ZTEST_VICENTE_2, extraido el 2026-08-12.
-- Generado por scripts\extraer-ddl-hana.ps1 a partir del catalogo. No editar a mano:
-- si el esquema cambia en HANA, vuelve a ejecutar el script y commitea el resultado.
-- Reconstruido del catalogo: cubre columnas, tipos, defaults, PK y unicos.

CREATE COLUMN TABLE "ZTEST_VICENTE_2"."printer_printer" (
    "printer_id" INTEGER NOT NULL,
    "printer_name" VARCHAR(120) NOT NULL,
    "spool_queue" VARCHAR(200) NOT NULL,
    "host" VARCHAR(255),
    "store_id" INTEGER NOT NULL,
    "is_active" BOOLEAN DEFAULT 1 NOT NULL,
    "capabilities_json" TEXT,
    "created_at_utc" TIMESTAMP NOT NULL,
    "updated_at_utc" TIMESTAMP NOT NULL,
    "connection_failures_streak" INTEGER DEFAULT 0 NOT NULL,
    "last_connection_ok" BOOLEAN,
    "last_connection_check_at_utc" TIMESTAMP,
    "last_connection_transport" VARCHAR(40),
    "last_connection_error" VARCHAR(400),
    "ipp_supported" TINYINT,
    PRIMARY KEY ("printer_id")
);

CREATE UNIQUE INDEX "ix_printer_printer_store_id_spool_queue" ON "ZTEST_VICENTE_2"."printer_printer" ("store_id", "spool_queue");
