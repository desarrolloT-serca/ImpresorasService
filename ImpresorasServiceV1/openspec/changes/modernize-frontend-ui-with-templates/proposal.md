## Why

El frontend actual mezcla estilos de plantilla (`style.bundle.css`) y Tailwind sin un sistema visual unico, lo que genera una experiencia inconsistente y poco profesional. Es prioritario modernizar la interfaz para elevar la percepcion de calidad y facilitar mantenimiento en futuras iteraciones.

## What Changes

- Definir un sistema visual unificado basado en los templates ya incorporados en el proyecto PHP.
- Reestructurar el layout principal (sidebar, topbar, contenedor de contenido) con jerarquia visual y navegacion coherente.
- Estandarizar componentes transversales: botones, formularios, tablas, alertas, badges y estados vacios.
- Migrar las pantallas core (`dashboard`, `cola`, `impresoras`, `reglas`, `login`) al nuevo sistema visual sin cambiar reglas de negocio.
- Consolidar soporte de tema oscuro con contraste y legibilidad consistentes.
- Eliminar estilos inline y duplicaciones de estilo para reducir deuda visual y tecnica.

## Capabilities

### New Capabilities
- `frontend-ui-modernization`: Define y aplica una capa de presentacion moderna, consistente y reutilizable para el frontend Laravel/PHP usando los templates existentes.

### Modified Capabilities
- Ninguna.

## Impact

- Afecta vistas Blade en `src/ImpresorasService.Web.PHP/resources/views/`.
- Afecta estilos en `src/ImpresorasService.Web.PHP/resources/css/` y uso de assets en `public/assets/`.
- No introduce cambios en contratos API ni endpoints backend.
- Reduce riesgo de regresiones visuales futuras al establecer convenciones de UI reutilizables.
