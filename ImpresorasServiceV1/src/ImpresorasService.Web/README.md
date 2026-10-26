# ImpresorasService.Web

Frontend nuevo de la cola de impresión, con el stack y las piezas del ERP nuevo
(`ERP_SERCA/erp/apps/web`) para que se vea y se comporte como él. Sustituirá al
frontend PHP por fases (`docs/PLAN.md`, B7).

Está hecho para que, el día que se decida, el módulo de impresión pueda
**abrirse en un iframe** dentro del ERP o **copiarse dentro de su código**.

## Arranque

```bash
npm ci
npm run dev        # http://localhost:5173; /api/impresion/… va a la API .NET (5105)
```

`API_URL` cambia la API de desarrollo. Comprobaciones: `npm test`, `npm run typecheck`,
`npm run lint`, `npm run build`.

Sesión: `POST /api/auth/login` de la API .NET deja el JWT en la cookie `impresoras_sesion`
(`httpOnly`, `SameSite=Strict`, `Path=/api`), como la del ERP; `GET /api/auth/me` dice quién es y
`POST /api/auth/logout` la borra. Despliegue: `docs/DESPLIEGUE-WEB.md` (nginx sirve `dist/` y pasa
`/api/impresion/` a Kestrel como `/api/`, en el mismo origen).

## Qué es de quién

| Ruta | Origen | Al integrar en el ERP |
| --- | --- | --- |
| `src/modules/impresion/` | Propio, escrito como un módulo del ERP: dos manifiestos, `impresion` (operación) e `impresionConfiguracion` (solo administración: reglas, umbrales, Telegram y pruebas en una pestaña, «Reglas y avisos») | Se copia a `apps/web/src/modules/` y se registran los dos en su `workspace/registro.ts` |
| `src/app/`, `src/main.tsx`, `src/core/sesion.ts`, `src/workspace/registro.ts` | Propio: contenedor de esta aplicación (login, sesión, menú) | Se tira |
| `src/app/transicion/` | Propio: Tiendas y Usuarios, solo mientras la app vaya sola. En el ERP los usuarios son sus operadores y las tiendas, sus almacenes | Se tira |
| `src/contratos/` | `comun.ts` copiado de `packages/contratos`; `impresion.ts`, contratos del módulo; `index.ts` sustituye a `@serca/contratos` | `impresion.ts` pasa a `packages/contratos` |
| Todo lo demás de `src/` y `test/core/api.test.ts` | Copiado del ERP | Se tira: el ERP ya lo tiene |

Copiado del ERP en el commit **`064b912`** (rama `cayetanoDevelop`, 23/10/2026): `index.css`,
`components/ui/*`, `components/{theme-provider,data-table,filtro-lista,campo-fecha,ficha}.tsx`,
`core/{api,atajos,impresion-paneles}.ts`, `workspace/{Escritorio,PanelSeguro,modulo,cerrar-panel}`,
`lib/*`, `hooks/*`, `public/{serca,ad-serca}.png` y la configuración (tsconfig, eslint,
prettier, `components.json`). Mismas versiones de dependencias que el ERP.

Cambios sobre la copia (lo único que hay que conservar al actualizarla):

- `core/api.ts`: entiende los errores `{ error }` y `{ errors: [] }` de la API .NET (`errorNet`) y
  hace PUT y DELETE (`metodo`).
- `test/core/api.test.ts`: las pruebas de esos casos.
- `workspace/modulo.ts`: grupo de menú «Impresión», donde van todas las pantallas del módulo
  (decisión del 23/10/2026). Al integrar, el ERP lo añade a su `GrupoMenu`.
- `components/data-table.tsx`: prop `getRowId`, para que la selección vaya por fila y no por
  posición (la cola selecciona trabajos y se refresca sola). Sirve también al ERP: proponérselo.

Para actualizar, se vuelve a copiar desde el ERP, se reaplican esos cambios y se anota
aquí el commit nuevo.

## Reglas del módulo

Para que se pueda meter en el ERP sin tocarlo:

1. No importa nada de `src/app/` ni de `src/workspace/registro.ts`. Solo usa lo que el ERP da
   a sus módulos: `@/workspace/modulo`, `@/core/api`, `@/core/sesion` (`usePermiso`),
   `@/components/…` y `@serca/contratos`. Lo comprueba `test/convenciones.test.ts`.
2. No sabe nada del login: decide con claves de permiso (`tienePermiso`), que calcula el
   contenedor.
3. Pide `/impresion/…` con las rutas de la API .NET. Aquí el proxy (nginx en producción) quita
   `/impresion`; en el ERP lo atenderá su pasarela con las mismas rutas.
4. Sigue las reglas de pantalla del README del ERP («Reglas de la base»).
