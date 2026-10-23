// Sustituto local de @serca/contratos (packages/contratos del ERP): solo lo
// que usan las piezas copiadas y el módulo de impresión. Al integrar el módulo
// en el ERP, sus contratos pasan a packages/contratos y este fichero se tira.
export * from "./comun.js"
export * from "./impresion.js"

// De packages/contratos/src/sesion.ts del ERP.
// Comodín de permisos: solo para el usuario de desarrollo.
export const TODOS_LOS_PERMISOS = "*"
export const tienePermiso = (permisos: readonly string[], p: string) =>
  permisos.includes(p) || permisos.includes(TODOS_LOS_PERMISOS)
