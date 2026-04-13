# Guia de estilo DBX UI

Esta guia fija la linea visual del dashboard y del resto de pantallas para mantener coherencia en listados y formularios.

## 1) Principios de diseno

- Priorizar lectura operativa: jerarquia clara y datos accionables.
- Densidad controlada: evitar bloques gigantes y ruido visual.
- Consistencia: mismos patrones de filtros, tablas, estados y formularios.
- Escalabilidad: componentes reutilizables (`dbx-*`) para todas las vistas.

## 2) Sistema de componentes DBX

Clases definidas en `resources/css/app.css`.

- **Contenedor**
  - `dbx-wrap`: separacion vertical estandar entre bloques.
  - `dbx-card` + `dbx-card-body`: tarjeta base para cualquier modulo.
- **Cabeceras**
  - `dbx-title-row`, `dbx-title`, `dbx-subtle`.
- **Filtros**
  - `dbx-toolbar`, `dbx-filters`, `dbx-filter-item`, `dbx-filter-label`, `dbx-toggle`.
- **KPIs**
  - `dbx-kpis`, `dbx-kpi`, `dbx-kpi-label`, `dbx-kpi-value`, `dbx-kpi-meta`.
- **Tablas**
  - `dbx-table-wrap`, `dbx-table`, `dbx-empty`.
- **Estados**
  - `dbx-pill` + variantes `healthy`, `warning`, `critical`.
- **Bloques por tienda**
  - `dbx-store-grid`, `dbx-store`, `dbx-store-head`, `dbx-mini-kpis`, `dbx-printers`.
- **Formularios**
  - `dbx-form-grid`, `dbx-form-actions`.

## 3) Reglas de aplicacion

1. Cada pantalla debe empezar con `dbx-wrap`.
2. Listados: bloque de filtros + bloque de tabla (ambos en `dbx-card`).
3. Formularios: una sola tarjeta `dbx-card` y layout `dbx-form-grid`.
4. Estados de salud/criticidad: siempre con `dbx-pill`.
5. Mensajes vacios: usar `dbx-empty`, no texto suelto sin estructura.

## 4) Patrones por tipo de pantalla

### 4.1 Listados administrativos

- Cabecera con CTA principal (crear nuevo).
- Filtros compactos arriba.
- Tabla densa con columnas esenciales.

### 4.2 Formularios CRUD

- Titulo contextual (crear/editar).
- Campos en una sola columna con ritmo uniforme.
- Boton primario a la derecha y cancelar secundario.

### 4.3 Dashboard operativo

- KPIs compactos en fila.
- Bloque de pulso (barras comparativas).
- Alertas priorizadas + detalle por tienda.
- En tienda: lista de impresoras con estado y cola.

## 5) Checklist rapido de revision visual

- [ ] No hay texto plano sin contenedor visual.
- [ ] Filtros y acciones siguen el mismo patron.
- [ ] Tablas con `dbx-table` y vacios con `dbx-empty`.
- [ ] Estados usan `dbx-pill` con colores consistentes.
- [ ] Formularios usan `dbx-form-grid` y `dbx-form-actions`.

## 6) Pantallas migradas a DBX

- `resources/views/dashboard.blade.php`
- `resources/views/cola.blade.php`
- `resources/views/alertas.blade.php`
- `resources/views/impresoras/index.blade.php`
- `resources/views/impresoras/form.blade.php`
- `resources/views/reglas/index.blade.php`
- `resources/views/reglas/form.blade.php`
- `resources/views/tiendas/index.blade.php`
- `resources/views/tiendas/form.blade.php`
- `resources/views/usuarios/index.blade.php`
- `resources/views/usuarios/form.blade.php`
- `resources/views/prueba.blade.php`

## 7) Proxima evolucion recomendada

- Extraer `dbx-*` en componentes Blade reutilizables (`<x-dbx-card>`, `<x-dbx-table>`, etc.).
- Unificar tambien `auth/login` y `welcome` al mismo sistema visual.
