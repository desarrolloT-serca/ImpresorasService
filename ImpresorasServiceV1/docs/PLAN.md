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
| ~~B0.1~~ | ~~`migrate_pdf_blob_nullable.sql`~~ | ✅ **Aplicado el 19/08/2026.** `pdf_blob` es nullable en las dos tablas; la retención de 90 días ya puede liberar |
| B0.2 | Ejecutar `scripts/extraer-ddl-hana.ps1` contra HANA y commitear el resultado | Confirma las `PRIMARY KEY` (hoy reconstruidas desde `_inventario.sql`, no desde una ejecución real) y añade `printer_worker_lock`, creado después de la extracción del 12/08 |
| B0.3 | **Gate I-1**: comprobar contra una impresora real del parque si responde `Get-Job-Attributes` por IPP | B3. Sin este dato, la confirmación por trabajo no se puede ni planificar |
| ~~B0.4~~ | ~~`migrate_user_revocation.sql`~~ | ✅ **Aplicado el 19/08/2026.** `is_active` y `token_version` existen; los 3 usuarios quedaron activos y en versión 0, sin cerrar ninguna sesión. **`main` vuelve a ser desplegable** |

> **Los cuatro `ALTER` quedaron aplicados el 19/08/2026** y verificados contra HANA. Nota para la
> próxima vez: `IMPRESION` no puede hacer `ALTER` (error 258, mismo muro que en H-14), así que
> todo DDL lo ejecuta el dueño del esquema. Y el `DEFAULT` de un `BOOLEAN` se declara `TRUE`,
> no `1` — el catálogo devuelve luego `default=[1]`, que despista al leerlo.

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

### ~~B4 · El núcleo — el bloque grande~~ ✅ HECHO 2026-08-19

| # | Trabajo | Hallazgo | Estado |
|---|---|---|---|
| ~~B4.1~~ | Claim atómico del trabajo antes de enviarlo al spooler | H-03, Fase 2.2 | ✅ Todas las transiciones del Worker por `TryTransitionAsync` (UPDATE con el estado en el WHERE). Test con dos contextos: dos Workers, un solo envío |
| ~~B4.2~~ | `cancel` y `route` condicionados al estado | Fase 2.5 | ✅ 0 filas → 409 `PrintJobStateConflictException`. Ya no se responde `Cancelled` mientras sale el papel |
| ~~B4.3~~ | Retirar `RowVersion` como defensa | H-03 | ✅ Fuera `BumpPrintJobRowVersionsForConcurrency`. La columna se queda (sin DDL) con un comentario de por qué no vuelve |

> **Cambio de contrato que conviene recordar.** Las escrituras van por `ExecuteUpdate`, que no
> refresca las entidades ya seguidas por el contexto. En producción da igual —cada ciclo abre su
> propio scope—, pero cualquier test que reutilice el contexto tiene que soltar el seguimiento
> antes de comprobar, o leerá la copia de antes del UPDATE.
>
> Queda una ventana sin cubrir por tests: reclamar entre la lectura y el UPDATE devuelve 409, y
> provocarlo exige interponerse en el comando SQL. El caso frecuente (ya reclamado al leer) sí
> está fijado.

### B5 · Higiene ✅ HECHO 2026-08-19 (parcial)

- ~~`TelegramController` inyecta `ITelegramNotifier` y no lo usa~~ ✅ retirado.
- **AUD-14** ✅ *a medias, sin DDL*: una alerta que no acepta ningún chat ya no se pierde — se
  deshace el avance de `NotifiedHealth` y el próximo ciclo la reintenta. **No es un outbox**: no
  hay estado por destinatario ni backoff, así que con Telegram caído se reintenta cada
  `CheckIntervalMinutes` hasta que entre. La granularidad por chat sí pediría tabla nueva.
- **H-11** ✅ *el primer trozo*: `WindowsPrintSpooler.ClassifyFailedExit` extraído y cubierto —
  es la decisión que determina si un fallo se reintenta, y cada reintento es otro envío al
  spooler. **Sigue sin cubrirse** el lanzamiento del proceso, IPP y HANA.

### Lo que queda sin cobertura (deuda reconocida)

| Qué | Por qué no está |
|---|---|
| `StoreHealthAlertBackgroundService` | Ningún test. La lógica de reintento de alerta se añadió sin cubrir: `RunOnceAsync` es privado y hacerlo testeable es un refactor mayor que el cambio |
| Lanzamiento real del proceso de impresión, IPP, provider HANA | Necesitan spooler, impresora o base de datos reales. Es el fondo de H-11 |
| Reclamar entre la lectura y el UPDATE (409 de `cancel`) | Exige interponerse en el comando SQL |

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
