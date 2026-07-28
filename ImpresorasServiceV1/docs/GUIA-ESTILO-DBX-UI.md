# Guía de estilo y sistema de interfaz (v2)

Especificación de la línea visual y de interacción de `ImpresorasService.Web.PHP`.
Sustituye a la guía v1 (catálogo de clases `dbx-*`) y la amplía con decisiones de sistema,
gramática por pantalla, navegación con contexto y criterios de accesibilidad.

---

## 0) Estado real verificado (2026-07-28)

Datos medidos sobre el repo, no estimados. Sirven de línea base para las fases del §9.

| Hecho | Medición | Dónde |
|---|---|---|
| CSS global | 9.601 líneas en 3 hojas | `app.css` 1.488 · `dbx.css` 2.163 · `system.css` 5.950 |
| `!important` | 349 totales, **92 % en una sola hoja** | `app.css` 11 · `dbx.css` 15 · **`system.css` 323** |
| Tokens de marca duplicados | mismo bloque definido dos veces | `app.css:1105` y `system.css:5` |
| Radios | 20 valores distintos; ya hay tokens (`--ui-radius:6px`, `--ui-radius-sm:4px`) pero conviven con `0`, `5`, `7`, `8`, `10`, `12px`, `0.7rem`, `0.75rem`, `999px` | `system.css:27-28` vs. literales dispersos |
| `--btn-radius: 7px` | definido **fuera de `:root`**, valor huérfano que rompe la escala | `system.css:3775` |
| Ancho máximo de contenido | **11 valores distintos** (1024, 1100, 1180, 1200, 1280, 1320, 1420, 1440, 1450, 1500, 1660) | los tres CSS |
| Tipografía | app usa `Futura, "Futura PT", Trebuchet MS`; **login carga Poppins desde `fonts.bunny.net`** | `auth/login.blade.php:8` |
| Fallback de estilos en login | `cdn.tailwindcss.com` si no hay build de Vite | `auth/login.blade.php:15` |
| `transition: all` | 2 usos | `app.css:132`, `app.css:754` |
| Sistemas de estado en paralelo | `badge` (19 usos) y `dbx-pill` (8 usos) | vistas Blade |
| `confirm()` nativo | 13 usos, incluidos borrados definitivos | tiendas, usuarios, impresoras, reglas, alertas, cola, pruebas |
| Etiquetas sin `for`/`id` | tiendas 3/3 sin asociar · usuarios 5/5 sin asociar · impresoras 1/5 · reglas 1/6 | `*/form.blade.php` |
| `autocomplete` | 4 apariciones en todo `resources/views` | — |
| Dashboards | conviven `dashboard.blade.php` y `dashboard-local.blade.php` | duplicidad a resolver |

**Correcciones al análisis de partida**

1. El problema de cascada **no** está repartido entre las tres hojas: `system.css` concentra el 92 % de los `!important`. Consolidar debe empezar y casi terminar ahí.
2. La dispersión de anchos es mayor de lo detectado: 11 valores, no 3.
3. Los componentes Blade reutilizables **ya existen** (`resources/views/components/ui/`: `card`, `table`, `toolbar`, `action-buttons`, `action-icon`, `empty-row`) y están adoptados en 16 vistas. La v1 los listaba como "próxima evolución"; hoy son la base canónica y la spec se apoya en ellos.

---

## 1) Principios

1. **Consola operativa, no dashboard genérico.** La app existe para detectar → investigar → actuar sobre trabajos de impresión. Todo lo que no ayude a ese ciclo es ruido.
2. **Un solo lenguaje visual.** Hoy conviven tarjetas redondeadas DBX, estética angular industrial (radio 0, mayúsculas, cuña lateral) y una capa "glass". Se elige **uno**: superficies limpias con radio pequeño consistente.
3. **Tres alturas, no más:** página (fondo), panel (`--ui-surface`), panel elevado (modal / barra contextual). Sin sombras decorativas intermedias.
4. **El color comunica estado, no decora.** Azul AD = navegación, foco y acción primaria. Rojo = fallo crítico y destrucción, **nunca acento decorativo**.
5. **Densidad controlada.** Tabla densa y legible por encima de tarjetas grandes con poco dato.
6. **Reutilizar antes que añadir.** Si existe `<x-ui.*>`, se usa; no se escribe HTML+clases a mano.

---

## 2) Fundamentos (tokens)

Fuente única: `resources/css/system.css` bloque `:root`. `app.css` **deja de declarar** tokens de marca (`app.css:1105-1123` se elimina tras verificar cascada).

### 2.1 Color

```
--ad-blue: #1b2f82        navegación, primario, foco
--ad-blue-deep: #102368   hover/active del primario
--ad-red: #ed1b2f         SOLO crítico y destructivo
--ui-danger: var(--ad-red)
```

- `--ui-accent` deja de apuntar a rojo. Un acento decorativo rojo compite con la señal de fallo.
- Severidad: 3 niveles alineados con `StoreHealthEvaluator` del backend — `healthy` / `warning` / `critical`. Sin cuarto estado inventado en la UI.
- Estados que no son severidad (activo/inactivo, sin comprobar, sin host) usan **gris neutro**, no amarillo.

### 2.2 Radio — escala cerrada

```
--ui-radius-sm: 4px   inputs, chips, botones pequeños
--ui-radius:    6px   tarjetas, paneles, botones
--ui-radius-pill: 999px  solo píldoras de estado
```

Prohibido: literales `5px`, `7px`, `8px`, `10px`, `12px`, `0.7rem`, `0.75rem`.
`--btn-radius` se elimina; los botones usan `--ui-radius`. `border-radius: 0` solo si se adopta deliberadamente la variante angular en **todo** el sistema, nunca por componente.

### 2.3 Espaciado

Escala única `4 / 8 / 12 / 16 / 24 / 32`, expuesta como `--sp-1 … --sp-6`. Cualquier valor fuera de escala es un bug de estilo.

### 2.4 Ancho de contenido

**Uno solo:** `--content-max: 1320px`. Excepción justificable únicamente para la Cola (tabla ancha), que puede usar `--content-max-wide: 1660px` declarado como token, no como literal suelto.

### 2.5 Tipografía

- Una única familia real en toda la app, **incluido login**. Se elimina la carga de Poppins desde `fonts.bunny.net` (`auth/login.blade.php:8`) o se adopta Poppins como fuente de producto y se cambia la pila global. Lo que no puede quedar es la divergencia.
- Escala: `12 / 13 / 14 / 16 / 20 / 24`.
- **Se retira el `text-transform: uppercase` generalizado.** Mayúsculas reservadas a etiquetas técnicas pequeñas de cabecera de tabla. Títulos, navegación y botones en lenguaje normal.
- Datos técnicos (IDs, hosts, códigos de error) en la pila monoespaciada ya definida.

### 2.6 Movimiento

- Nunca `transition: all` (`app.css:132`, `app.css:754`). Declarar la propiedad concreta.
- Duración 120–180 ms, `ease-out`. Se conserva el bloque `prefers-reduced-motion` existente en `system.css`.

---

## 3) Componentes canónicos

`resources/views/components/ui/` es la capa obligatoria. Escribir markup crudo cuando existe componente es deuda.

| Componente | Uso |
|---|---|
| `<x-ui.card>` | toda superficie de contenido |
| `<x-ui.table>` | todo listado tabular |
| `<x-ui.toolbar>` | filtros y acciones de cabecera |
| `<x-ui.action-buttons>` / `<x-ui.action-icon>` | acciones de fila |
| `<x-ui.empty-row>` | estado vacío dentro de tabla |
| `<x-form-errors>` | errores de validación |
| `<x-severity-picker>`, `<x-threshold-rule-list>` | umbrales y severidad |

### 3.1 Pendientes de extraer (no existen y hacen falta)

- `<x-ui.status>` — **píldora de estado única**. Unifica `badge` y `dbx-pill`, que hoy conviven. API: `:level` (`healthy|warning|critical|neutral`), `:label`, `:title`. Ninguna vista vuelve a escribir `class="badge …"`.
- `<x-ui.confirm-form>` — sustituye los 13 `confirm()` nativos. Diálogo propio con: acción, **nombre del objeto**, impacto ("se borrará también el histórico"), y confirmación explícita. Para borrado definitivo, exigir tecleo del identificador.
- `<x-ui.field>` — envuelve `label` + control y **genera el `id` y el `for`** automáticamente. Es la corrección estructural del problema de etiquetas: se arregla una vez, no formulario a formulario.
- `<x-ui.bulk-bar>` — barra contextual de selección múltiple (§5.2).
- `<x-ui.empty-state>` — vacío accionable fuera de tabla (§6.3).

### 3.2 Reglas de acción

- **Un solo patrón de acciones de fila en toda la app.** Hoy Tiendas/Usuarios usan iconos y Reglas/Cola texto. Se fija: **icono + `aria-label` + tooltip**, con texto solo en la acción primaria de cabecera.
- Botón primario: uno por pantalla. El resto, secundario o fantasma.
- Acción destructiva: siempre la última, siempre con `<x-ui.confirm-form>`.

---

## 4) Menú lateral

Es el mayor salto de percepción por unidad de esfuerzo.

### 4.1 Agrupación

```
OPERACIÓN      Dashboard · Cola · Alertas · Impresoras
CONFIGURACIÓN  Reglas · Tiendas · Usuarios
SISTEMA        Ajustes · Telegram
DESARROLLO     Pruebas        (solo Admin, marcado como entorno de pruebas)
```

**Alertas sube junto a Cola**: hoy queda al final del `<nav>` (`layouts/app.blade.php`), separada del ciclo operativo al que pertenece. Los títulos de grupo son etiquetas pequeñas, no enlaces.

### 4.2 Contadores

Cada enlace de Operación admite un contador discreto a la derecha: alertas críticas, trabajos bloqueados, cola pendiente. Convierte la navegación en radar. Requisitos:

- Origen: el mismo endpoint que ya alimenta el dashboard; no crear consulta nueva por badge.
- Solo se pinta si `> 0`. Cero no se muestra.
- Rojo únicamente para crítico; el resto neutro.
- Accesible: el número forma parte del texto accesible del enlace (`Alertas, 3 críticas`), no un `<span>` mudo.

### 4.3 Estado activo

**Una sola señal fuerte**: fondo azul **o** barra lateral izquierda. Hoy se acumulan fondo, borde, sombra y cuña. Se conserva `aria-current="page"`, que ya está bien implementado.

### 4.4 Pie del lateral

Añadir identidad de sesión (nombre + badge de rol) junto a los toggles de tema y compacto ya existentes. **Cerrar sesión** baja aquí y descarga la cabecera.

### 4.5 Modo compacto

El comportamiento actual (iconos + tooltip + persistencia en `localStorage` + anti-flash `sc-init`) es correcto y se conserva. Añadir: retardo breve en tooltip y contador resumido a un punto cuando no cabe el número.

---

## 5) Gramática por pantalla

### 5.1 Dashboard

Dos zonas explícitamente separadas, con encabezado propio:

1. **Ahora requiere atención** — incidencias críticas, trabajos sin reenviar, impresoras desconectadas, marca de última actualización.
2. **Tendencia del periodo** — evolución y diagnóstico.

Los bloques por tienda deben **llevar a una inspección concreta** (cola filtrada por esa tienda), no repetir el KPI de arriba.
Resolver la duplicidad `dashboard.blade.php` / `dashboard-local.blade.php`: una vista, un contrato de datos (`docs/contrato-kpi-dashboard.md`).

### 5.2 Cola

- Filtros y acciones en **barra operativa persistente** (`<x-ui.toolbar>` fijada arriba).
- Los controles masivos **no son protagonistas permanentes**: aparecen en `<x-ui.bulk-bar>` al seleccionar filas, mostrando cantidad, consecuencia y acciones.
- El contador de selección ya usa `aria-live="polite"` correctamente; se mantiene.

### 5.3 Alertas

- Separar **activas** de **histórico** (pestañas o secciones, no mezcla).
- Cada alerta responde visualmente a cinco preguntas: qué falla · desde cuándo · impacto · causa conocida · acción disponible.

### 5.4 Impresoras

- Separar **configuración** (host, cola de spool, reglas) de **salud en vivo** (conectividad, IPP, racha de fallos).
- Jerarquizar los tres no-estados que hoy se confunden: `sin host` (configuración incompleta) · `sin comprobar` (`IppSupported = null`) · `error real` (racha de fallos). Solo el tercero es rojo.
- Enlazar a cola y reglas afectadas de esa impresora.

### 5.5 Reglas

- Se conserva el master-detail tienda → reglas: es el patrón más maduro de la app.
- Cada regla se muestra como **frase legible**: *"Si [tipo] y [canal], enviar a [impresora], prioridad [n]"*.
- Advertir visualmente: comodines, prioridades solapadas y reglas que nunca se alcanzan por quedar tapadas por otra de mayor prioridad.

### 5.6 Tiendas

- Los contadores de usuarios e impresoras son **enlaces** al listado ya filtrado, no números muertos.
- Añadir columna de salud operativa (`<x-ui.status>`), que convierte el CRUD en centro de administración.

### 5.7 Usuarios

- Mostrar **badge de rol** y **nombre de tienda**, no el ID.
- El formulario explica en línea qué permisos cambia cada rol y cuándo la tienda pasa a ser obligatoria.

### 5.8 Ajustes

Los umbrales necesitan **vista previa de impacto**: *"con 12 trabajos en cola, esta tienda quedará en Warning"*. El valor numérico solo no es comprensible.

### 5.9 Telegram

Cuatro bloques separados: destino (chats) · política de notificación (severidad mínima, intervalo) · estado del servicio · prueba.
La prueba deja **evidencia persistente**: resultado, fecha y destinatarios alcanzados.

### 5.10 Pruebas

Sandbox técnico. Fuera de la navegación operativa (grupo Desarrollo) y con banner que identifica el entorno.

---

## 6) Sinergias y navegación

### 6.1 El contexto se conserva al navegar

Regla: si se navega desde una entidad, la entidad viaja en la URL.

- Alerta de tienda → Cola ya filtrada por esa tienda e incidencia.
- Impresora → sus reglas y sus trabajos.
- Regla → impresora destino y efecto real.

Implementación esperada: parámetros de query respetados por los controladores existentes, sin estado en sesión.

### 6.2 Un único patrón de detalle

- **Edición** → página completa.
- **Inspección rápida** → panel lateral o modal.

Sin mezclas. Hoy conviven tablas, títulos y acciones sin gramática común.

### 6.3 Estados vacíos accionables

"No hay alertas" es correcto pero insuficiente. El vacío ofrece salida: *"Ver cola"*, *"Cambiar periodo"*, o explica qué condición se está cumpliendo.

### 6.4 Confirmaciones

Ningún `confirm()` nativo sobrevive. Ver `<x-ui.confirm-form>` (§3.1).

---

## 7) Accesibilidad (obligatorio, no opcional)

1. **Etiquetas**: todo control tiene `label` asociado por `for`/`id`. Corregir vía `<x-ui.field>`; prioridad `tiendas/form` y `usuarios/form` (0 asociaciones), después `impresoras/form` y `reglas/form` (1 sin asociar cada uno).
2. **`autocomplete`**: obligatorio en credenciales (`username`, `current-password`, `new-password`) y campos frecuentes. Hoy hay 4 en todo el proyecto.
3. **`aria-live` acotado**: el `aria-live="polite"` sobre el contenedor completo del dashboard (`dashboard.blade.php:132`) hace que un lector de pantalla relea la página entera en cada refresco. Se sustituye por una región pequeña que anuncie solo *"Actualizado hace X s"* y los cambios relevantes. Mantener los `aria-live` acotados existentes (contador de selección, countdown).
4. **Foco visible** en todo elemento interactivo, sin excepción por estética. El bloque `:focus-visible` de `system.css` se conserva.
5. **Contraste** mínimo 4.5:1 en texto; el estado no se comunica solo por color (icono o texto acompañan a la píldora).
6. **Teclado**: tablas y barras contextuales operables sin ratón; el diálogo de confirmación atrapa el foco y cierra con `Esc`.
7. **Modo oscuro y `prefers-reduced-motion`**: ya resueltos en `system.css`. **Preservar estas decisiones al consolidar hojas** — es el riesgo principal de la fase de limpieza CSS.

Criterio de referencia: Web Interface Guidelines.

---

## 8) Checklist de revisión

- [ ] Ningún valor de radio, espaciado o ancho fuera de la escala de §2.
- [ ] Ningún `!important` nuevo. Si parece necesario, el bug está en la especificidad.
- [ ] Estado mostrado con `<x-ui.status>`; no quedan `badge` ni `dbx-pill` sueltos.
- [ ] Acciones de fila con icono + `aria-label`, patrón idéntico en todas las pantallas.
- [ ] Acción destructiva con `<x-ui.confirm-form>`, nunca `confirm()`.
- [ ] Todo control con `label` asociado y `autocomplete` cuando aplica.
- [ ] Vacío accionable, no solo descriptivo.
- [ ] Navegación entre entidades conserva el contexto en la URL.
- [ ] Rojo solo en crítico o destructivo.
- [ ] Sin `transition: all`.

---

## 9) Orden de ejecución

Ordenado por impacto percibido / coste. Cada fase es independiente y desplegable.

| Fase | Alcance | Por qué primero |
|---|---|---|
| **1. Fundamentos** | Tokens únicos en `system.css`, eliminar duplicado de `app.css:1105`, `--btn-radius`, literales de radio, 11 anchos → 1, quitar `transition: all` | Desbloquea todo lo demás; sin escala única las fases siguientes reintroducen la deriva |
| **2. Lateral** | Agrupación, contadores, estado activo único, pie con sesión y rol | Máximo salto de percepción, riesgo bajo, un solo archivo |
| **3. Estado y acciones** | `<x-ui.status>`, `<x-ui.confirm-form>`, patrón único de acciones de fila | Elimina las dos incoherencias más visibles y sube la seguridad de los borrados |
| **4. Formularios y a11y** | `<x-ui.field>`, `autocomplete`, `aria-live` del dashboard | Corrige accesibilidad de raíz con diff acotado |
| **5. Tipografía** | Decidir familia única, retirar mayúsculas generalizadas, alinear login | Cambio de percepción alto, pero conviene tras estabilizar tokens |
| **6. Pantallas** | Dashboard (dos zonas), Cola (`bulk-bar`), Alertas (activas/histórico), Impresoras (config vs. salud), Reglas (frase legible), Tiendas/Usuarios (contexto) | Trabajo por pantalla, ya sobre un sistema consistente |
| **7. Consolidación CSS** | Reducir los 323 `!important` de `system.css`, fusionar capas | El más caro y el de mayor riesgo de regresión; se hace último y con el checklist §8 como red |

---

## 10) Vistas cubiertas

Con `<x-ui.*>` adoptado: `alertas`, `alertas/configuracion`, `cola`, `dashboard/partials/filters`, `dashboard-local`, `impresoras/index`, `impresoras/form`, `pruebas/*`, `reglas/index`, `reglas/form`, `tiendas/index`, `tiendas/form`, `usuarios/index`, `usuarios/form`.

Fuera del sistema, pendientes de alinear: `auth/login` (tipografía propia + fallback Tailwind CDN), `dashboard.blade.php` (duplicidad con `dashboard-local`).
