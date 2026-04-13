# Resumen rapido del proyecto

Este documento explica el proyecto de forma corta y practica para poder orientarte sin leer todo el codigo.

## 1) Que es este proyecto

`ImpresorasServiceV1` es una plataforma para:
- recibir trabajos de impresion desde un origen externo (polling),
- guardarlos en una cola interna,
- decidir a que impresora enviarlos (enrutado),
- y ejecutarlos con reintentos/control de estado.

La solucion esta separada por capas (Domain, Application, Infrastructure) y por procesos de ejecucion (API, Worker, UIs).

## 2) Mapa mental en 30 segundos

Piensa en 4 bloques:
- **Origen**: tabla/fuente externa con trabajos (`SourcePrintJobs`).
- **Cola interna**: base de datos local con estados del trabajo (`PrintJobs` y eventos).
- **Decision**: reglas que eligen impresora segun tienda/tipo de documento/canal.
- **Ejecucion**: worker que manda el PDF al spooler y actualiza estado.

## 3) Estructura principal (4 bloques)

- `src/ImpresorasService.Web.PHP`: UI oficial en Laravel/PHP.
- `src/ImpresorasService.Api`: API REST (auth, cola, impresoras, reglas, health).
- `src/ImpresorasService.Worker`: procesos en background (ingesta + ejecucion de impresion).
- `src/ImpresorasService.Core`: logica unificada (dominio + aplicacion + infraestructura tecnica).
- `tests/ImpresorasService.Api.IntegrationTests`: pruebas de integracion de API.

Decision de cohesion para esta fase:
- Frontend unico: `src/ImpresorasService.Web.PHP`.

## 4) Servicios/procesos y responsabilidad

### API (`ImpresorasService.Api`)
- Expone endpoints para monitorizar cola y administrar impresoras/reglas.
- Gestiona autenticacion JWT.
- Inicializa esquema base de BD al arrancar.

### Worker (`ImpresorasService.Worker`)
- Ejecuta polling de origen de datos y mete trabajos nuevos en la cola interna.
- Procesa trabajos enrutados para imprimirlos.
- Gestiona reintentos y transiciones de estado.

### Core (`ImpresorasService.Core`)
- Contiene toda la logica de negocio y tecnica en un unico proyecto:
  - entidades y estados de dominio,
  - servicios de ingesta con idempotencia,
  - acceso a datos con EF Core y repositorios,
  - enrutado y ejecucion de impresion (spooler real/simulado).

## 5) Flujo end-to-end (de origen a impresora)

1. **Llega trabajo** al origen externo.
2. **IngestionBackgroundService** hace polling y llama a `IngestionService`.
3. `IngestionService` aplica **idempotencia** y crea job en cola interna.
4. `RoutingService` intenta asignar impresora segun reglas activas.
5. Job pasa a estado **Routed** (o a error final si no hay ruta).
6. **PrintExecutionBackgroundService** recoge jobs enrutados.
7. `PrintExecutionService` intenta imprimir (spooler real o simulado).
8. Estado final: `SpoolAccepted`, `RetryScheduled` o `ErrorFinal`.

Estados habituales: `Pending`, `Routed`, `Printing`, `RetryScheduled`, `SpoolAccepted`, `ErrorFinal`.

## 6) Puntos de entrada para entender rapido

Orden recomendado de lectura:
1. `README.md`
2. `src/ImpresorasService.Api/Program.cs`
3. `src/ImpresorasService.Worker/Program.cs`
4. `src/ImpresorasService.Core/ImpresorasService.Core.csproj`
5. `src/ImpresorasService.Core/Infrastructure/DependencyInjection.cs`
6. `src/ImpresorasService.Core/Application/Services/IngestionService.cs`
7. `src/ImpresorasService.Core/Infrastructure/Services/RoutingService.cs`
8. `src/ImpresorasService.Core/Infrastructure/Services/PrintExecutionService.cs`
9. `src/ImpresorasService.Api/Controllers/PrintJobsController.cs`

## 7) Configuracion que mas impacta

En `appsettings.json` (API/Worker):
- `Database:Provider`
- `ConnectionStrings:PrintQueue`
- `Ingestion:PollIntervalSeconds`
- `Ingestion:BatchSize`
- `Source:Mode` (`SqlTest` / `SapHana`)
- opciones de spooler real/simulado y reintentos

## 8) Como ejecutarlo sin complicarte

Desde `ImpresorasServiceV1`:

```powershell
dotnet restore
dotnet build -c Debug
dotnet run --project "src/ImpresorasService.Api"
dotnet run --project "src/ImpresorasService.Worker"
```

UI oficial (PHP), en otra terminal:

```powershell
cd src/ImpresorasService.Web.PHP
composer install
npm install
php artisan serve
```

Para pruebas rapidas:
- `scripts/probar-impresion.ps1`
- `scripts/verificar-estado.ps1`
- `scripts/verificar-bd.ps1`

## 9) Normalizacion de rutas (importante)

- Ruta oficial del frontend: `ImpresorasServiceV1/src/ImpresorasService.Web.PHP`.
- Si trabajas en UI, usa siempre la ruta oficial dentro de `ImpresorasServiceV1`.

## 10) Si te pierdes con tantos servicios

Regla practica:
- Si quieres entender **que se expone** -> mira `Api/Controllers`.
- Si quieres entender **como entra el trabajo** -> mira `IngestionService`.
- Si quieres entender **por que va a una impresora u otra** -> mira `RoutingService`/`RoutingResolver`.
- Si quieres entender **por que fallo al imprimir** -> mira `PrintExecutionService` y spooler.

Con ese recorrido ya puedes depurar el 80-90% de incidencias funcionales sin recorrer todo el repo.
