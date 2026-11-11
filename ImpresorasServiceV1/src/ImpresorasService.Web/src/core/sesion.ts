// Sesión: aquí no hay. Desde el 11/11/2026 al servicio solo se entra desde el ERP con su pase, y el
// módulo vive en ERP_SERCA/erp/apps/web, donde usePermiso sale de su core/sesion.ts (permisos del
// operador). Este proyecto solo guarda el origen del módulo y sus pruebas.
// ponytail: deja ver todo, porque aquí nadie lo ejecuta; quien decide de verdad es la API.
export const usePermiso: (permiso?: string) => boolean = () => true
