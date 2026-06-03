# Estado Git de `ImpresorasService.Web.PHP`

## Diagnóstico (rama IU)

| Aspecto | Estado |
|---------|--------|
| Tipo en el repo padre | **Gitlink (modo 160000)** — apunta al commit `51cd697…` |
| `.gitmodules` en el repo padre | **No existe** → submódulo **no formalizado** |
| `.git` interno en la carpeta | **Sí existe** (repositorio anidado) |
| Remote detectado en el anidado | `https://github.com/laravel/laravel.git` (plantilla Laravel genérica) |

**Conclusión:** no es una carpeta normal totalmente integrada en el monorepo; es un **repositorio anidado / gitlink sin `.gitmodules` correcto**. El contenido local no debe borrarse en limpiezas del monorepo.

## Qué hacer al clonar (estado actual)

```powershell
git clone https://github.com/desarrolloT-serca/ImpresorasService.git
cd ImpresorasService
git checkout IU
# Si el gitlink no se resuelve:
cd ImpresorasServiceV1/src/ImpresorasService.Web.PHP
git status
```

Si la carpeta está vacía o en detached HEAD del template Laravel, el frontend debe alinearse con el repositorio Laravel real del equipo (ver decisión pendiente abajo).

## Decisiones pendientes (requieren acuerdo del equipo)

**A) Integrar como carpeta normal**  
Eliminar solo el `.git` interno (no el código), añadir todos los archivos fuente al monorepo y dejar de usar gitlink.

**B) Submódulo formal**  
Crear `.gitmodules` con la URL del repo Laravel real del proyecto y documentar:

```bash
git clone --recurse-submodules …
git submodule update --init --recursive
```

**C) Repositorio externo**  
Mantener el frontend en otro repo; documentar despliegue y versión compatible con la API.

**No se ha aplicado ninguna de estas opciones en la limpieza estructural** para no perder contenido ni asumir la URL correcta del submódulo.

## Archivos que no deben versionarse (carpeta normal o submódulo)

- `vendor/`, `node_modules/`, `.env`
- `storage/logs/`, `bootstrap/cache/`
- `public/build/`, `public/hot/`

Reglas reflejadas en `ImpresorasServiceV1/.gitignore`.
