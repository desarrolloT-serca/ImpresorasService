# ImpresorasServiceV1

Base de implementacion para la Fase 1 de V1:
- ingesta por polling
- idempotencia por `SourceSystem + ExternalJobId`
- cola interna en SQLite local (sin servidor externo)

## Estructura

- `src/ImpresorasService.Domain`: entidades y estados del dominio.
- `src/ImpresorasService.Application`: contratos y servicio de ingesta.
- `src/ImpresorasService.Infrastructure`: EF Core, repositorios y adaptadores de origen (`SqlTest`/`SapHana`).
- `src/ImpresorasService.Worker`: worker de polling para ingestar lotes.
- `src/ImpresorasService.Api`: API de monitorizacion basica (`/health`, `/api/printjobs`).
- `src/ImpresorasService.Web`: panel Blazor base (preparado para iterar).

## Configuracion minima

En `appsettings.json` de Worker/API:
- `Database:Provider` (`Sqlite` por defecto)
- `ConnectionStrings:PrintQueue`
- `Ingestion:PollIntervalSeconds`, `Ingestion:BatchSize`
- `Source:Mode` (`SqlTest` o `SapHana`)

Con `Sqlite`, la BD se crea automaticamente al iniciar API/Worker (`EnsureCreated`).

## Comandos

Ejecutar desde el directorio `ImpresorasServiceV1`:

```powershell
cd ImpresorasServiceV1
dotnet restore
dotnet build -c Debug
dotnet run --project "src/ImpresorasService.Api"
dotnet run --project "src/ImpresorasService.Worker"
```

O con rutas absolutas desde la raíz del repo:

```powershell
dotnet run --project "ImpresorasServiceV1/src/ImpresorasService.Api"
dotnet run --project "ImpresorasServiceV1/src/ImpresorasService.Worker"
```

## Prueba local rapida (sin SSMS)

1. Arranca API y Worker.
2. Abre Swagger de la API y usa `POST /api/sourceprintjobs/test` para crear un trabajo de origen.
3. Espera un ciclo de polling (5s) y consulta `GET /api/printjobs`.

## Prueba de impresion real

1. Arranca **API** y **Worker** (en dos terminales).
2. En una tercera terminal, desde la carpeta `ImpresorasServiceV1`:
   ```powershell
   .\scripts\probar-impresion.ps1
   ```
3. El script crea un trabajo, lo enruta y el Worker lo envia a la impresora.

**Requisitos previos:** Impresora creada; regla de enrutado; `UseRealSpooler: true` en Worker; SumatraPDF instalado.

Para verificar si funciono:
```powershell
.\scripts\verificar-estado.ps1   # Muestra estado de todos los trabajos
```
Estados: `SpoolAccepted` = exito (PDF enviado al spooler); `ErrorFinal` = fallo; `Routed` = aun esperando que el Worker lo procese.

Para crear la regla de enrutado (si falta):
```powershell
.\scripts\crear-regla-enrutado.ps1        # Lista impresoras
.\scripts\crear-regla-enrutado.ps1 1      # Crea regla para printerId=1
```

## Siguiente paso recomendado

Implementar EF Migrations para controlar versionado de esquema entre entornos.
