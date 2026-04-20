# ImpresorasServiceV1

Backend de gestion de trabajos de impresion con:
- ingesta por polling,
- idempotencia por `SourceSystem + ExternalJobId`,
- cola interna en BD local/remota,
- enrutado por reglas y ejecucion con reintentos.

## Decision de arquitectura (cohesion)

- **UI oficial de esta fase:** `src/ImpresorasService.Web.PHP` (Laravel).
- **Punto de entrada funcional principal:** API + Worker + UI PHP.

## Estructura oficial (4 bloques)

- `src/ImpresorasService.Web.PHP`: frontend oficial (Laravel) que consume la API.
- `src/ImpresorasService.Api`: backend HTTP (API REST y autenticacion JWT).
- `src/ImpresorasService.Worker`: backend de procesos (ingesta y ejecucion de impresion).
- `src/ImpresorasService.Core`: nucleo unico con dominio + casos de uso + infraestructura tecnica.
- `tests/ImpresorasService.Api.IntegrationTests`: pruebas de integracion.

## Configuracion minima

En `appsettings.json` de API/Worker:
- `Database:Provider` (`Sqlite` recomendado en local, `Hana` en entornos con provider SAP disponible)
- `ConnectionStrings:PrintQueue`
- `Ingestion:PollIntervalSeconds`, `Ingestion:BatchSize`
- `Source:Mode` (`SqlTest` recomendado en local, `SapHana` para dual-run/modelo ORM actual)
- `PrintExecution:*` (spooler real/simulado, reintentos)
- `Jwt:Secret` (obligatorio por entorno, minimo 32 caracteres, sin defaults inseguros)

El esquema se aplica al iniciar API/Worker mediante migraciones EF Core (`Database.Migrate`).

### Nota importante sobre `SapHana` (estado actual)

En el estado actual del proyecto, el adapter `SapHana` opera sobre `SourcePrintJobs` del `ImpresorasDbContext` (modelo ORM interno) y no sobre una lectura SQL remota directa a una tabla externa HANA.  
Para desarrollo local se recomienda usar `Database:Provider=Sqlite` y `Source:Mode=SqlTest`.

## Arranque recomendado (flujo oficial)

**Importante:** API y Worker deben ejecutarse desde `ImpresorasServiceV1` para compartir `impresoras-local.db`.

1) Arrancar backend:

```powershell
cd ImpresorasServiceV1
dotnet restore
dotnet build -c Debug
dotnet run --project "src/ImpresorasService.Api"
dotnet run --project "src/ImpresorasService.Worker"
```

Para local sin provider SAP:

```powershell
$env:Database__Provider = "Sqlite"
$env:ConnectionStrings__PrintQueue = "Data Source=impresoras-local.db"
$env:Source__Mode = "SqlTest"
$env:Jwt__Secret = "REEMPLAZAR_CON_SECRETO_LARGO_MIN_32"
$env:Bootstrap__SeedDefaultUsers = "false"
```

2) Arrancar UI oficial (PHP), en otra terminal:

```powershell
cd ImpresorasServiceV1/src/ImpresorasService.Web.PHP
composer install
npm install
php artisan serve
```

Para comprobar que API y Worker comparten BD: `.\scripts\verificar-bd.ps1`

## Pruebas rapidas

- Crear trabajo de origen en Swagger: `POST /api/sourceprintjobs/test`
- Consultar cola: `GET /api/printjobs`
- Probar impresion:
  - `.\scripts\probar-impresion.ps1`
  - `.\scripts\verificar-estado.ps1`

Estados habituales:
- `SpoolAccepted`: exito
- `PrintedUnknown`: cierre por watchdog sin confirmacion explicita de impresion
- `RetryScheduled`: reintento pendiente
- `ErrorFinal`: fallo definitivo

## Documentacion relacionada

- Resumen rapido del proyecto: `docs/RESUMEN-PROYECTO.md`
- Checklist de cohesion y limpieza: `docs/CHECKLIST-COHESION.md`
- Despliegue frontend PHP: `docs/DESPLIEGUE-PHP.md`
- Smoke tests de regresion: `docs/SMOKE-TESTS-PHP.md`
- Checklist accesibilidad dark mode: `docs/CHECKLIST-ACCESIBILIDAD-DARKMODE.md`
- Convenciones UI frontend PHP: `docs/UI-CONVENCIONES-FRONTEND-PHP.md`
