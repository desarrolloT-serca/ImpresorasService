# Plan de trabajo — ImpresorasService

**Fuente de verdad única del trabajo pendiente.**

**Estado a 2026-10-23**, rama `feat/postgres`. `dotnet test` 181/181.

El 23/10/2026 la cola pasó de SAP HANA a PostgreSQL y el origen de los trabajos pasó de SAP al ERP
propio. La documentación y los scripts de la etapa HANA se retiraron del repositorio; siguen en el
historial de git (hasta el commit `602018f`).

---

## 1. Documentos vivos

| Documento | Para qué |
|---|---|
| **`PLAN.md`** (este) | Qué hacer y en qué orden |
| `contrato-kpi-dashboard.md` | Contrato de los KPI del dashboard |
| `gate-i1-ipp-por-trabajo.md` | Medición del parque: por qué no se puede confirmar la impresión por trabajo |
| `ejecutable.md`, `DESPLIEGUE-PHP.md`, `SMOKE-TESTS-PHP.md`, `GUIA-ESTILO-DBX-UI.md`, `FRONTEND-WEB-PHP-GIT.md` | Guías operativas |
| `RESUMEN-PROYECTO.md` | Orientación rápida en el código |

---

## 2. El trabajo, por bloques

### B0 · Puesta en marcha sobre PostgreSQL

Hecho el 23/10/2026: esquema creado (`scripts/crear-esquema.ps1`), 29 tiendas (almacenes activos de
`gesserca.fdalmacen`, `store_id` = `cod_alm`), 59 usuarios y servicios de Windows reinstalados.

| # | Acción | Estado |
|---|---|---|
| B0.1 | **El ERP escribe en `printer_source_print_job`.** Mínimo: `external_job_id`, `store_id`, `document_type`, `pdf_blob` (contrato en el comentario de la tabla en `crear-esquema.sql`). Un `external_job_id` repetido se descarta como duplicado: cada reimpresión necesita uno nuevo | ⏳ Sin esto no entra ningún trabajo |
| B0.2 | Impresoras, reglas de enrutado y Telegram | ⏳ Los configura el usuario. Telegram: `install-windows-services.ps1 -SkipPublish -ConfigureTelegram` |
| B0.3 | Esquema definitivo. Hoy **`admon`, provisional**: `pb8_xp57` no puede crear esquemas. Para mover: el DBA crea el destino (`CREATE SCHEMA impresoras AUTHORIZATION pb8_xp57`), `ALTER TABLE admon.<tabla> SET SCHEMA impresoras;` en las 12 `printer_*` (arrastra índices y secuencias) y cambiar `Search Path` en la cadena (`.env` y servicios) | ⏳ Decisión |
| B0.4 | Usuarios con contraseña = login (decisión temporal). Varias no cumplen el mínimo de 6 de la Api: se insertaron por SQL con el mismo BCrypt. Al embeber la app en el ERP, el login automático debería usar un secreto del ERP, no estas contraseñas | ⏳ Temporal |
| B0.5 | Permisos: las `printer_*` solo las lee `pb8_xp57` (dueño) y los superusuarios; `admon` no tiene permisos por defecto. Si el ERP conecta con otro usuario: `GRANT SELECT, INSERT ON admon.printer_source_print_job TO <usuario>` | ⏳ Según el usuario del ERP |

### B6 · Simplificar lo heredado de HANA

Rodeos que solo existían por limitaciones del proveedor de HANA. Cada uno está marcado en el código
con `herencia-hana:` (`grep -rn herencia-hana src`). Al simplificarlo, se quita la marca. Verificar
siempre contra PostgreSQL: `PostgresQueryTranslationTests` para la traducción y la BD real para los tipos.

| # | Qué | Dónde | Nota |
|---|---|---|---|
| ~~B6.1~~ | ~~Fechas como texto → `timestamptz`~~ | `ImpresorasDbContext`, `StoresController`, `crear-esquema.sql` | ✅ 23/10/2026. Migración `scripts/sql/migraciones/2026-10-23-fechas-timestamptz.sql` aplicada en la BD real y desplegada: 21 columnas, 0 diferencias en los datos, servicios y frontend verificados. Solo tocó las `printer_*` |
| ~~B6.2~~ | ~~`AnyAsync` en vez de `Take(1).CountAsync() > 0`~~ | 12 comprobaciones de existencia | ✅ 23/10/2026. Verificado contra PostgreSQL |
| ~~B6.3~~ | ~~Dashboard: `Any()` correlacionado y `NOT IN` directo~~ | `DashboardController` | ✅ 23/10/2026. Prueba A/B contra PostgreSQL: 8 de 8 respuestas del dashboard idénticas a la versión anterior. El agrupado por tramos se queda en memoria a propósito (marca `ponytail:` con su techo): en SQL necesitaría código solo para PostgreSQL y sin tests |
| ~~B6.4~~ | ~~`UPDATE` de impresora por EF en vez de SQL crudo~~ | `PrintersController.Update` | ✅ 23/10/2026. Test nuevo: el cambio de host anula `ipp_supported` y la telemetría del Worker no se pisa. Verificado contra PostgreSQL |
| B6.5 | Booleanos: variables `activeOnly`/`pendingOnly` usadas solo para que el proveedor no plegase la constante | `StoreHealthAlertBackgroundService` y el resto del código | Cosmético |
| B6.6 | `row_version`: columna que no participa en la concurrencia | `ImpresorasDbContext`, `crear-esquema.sql` | Borrarla, o usar `xmin` como token real si hiciera falta |

### B3 · Confirmación de impresión

| # | Trabajo | Estado |
|---|---|---|
| B3.1 | Confirmación IPP por trabajo | ❌ Descartado: el `job-id` lo asigna la impresora y nunca llega a nuestro proceso (enviamos por SumatraPDF → spooler de Windows). Ver `gate-i1-ipp-por-trabajo.md` |
| B3.2 | Renombrar `PrintedConfirmed` y sus etiquetas para que no afirmen lo que no comprueban | ⏳ Decisión de producto: toca UI, KPI e histórico |

Hoy una lectura de `printer-state` marca `PrintedConfirmed` a todo el lote de esa impresora, PHP lo
etiqueta «Impreso» y el KPI `printed` incluye además `SpoolAccepted` y `PrintedUnknown`.

### B5 · Higiene y deuda reconocida

| Qué | Estado |
|---|---|
| Alertas de Telegram sin outbox | Una alerta que no acepta ningún chat se reintenta en el siguiente ciclo, pero no hay estado por destinatario ni backoff |
| `StoreHealthAlertBackgroundService` sin tests | `RunOnceAsync` es privado; hacerlo testeable es un refactor |
| Lanzamiento real del proceso de impresión e IPP sin tests | Necesitan spooler o impresora reales. `WindowsPrintSpooler.ClassifyFailedExit` sí está cubierto |
| Reclamar entre la lectura y el UPDATE (409 de `cancel`) sin test | Exige interponerse en el comando SQL |
| Ruido en el registro de eventos | Cada `dotnet test` escribe avisos y errores en el Event Log de Windows: el entorno `Testing` no debería registrar ese proveedor |

---

## 3. Decisiones abiertas

| Decisión | Quién |
|---|---|
| Esquema definitivo (B0.3) | Usuario / DBA |
| El servidor es PostgreSQL **19beta4** (beta) y autentica con MD5 (obsoleto) | Infraestructura |
| Contraseñas = login (B0.4) | Usuario, cuando se embeba en el ERP |
| ¿Se revocó con BotFather el token de Telegram que quedó escrito en un documento? El documento ya no está, pero el token sigue en el historial de git | Operaciones |

---

## 4. Definición de hecho

- Un test que falla antes del cambio y pasa después.
- `dotnet test`, `php artisan test` y `npm run build` en verde según el componente tocado.
- Si toca SQL, concurrencia o tipos: verificado contra PostgreSQL, no solo contra SQLite.
- La fila de este plan tachada o actualizada.
