# ImpresorasService — Contexto del proyecto

Sistema de cola de impresión para tiendas de retail. Ingesta trabajos (albaranes) que el ERP propio deja en la tabla `printer_source_print_job` de PostgreSQL, los enruta a la impresora correcta según reglas configurables, los ejecuta con reintentos, y monitorea el estado en tiempo real. Incluye alertas por Telegram y confirmación de impresión vía protocolo IPP.

---

## Arquitectura: componentes y roles

| Componente | Tecnología | Puerto | Rol |
|---|---|---|---|
| `ImpresorasService.Core` | .NET 8, EF Core 8 | — | Dominio, aplicación, infraestructura compartida |
| `ImpresorasService.Api` | ASP.NET Core 8 | 5105 | REST API con JWT; consume Core |
| `ImpresorasService.Worker` | .NET 8 Host Service | — | Servicios de fondo (ingesta, ejecución, watchdog, alertas) |
| `ImpresorasService.Web.PHP` | Laravel 12, PHP 8.5 | 8000 | Frontend web (Blade + CSS glass-effect) |
| `ImpresorasService.Web` | React 19, Vite (stack del ERP nuevo) | 5173 (dev) | Frontend que sustituirá al PHP; en construcción (`docs/PLAN.md` B7) |
| PostgreSQL | Npgsql EF provider | 5433 | Base de datos única (`10.110.46.121`, BD `serca`, usuario `pb8_xp57`, esquema **`admon` provisional**, ver `docs/PLAN.md` B0.5). Servidor 19beta4 |
| Telegram Bot API | HTTP | — | Notificaciones de alertas de tienda |

> **No hay Docker**. En producción: Nginx hace reverse proxy al frontend PHP, la API .NET corre en Kestrel, el Worker como servicio de sistema.

---

## Dominio: entidades clave

### PrintJob — ciclo de vida completo

```
Pending → Routed → SpoolAccepted → PrintedConfirmed
                              ↘ PrintedUnknown
       ↘ RetryScheduled → (Routed de nuevo) → ErrorFinal
       ↘ ErrorFinal  (si ROUTE_NOT_FOUND o max reintentos)

ErrorFinal → (reintento, POST route, evento RETRY_ROUTE) → Pending
PrintedConfirmed | PrintedUnknown → (reimpresión con motivo, POST reprint, evento REPRINT) → Pending
```
Toda reimpresión de lo ya salido queda en `printer_print_job_event` (`REPRINT`, quién y por qué):
es el registro de reimpresiones (`docs/PLAN.md` B10).

**Campos críticos**: `Status`, `PrinterId`, `AttemptCount`, `NextRetryAtUtc`, `LastErrorCode`, `PdfBlob`

### Printer
- `Host`: IP/hostname para sondeo de conectividad
- `SpoolQueue`: nombre de cola Windows para spooler
- `ConnectionFailuresStreak`: conteo de fallos consecutivos
- `IppSupported` (bool?): null = sin comprobar, true/false = resultado IPP

### Otras entidades
- **RoutingRule**: prioridad + filtros (storeId, documentType, channel) → PrinterId
- **Store**: agrupación de impresoras; tiene salud calculada
- **DashboardThreshold**: umbrales de salud (singleton, fila id=1)
- **TelegramConfig**: config singleton (Enabled, BotToken, MinSeverity, CheckIntervalMinutes)
- **TelegramChat**: ChatId (long) + IsActive; uno por destinatario
- **StoreAlertState**: último estado notificado por tienda (evita spam)
- **User**: autenticación JWT; roles Admin/Supervisor/Operator
- **PrintJobEvent**: log inmutable de transiciones de estado. Guarda además, sin trabajo detrás, el historial de avisos de Telegram (`TELEGRAM_ALERT`, con `job_id` fijo por tienda = `TelegramMensajes.JobIdHistorial(storeId)`; decisión del 27/10/2026 para no crear tabla). Una consulta que recorra eventos sin cruzar con `printer_print_job` debe filtrar por `event_type`; al purgar una tienda se borran también los suyos

---

## Flujo end-to-end

```
1. INGESTA  (IngestionBackgroundService, poll 2s)
   printer_source_print_job (la escribe el ERP) → claim/ack 90s lease
   → PDF en pdf_blob o descargado de pdf_url (Source:PdfUrlHosts; si no sirve, ErrorFinal PDF_DOWNLOAD_FAILED)
   → IngestionService.ProcessAsync() → PrintJob{Pending}

2. ENRUTADO (dentro de IngestionService)
   RoutingResolver busca RoutingRule con mayor prioridad
   → match: PrintJob{Routed, PrinterId asignado}
   → sin match: PrintJob{ErrorFinal, ROUTE_NOT_FOUND}

3. EJECUCIÓN (PrintExecutionBackgroundService, poll 5s, lote 10)
   Routed | RetryScheduled vencidos → primero por IP: IPP Print-Job con el PDF o, si no lo acepta,
   como imagen PWG (PwgRaster, PDFium) (IppConfirmationService.PrintPdfAsync); si no se puede, cola
   de Windows (IPrinterSpooler, Sumatra). Lo que sale por IP guarda su número de trabajo (ipp_job_id).
   Un envío por IP cortado a medias no se repite por la cola: PrintedUnknown
   → éxito: SpoolAccepted
   → error < 4 intentos: RetryScheduled (backoff 15/30/60s)
   → error >= 4 intentos: ErrorFinal

4. CONFIRMACIÓN IPP (SpoolAcceptedWatchdogBackgroundService, poll 10s)
   SpoolAccepted > 120s → con ipp_job_id, estado de ese trabajo (QueryJobStateAsync: terminado,
   cancelado/abortado → ErrorFinal, detenido); si no, IppConfirmationService.QueryPrinterStateAsync()
   → Idle: PrintedConfirmed
   → Stopped: ErrorFinal (IPP_PRINTER_STOPPED)
   → Unavailable o IPP desactivado: PrintedUnknown

5. CONECTIVIDAD (PrinterConnectivityMonitorService, cada 30s)
   Sondea puertos 515, 9100, 631 (timeout 600ms, máx 3 paralelas)
   Puerto 631 abierto → query IPP → actualiza IppSupported
   Actualiza ConnectionFailuresStreak, LastConnectionOk

6. ALERTAS (StoreHealthAlertBackgroundService, cada 5min)
   StoreHealthEvaluator.Compute() → healthy | warning | critical
   Transición de estado → TelegramNotifierService.SendAlertAsync()
   Estado persistente → sin re-notificación (no spam)
```

---

## Stack técnico

| Capa | Tecnología |
|---|---|
| Backend | .NET 8, ASP.NET Core, EF Core 8 |
| Base de datos | PostgreSQL (Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11) |
| Tests | xUnit + SQLite en memoria (EF, no mocks) |
| Auth | JWT Bearer, BCrypt |
| Printer protocol | IPP (RFC 8010/8011) vía HttpClient raw |
| Notifications | Telegram Bot API (REST) |
| Frontend | Laravel 12 (Blade, PHP 8.5), CSS custom glass-effect |
| CI/CD | GitHub Actions |

> **DDL externo**: no hay migraciones EF. El esquema vive en `scripts/sql/crear-esquema.sql` y se aplica con `scripts/crear-esquema.ps1`; la aplicación nunca lo crea ni lo altera (solo los tests, con `EnsureCreated` sobre SQLite).

---

## Configuración clave (appsettings.json)

```jsonc
// Worker — sección PrintExecution
{
  "UseRealSpooler": true,        // false en Linux/test → NoOpPrintSpooler
  "MaxAttempts": 4,
  "BackoffSeconds": [15,30,60],   // solo se consumen MaxAttempts-1 esperas
  "IppConfirmationEnabled": true,
  "IppTimeoutMs": 3000
}

// Worker — sección Telegram
{
  "Enabled": false,              // activar en producción con BotToken real
  "BotToken": ""                 // NUNCA en repo; usar variable de entorno (o ponerlo desde la pantalla)
}

// Worker — sección PrinterConnectivity
{
  "IntervalSeconds": 30,
  "TimeoutMsPerPort": 600,
  "Ports": [515, 9100, 631],
  "MaxParallelChecks": 3
}
```

Variables de entorno que sobreescriben appsettings (patrón `Section__Key`):
- `Telegram__BotToken`, `Jwt__Secret`, `ConnectionStrings__PrintQueue`

**Token del bot de Telegram**: el que se pone desde la pantalla de Telegram (`PUT /api/telegram/token`,
Admin; se comprueba con `getMe` antes de guardarlo) va a `printer_telegram_config.bot_token` y **manda
sobre** `Telegram:BotToken`; vacío, vuelve el del servicio (`TelegramToken.ResolverAsync`, lo usan el
Worker y la prueba de envío). La API nunca lo devuelve: solo de dónde sale y sus 4 últimos caracteres.
En la BD se guarda en claro (27/10/2026): lo protege el acceso a la base.

`ConnectionStrings:PrintQueue` es la **única** configuración de BD (host, puerto, BD, usuario y esquema
vía `Search Path`); Api, Worker y `scripts/crear-esquema.ps1` la leen de ahí. En local va en el `.env` de la
raíz (fuera de git, lo carga `Infrastructure/DotEnv.cs`; contraseña en `PGPASSWORD`). No poner nombres de
esquema en código, config ni DDL. La sección `Source` (`SourceSystem=ERP`, `LeaseSeconds`) describe el origen.

**Fechas**: en PostgreSQL son `timestamptz` y siempre UTC (`ImpresorasDbContext` normaliza cualquier
`DateTimeOffset` con otro offset; Npgsql rechaza offsets distintos de 0). Solo en los tests (SQLite) se
guardan como texto ISO, porque SQLite no compara fechas en SQL: el modelo depende del proveedor y se
cachea por proveedor. Las fechas en PostgreSQL las cubre `PostgresQueryTranslationTests` y la prueba
contra un PostgreSQL real.

---

## Estado actual (2026-10-23)

### Implementado y en producción
- Ingesta desde el ERP con claim/ack (solo se acusa recibo de lo realmente persistido)
- Enrutado por reglas configurables
- Ejecución con reintentos exponenciales
- Monitoreo de conectividad de impresoras
- API REST con JWT, rate limiting, security headers
- Frontend Laravel cola + dashboard con salud de tiendas
- Dashboard con umbrales configurables en BD, KPIs por periodo (contrato en `docs/contrato-kpi-dashboard.md`) y timezone de negocio configurable (`Dashboard:BusinessTimeZone`)
- **IPP**: `IIppConfirmationService`/`IppConfirmationService`, `IppSupported` en `Printer`, sondeo desde `PrinterConnectivityMonitorService`, filtro en el watchdog, badge en UI PHP (`resources/views/impresoras/index.blade.php`)
- **Telegram**: `TelegramNotifierService`, `StoreHealthAlertBackgroundService`, `TelegramController` (API). `Telegram:Enabled=false` por defecto en `Worker/appsettings.json`; activar en producción con `BotToken` real. **La página PHP de alertas se retiró** (commit `fcd05d7`): la configuración se toca por API.
- **Lock de instancia única** (`WorkerLockCoordinator` + `WorkerLockBackgroundService`): relevo verificado con dos procesos Worker reales; la hora del lease la da la BD (`now()`)
- **Health check `worker`** en la Api (`WorkerActivityHealthCheck`): delata un Worker inerte usando la huella del monitor de conectividad. Reporta `Degraded`, sin tag `ready`.
- **Retención de PDF** (`PdfRetentionBackgroundService`): **activa, 90 días** (decisión del 19/08/2026). Libera el blob de los trabajos en estado terminal y de las filas de origen ya procesadas, conservando fila, `pdf_sha256` y metadatos.
- **Revocación de acceso**: `User.IsActive` y `User.TokenVersion` comprobados en cada petición (`UserRevocationValidator`, enganchado a `OnTokenValidated`). Desactivar, borrar o cambiar la contraseña corta el acceso en la siguiente petición; el frontend ya redirige al login ante un 401.
- **Errores para la gente** (`Domain/ErroresTrabajo.cs`): texto y qué hacer de cada `last_error_code`, que la API manda en la cola. Un código nuevo necesita su entrada: `ErroresTrabajoTests` lo exige.
- **Caídas de la BD** (`Infrastructure/Persistence/EstadoBd.cs`): un fallo de conexión se anota una vez con su motivo y su vuelta con lo que duró; la Api arranca sin base y responde 503 (no 500 ni 401); el Worker se reconecta al renovar el lock. Para detectar «la base no responde» usar `FallosBd.EsFalloDeConexion`, no comparar mensajes. Servicios de Windows con reinicio automático (`docs/PLAN.md` B8).

### Pendiente
**Ver `docs/PLAN.md`** — fuente de verdad única del trabajo pendiente, en bloques por dependencia.
No se duplica aquí para que no diverja.

### DDL
Esquema completo para una BD vacía: `scripts/sql/crear-esquema.sql` (PostgreSQL, sin nombre de esquema; se
aplica con `scripts/crear-esquema.ps1`, en una transacción). Si cambias el modelo, actualízalo ahí.
Copia del esquema real: `pg_dump --schema-only -n <esquema>`. Puesta en marcha y pendientes: `docs/PLAN.md`, B0.

---

## Convenciones del proyecto

### Código .NET
- Patrón: dominio en `Core/Domain`, contratos en `Core/Application/Abstractions`, implementaciones en `Core/Infrastructure/Services`
- Nuevas entidades: clase en `Domain/Entities/` → mapeo en `ImpresorasDbContext.cs` → registro en `DependencyInjection.cs`
- Opciones de configuración: clase en `Infrastructure/Options/` → sección en appsettings → validación en `DependencyInjection.cs`
- Sin migraciones EF automáticas; si cambias el modelo, añade DDL en `scripts/sql/`
- Tests usan SQLite en memoria; no mockear la BD (lección aprendida: divergencia de mocks vs producción)

### Frontend React (`ImpresorasService.Web`, módulo copiado en el ERP)
- **Gráficas: siempre con shadcn/ui chart** (`src/components/ui/chart.tsx`, Recharts 3), instalado en los dos proyectos. Colores solo con tokens (`var(--chart-1…5)`, `--destructive`, `--success`, `--warning`) para que el estilo y el modo oscuro sigan los del ERP; no añadir otra librería de gráficas. Mapa de calor y diagramas de nodos (reglas de enrutado), a mano con CSS o SVG. Regla también en `ERP_SERCA/erp/README.md` («Reglas de la base»), 27/10/2026.
- Un cambio en `src/modules/impresion` se copia igual a `ERP_SERCA/erp/apps/web/src/modules/impresion` (mismo código; allí las líneas acaban en CRLF).

### Frontend PHP
- Cliente API en `app/Http/Controllers/` → llama a `ApiClient` que envuelve todas las peticiones al backend .NET
- Blade templates en `resources/views/`; estilos en `resources/css/dbx.css`
- Diseño glass-effect con variables CSS propias; no añadir frameworks JS externos

### Git
- Rama principal: `main`
- Rama activa: `feat/web-erp`
- Commits convencionales: `feat:`, `fix:`, `chore:`, `refactor:`

---

## Mapa de archivos clave

```
src/ImpresorasService.Core/
  Domain/Entities/                    → entidades del dominio
  Application/Abstractions/           → interfaces (IPrintExecutionService, IIppConfirmationService, ITelegramNotifier...)
  Infrastructure/Persistence/ImpresorasDbContext.cs → mappings EF
  Infrastructure/DependencyInjection.cs             → registro de todos los servicios
  Infrastructure/Services/            → implementaciones concretas
  Infrastructure/Options/             → clases de configuración tipada

src/ImpresorasService.Worker/
  IngestionBackgroundService.cs           → polling tabla origen → cola
  PrintExecutionBackgroundService.cs      → cola → spooler
  SpoolAcceptedWatchdogBackgroundService.cs → confirmación IPP
  PrinterConnectivityMonitorService.cs    → sondeo de puertos + IPP
  StoreHealthAlertBackgroundService.cs    → alertas Telegram
  WorkerLockBackgroundService.cs          → lock de instancia única (el resto lee WorkerLockState)
  PdfRetentionBackgroundService.cs        → libera el PDF de los trabajos cerrados

src/ImpresorasService.Api/
  Controllers/                        → endpoints REST
  Security/                           → JWT middleware, roles

src/ImpresorasService.Web.PHP/
  app/Http/Controllers/               → controladores Laravel
  resources/views/                    → Blade templates
  resources/css/dbx.css               → estilos del sistema

src/ImpresorasService.Web/            → frontend React nuevo; modules/impresion es un módulo del ERP (ver su README)

tests/ImpresorasService.Api.IntegrationTests/  → xUnit + SQLite en memoria

scripts/sql/crear-esquema.sql         → DDL PostgreSQL (único; se aplica con scripts/crear-esquema.ps1)
docs/PLAN.md                          → QUÉ HACER: plan único de trabajo pendiente
docs/                                 → guías de despliegue, estilo, smoke tests
```
