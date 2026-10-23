# ImpresorasServiceV1

Plataforma de cola de impresión sobre **PostgreSQL**: ingesta por polling con claim/ack de los trabajos que deja el ERP, cola interna, enrutado y ejecución con reintentos.

## Arquitectura (4 bloques + tests)

| Componente | Ruta | Rol |
|------------|------|-----|
| Frontend oficial | `src/ImpresorasService.Web.PHP` | UI Laravel (consume la API) |
| API | `src/ImpresorasService.Api` | REST + JWT |
| Worker | `src/ImpresorasService.Worker` | Ingesta, impresión, watchdog, monitorización |
| Core | `src/ImpresorasService.Core` | Dominio, aplicación e infraestructura |
| Tests | `tests/ImpresorasService.Api.IntegrationTests` | Integración (SQLite en memoria solo en tests) |

Estado Git del frontend: ver `docs/FRONTEND-WEB-PHP-GIT.md` (gitlink/submódulo no formalizado).

## Configuración mínima oficial (PostgreSQL)

En `appsettings.json` de API y Worker (secretos por entorno / variables / `.env`):

```json
{
  "ConnectionStrings": { "PrintQueue": "Host=HOST;Port=5432;Database=BD;Username=USUARIO;Search Path=impresoras" },
  "Source": {
    "SourceSystem": "ERP",
    "LeaseSeconds": 90
  },
  "Jwt": { "Secret": "<mínimo 32 caracteres>" },
  "PrintExecution": { "UseRealSpooler": false }
}
```

Variables de entorno equivalentes: `ConnectionStrings__PrintQueue`, `Source__SourceSystem`, `Jwt__Secret`, etc.
`Source` describe el **origen** de los trabajos: la tabla `printer_source_print_job` que escribe el ERP.

### Base de datos y cambio de base de datos

`ConnectionStrings:PrintQueue` es la **única** configuración de base de datos: Api, Worker y
`scripts/crear-esquema.ps1` sacan de ella servidor, puerto, BD, usuario y esquema (`Search Path`).
Ningún fichero lleva el esquema escrito. La contraseña puede ir en la cadena (`Password=`) o en `PGPASSWORD`.

En desarrollo va en el fichero `.env` de la raíz (copiar `.env.example`; no se sube a git). Api y Worker lo
cargan al arrancar (`DotEnv.cs`) sin pisar variables ya definidas.

```powershell
.\scripts\crear-esquema.ps1   # crea el esquema y las tablas (scripts/sql/crear-esquema.sql) si está vacío; todo o nada
```

En producción la cadena vive en las variables de cada servicio:
`.\scripts\install-windows-services.ps1 -SkipPublish -Reconfigure -DbHost HOST -DbPort 5433 -DbName BD -DbUser USUARIO`.

Copia del esquema real: `pg_dump --schema-only -n <esquema>`. No hay migraciones EF: el esquema vive en
`scripts/sql/crear-esquema.sql` y la aplicación nunca lo crea ni lo altera (salvo en los tests).

## Arranque

```powershell
cd ImpresorasServiceV1
dotnet restore
dotnet build -c Debug

# Conexión: .env en la raíz (ver .env.example)
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

Verificación de la BD con la Api en marcha (JWT admin): `GET /diagnostics` y `GET /diagnostics/db`.

## Pruebas

```powershell
dotnet test tests/ImpresorasService.Api.IntegrationTests
```

Scripts operativos: `scripts/probar-impresion.ps1`, `scripts/verificar-estado.ps1`

## Documentación

- Resumen: `docs/RESUMEN-PROYECTO.md`
- Plan de trabajo: `docs/PLAN.md`
- Despliegue PHP: `docs/DESPLIEGUE-PHP.md`
- Smoke UI: `docs/SMOKE-TESTS-PHP.md`
