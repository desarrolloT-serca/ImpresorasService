-- SAP HANA - esquema real de ZTEST_VICENTE_2, extraido el 2026-08-12.
-- Generado por scripts\extraer-ddl-hana.ps1 a partir del catalogo. No editar a mano:
-- si el esquema cambia en HANA, vuelve a ejecutar el script y commitea el resultado.
-- Reconstruido del catalogo: cubre columnas, tipos, defaults, PK y unicos.

CREATE COLUMN TABLE "ZTEST_VICENTE_2"."printer_telegram_config" (
    "id" INTEGER NOT NULL,
    "min_severity" NVARCHAR(20) DEFAULT 'critical' NOT NULL,
    "notify_on_recovery" TINYINT DEFAULT 1 NOT NULL,
    "check_interval_minutes" INTEGER DEFAULT 5 NOT NULL,
    "updated_at_utc" NVARCHAR(26) NOT NULL
);
