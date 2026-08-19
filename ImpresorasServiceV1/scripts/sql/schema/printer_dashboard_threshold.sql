-- SAP HANA - esquema real de ZTEST_VICENTE_2, extraido el 2026-08-12.
-- Generado por scripts\extraer-ddl-hana.ps1 a partir del catalogo. No editar a mano:
-- si el esquema cambia en HANA, vuelve a ejecutar el script y commitea el resultado.
-- Reconstruido del catalogo: cubre columnas, tipos, defaults, PK y unicos.

CREATE COLUMN TABLE "ZTEST_VICENTE_2"."printer_dashboard_threshold" (
    "id" INTEGER NOT NULL,
    "warning_queue_min" INTEGER DEFAULT 10 NOT NULL,
    "critical_queue_min" INTEGER DEFAULT 30 NOT NULL,
    "queue_warning_severity" VARCHAR(32) DEFAULT 'warning' NOT NULL,
    "queue_critical_severity" VARCHAR(32) DEFAULT 'critical' NOT NULL,
    "warning_failed_without_retry_min" INTEGER DEFAULT 1 NOT NULL,
    "critical_failed_without_retry_min" INTEGER DEFAULT 5 NOT NULL,
    "failed_warning_severity" VARCHAR(32) DEFAULT 'warning' NOT NULL,
    "failed_critical_severity" VARCHAR(32) DEFAULT 'critical' NOT NULL,
    "missing_host_min" INTEGER DEFAULT 1 NOT NULL,
    "missing_host_severity" VARCHAR(32) DEFAULT 'warning' NOT NULL,
    "conn_warning_failures_min" INTEGER DEFAULT 2 NOT NULL,
    "conn_critical_failures_min" INTEGER DEFAULT 3 NOT NULL,
    "conn_warning_severity" VARCHAR(32) DEFAULT 'warning' NOT NULL,
    "conn_critical_severity" VARCHAR(32) DEFAULT 'critical' NOT NULL,
    "updated_at_utc" TIMESTAMP NOT NULL,
    PRIMARY KEY ("id")
);
