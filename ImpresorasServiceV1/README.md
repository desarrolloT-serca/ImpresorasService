# ImpresorasServiceV1

Plataforma de cola de impresión centrada en **SAP HANA**: ingesta por polling con claim/ack, cola interna, enrutado y ejecución con reintentos.

## Arquitectura (4 bloques + tests)

| Componente | Ruta | Rol |
|------------|------|-----|
| Frontend oficial | `src/ImpresorasService.Web.PHP` | UI Laravel (consume la API) |
| API | `src/ImpresorasService.Api` | REST + JWT |
| Worker | `src/ImpresorasService.Worker` | Ingesta, impresión, watchdog, monitorización |
| Core | `src/ImpresorasService.Core` | Dominio, aplicación e infraestructura |
| Tests | `tests/ImpresorasService.Api.IntegrationTests` | Integración (SQLite en memoria solo en tests) |

Estado Git del frontend: ver `docs/FRONTEND-WEB-PHP-GIT.md` (gitlink/submódulo no formalizado).

## Configuración mínima oficial (HANA)

En `appsettings.json` de API y Worker (secretos por entorno / variables):

```json
{
  "Database": { "Provider": "Hana", "ApplyMigrations": false },
  "ConnectionStrings": { "PrintQueue": "<HANA EF>" },
  "Source": { "Mode": "SapHana" },
  "SapHana": {
    "ConnectionString": "<HANA ODBC para diagnóstico opcional>",
    "Schema": "<schema>",
    "Table": "printer_source_print_job",
    "SourceSystem": "SAP-HANA",
    "LeaseSeconds": 90
  },
  "Jwt": { "Secret": "<mínimo 32 caracteres>" },
  "PrintExecution": { "UseRealSpooler": false }
}
```

Variables de entorno equivalentes: `Database__Provider`, `ConnectionStrings__PrintQueue`, `Source__Mode`, `SapHana__ConnectionString`, `Jwt__Secret`, etc.

**No usar en producción:** `Sqlite`, `SqlTest`, `SqlServer`, `SapPostgres` (histórico; ver `docs/archive/` y `Infrastructure/Legacy/`).

El esquema HANA se gestiona con DDL externo (`scripts/sql/`). `Database:ApplyMigrations` debe permanecer en `false`. Las migraciones EF en el repo son referencia histórica SQLite (ver `Infrastructure/Persistence/Migrations/README.md`).

## Arranque

```powershell
cd ImpresorasServiceV1
dotnet restore
dotnet build -c Debug

# Configurar secretos HANA (ejemplo)
$env:Database__Provider = "Hana"
$env:ConnectionStrings__PrintQueue = "ServerNode=...;UID=...;PWD=...;Current Schema=..."
$env:Source__Mode = "SapHana"
$env:SapHana__ConnectionString = "Driver={HDBODBC};ServerNode=...;UID=...;PWD=..."
$env:SapHana__Schema = "ZTEST_VICENTE_2"
$env:Jwt__Secret = "REEMPLAZAR_CON_SECRETO_LARGO_MIN_32"
$env:Bootstrap__SeedDefaultUsers = "false"

dotnet run --project src/ImpresorasService.Api
dotnet run --project src/ImpresorasService.Worker
```

UI Laravel (otra terminal; ver `docs/FRONTEND-WEB-PHP-GIT.md` si el gitlink no está inicializado):

```powershell
cd src/ImpresorasService.Web.PHP
composer install
npm install
php artisan serve
```

Verificación HANA: `.\scripts\verificar-hana.ps1`

## Pruebas

```powershell
dotnet test tests/ImpresorasService.Api.IntegrationTests
```

Scripts operativos: `scripts/probar-impresion.ps1`, `scripts/verificar-estado.ps1`

## Documentación

- Resumen: `docs/RESUMEN-PROYECTO.md`
- Histórico (SQLite/Postgres/SqlTest): `docs/archive/`
- Despliegue PHP: `docs/DESPLIEGUE-PHP.md`
- Smoke UI: `docs/SMOKE-TESTS-PHP.md`
