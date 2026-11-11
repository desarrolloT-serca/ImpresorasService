# ImpresorasService.Web

Origen del **módulo de impresión del ERP nuevo** (`src/modules/impresion`), con sus contratos y sus
pruebas. Se copia tal cual a `ERP_SERCA/erp/apps/web/src/modules/impresion` (allí las líneas acaban en
CRLF) y `src/contratos/impresion.ts` a `ERP_SERCA/erp/packages/contratos`.

**No es una aplicación**: desde el 11/11/2026 no tiene login, menú ni despliegue. Al servicio solo se
entra desde el ERP con su pase, y la pantalla se ve allí. El contenedor anterior (login, Tiendas y
Usuarios) queda en la etiqueta git `archivo/frontales-propios`.

## Comprobaciones

```bash
npm ci
npm run typecheck
npm run lint
npm test
```

## Qué es de quién

| Ruta | Origen | En el ERP |
| --- | --- | --- |
| `src/modules/impresion/` | Propio, escrito como un módulo del ERP: dos manifiestos, `impresion` (operación) e `impresionConfiguracion` (solo administración) | Se copia a `apps/web/src/modules/` |
| `src/contratos/` | `comun.ts` copiado de `packages/contratos`; `impresion.ts`, contratos del módulo; `index.ts` sustituye a `@serca/contratos` | `impresion.ts` está en `packages/contratos` |
| `src/core/sesion.ts` | Sustituto mínimo de `usePermiso` para tipos y pruebas: aquí deja ver todo | Se usa el del ERP (permisos del operador) |
| Todo lo demás de `src/` y `test/core/api.test.ts` | Copiado del ERP | Ya lo tiene |

Copiado del ERP en el commit **`064b912`** (rama `cayetanoDevelop`, 23/10/2026). Cambios sobre la copia
que hay que conservar al actualizarla:

- `core/api.ts`: entiende los errores `{ error }` y `{ errors: [] }` de la API .NET (`errorNet`) y
  hace PUT y DELETE (`metodo`).
- `test/core/api.test.ts`: las pruebas de esos casos.
- `workspace/modulo.ts`: grupo de menú «Impresión».
- `components/data-table.tsx`: prop `getRowId`, para que la selección vaya por fila y no por posición.
- `components/ui/{chart,card}.tsx` y `recharts`: gráficas de shadcn/ui. Toda gráfica nueva se hace con ellas.

## Reglas del módulo

1. Solo usa lo que el ERP da a sus módulos: `@/workspace/modulo`, `@/core/api`, `@/core/sesion`
   (`usePermiso`), `@/components/…` y `@serca/contratos`. Lo comprueba `test/convenciones.test.ts`.
2. Decide con claves de permiso (`usePermiso`), que calcula el ERP; quien decide de verdad es la API.
3. Pide `/impresion/…` con las rutas de la API .NET: en el ERP las atiende su pasarela.
4. Sigue las reglas de pantalla del README del ERP («Reglas de la base»).
