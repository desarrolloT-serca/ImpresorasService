## Context

El frontend de `ImpresorasService.Web.PHP` ya dispone de assets de template en `public/assets`, pero actualmente convive con clases Tailwind y estilos inline sin una convencion unica de UI. Esta mezcla produce inconsistencias visuales entre pantallas (`dashboard`, `cola`, `impresoras`, `reglas`, `login`) y aumenta el coste de mantenimiento porque cada vista resuelve estilos de forma diferente.

Restricciones principales:
- Mantener funcionalidad actual y flujos de negocio sin cambios.
- Mantener compatibilidad con el stack Laravel + Vite + Tailwind existente.
- Aprovechar templates ya incluidos, sin introducir dependencias externas adicionales en esta fase.

Stakeholders:
- Operacion (usuarios de cola, impresoras y alertas).
- Administracion (gestion de reglas e impresoras).
- Equipo de desarrollo (mantenibilidad y velocidad de iteracion).

## Goals / Non-Goals

**Goals:**
- Definir un sistema visual coherente para toda la app con layout, componentes y estados consistentes.
- Reutilizar el template ya integrado para acelerar el salto de calidad visual.
- Estandarizar dark mode, tipografia, espaciados y jerarquia de informacion.
- Reducir estilos inline y duplicacion entre vistas Blade.
- Dejar una base reutilizable para nuevas pantallas.

**Non-Goals:**
- No cambiar reglas de negocio, permisos o contratos API.
- No redisenar procesos funcionales (solo presentacion e interaccion visual).
- No realizar en esta fase una migracion completa de framework CSS.
- No incluir rebranding corporativo completo (logo/identidad final) fuera de ajustes de UI.

## Decisions

### Decision 1: Enfoque template-first con normalizacion de componentes
- **Decision**: Tomar el template existente como base visual y encapsular patrones comunes en clases/utilidades reutilizables.
- **Rationale**: Entrega rapida de apariencia profesional y menor esfuerzo que construir un design system desde cero.
- **Alternatives considered**:
  - Tailwind-first puro: mas limpio a largo plazo, pero mas costoso para lograr salto visual inmediato.
  - Reemplazo completo por otro framework: alto riesgo y mayor coste de migracion.

### Decision 2: Migracion por fases con pantalla faro
- **Decision**: Implementar primero `layouts/app.blade.php` + `dashboard.blade.php` como referencia; luego migrar vistas listadas.
- **Rationale**: Minimiza regresiones y permite validar direccion visual antes de extender cambios.
- **Alternatives considered**:
  - Big bang de todas las vistas: mas rapido en teoria, mayor riesgo operativo.

### Decision 3: Contratos de componente visual para tablas/formularios/alertas
- **Decision**: Estandarizar estilos de botones, inputs, selects, tablas, badges y alerts con variantes definidas.
- **Rationale**: Elimina divergencias y facilita consistencia entre modulos.
- **Alternatives considered**:
  - Mantener estilos por vista: perpetua deuda tecnica visual.

### Decision 4: Dark mode unificado por token y clases semanticas
- **Decision**: Centralizar colores de tema (claro/oscuro) y aplicarlos de forma uniforme en layout y componentes.
- **Rationale**: Evita contrastes rotos y mejora legibilidad global.
- **Alternatives considered**:
  - Ajustes aislados por vista: inconsistencias y retrabajo.

## Risks / Trade-offs

- [Riesgo] Mezcla residual de clases antiguas y nuevas durante la migracion por fases.  
  -> Mitigacion: checklist por vista y criterio de "sin inline styles" al cerrar cada pantalla.

- [Riesgo] Regresiones visuales en resoluciones pequenas o dark mode.  
  -> Mitigacion: smoke UI en breakpoints clave y validacion de contraste en pantallas core.

- [Trade-off] Enfoque template-first puede dejar deuda de abstraccion CSS intermedia.  
  -> Mitigacion: documentar convenciones de componentes y refactor incremental posterior.

- [Trade-off] Mantener compatibilidad con lo existente limita cambios radicales iniciales.  
  -> Mitigacion: priorizar coherencia visual y experiencia profesional en iteraciones sucesivas.

## Migration Plan

1. Definir tokens visuales y convenciones base en layout y CSS global.
2. Migrar `dashboard` como pantalla faro para validar la direccion.
3. Migrar secuencialmente `cola`, `impresoras/index`, `reglas/index`, `login`.
4. Sustituir estilos inline por clases normalizadas y limpiar duplicaciones.
5. Ejecutar smoke visual y funcional en login, navegacion, filtros y tablas.
6. Publicar guia corta de patrones UI para futuras pantallas.

Rollback:
- Si aparece regresion visual severa, revertir vista por vista al estado previo usando control de cambios, sin afectar backend ni API.

## Open Questions

- Que nivel de fidelidad al template original se desea (alta fidelidad vs adaptacion parcial)?
- Se incluiran iconos y microinteracciones del template en esta primera fase o en una fase 2?
- Debe definirse una paleta corporativa final ahora o mantener una paleta transitoria basada en el template?
