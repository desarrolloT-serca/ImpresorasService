# Plan de trabajo — ImpresorasService

**Fuente de verdad única del trabajo pendiente.** Cualquier otro roadmap o auditoría del repositorio
es evidencia o historia, no una lista de tareas.

**Estado a 2026-08-19**, sobre `main`. Suites: `dotnet test` 154/154, `php artisan test`
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
| B0.1 | Aplicar `scripts/sql/migrate_pdf_blob_nullable.sql` en HANA (**dos** `ALTER`: `printer_print_job` y `printer_source_print_job`) | La retención entera. El código está listo desde el 19/08; sin este DDL no libera nada, `pdf_blob` es `BLOB NOT NULL` y el barrido solo deja un warning |
| B0.2 | Ejecutar `scripts/extraer-ddl-hana.ps1` contra HANA y commitear el resultado | Confirma las `PRIMARY KEY` (hoy reconstruidas desde `_inventario.sql`, no desde una ejecución real) y añade `printer_worker_lock`, creado después de la extracción del 12/08 |
| B0.3 | **Gate I-1**: comprobar contra una impresora real del parque si responde `Get-Job-Attributes` por IPP | B3. Sin este dato, la confirmación por trabajo no se puede ni planificar |
| B0.4 | Aplicar `scripts/sql/migrate_user_revocation.sql` en HANA | La revocación de acceso (B2.2). Sin las columnas, la Api falla al consultar `printer_user`. Ambas llevan `DEFAULT`, así que aplicarlo no cierra ninguna sesión abierta |

> **B0.1 y B0.4 necesitan al DBA.** Intentados el 19/08/2026 con la conexión del servicio:
> los cuatro `ALTER` fallan con **error 258 (insufficient privilege)**. `IMPRESION` tiene
> SELECT/INSERT/UPDATE/DELETE/INDEX tabla a tabla sobre `ZTEST_VICENTE_2`, pero ningún `ALTER` —
> mismo muro que con `printer_worker_lock` en H-14. Nada quedó aplicado a medias (verificado).
> O los ejecuta el dueño del esquema, o hace falta `GRANT ALTER` sobre las tres tablas.
>
> **B0.4 es bloqueante para desplegar.** Sin `is_active` y `token_version`, la Api falla al
> consultar `printer_user` y el login deja de funcionar: el DDL va antes que el binario.

> B0.3 es una prueba de media hora con una impresora y `curl`. Es la que más decisiones destraba.

### ~~B1 · Ejecutable ya — sin decisión previa~~ ✅ HECHO 2026-08-19

| # | Acción | Hallazgo | Estado |
|---|---|---|---|
| ~~B1.1~~ | Retención extendida a `printer_source_print_job` | H-15 | ✅ `PdfRetention.ReleaseExpiredSourcePdfsAsync`, corte `is_processed` + antigüedad. Una fila sin procesar no se toca nunca (test). **Falta B0.1**, que ahora cubre las dos tablas |
| ~~B1.2~~ | Umbral del lease desde el reloj de la base de datos | H-10 (a) | ✅ `CURRENT_UTCTIMESTAMP` cuando el proveedor es HANA, con fallback al reloj local y traza. En SQLite sigue el `TimeProvider`, que es lo que hace deterministas los tests del relevo. **Sin verificar contra HANA** |
| ~~B1.3~~ | Job .NET del CI en `windows-latest` | H-11 (parcial) | ✅ El CI deja de correr todo con `NoOpPrintSpooler`. **No cierra H-11**: sigue sin haber pruebas del spooler en sí (ver B5) |

### ~~B2 · Necesitan una decisión tuya antes de tocar código~~ ✅ HECHO 2026-08-19

| # | Trabajo | Decisión tomada | Estado |
|---|---|---|---|
| ~~B2.1~~ | Plazo de retención del PDF | **90 días** | ✅ `PdfRetention:Enabled=true`, `RetentionDays=90`. **No libera nada hasta B0.1** |
| ~~B2.2~~ | Invalidación de acceso (Fase 5) | **`IsActive` + `TokenVersion`** | ✅ Comprobados en cada petición (`UserRevocationValidator`). Requiere el DDL de B0.4. Expiración fija en 8 h: descartada por ahora |
| ~~B2.3~~ | Reintentos de impresión | **Quitar el 90 sobrante** | ✅ `BackoffSeconds=[15,30,60]`. Sin cambio de comportamiento |

> **UI incluida.** `/usuarios` muestra el estado y ofrece activar/desactivar por fila
> (`POST /usuarios/{id}/activacion`). Desactivar pide confirmación; reactivar no, porque no rompe nada.

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
| ~~Plazo de conservación del PDF~~ | Protección de datos | ✅ 90 días (19/08/2026) |
| ~~Alcance de la revocación de acceso~~ | Producto / seguridad | ✅ `IsActive` + `TokenVersion` (19/08/2026) |
| ¿El parque soporta IPP por trabajo? | Operaciones (medición) | B3 completo |
| ¿Se revocó el token de Telegram con BotFather? | Operaciones | Nada de código. El literal se queda en `auditoriaimpresoras.md` por decisión tomada el 19/08/2026 |

---

## 4. Definición de hecho

- Un test que falla antes del cambio y pasa después.
- `dotnet test`, `php artisan test` y `npm run build` verdes según el componente tocado.
- Si toca SQL, concurrencia o tipos: verificado contra HANA, no solo contra SQLite.
- El hallazgo correspondiente actualizado en `auditoria/revision-garantias-2026-08-12.md`, y la fila de este plan tachada.
