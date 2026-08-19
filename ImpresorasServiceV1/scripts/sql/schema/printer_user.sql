-- SAP HANA - esquema real de ZTEST_VICENTE_2, extraido el 2026-08-12.
-- Generado por scripts\extraer-ddl-hana.ps1 a partir del catalogo. No editar a mano:
-- si el esquema cambia en HANA, vuelve a ejecutar el script y commitea el resultado.
-- Reconstruido del catalogo: cubre columnas, tipos, defaults, PK y unicos.

CREATE COLUMN TABLE "ZTEST_VICENTE_2"."printer_user" (
    "user_id" INTEGER NOT NULL,
    "login" VARCHAR(80) NOT NULL,
    "password_hash" VARCHAR(256) NOT NULL,
    "role" VARCHAR(40) NOT NULL,
    "store_id" INTEGER,
    "display_name" VARCHAR(120),
    PRIMARY KEY ("user_id")
);

CREATE UNIQUE INDEX "ix_printer_user_login" ON "ZTEST_VICENTE_2"."printer_user" ("login");
