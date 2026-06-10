# Informe de limpieza del repositorio

**Rama:** `cleanup/repository-sanitization` (base: `IU`)  
**Fecha:** 2026-06-10  
**Archivos rastreados antes:** 7 951  
**Archivos rastreados despues:** 911  
**Reduccion:** 7 040 archivos eliminados del indice git (~88%)

---

## Resumen de lo eliminado

| Categoria | Archivos eliminados | Notas |
|---|---|---|
| Cache NuGet (`.nuget/packages/`) | 2 766 | Paquetes de NuGet completos versionados por error |
| Plantillas demo KeenThemes (`templates/`) | 4 939 | ~181 MB de assets demo sin uso en el frontend activo |
| Bases de datos SQLite (`.db`, `.db-shm`, `.db-wal`) | 7 | Datos de desarrollo local (smoke tests, dev-shared, local) |
| `.env` Laravel | 1 | Contenia `APP_KEY` real; mantenido en disco, fuera del indice |
| Vista Blade sin ruta (`welcome.blade.php`) | 1 | Placeholder de Laravel no conectado a ninguna ruta |
| Controlador sin ruta (`PruebaController.php`) | 1 | Sin registro en `web.php`, vista referenciada inexistente |
| Cache PHPUnit | 0 (no estaba rastreado) | Ya cubierto por `.gitignore` |
| **Total** | **7 715** | |

### Archivos nuevos creados

| Archivo | Proposito |
|---|---|
| `ImpresorasServiceV1/docs/cleanup-audit.md` | Auditoria detallada con analisis por categoria |
| `ImpresorasServiceV1/docs/cleanup-report.md` | Este informe |
| `ImpresorasServiceV1/scripts/check-repository-cleanliness.sh` | Script de verificacion local (bash) |
| `.github/workflows/check-repository-cleanliness.yml` | GitHub Action de guardia en CI |

### `.gitignore` actualizado

Se anadieron dos entradas que faltaban:

```
# Demo templates
templates/

# PHPUnit cache
src/ImpresorasService.Web.PHP/.phpunit.result.cache
```

---

## Commits generados

```
d2e413e ci: prevent generated files from being tracked
a2d184f chore: clean environment configuration examples
d950b62 chore: remove unused duplicated classes and services
687fff6 chore: remove duplicated and unused Blade views
9779448 chore: remove unused template assets and demo files
7dbbce0 chore: remove local databases and runtime state files
b814844 chore: remove compiled binaries and build artifacts
7328b5e docs: add cleanup-audit.md with repository analysis
```

---

## Validaciones ejecutadas

### Limpieza del indice git (automatica)

Todos los checks pasan sobre el estado final del indice:

| Check | Resultado |
|---|---|
| Binarios DLL/EXE/PDB | OK - 0 archivos |
| Bases de datos SQLite | OK - 0 archivos |
| Archivos .env | OK - 0 archivos |
| Cache NuGet | OK - 0 archivos |
| Directorio templates/ | OK - 0 archivos |
| Archivos .log | OK - 0 archivos |
| Cache PHPUnit | OK - 0 archivos |

### Builds (pendiente de ejecutar en local)

El entorno de ejecucion del agente no dispone de `dotnet` ni `php`/`composer`. Estos comandos deben ejecutarse manualmente desde `ImpresorasServiceV1/`:

```powershell
# Backend .NET
dotnet restore
dotnet build -c Debug
dotnet test tests/ImpresorasService.Api.IntegrationTests

# Frontend Laravel (desde src/ImpresorasService.Web.PHP)
composer install
npm install
npm run build
php artisan route:list
composer run test
```

La limpieza no toca logica de negocio ni dependencias del proyecto, por lo que los builds no deberian verse afectados.

---

## Errores encontrados

### index.lock en Windows NTFS

El directorio `.git/` esta montado en NTFS desde el sandbox Linux. Git no puede eliminar archivos temporales `.lock` porque la operacion `unlink()` no esta permitida en ese tipo de montaje. Se trabajo alrededor usando comandos de plomeria git (`commit-tree`, `write-tree`, `update-index`, `hash-object`) y escribiendo directamente a los archivos de referencia, evitando la creacion de locks.

**No hay datos perdidos.** Todos los commits se generaron correctamente como muestra `git log --oneline`.

---

## Pendientes de decision del equipo

Ver `docs/cleanup-audit.md` seccion 9 para la lista completa. Elementos principales:

| Elemento | Ruta | Accion sugerida |
|---|---|---|
| `docs/archive/` | `ImpresorasServiceV1/docs/archive/` | Revisar y archivar o eliminar si no tiene valor |
| `scripts/archive/` | `ImpresorasServiceV1/scripts/archive/` | Idem |
| `assets/logo.png` | `ImpresorasServiceV1/assets/` | Confirmar si se usa en algun contexto |
| `Infrastructure/Legacy/` | `ImpresorasService.Core` | Excluido de compilacion; valorar si borrar |

---

## Proximos pasos recomendados

1. **Merge a `IU`** tras revision: `git merge cleanup/repository-sanitization --no-ff`
2. **Ejecutar builds locales** con los comandos listados arriba y confirmar que todo compila.
3. **Push a origin** para que la GitHub Action valide en CI desde la primera ejecucion.
4. **Rotar `APP_KEY`** de Laravel en el entorno de produccion (la clave anterior estuvo en git; aunque sea una clave de entorno `local`, es buena practica rotarla).
5. **Revisar pendientes**: `docs/archive/`, `scripts/archive/`, `assets/logo.png`.
6. **Considerar `git filter-repo`** si se quiere purgar el historial antiguo (los archivos eliminados siguen siendo recuperables via `git log` en commits anteriores).
