import { z } from "zod"

// Importes y cantidades viajan como texto: en la base son numeric y no pueden
// pasar por coma flotante. Se operan con una librería decimal, nunca con Number.
export const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, "Importe no válido")

// Fecha de hoy en Madrid (AAAA-MM-DD), comparable con las date que llegan como
// texto. Única para API y web.
export function hoyMadrid() {
  const partes = new Intl.DateTimeFormat("en-CA", {
    timeZone: "Europe/Madrid",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(new Date())
  const parte = (tipo: string) => partes.find((p) => p.type === tipo)!.value
  return `${parte("year")}-${parte("month")}-${parte("day")}`
}

// Entero que llega como texto en la URL: solo dígitos. z.coerce.number()
// aceptaría "1e3", "0x10" o " 12 ".
export const entero = (min: number, max: number) =>
  z
    .union([
      z.number(),
      z
        .string()
        .regex(/^\d{1,10}$/, "Debe ser un entero")
        .transform(Number),
    ])
    .pipe(z.number().int().min(min).max(max))

// Fecha de un filtro de consulta. z.iso.date() admite el año 0000, que
// PostgreSQL rechaza (respondía 500); ninguna consulta busca fuera de este rango.
export const fechaFiltro = z.iso
  .date()
  .refine(
    (v) => v >= "1900-01-01" && v <= "2999-12-31",
    "Fecha fuera de rango (1900-2999)"
  )

// Filtro de varios valores: una lista en el cuerpo de los POST .../buscar, o
// separados por comas en la URL ("001001,002003"). Sin él, todos.
export const lista = <T extends z.ZodType>(valor: T, max: number) =>
  z
    .union([
      z.array(z.unknown()),
      z
        .string()
        .max(20_000)
        .transform((s) => s.split(",")),
    ])
    .pipe(z.array(valor).max(max))
    .optional()

// Código numérico de los criterios de búsqueda avanzada (coincidencia exacta).
export const codigo = z.preprocess(
  (v) => (typeof v === "string" ? v.trim() : v),
  entero(0, 32767)
)
export const SN = z.enum(["S", "N"])

// Opción de un filtro de lista: código y nombre.
export const Opcion = z.object({ cod: z.string(), nombre: z.string() })

// Filas por página que ofrecen las tablas. La API acepta de 1 a 100 (el
// buscador universal pide 6): nunca se devuelven más de 100 filas por consulta.
export const TAMANOS_PAGINA = [25, 50, 100] as const
const tamano = entero(1, 100).default(50)

// Campos de orden de un listado: los ids de las columnas de su tabla. "-campo"
// ordena descendente.
const orden = (campos: readonly [string, ...string[]]) =>
  z.enum([...campos, ...campos.map((c) => `-${c}`)]).optional()

// Consulta paginada estándar de cualquier listado.
export const consultaPagina = (camposOrden: readonly [string, ...string[]]) =>
  z.object({
    pagina: entero(1, 1_000_000).default(1),
    tamano,
    q: z.string().trim().max(100).optional(),
    orden: orden(camposOrden),
  })

export type Pagina<T> = {
  items: T[]
  total: number
  pagina: number
  tamano: number
}
export const pagina = <T extends z.ZodType>(item: T) =>
  z.object({
    items: z.array(item),
    total: z.number().int(),
    pagina: z.number().int(),
    tamano: z.number().int(),
  })

// Paginación por cursor para tablas grandes (cientos de miles de filas o más):
// sin OFFSET ni count(*), cada página cuesta lo mismo. El cursor es opaco para
// el cliente; solo lo interpreta la API.
export const consultaCursor = (camposOrden: readonly [string, ...string[]]) =>
  z.object({
    tamano,
    cursor: z.string().max(500).optional(),
    q: z.string().trim().max(100).optional(),
    orden: orden(camposOrden),
  })

// Filas que cumplen los filtros, contadas hasta un tope: total si no lo pasan,
// masDe (el tope) si lo pasan. Cada módulo dice qué permite cada caso (en
// artículos, con masDe solo se ordena por referencia).
export type PaginaCursor<T> = {
  items: T[]
  siguiente: string | null
  total?: number
  masDe?: number
}
export const paginaCursor = <T extends z.ZodType>(item: T) =>
  z.object({
    items: z.array(item),
    siguiente: z.string().nullable(),
    total: z.number().int().optional(),
    masDe: z.number().int().optional(),
  })

// Formato único de error de la API (RFC 9457, application/problem+json).
export const Problema = z.object({
  title: z.string(),
  status: z.number(),
  detail: z.string().optional(),
  traza: z.string().optional(),
  errores: z
    .array(z.object({ campo: z.string(), mensaje: z.string() }))
    .optional(),
})
export type Problema = z.infer<typeof Problema>

// Cabeceras de contexto que el cliente envía en cada petición.
export const CABECERA_EMPRESA = "x-empresa"
export const CABECERA_TRAZA = "x-request-id"
