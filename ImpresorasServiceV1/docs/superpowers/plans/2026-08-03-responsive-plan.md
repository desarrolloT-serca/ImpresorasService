# Responsive Layout — Plan de Implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establecer un sistema responsivo coherente con dos modos bien definidos y sin breakpoints contradictorios entre archivos.

**Architecture:** Dos modos de layout separados por un único breakpoint (480px). Por encima: sidebar `position: fixed` siempre visible, hover-expand como overlay, pin empuja el contenido. Por debajo: hamburger + sidebar slide-in. Todo el comportamiento visual se resuelve en CSS; el JS solo gestiona el drawer móvil.

**Tech Stack:** CSS puro (sin JS adicional), Vite para compilar assets, `app.css` + `system.css` + `dbx.css`.

## Global Constraints

- Sin frameworks JS externos ni dependencias nuevas.
- No romper el comportamiento de la sidebar a pantalla completa (≥1025px) — el commit `8d43db8` es la referencia.
- El `npm run build` debe pasar sin errores antes de cada commit.
- Probar siempre en tres anchuras: 400px (móvil), 800px (intermedio), 1440px (escritorio).
- Solo cambios en CSS y en el handler `resize` del JS en `app.blade.php`.

---

## Auditoría de breakpoints actuales (referencia, no tocar)

| Archivo | Breakpoint | Qué controla | Estado |
|---|---|---|---|
| `app.css:763` | `max-width: 1024px` | Sidebar fixed + overlay + hamburger visible | ❌ demasiado ancho |
| `app.css:1118` | `min-width: 1025px` | `margin-left: 284px` en content | ❌ sobreescrito por system.css |
| `app.css:1277` | `max-width: 1024px` | sidebar-toggle visible | ❌ demasiado ancho |
| `system.css:95` | `min-width: 1025px` | `margin-left: 248px` en content | ❌ solo sirve a desktop |
| `system.css:633` | `max-width: 1024px` | Sidebar 284px, content margin 0 | ❌ rompe modo intermedio |
| `system.css:3259` | `max-width: 1024px` | Oculta pin button | ❌ demasiado ancho |
| `system.css:3266` | `min-width: 1025px` | Sidebar transition + compact/pin | ✅ correcto en destino |
| `system.css:3436` | `min-width: 1025px` | Preload sidebar-pinned sin flash | ✅ correcto |
| `system.css:3450` | `max-width: 1024px` | Resetea compact overrides en mobile | ❌ mezcla móvil con tablet |
| `dbx.css:1` | `min-width: 1025px` | app-shell layout | ❌ usa breakpoint sidebar |
| `dbx.css:618` | `min-width: 1025px` | Sidebar appearance overrides | ❌ usa breakpoint sidebar |
| `dbx.css:1014` | `min-width: 1025px` | Sidebar appearance overrides | ❌ usa breakpoint sidebar |
| Resto (`760px`,`900px`,`980px`…) | Varios | Componentes de contenido | ✅ independientes, no tocar |

---

## Diseño objetivo

```
≤480px  → MÓVIL
  - Sidebar oculta (translateX -100%)
  - Hamburger (sidebar-toggle) visible
  - Overlay oscuro al abrir
  - content: margin-left: 0

481px–∞ → DESKTOP/INTERMEDIO (un solo sistema)
  - Sidebar position: fixed, siempre visible
  - Compact (72px): estado por defecto cuando no pinned
  - Hover nav/brand: expande a 248px como OVERLAY (content no se mueve)
  - Pin: content pasa a margin-left: 248px (sidebar empuja)
  - Pin button: siempre visible
  - content: margin-left: 72px (compact) | 248px (pinned)
```

**Modelo mental:** la sidebar nunca empuja el contenido al hacer hover. Solo el pin empuja. Funciona igual a 800px que a 1440px.

---

## Archivos a modificar

| Archivo | Cambios |
|---|---|
| `resources/css/app.css` | Bajar `1024px`→`480px` en bloques de móvil; eliminar bloque `min-width: 1025px` que ya no hace nada |
| `resources/css/system.css` | Reescribir los 4 bloques `1024/1025px` del sidebar; ampliar hover-expand a overlay en todos los anchos |
| `resources/css/dbx.css` | Cambiar 3 bloques `min-width: 1025px` que afectan al shell a `min-width: 481px` |
| `resources/views/layouts/app.blade.php` | Cambiar `> 1024` a `> 480` en el handler resize |

---

## Task 1: app.css — bajar el breakpoint móvil

**Files:**
- Modify: `resources/css/app.css:763-792` (bloque sidebar fixed, overlay, toggle)
- Modify: `resources/css/app.css:1118-1126` (bloque min-width 1025px — eliminar)
- Modify: `resources/css/app.css:1277-1284` (bloque sidebar-toggle visible)

- [ ] **Paso 1: Cambiar el bloque de sidebar móvil de `max-width: 1024px` a `max-width: 480px`**

En `resources/css/app.css`, localizar el bloque que empieza con `@media (max-width: 1024px)` en la línea ~763 y que contiene `.app-sidebar { position: fixed; ... }`. Reemplazar:

```css
/* ANTES */
@media (max-width: 1024px) {
    .app-sidebar {
        position: fixed;
        top: 0;
        left: 0;
        height: 100vh;
        z-index: 50;
        transform: translateX(-100%);
        box-shadow: 0 22px 45px rgba(2, 6, 23, 0.45);
    }

    body.sidebar-open .app-sidebar {
        transform: translateX(0);
    }

    .sidebar-overlay {
        position: fixed;
        inset: 0;
        background: rgba(2, 6, 23, 0.45);
        z-index: 40;
    }

    body.sidebar-open .sidebar-overlay {
        display: block;
    }

    .sidebar-toggle {
        display: inline-flex;
    }
}
```

```css
/* DESPUÉS */
@media (max-width: 480px) {
    .app-sidebar {
        position: fixed;
        top: 0;
        left: 0;
        height: 100vh;
        z-index: 50;
        transform: translateX(-100%);
        box-shadow: 0 22px 45px rgba(2, 6, 23, 0.45);
    }

    body.sidebar-open .app-sidebar {
        transform: translateX(0);
    }

    .sidebar-overlay {
        position: fixed;
        inset: 0;
        background: rgba(2, 6, 23, 0.45);
        z-index: 40;
    }

    body.sidebar-open .sidebar-overlay {
        display: block;
    }

    .sidebar-toggle {
        display: inline-flex;
    }
}
```

- [ ] **Paso 2: Eliminar el bloque `min-width: 1025px` de app.css**

Localizar (~línea 1118):

```css
@media (min-width: 1025px) {
    .app-shell .app-content {
        margin-left: 284px !important;
    }

    .app-shell .app-sidebar {
        width: 284px !important;
    }
}
```

Eliminarlo completamente. `system.css` ya gestiona estos valores con más precisión.

- [ ] **Paso 3: Bajar el bloque sidebar-toggle de `max-width: 1024px` a `max-width: 480px`**

Localizar (~línea 1277):

```css
/* ANTES */
@media (max-width: 1024px) {
    .sidebar-toggle {
        display: inline-flex !important;
```

```css
/* DESPUÉS */
@media (max-width: 480px) {
    .sidebar-toggle {
        display: inline-flex !important;
```

- [ ] **Paso 4: Build y verificar sin errores**

```
npm run build
```

Esperado: `✓ built in X.XXs` sin errores.

- [ ] **Paso 5: Commit**

```
git add resources/css/app.css
git commit -m "fix(responsive): bajar breakpoint móvil de 1024px a 480px en app.css"
```

---

## Task 2: system.css — reescribir el sistema de sidebar

**Files:**
- Modify: `resources/css/system.css:95-99` (content margin inicial)
- Modify: `resources/css/system.css:633-640` (bloque max-width 1024px — sidebar/content)
- Modify: `resources/css/system.css:3259-3263` (pin button oculto en ≤1024px)
- Modify: `resources/css/system.css:3266-3296` (desktop sidebar system)
- Modify: `resources/css/system.css:3436-3446` (preload sidebar-pinned)
- Modify: `resources/css/system.css:3450-3464` (reset compact overrides en mobile)

- [ ] **Paso 1: Cambiar el bloque de margin inicial (`min-width: 1025px` → `min-width: 481px`, valor `72px`)**

Localizar (~línea 95):

```css
/* ANTES */
@media (min-width: 1025px) {
    .app-shell .app-content {
        margin-left: 248px !important;
    }
}
```

```css
/* DESPUÉS */
@media (min-width: 481px) {
    .app-shell .app-content {
        margin-left: 72px !important;
    }
}
```

Este es el margin-left base para cualquier pantalla no-móvil. La sidebar compacta (72px) siempre está visible, y el contenido no se superpone con ella.

- [ ] **Paso 2: Reescribir el bloque `max-width: 1024px` de sidebar/content (~línea 633)**

Localizar:

```css
/* ANTES */
@media (max-width: 1024px) {
    .app-shell .app-sidebar {
        width: 284px !important;
    }

    .app-shell .app-content {
        margin-left: 0 !important;
        padding: 14px;
    }
```

```css
/* DESPUÉS */
@media (max-width: 480px) {
    .app-shell .app-content {
        margin-left: 0 !important;
        padding: 14px;
    }
```

El `width: 284px` del sidebar se elimina — en móvil el sidebar está oculto (translateX) y el ancho lo gestiona su regla base. El margin-left: 0 solo aplica en móvil real (≤480px).

- [ ] **Paso 3: Cambiar el ocultamiento del pin button a `max-width: 480px`**

Localizar (~línea 3259):

```css
/* ANTES */
@media (max-width: 1024px) {
    .sidebar-pin-btn {
        display: none !important;
    }
}
```

```css
/* DESPUÉS */
@media (max-width: 480px) {
    .sidebar-pin-btn {
        display: none !important;
    }
}
```

El pin button es irrelevante en móvil (no hay sidebar visible). En todo lo demás (≥481px) debe ser visible.

- [ ] **Paso 4: Reescribir el bloque desktop de sidebar (`min-width: 1025px` → incluir overlay en hover)**

Localizar (~línea 3265-3296) el bloque `/* Desktop: width + transition */`. Reemplazarlo:

```css
/* Sidebar: sistema unificado para ≥481px
   - Compact (72px): visible siempre, contenido en margin-left: 72px
   - Hover-expand (248px): overlay sobre contenido (margin no cambia)
   - Pinned (248px): contenido pasa a margin-left: 248px             */
@media (min-width: 481px) {
    .app-shell .app-sidebar {
        position: fixed !important;
        top: 0 !important;
        left: 0 !important;
        height: 100vh !important;
        z-index: 50 !important;
        width: 248px !important;
        overflow: hidden;
        transition: width 0.28s ease-in-out !important;
    }

    body.sidebar-compact .app-shell .app-sidebar {
        width: 72px !important;
    }

    /* Hover-expand: sidebar crece pero contenido NO se mueve */
    .app-shell .app-content {
        transition: margin-left 0.28s ease-in-out !important;
    }

    /* Pinned: única situación donde el contenido se desplaza */
    body.sidebar-pinned .app-shell .app-sidebar,
    html.sidebar-pinned .app-shell .app-sidebar {
        width: 248px !important;
    }

    body.sidebar-pinned .app-shell .app-content,
    html.sidebar-pinned .app-shell .app-content {
        margin-left: 248px !important;
    }
}
```

- [ ] **Paso 5: Actualizar el preload de sidebar-pinned (`min-width: 1025px` → `min-width: 481px`)**

Localizar (~línea 3436):

```css
/* ANTES */
@media (min-width: 1025px) {
    html.sidebar-pinned .app-shell .app-sidebar,
```

```css
/* DESPUÉS */
@media (min-width: 481px) {
    html.sidebar-pinned .app-shell .app-sidebar,
```

- [ ] **Paso 6: Ajustar el reset de compact overrides a `max-width: 480px`**

Localizar (~línea 3450) el bloque `/* Mobile/tablet: reset all compact overrides */`:

```css
/* ANTES */
@media (max-width: 1024px) {
    body.sidebar-compact .app-shell .app-sidebar {
        width: 284px !important;
        padding-left: 14px !important;
        padding-right: 14px !important;
    }

    body.sidebar-compact .app-shell .app-content {
        margin-left: 0 !important;
```

```css
/* DESPUÉS */
@media (max-width: 480px) {
    body.sidebar-compact .app-shell .app-sidebar {
        width: 272px !important;
    }

    body.sidebar-compact .app-shell .app-content {
        margin-left: 0 !important;
```

En móvil, cuando está oculta (translateX), el ancho "compact" no tiene efecto visual pero igualmente lo reseteamos para consistencia.

- [ ] **Paso 7: Build y verificar**

```
npm run build
```

Esperado: `✓ built in X.XXs` sin errores.

- [ ] **Paso 8: Test manual en tres anchuras**

Abrir la app y redimensionar la ventana del navegador:

| Anchura | Esperado |
|---|---|
| 400px | Hamburger visible, sidebar oculta, contenido a ancho completo |
| 800px | Sidebar compacta (72px) siempre visible, pin visible, hover expande sin mover contenido |
| 1440px | Igual que 800px; pin mueve el contenido a 248px |

- [ ] **Paso 9: Commit**

```
git add resources/css/system.css
git commit -m "fix(responsive): sidebar siempre fixed ≥481px, hover-overlay, pin empuja"
```

---

## Task 3: dbx.css — alinear breakpoints del shell

**Files:**
- Modify: `resources/css/dbx.css:1-15` (bloque `min-width: 1025px` — app-shell)
- Modify: `resources/css/dbx.css:618-640` (bloque `min-width: 1025px` — sidebar appearance)
- Modify: `resources/css/dbx.css:1014-1030` (bloque `min-width: 1025px` — sidebar appearance)

Hay tres bloques en `dbx.css` que usan `min-width: 1025px` para estilos del shell/sidebar. Ahora que el sidebar es fixed desde 481px, estos deben aplicar desde 481px también.

- [ ] **Paso 1: Cambiar los tres bloques `min-width: 1025px` a `min-width: 481px` en dbx.css**

Localizar las líneas 1, 618 y 1014 de `dbx.css`. En cada una, cambiar:

```css
/* ANTES */
@media (min-width: 1025px) {
    .app-shell {
```

```css
/* DESPUÉS */
@media (min-width: 481px) {
    .app-shell {
```

Hacer lo mismo para las ocurrencias en líneas ~618 y ~1014 si también afectan al `.app-shell .app-sidebar` o al shell layout. Verificar que los bloques son solo de shell/sidebar y no de contenido (los de contenido se dejan como están).

- [ ] **Paso 2: Build y verificar**

```
npm run build
```

- [ ] **Paso 3: Commit**

```
git add resources/css/dbx.css
git commit -m "fix(responsive): alinear breakpoints del shell en dbx.css a 481px"
```

---

## Task 4: JS — sincronizar el handler resize

**Files:**
- Modify: `resources/views/layouts/app.blade.php` — handler `resize` (~línea 344)

- [ ] **Paso 1: Cambiar el threshold del resize handler**

Localizar en `app.blade.php`:

```javascript
// ANTES
window.addEventListener('resize', function() {
    if (window.innerWidth > 1024) {
        closeSidebar();
    }
});
```

```javascript
// DESPUÉS
window.addEventListener('resize', function() {
    if (window.innerWidth > 480) {
        closeSidebar();
    }
});
```

`closeSidebar()` elimina la clase `sidebar-open` del body. Solo tiene efecto en móvil (donde el drawer se abre con el hamburger). Al pasar de ≤480px a ≥481px, cerramos el drawer para que el sistema fijo tome el control.

- [ ] **Paso 2: Build y verificar**

```
npm run build
```

- [ ] **Paso 3: Test del resize**

1. Abrir la app con la ventana a 400px de ancho.
2. Abrir el sidebar con el hamburger.
3. Ampliar la ventana a 600px — el drawer debe cerrarse automáticamente y aparecer el sidebar compacto.

- [ ] **Paso 4: Commit**

```
git add resources/views/layouts/app.blade.php
git commit -m "fix(responsive): cerrar drawer móvil al pasar a ≥481px en resize"
```

---

## Task 5: Verificación final y paso de regresión

- [ ] **Paso 1: Test en los tres modos**

Abrir la app en el navegador. Probar en este orden:

**Móvil (400px):**
- [ ] El hamburger (≡) es visible en la topbar
- [ ] El sidebar está oculto
- [ ] Al pulsar el hamburger, el sidebar aparece con overlay oscuro
- [ ] Al pulsar fuera del sidebar (en el overlay), se cierra
- [ ] El pin button NO es visible (correcto: en móvil no tiene sentido)

**Intermedio (800px):**
- [ ] El hamburger NO es visible
- [ ] El sidebar compacto (72px) está siempre visible
- [ ] El pin button es visible
- [ ] Al pasar el ratón por la zona de navegación, el sidebar se expande a 248px como overlay (el contenido NO se desplaza)
- [ ] Al salir del sidebar, vuelve a 72px
- [ ] Al pulsar el pin, el sidebar queda fijo a 248px y el contenido pasa a `margin-left: 248px`
- [ ] Al despinnar, el sidebar vuelve a compacto y el contenido a `margin-left: 72px`

**Escritorio (1440px):**
- [ ] Mismo comportamiento que en 800px (el sistema es unificado)
- [ ] El texto "Impresoras Service" es visible en el sidebar expandido

- [ ] **Paso 2: Verificar las pantallas principales**

Navegar a estas páginas y comprobar que no hay regresiones de layout:

- [ ] Dashboard
- [ ] Cola de impresión
- [ ] Impresoras (tabla con filtros)
- [ ] Tiendas
- [ ] Usuarios
- [ ] Reglas de enrutado
- [ ] Ajustes

- [ ] **Paso 3: Build de producción final**

```
npm run build
```

- [ ] **Paso 4: Push**

```
git push origin main
```

---

## Notas de implementación

**¿Por qué un único sistema desde 481px?**
Tener "tablet" y "desktop" como modos CSS distintos multiplica la superficie de bugs. El sidebar fixed con hover-overlay funciona igual de bien a 800px que a 1440px. Solo el pin decide si empuja el contenido. Esta uniformidad elimina la categoría entera de bugs "se rompe a X ancho".

**¿Por qué 480px como corte móvil?**
480px cubre teléfonos en vertical y en horizontal. Por encima ya hay suficiente espacio para mostrar los 72px del sidebar compacto sin que el contenido quede inutilizable.

**¿Por qué NO tocar los breakpoints de contenido (760px, 900px, 980px…)?**
Esos breakpoints controlan componentes específicos (grids, tablas, filtros) y son independientes del layout del shell. Mezclarlos con el cambio de sidebar generaría exactamente el ciclo de parches que queremos evitar. Si alguno causa problemas, se aborda por separado una vez estabilizado el shell.

**Referencia de estado limpio:** commit `101814f` (estado actual de main antes de este plan).
