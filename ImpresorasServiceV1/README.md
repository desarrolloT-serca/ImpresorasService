# ImpresorasServiceV1

Plataforma de cola de impresión sobre **PostgreSQL**: ingesta por polling con claim/ack de los trabajos que deja el ERP, cola interna, enrutado y ejecución con reintentos.

## Arquitectura

| Componente | Ruta | Rol |
|------------|------|-----|
| API | `src/ImpresorasService.Api` | REST; solo se entra con el pase del ERP (`Erp:Secret`) |
| Worker | `src/ImpresorasService.Worker` | Ingesta, impresión, watchdog, monitorización |
| Core | `src/ImpresorasService.Core` | Dominio, aplicación e infraestructura |
| Módulo web | `src/ImpresorasService.Web` | Origen del módulo de impresión que se copia al ERP (sus pruebas); no se despliega |
| Tests | `tests/ImpresorasService.Api.IntegrationTests` | Integración (SQLite en memoria solo en tests) |

**Sin frontal ni usuarios propios desde el 11/11/2026**: las pantallas están en el ERP web, que entra
con su pase (operador y permisos del ERP), y las tiendas son los almacenes del ERP (vista
`printer_store` sobre `gesserca.fdalmacen`). El frontal Laravel y el contenedor React con login quedan
en la etiqueta git `archivo/frontales-propios`.

## Configuración mínima oficial (PostgreSQL)

En `appsettings.json` de API y Worker (secretos por entorno / variables / `.env`):

```json
{
  "ConnectionStrings": { "PrintQueue": "Host=HOST;Port=5432;Database=BD;Username=USUARIO;Search Path=impresoras" },
  "Source": {
    "SourceSystem": "ERP",
    "LeaseSeconds": 90
  },
  "Erp": { "Secret": "<mínimo 32 caracteres; el mismo que IMPRESION_SECRETO del ERP>" },
  "PrintExecution": { "UseRealSpooler": false }
}
```

Variables de entorno equivalentes: `ConnectionStrings__PrintQueue`, `Source__SourceSystem`, `Erp__Secret`, etc.
Sin `Erp:Secret` la Api no arranca: no habría forma de entrar.
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

En Linux (systemd), solo por IP: `sudo ./scripts/install-linux.sh`. La configuración va en
`/etc/impresoras/impresoras.env` (plantilla en `deploy/linux/impresoras.env.example`, con
`PrintExecution__SoloIp=true`). El mismo código sirve en los dos sistemas: cambiar de sistema es cambiar
de instalador y de configuración, no de rama.

Copia del esquema real: `pg_dump --schema-only -n <esquema>`. No hay migraciones EF: el esquema vive en
`scripts/sql/crear-esquema.sql` y la aplicación nunca lo crea ni lo altera (salvo en los tests).

## Arranque

```powershell
cd ImpresorasServiceV1
dotnet restore
dotnet build -c Debug

# Conexión y Erp__Secret: .env en la raíz (ver .env.example)
dotnet run --project src/ImpresorasService.Api
dotnet run --project src/ImpresorasService.Worker
```

Las pantallas se prueban desde el ERP web en local (`ERP_SERCA/erp`), con el mismo secreto en su
`IMPRESION_SECRETO`. Verificación de la BD con la Api en marcha (pase de administración):
`GET /diagnostics` y `GET /diagnostics/db`.

## Pruebas

```powershell
dotnet test tests/ImpresorasService.Api.IntegrationTests
cd src/ImpresorasService.Web; npm ci; npm run typecheck; npm run lint; npm test
```

Scripts operativos: `scripts/probar-impresion.ps1`, `scripts/verificar-estado.ps1`

## Documentación

- Resumen: `docs/RESUMEN-PROYECTO.md`
- Plan de trabajo: `docs/PLAN.md`
