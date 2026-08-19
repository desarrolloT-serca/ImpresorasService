-- SAP HANA - esquema real de ZTEST_VICENTE_2, extraido el 2026-08-12.
-- Generado por scripts\extraer-ddl-hana.ps1 a partir del catalogo. No editar a mano:
-- si el esquema cambia en HANA, vuelve a ejecutar el script y commitea el resultado.
-- Reconstruido del catalogo: cubre columnas, tipos, defaults, PK y unicos.

CREATE COLUMN TABLE "ZTEST_VICENTE_2"."printer_alert_state" (
    "store_id" INTEGER NOT NULL,
    "last_health" NVARCHAR(20) DEFAULT 'healthy' NOT NULL,
    "notified_health" NVARCHAR(20),
    "notified_at_utc" NVARCHAR(26),
    "checked_at_utc" NVARCHAR(26) NOT NULL,
    PRIMARY KEY ("store_id")
);
