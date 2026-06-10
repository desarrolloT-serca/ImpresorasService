# Auditoría de limpieza del repositorio

**Rama auditada:** `IU` (base para `cleanup/repository-sanitization`)  
**Fecha:** 2026-06-10  
**Total archivos rastreados en git:** 7 951

---

## 1. Artefactos de compilación rastreados

### 1.1 Paquetes NuGet — `.nuget/packages/`

**Estado: ELIMINAR**

Se rastrean **2 766 archivos** del directorio local de caché NuGet, incluyendo DLLs, PDBs, nupkg y metadatos de paquetes. Estos son artefactos generados por `dotnet restore` y no deben estar en el repositorio.

- Ejemplos: `.nuget/packages/azure.core/`, `.nuget/packages/microsoft.data.sqlclient/`, …
- Peso estimado: cientos de MB.
- El archivo `.gitignore` ya incluye `**/.nuget/` pero la entrada no cubría el directorio raíz `.nuget/` tal y como fue versionado originalmente.

### 1.2 Directorios `bin/` y `obj/`

**Estado: NO rastreados en git** (cubiertos por `.gitignore`). Presentes en disco por compilaciones locales; se eliminan con `dotnet clean`.

### 1.3 `_build_out/` y `.tmp-build/` (en `ImpresorasService.Core/`)

**Estado: NO rastreados en git** pero presentes en disco. No cubiertos por el `.gitignore` raíz.  
→ Añadir al `.gitignore`.

### 1.4 `.vs/` (Visual Studio)

**Estado: NO rastreados**. Cubiertos por `.gitignore`. No hay nada que eliminar del índice.

---

## 2. Bases de datos locales y archivos de estado de ejecución

### 2.1 SQLite — rastreados en git

**Estado: ELIMINAR**

| Archivo | Ruta |
|---|---|
| `impresoras-dev-shared.db` | `ImpresorasServiceV1/` |
| `impresoras-dev-shared.db-shm` | `ImpresorasServiceV1/` |
| `impresoras-dev-shared.db-wal` | `ImpresorasServiceV1/` |
| `impresoras-local.db` | `ImpresorasServiceV1/` |
| `impresoras-smoke-s3.db` | `ImpresorasServiceV1/` |
| `impresoras-smoke-s1.db` | `ImpresorasServiceV1/src/ImpresorasService.Api/` |
| `impresoras-local.db` | `ImpresorasServiceV1/src/ImpresorasService.Worker/` |

El `.gitignore` ya tiene patrones `impresoras-*.db*` y `*.db`, pero estos archivos se añadieron al índice antes de que existieran esas reglas.

### 2.2 SQLite — en disco pero no rastreados

Los ficheros `.db-shm`/`.db-wal` en `ImpresorasServiceV1/` y en `src/ImpresorasService.Api/` y `src/ImpresorasService.Worker/` no están rastreados (ya ignorados). No requieren acción de git.

### 2.3 Logs de Laravel

**Estado: NO rastreados** (`storage/logs/` está en `.gitignore`). Los archivos `.err.log`, `.out.log`, `laravel.log` están en disco y se pueden borrar localmente.

### 2.4 `.phpunit.result.cache`

**Estado: ELIMINAR** (`src/ImpresorasService.Web.PHP/.phpunit.result.cache` está rastreado en git). Es un artefacto de ejecución de tests.

---

## 3. Frontends

### 3.1 Frontend oficial — Laravel 12 PHP

**Estado: ACTIVO. Mantener.**

Ubicación: `ImpresorasServiceV1/src/ImpresorasService.Web.PHP/`

Frontend completamente funcional con rutas, controladores, vistas Blade y servicios propios.

### 3.2 Frontend Blazor/Razor

**Estado: NO EXISTE.**

No hay proyectos `.razor` ni `.cshtml` en el repositorio. No hay proyecto Blazor en la solución `.sln`. No se requiere acción.

### 3.3 `templates/` (KeenThemes demo)

**Estado: ELIMINAR**

Directorio `ImpresorasServiceV1/templates/` con **4 939 archivos rastreados** (~181 MB). Son assets de la plantilla de demostración KeenThemes, sin referencia alguna desde el frontend Laravel activo. No se usan en ninguna ruta ni vista Blade.

---

## 4. Vistas Blade — análisis de referencias

| Vista | Referenciada | Acción |
|---|---|---|
| `dashboard.blade.php` | Sí — `DashboardController::index()` | Mantener |
| `dashboard-local.blade.php` | Sí — `DashboardController::index()` (branch no-admin) | Mantener |
| `dashboard/partials/filters.blade.php` | Sí — incluida desde `dashboard.blade.php` | Mantener |
| `dashboard/partials/tabs.blade.php` | Sí — incluida desde `dashboard.blade.php` | Mantener |
| `ajustes.blade.php` | Sí — `DashboardController::ajustes()`, enlace en nav | Mantener |
| `alertas.blade.php` | Sí — `AlertasController`, enlace en nav | Mantener |
| `cola.blade.php` | Sí — `ColaController` | Mantener |
| `impresoras/index.blade.php` | Sí | Mantener |
| `impresoras/form.blade.php` | Sí — create/edit | Mantener |
| `reglas/index.blade.php` | Sí | Mantener |
| `reglas/form.blade.php` | Sí | Mantener |
| `tiendas/index.blade.php` | Sí | Mantener |
| `tiendas/form.blade.php` | Sí | Mantener |
| `usuarios/index.blade.php` | Sí | Mantener |
| `usuarios/form.blade.php` | Sí | Mantener |
| `auth/login.blade.php` | Sí — `AuthController` | Mantener |
| `layouts/app.blade.php` | Sí — layout base | Mantener |
| `components/*` | Sí — usados en vistas | Mantener |
| `welcome.blade.php` | **No** — sin ruta ni referencia | **ELIMINAR** |

---

## 5. Controladores PHP — análisis de referencias

| Controlador | Registrado en rutas | Acción |
|---|---|---|
| `AuthController.php` | Sí | Mantener |
| `AlertasController.php` | Sí | Mantener |
| `ColaController.php` | Sí | Mantener |
| `DashboardController.php` | Sí | Mantener |
| `ImpresorasController.php` | Sí | Mantener |
| `ReglasController.php` | Sí | Mantener |
| `TiendasController.php` | Sí | Mantener |
| `UsuariosController.php` | Sí | Mantener |
| `StoreFilterController.php` | Sí | Mantener |
| `Controller.php` | Base — heredado | Mantener |
| `PruebaController.php` | **No** — sin ruta registrada, vista `prueba.blade.php` inexistente | **ELIMINAR** |

---

## 6. Configuración — secretos y archivos de entorno

### 6.1 `.env` de Laravel — rastreado

**Estado: ELIMINAR del índice git**

`src/ImpresorasService.Web.PHP/.env` está rastreado y contiene `APP_KEY=base64:kl1T2o+...` (clave de aplicación real). Este archivo debe mantenerse en local pero nunca en git. El `.gitignore` ya tiene la regla pero el archivo fue añadido antes.

### 6.2 `appsettings.json` y `appsettings.Development.json`

**Estado: OK — sin secretos reales**

Ambos archivos de API y Worker tienen `ConnectionString: ""` y `Jwt.Secret: ""` vacíos. Son seguros para versionar.

---

## 7. Solución `.sln`

**Estado: OK — sin cambios necesarios**

`ImpresorasServiceV1.sln` referencia exactamente los 4 proyectos activos:
- `ImpresorasService.Api`
- `ImpresorasService.Worker`
- `ImpresorasService.Core`
- `ImpresorasService.Api.IntegrationTests`

No hay proyectos fantasma ni referencias rotas.

---

## 8. `.gitignore` — gaps detectados

El `.gitignore` actual es sólido pero faltan las siguientes entradas:

| Patrón faltante | Motivo |
|---|---|
| `.nuget/` (en raíz, sin `**/`) | Los 2 766 archivos de paquetes NuGet rastreados no quedan cubiertos por `**/.nuget/` si ya están en el índice |
| `_build_out/` | Directorio de build alternativo en `ImpresorasService.Core/` |
| `.tmp-build/` | Directorio de build temporal en `ImpresorasService.Core/` |
| `src/ImpresorasService.Web.PHP/.phpunit.result.cache` | Cache de PHPUnit rastreada |
| `templates/` | Directorio de demo KeenThemes |

---

## 9. Pendiente de decisión

Los siguientes elementos requieren confirmación del equipo antes de eliminarlos:

| Elemento | Ruta | Motivo de duda |
|---|---|---|
| `docs/archive/` | `ImpresorasServiceV1/docs/archive/` | Documentación histórica — puede tener valor de referencia |
| `scripts/archive/` | `ImpresorasServiceV1/scripts/archive/` | Scripts históricos — misma consideración |
| `assets/logo.png` | `ImpresorasServiceV1/assets/` | Único archivo en la carpeta; podría ser usado |
| `openspec/` (raíz) | `ImpresorasServiceV1/openspec/` | Tooling independiente; se mantiene salvo indicación contraria |
| `Infrastructure/Legacy/` | `ImpresorasService.Core` | Excluido de compilación pero puede servir de referencia |
| `src/ImpresorasService.Web.PHP/storage/framework/views/*.php` | Laravel | Vistas compiladas en cache — no rastreadas actualmente; OK |
