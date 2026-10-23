# ImpresorasService — Contexto del proyecto

Sistema de cola de impresión para tiendas de retail. Ingesta trabajos (albaranes) que el ERP propio deja en la tabla `printer_source_print_job` de PostgreSQL, los enruta a la impresora correcta según reglas configurables, los ejecuta con reintentos, y monitorea el estado en tiempo real. Incluye alertas por Telegram y confirmación de impresión vía protocolo IPP.

---

## Arquitectura: componentes y roles

| Componente | Tecnología | Puerto | Rol |
|---|---|---|---|
| `ImpresorasService.Core` | .NET 8, EF Core 8 | — | Dominio, aplicación, infraestructura compartida |
| `ImpresorasService.Api` | ASP.NET Core 8 | 5105 | REST API con JWT; consume Core |
| `ImpresorasService.Worker` | .NET 8 Host Service | — | Servicios de fondo (ingesta, ejecución, watchdog, alertas) |
| `ImpresorasService.Web.PHP` | Laravel 11, PHP 8.2 | 8000 | Frontend web (Blade + CSS glass-effect) |
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
```

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
- **PrintJobEvent**: log inmutable de transiciones de estado

---

## Flujo end-to-end

```
1. INGESTA  (IngestionBackgroundService, poll 2s)
   printer_source_print_job (la escribe el ERP) → claim/ack 90s lease
   → IngestionService.ProcessAsync() → PrintJob{Pending}

2. ENRUTADO (dentro de IngestionService)
   RoutingResolver busca RoutingRule con mayor prioridad
   → match: PrintJob{Routed, PrinterId asignado}
   → sin match: PrintJob{ErrorFinal, ROUTE_NOT_FOUND}

3. EJECUCIÓN (PrintExecutionBackgroundService, poll 5s, lote 10)
   Routed | RetryScheduled vencidos → IPrinterSpooler.SendAsync()
   → éxito: SpoolAccepted
   → error < 4 intentos: RetryScheduled (backoff 15/30/60s)
   → error >= 4 intentos: ErrorFinal

4. CONFIRMACIÓN IPP (SpoolAcceptedWatchdogBackgroundService, poll 10s)
   SpoolAccepted > 120s → IppConfirmationService.QueryPrinterStateAsync()
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
| Frontend | Laravel 11 (Blade, PHP 8.2), CSS custom glass-effect |
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
  "BotToken": ""                 // NUNCA en repo; usar variable de entorno
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

`ConnectionStrings:PrintQueue` es la **única** configuración de BD (host, puerto, BD, usuario y esquema
vía `Search Path`); Api, Worker y `scripts/crear-esquema.ps1` la leen de ahí. En local va en el `.env` de la
raíz (fuera de git, lo carga `Infrastructure/DotEnv.cs`; contraseña en `PGPASSWORD`). No poner nombres de
esquema en código, config ni DDL. La sección `Source` (`SourceSystem=ERP`, `LeaseSeconds`) describe el origen.

**Fechas como texto**: el modelo EF convierte todo `DateTimeOffset` a `"yyyy-MM-dd HH:mm:ss"` y las
columnas de fecha son `varchar(26)`, igual que en los tests SQLite. No poner `timestamp` en el DDL sin
quitar antes esos converters de `ImpresorasDbContext`: Npgsql rechaza texto en columnas `timestamp`.

**`herencia-hana:`** marca en el código los rodeos que solo existían por el proveedor de HANA (la BD
hasta el 23/10/2026). Son el bloque B6 de `docs/PLAN.md`: al simplificar uno, quitar su marca.

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

### Frontend PHP
- Cliente API en `app/Http/Controllers/` → llama a `ApiClient` que envuelve todas las peticiones al backend .NET
- Blade templates en `resources/views/`; estilos en `resources/css/dbx.css`
- Diseño glass-effect con variables CSS propias; no añadir frameworks JS externos

### Git
- Rama principal: `main`
- Rama activa: `feat/postgres`
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

tests/ImpresorasService.Api.IntegrationTests/  → xUnit + SQLite en memoria

scripts/sql/crear-esquema.sql         → DDL PostgreSQL (único; se aplica con scripts/crear-esquema.ps1)
docs/PLAN.md                          → QUÉ HACER: plan único de trabajo pendiente
docs/                                 → guías de despliegue, estilo, smoke tests
```
