-- SAP HANA
-- Objetivo: tabla singleton para el lock de instancia única del Worker (G4.1).
-- Ver docs/roadmapimpresoras.md Fase 2.1 y docs/roadmap-integral-2026-07-21.md G4.1.
--
-- IMPORTANTE — nombres en minúsculas y entrecomillados.
-- El resto del esquema (printer_print_job, printer_printer, ...) está creado en minúsculas, y HANA
-- distingue mayúsculas cuando el identificador va entre comillas dobles. Sin las comillas, HANA
-- crearía PRINTER_WORKER_LOCK en mayúsculas y el Worker —que consulta "printer_worker_lock"—
-- seguiría sin encontrarla: la tabla existiría y el problema no estaría resuelto.
--
-- Sustituya <ESQUEMA> por el esquema de la aplicación (el "Current Schema" de la cadena de
-- conexión; aparece en el log de arranque del Worker).
--
-- Verificación tras ejecutar, debe devolver una fila:
--   SELECT * FROM "<ESQUEMA>"."printer_worker_lock";

CREATE COLUMN TABLE "<ESQUEMA>"."printer_worker_lock" (
    "id"            INTEGER NOT NULL PRIMARY KEY,
    "holder"        NVARCHAR(200),
    "heartbeat_utc" TIMESTAMP NOT NULL
);

-- El Worker crea la fila id=1 solo si falta al arrancar (WorkerLockCoordinator), pero sembrarla
-- aquí evita depender de esa ruta en el primer despliegue.
INSERT INTO "<ESQUEMA>"."printer_worker_lock" ("id", "holder", "heartbeat_utc")
SELECT 1, NULL, '1970-01-01 00:00:00'
FROM DUMMY
WHERE NOT EXISTS (SELECT 1 FROM "<ESQUEMA>"."printer_worker_lock" WHERE "id" = 1);
