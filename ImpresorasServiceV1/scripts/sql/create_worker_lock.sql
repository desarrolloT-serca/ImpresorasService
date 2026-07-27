-- SAP HANA
-- Objetivo: tabla singleton para el lock de instancia única del Worker (G4.1).
-- Ver docs/roadmapimpresoras.md Fase 2.1 y docs/roadmap-integral-2026-07-21.md G4.1.
-- El Worker crea la fila id=1 solo si falta al arrancar (WorkerLockCoordinator), pero sembrarla
-- aquí evita depender de esa ruta en el primer despliegue.

CREATE COLUMN TABLE printer_worker_lock (
    id INTEGER NOT NULL PRIMARY KEY,
    holder NVARCHAR(200),
    heartbeat_utc TIMESTAMP NOT NULL
);

INSERT INTO printer_worker_lock (id, holder, heartbeat_utc)
SELECT 1, NULL, '1970-01-01 00:00:00'
FROM DUMMY
WHERE NOT EXISTS (SELECT 1 FROM printer_worker_lock WHERE id = 1);
