# ImpresorasService — Contexto del proyecto

Sistema de cola de impresión para tiendas de retail. Ingesta trabajos desde SAP HANA, los enruta a la impresora correcta según reglas configurables, los ejecuta con reintentos, y monitorea el estado en tiempo real. Incluye alertas por Telegram y confirmación de impresión vía protocolo IPP.

---

## Arquitectura: componentes y roles

| Componente | Tecnología | Puerto | Rol |
|---|---|---|---|
| `ImpresorasService.Core` | .NET 8, EF Core 8 | — | Dominio, aplicación, infraestructura compartida |
| `ImpresorasService.Api` | ASP.NET Core 8 | 5105 | REST API con JWT; consume Core |
| `ImpresorasService.Worker` | .NET 8 Host Service | — | Servicios de fondo (ingesta, ejecución, watchdog, alertas) |
| `ImpresorasService.Web.PHP` | Laravel 11, PHP 8.2 | 8000 | Frontend web (Blade + CSS glass-effect) |
| SAP HANA | ODBC / EF provider | — | Base de datos única en producción |
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
   SAP HANA printer_source_print_job → claim/ack 90s lease
   → IngestionService.ProcessAsync() → PrintJob{Pending}

2. ENRUTADO (dentro de IngestionService)
   RoutingResolver busca RoutingRule con mayor prioridad
   → match: PrintJob{Routed, PrinterId asignado}
   → sin match: PrintJob{ErrorFinal, ROUTE_NOT_FOUND}

3. EJECUCIÓN (PrintExecutionBackgroundService, poll 5s, lote 10)
   Routed | RetryScheduled vencidos → IPrinterSpooler.SendAsync()
   → éxito: SpoolAccepted
   → error < 4 intentos: RetryScheduled (backoff 15/30/60/90s)
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
| Base de datos | SAP HANA (Sap.EntityFrameworkCore.Hana.v8.0) |
| Tests | xUnit + SQLite en memoria (EF, no mocks) |
| Auth | JWT Bearer, BCrypt |
| Printer protocol | IPP (RFC 8010/8011) vía HttpClient raw |
| Notifications | Telegram Bot API (REST) |
| Frontend | Laravel 11 (Blade, PHP 8.2), CSS custom glass-effect |
| CI/CD | GitHub Actions |

> **DDL externo**: EF Migrations son solo referencia histórica. El DBA aplica el DDL manualmente desde `scripts/sql/`. No ejecutar `dotnet ef database update` en producción.

---

## Configuración clave (appsettings.json)

```jsonc
// Worker — sección PrintExecution
{
  "UseRealSpooler": true,        // false en Linux/test → NoOpPrintSpooler
  "MaxAttempts": 4,
  "BackoffSeconds": [15,30,60,90],
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
- `Telegram__BotToken`, `Jwt__Secret`, `ConnectionStrings__PrintQueue`, `SapHana__Schema`

---

## Estado actual (rama `develop` — 2026-07-20)

### Implementado y en producción
- Ingesta SAP HANA con claim/ack
- Enrutado por reglas configurables
- Ejecución con reintentos exponenciales
- Monitoreo de conectividad de impresoras
- API REST con JWT, rate limiting, security headers
- Frontend Laravel cola + dashboard con salud de tiendas
- Dashboard con umbrales configurables en BD, KPIs por periodo (contrato en `docs/contrato-kpi-dashboard.md`) y timezone de negocio configurable (`Dashboard:BusinessTimeZone`)
- **IPP**: `IIppConfirmationService`/`IppConfirmationService`, `IppSupported` en `Printer`, sondeo desde `PrinterConnectivityMonitorService`, filtro en el watchdog, badge en UI PHP (`resources/views/impresoras/index.blade.php`)
- **Telegram**: `TelegramNotifierService`, `StoreHealthAlertBackgroundService`, `TelegramController` (API), UI PHP de configuración/gestión de chats (`AlertasController` + `resources/views/alertas/`). `Telegram:Enabled=false` por defecto en `Worker/appsettings.json`; activar en producción con `BotToken` real.

### Pendiente
- Confirmación IPP **por trabajo** (hoy solo consulta `printer-state` global, no `job-id`) — Fase 3 de `docs/roadmapimpresoras.md`
- Claim atómico / lock de instancia única del Worker — Fase 2 de `docs/roadmapimpresoras.md` (núcleo, bloqueante para 2+ workers)
- Tests e2e de IPP/watchdog/conectividad
- Pruebas con bot Telegram real en producción (operativo, no código)

### DDL pendiente de aplicar en producción
```sql
-- 4 objetos nuevos:
-- tabla printer_telegram_config
-- tabla printer_telegram_chat
-- tabla printer_alert_state
-- columna ipp_supported TINYINT NULLABLE en printer_printer
```
Ver `ImpresorasServiceV1/TELEGRAM_AND_IPP_ROADMAP.md` para detalle.

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
- Rama activa: `IU` (Interface Updates)
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
  IngestionBackgroundService.cs           → polling HANA → cola
  PrintExecutionBackgroundService.cs      → cola → spooler
  SpoolAcceptedWatchdogBackgroundService.cs → confirmación IPP
  PrinterConnectivityMonitorService.cs    → sondeo de puertos + IPP
  StoreHealthAlertBackgroundService.cs    → alertas Telegram

src/ImpresorasService.Api/
  Controllers/                        → endpoints REST
  Security/                           → JWT middleware, roles

src/ImpresorasService.Web.PHP/
  app/Http/Controllers/               → controladores Laravel
  resources/views/                    → Blade templates
  resources/css/dbx.css               → estilos del sistema

tests/ImpresorasService.Api.IntegrationTests/  → xUnit + SQLite en memoria

scripts/sql/                          → DDL para SAP HANA
docs/                                 → guías de despliegue, estilo, smoke tests
TELEGRAM_AND_IPP_ROADMAP.md           → roadmap detallado de próximas características
```
