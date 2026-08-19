# Plan de trabajo — ImpresorasService

**Fuente de verdad única del trabajo pendiente.** Cualquier otro roadmap o auditoría del repositorio
es evidencia o historia, no una lista de tareas.

**Estado a 2026-08-19**, sobre `main` @ `7b7a7af`. Suites: `dotnet test` 153/153, `php artisan test`
13/13, `npm run build` limpio, `composer audit` limpio.

---

## 1. Qué documento manda

El repositorio acumula cinco roadmaps y ocho documentos de auditoría solapados. Esta tabla dice
cuál sigue vivo y para qué; el resto no hay que leerlo para decidir en qué trabajar.

| Documento | Rol |
|---|---|
| **`PLAN.md`** (este) | **Qué hacer y en qué orden.** Lo único que hay que leer para elegir tarea |
| `auditoria/revision-garantias-2026-08-12.md` | Evidencia de los hallazgos H-01…H-15, con su estado real. La referencia técnica cuando toque uno |
| `roadmapimpresoras.md` | Especificación de las fases 2.2, 2.5, 3.4, 5.x y 6.2. **Contiene el diseño, no el estado** — no lleva marcas de hecho/pendiente |
| `roadmap-integral-2026-07-21.md` | Cerrado salvo G2 (#2-6), que necesita tráfico real. Se consulta, no se planifica desde él |
| `auditoria/plan-remediacion.md` | Origen de los AUD-xx. Casi todo absorbido aquí |
| `contrato-kpi-dashboard.md` | Contrato vivo de los KPI. No es un roadmap |
| `ejecutable.md`, `DESPLIEGUE-PHP.md`, `GUIA-ESTILO-DBX-UI.md`, `SMOKE-TESTS-PHP.md` | Guías operativas vivas |
| `TELEGRAM_AND_IPP_ROADMAP.md` | **Completado.** IPP y Telegram están implementados y su DDL aplicado |
| `roadmap-kpi-dashboard.md`, `prompt-roadmap-kpi-dashboard.md` | Cerrados (trabajo de KPI ya entregado) |
| `auditoriaimpresoras.md`, `auditoria-integral-2026-07-21.md`, `PLAN-REMEDIACION-SPRINTS.md`, `cleanup-*.md`, `auditoria/` (los 6 restantes) | Histórico y evidencia. No planificar desde aquí |

---

## 2. El trabajo, por dependencia

Cinco bloques. **B0 desbloquea a los demás y no es código.** Dentro de cada bloque el orden da igual.

### B0 · Operativo — desbloquea el resto

| # | Acción | Desbloquea |
|---|---|---|
| B0.1 | Aplicar `scripts/sql/migrate_pdf_blob_nullable.sql` en HANA | B1.1. Sin esto `PdfRetention` no libera nada aunque se active: `pdf_blob` es `BLOB NOT NULL` y el barrido solo deja un warning |
| B0.2 | Ejecutar `scripts/extraer-ddl-hana.ps1` contra HANA y commitear el resultado | Confirma las `PRIMARY KEY` (hoy reconstruidas desde `_inventario.sql`, no desde una ejecución real) y añade `printer_worker_lock`, creado después de la extracción del 12/08 |
| B0.3 | **Gate I-1**: comprobar contra una impresora real del parque si responde `Get-Job-Attributes` por IPP | B3. Sin este dato, la confirmación por trabajo no se puede ni planificar |

> B0.3 es una prueba de media hora con una impresora y `curl`. Es la que más decisiones destraba.

### B1 · Ejecutable ya — sin decisión previa

| # | Acción | Hallazgo | Criterio de cierre |
|---|---|---|---|
| B1.1 | Extender la retención a `printer_source_print_job` | H-15 | Una fila procesada de hace N+1 días conserva metadatos y no el PDF. **Ojo:** su `pdf_blob` también es `NOT NULL`, así que B0.1 debe cubrir las dos tablas |
| B1.2 | Derivar el umbral del lease del reloj de HANA (`CURRENT_UTCTIMESTAMP`), no del proceso | H-10 (a) | Dos Workers con relojes desfasados no se solapan. Hoy una instancia adelantada se lleva un lease vivo |
| B1.3 | Mover el job .NET del CI a `windows-latest` | H-11 (parcial) | El CI deja de ejercitar solo la rama `NoOpPrintSpooler`. **No cierra H-11**: sigue sin haber pruebas del spooler en sí |

### B2 · Necesitan una decisión tuya antes de tocar código

| # | Trabajo | Decisión que falta |
|---|---|---|
| B2.1 | Plazo de retención del PDF | Cuántos días. Es de protección de datos, no técnica. Hoy `RetentionDays = 30` y `Enabled = false` |
| B2.2 | Invalidación de acceso (Fase 5) | Alcance: ¿solo `User.IsActive`? ¿además `TokenVersion` para invalidar al cambiar contraseña? ¿expiración < 8 h? Hoy borrar un usuario no le quita el acceso hasta 8 horas después |
| B2.3 | Reintentos de impresión | Si `MaxAttempts` sube a 5 para consumir los cuatro backoffs, o el cuarto valor sobra. Hoy con `MaxAttempts = 4` se usan 15/30/60 y el 90 nunca |

### B3 · Bloqueado por el gate I-1

| # | Trabajo | Hallazgo |
|---|---|---|
| B3.1 | Confirmación IPP **por trabajo** en vez de por `printer-state` | H-01, Fase 3.4 |
| B3.2 | Si I-1 sale negativo: renombrar `PrintedConfirmed` y sus etiquetas para que no afirmen lo que no comprueban | H-01, Fase 3.5 |

Hoy una lectura de `printer-state` marca `PrintedConfirmed` a **todo el lote** de esa impresora, PHP
lo etiqueta «Impreso» y el KPI `printed` incluye además `SpoolAccepted` y `PrintedUnknown`. Sale una
de las dos: o se confirma de verdad, o se deja de afirmar.

### B4 · El núcleo — el bloque grande

| # | Trabajo | Hallazgo |
|---|---|---|
| B4.1 | Claim atómico: `UPDATE … WHERE job_id = @id AND status IN ('Routed','RetryScheduled')`, seguir solo si rowcount = 1 | H-03, Fase 2.2 |
| B4.2 | `cancel` y `route` por UPDATE condicional de estado; si afecta 0 filas, responder conflicto | Fase 2.5 |
| B4.3 | Retirar `RowVersion` como defensa y su docstring, que afirma un control de concurrencia que no existe | H-03 |

Van juntos: los tres son el mismo patrón. Hoy cancelar un trabajo que el Worker acaba de reclamar
responde `Cancelled` y el papel sale igual.

### B5 · Cuando no haya nada mejor

- **AUD-14**: outbox de Telegram con estado de entrega y reintentos. El log ya es honesto; falta la persistencia.
- `TelegramController` inyecta `ITelegramNotifier` y no lo usa nunca — borrar.
- Cobertura real de `WindowsPrintSpooler` e IPP (lo que de verdad cierra H-11).

---

## 3. Decisiones abiertas, con propietario

| Decisión | Quién | Bloquea |
|---|---|---|
| Plazo de conservación del PDF | Protección de datos | B2.1 |
| Alcance de la revocación de acceso | Producto / seguridad | B2.2 |
| ¿El parque soporta IPP por trabajo? | Operaciones (medición) | B3 completo |
| ¿Se revocó el token de Telegram con BotFather? | Operaciones | Nada de código. El literal se queda en `auditoriaimpresoras.md` por decisión tomada el 19/08/2026 |

---

## 4. Definición de hecho

- Un test que falla antes del cambio y pasa después.
- `dotnet test`, `php artisan test` y `npm run build` verdes según el componente tocado.
- Si toca SQL, concurrencia o tipos: verificado contra HANA, no solo contra SQLite.
- El hallazgo correspondiente actualizado en `auditoria/revision-garantias-2026-08-12.md`, y la fila de este plan tachada.
