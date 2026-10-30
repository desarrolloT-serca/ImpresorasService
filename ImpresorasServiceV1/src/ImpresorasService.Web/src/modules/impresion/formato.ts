import {
  ESTADOS_TRABAJO as E,
  MAX_NOTA_REIMPRESION,
  type Impresora,
  type MotivoReimpresion,
  type ReglaEnrutado,
  type Salud,
  type SeveridadConexion,
  type Tienda,
  type TrabajoCola,
  type VentanaDashboard,
} from "@serca/contratos"

export type Tono = "ok" | "aviso" | "error" | "info" | "neutro"

// Etiqueta y tono de cada estado, como el PHP (StatusLabels y cola.blade.php).
export const ESTADOS: Record<number, { etiqueta: string; tono: Tono }> = {
  [E.pendiente]: { etiqueta: "Pendiente", tono: "aviso" },
  [E.enrutado]: { etiqueta: "Enrutado", tono: "info" },
  [E.imprimiendo]: { etiqueta: "Imprimiendo", tono: "aviso" },
  [E.aceptado]: { etiqueta: "Aceptado", tono: "ok" },
  [E.impreso]: { etiqueta: "Impreso", tono: "ok" },
  [E.sinConfirmar]: { etiqueta: "Sin confirmar", tono: "ok" },
  [E.reintentoProgramado]: { etiqueta: "Reintento programado", tono: "aviso" },
  [E.cancelado]: { etiqueta: "Cancelado", tono: "neutro" },
  [E.error]: { etiqueta: "Error", tono: "error" },
  [E.bloqueado]: { etiqueta: "Bloqueado", tono: "aviso" },
}

export const TONO_SEVERIDAD: Record<SeveridadConexion, Tono> = {
  healthy: "ok",
  warning: "aviso",
  critical: "error",
  neutral: "neutro",
}

// Clases de cada tono con los colores del ERP (success, warning, destructive).
export const CLASE_TONO: Record<Tono, string> = {
  ok: "bg-success/10 text-success",
  aviso: "bg-warning/10 text-warning",
  error: "bg-destructive/10 text-destructive",
  info: "bg-secondary text-secondary-foreground",
  neutro: "border-border text-muted-foreground",
}

// Punto de color (semáforo) de cada tono: selector de tienda, esquema de reglas.
export const COLOR_PUNTO: Record<Tono, string> = {
  ok: "bg-success",
  aviso: "bg-warning",
  error: "bg-destructive",
  info: "bg-secondary-foreground",
  neutro: "bg-muted-foreground/40",
}

// Reintentar es seguro si el trabajo no ha salido (pendiente o error). Sin
// confirmar: el sistema no sabe si salió, reimprimir puede duplicarlo.
// Cancelar, en esos y en bloqueado (como el PHP).
export const puedeReintentar = (s: number) => s === E.pendiente || s === E.error
// Reimprimir es lo que ya salió, con o sin confirmación (mal impreso, perdido,
// mojado...): puede duplicarlo, así que va con aviso y motivo. Lo que acaba de
// salir y espera confirmación (aceptado), no.
export const puedeReimprimir = (s: number) =>
  s === E.sinConfirmar || s === E.impreso

// Qué falta para poder reimprimir, o null: motivo; con «Otro», explicarlo; y
// aceptar que puede salir duplicado. Mismas reglas que RoutingService.
export function faltaParaReimprimir(d: {
  motivo: MotivoReimpresion | ""
  nota: string
  entiendo: boolean
}) {
  if (!d.motivo) return "Elige el motivo"
  if (d.motivo === "OTRO" && !d.nota.trim()) return "Explica el motivo"
  if (d.nota.trim().length > MAX_NOTA_REIMPRESION)
    return `La nota admite como mucho ${MAX_NOTA_REIMPRESION} caracteres`
  if (!d.entiendo) return "Confirma que entiendes que puede salir duplicado"
  return null
}
export const puedeCancelar = (s: number) =>
  puedeReintentar(s) || s === E.sinConfirmar || s === E.bloqueado

// Fechas UTC de la API en hora de Madrid: "DD/MM/AAAA HH:MM".
const fmtFechaHora = new Intl.DateTimeFormat("es-ES", {
  timeZone: "Europe/Madrid",
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
})
export const fechaHora = (iso: string | null) =>
  iso ? fmtFechaHora.format(new Date(iso)).replace(",", "") : "-"

// Documento del ERP de un trabajo (contrato de la tabla de entrada,
// crear-esquema.sql): el tipo es el código del ERP antiguo y el id externo, el
// número, con «-n» en cada reimpresión. Las pruebas no corresponden a ninguno.
// ponytail: sin enlace hasta que el ERP tenga pantalla de documento; entonces,
// abrir(<su panel>, `${tipo}-${numero}`) con un Button variant="link".
export const TIPOS_DOCUMENTO: Record<string, string> = {
  A: "Albarán",
  C: "Contado",
  P: "Presupuesto",
}
export function documentoErp(tipo: string, idExterno: string) {
  const numero = Number(/^(\d{1,10})(?:-\d{1,3})?$/.exec(idExterno)?.[1])
  return TIPOS_DOCUMENTO[tipo] && numero > 0 && numero <= 2147483647
    ? { tipo, nombre: TIPOS_DOCUMENTO[tipo], numero }
    : null
}

// Franjas de la gráfica, en minutos (las de la API; 0 = automática, que la
// elige la API según el periodo). Cada periodo ofrece las que dan al menos 2
// tramos y como mucho 500.
export const FRANJAS = [
  { minutos: 0, etiqueta: "Automática" },
  { minutos: 15, etiqueta: "15 min" },
  { minutos: 30, etiqueta: "30 min" },
  { minutos: 60, etiqueta: "1 h" },
  { minutos: 120, etiqueta: "2 h" },
  { minutos: 240, etiqueta: "4 h" },
  { minutos: 720, etiqueta: "12 h" },
  { minutos: 1440, etiqueta: "1 día" },
]
const MINUTOS_PERIODO: Record<VentanaDashboard, number> = {
  today: 1440,
  "7d": 7 * 1440,
  "30d": 30 * 1440,
}
export const franjasDe = (ventana: VentanaDashboard) =>
  FRANJAS.filter(
    ({ minutos: f }) =>
      f === 0 ||
      (MINUTOS_PERIODO[ventana] / f >= 2 &&
        Math.ceil(MINUTOS_PERIODO[ventana] / f) <= 500)
  )

// Quién hizo una acción, para leerlo. La API lo guarda como «ERP <código>
// <nombre>» si entró con el pase del ERP (PaseErp.cs: el prefijo lo distingue
// de un usuario del PHP en la base) y como su login si entró por el PHP. En
// pantalla, código de operador y nombre por separado; el login va de nombre.
export function autor(actor: string | null) {
  const erp = actor ? /^ERP (\S+)(?: (.+))?$/.exec(actor) : null
  return erp
    ? { codigo: erp[1], nombre: erp[2] ?? null }
    : { codigo: null, nombre: actor }
}

// Lo de hoy, solo la hora; lo de otros días, fecha y hora (decisión del
// usuario, 26/10/2026). «Hoy» es el de Madrid.
export const fechaHoraCorta = (iso: string | null, ahora = Date.now()) => {
  if (!iso) return "-"
  const completa = fechaHora(iso)
  return completa.slice(0, 10) ===
    fechaHora(new Date(ahora).toISOString()).slice(0, 10)
    ? completa.slice(11)
    : completa
}

// Hora de un trabajo en la cola. Reintentar o reimprimir reutiliza el trabajo:
// si ha vuelto a salir, la hora es la de esa salida y lo marca un icono; si aún
// no, la de cuando se volvió a mandar (la cola ordena por ella), y sin nada de
// eso, la de llegada. Reintento: venía de un error, no había salido. Reimpresión:
// venía de «sin confirmar», puede haber salido ya y la copia ser un duplicado
// (manda sobre el reintento). El detalle va en el title.
export function horaTrabajo(
  t: Pick<
    TrabajoCola,
    "createdAtUtc" | "sentAtUtc" | "lastRequeuedAtUtc" | "retries" | "reprints"
  >,
  ahora = Date.now()
) {
  const veces = (n: number) => (n === 1 ? "1 vez" : `${n} veces`)
  const marca = t.reprints ? "reimpresion" : t.retries ? "reintento" : null
  const hora = marca
    ? (t.sentAtUtc ?? t.lastRequeuedAtUtc ?? t.createdAtUtc)
    : t.createdAtUtc
  const detalle = [
    `Llegó ${fechaHora(t.createdAtUtc)}`,
    t.retries ? `Reintentado tras un error ${veces(t.retries)}` : null,
    t.reprints ? `Reimpreso ${veces(t.reprints)}: puede estar duplicado` : null,
    marca &&
      (t.sentAtUtc
        ? `Salió de nuevo ${fechaHora(t.sentAtUtc)}`
        : t.lastRequeuedAtUtc
          ? `Se volvió a mandar ${fechaHora(t.lastRequeuedAtUtc)}; aún no ha salido`
          : "Aún no ha vuelto a salir"),
  ]
  return {
    marca,
    hora: fechaHoraCorta(hora, ahora),
    detalle: detalle.filter(Boolean).join("\n"),
  } as const
}

// Cuánto hace, como el «Desde» del PHP: «ahora mismo», «12 min», «3 h 05 min»,
// «2 d 4 h». La fecha exacta va en el title.
export function hace(iso: string | null, ahora = Date.now()) {
  if (!iso) return "sin dato"
  const min = Math.floor((ahora - new Date(iso).getTime()) / 60_000)
  if (Number.isNaN(min)) return "sin dato"
  if (min < 1) return "ahora mismo"
  if (min < 60) return `${min} min`
  const h = Math.floor(min / 60)
  if (h < 24)
    return min % 60
      ? `${h} h ${String(min % 60).padStart(2, "0")} min`
      : `${h} h`
  return `${Math.floor(h / 24)} d ${h % 24} h`
}

export const SALUD: Record<Salud, { etiqueta: string; tono: Tono }> = {
  critical: { etiqueta: "Crítica", tono: "error" },
  warning: { etiqueta: "Aviso", tono: "aviso" },
  info: { etiqueta: "Info", tono: "info" },
  healthy: { etiqueta: "Saludable", tono: "ok" },
}

export const nombreTienda = (id: number, tiendas: readonly Tienda[]) => {
  const t = tiendas.find((x) => x.storeId === id)
  return t ? `${id} · ${t.name}` : String(id)
}

export const conProblema = (i: Impresora) =>
  i.connectivitySeverity === "critical" || i.connectivitySeverity === "warning"

// Estado de un grupo de impresoras, como is-printer-status del PHP: manda el
// peor caso.
export function resumen(impresoras: readonly Impresora[]): {
  tono: Tono
  texto: string
} {
  const errores = impresoras.filter(
    (i) => i.connectivitySeverity === "critical"
  ).length
  const avisos = impresoras.filter(
    (i) => i.connectivitySeverity === "warning"
  ).length
  if (!impresoras.length) return { tono: "neutro", texto: "Sin impresoras" }
  if (errores) return { tono: "error", texto: `${errores} con error` }
  // Aviso: sin host o sin comprobar.
  if (avisos)
    return { tono: "aviso", texto: plural(avisos, "pendiente", "pendientes") }
  return { tono: "ok", texto: "OK" }
}

// Tope del eje de la gráfica, redondeado al alza a un múltiplo del paso, como
// el PHP: 7 → 8, 47 → 50, 130 → 150, 430 → 500.
export function escala(maximo: number) {
  const bruto = Math.max(maximo, 1)
  const paso = bruto <= 10 ? 2 : bruto <= 50 ? 10 : bruto <= 200 ? 25 : 100
  return Math.max(paso, Math.ceil(bruto / paso) * paso)
}

// Orden en que el RoutingResolver prueba las reglas: de 5 (tienda + tipo +
// canal) a 1 (global sin tipo ni canal); dentro de cada nivel, la de menor
// prioridad. 0: una global con tipo o canal, que no se aplica nunca.
export const NIVELES_REGLA = [
  "No se aplica nunca",
  "Global",
  "Solo tienda",
  "Tienda + canal",
  "Tienda + tipo",
  "Tienda + tipo + canal",
] as const

export function nivelRegla(
  r: Pick<ReglaEnrutado, "storeId" | "documentType" | "channel">
) {
  const tipo = !!r.documentType?.trim()
  const canal = !!r.channel?.trim()
  if (r.storeId === null) return tipo || canal ? 0 : 1
  return tipo && canal ? 5 : tipo ? 4 : canal ? 3 : 2
}

// Las reglas en el orden en que se prueban: nivel de más a menos y, dentro,
// prioridad y número (el desempate del RoutingResolver).
export const enOrden = <R extends ReglaEnrutado>(reglas: readonly R[]) =>
  [...reglas].sort(
    (a, b) =>
      nivelRegla(b) - nivelRegla(a) ||
      a.priority - b.priority ||
      a.ruleId - b.ruleId
  )

// "tcp/9100" → "9100".
export const puerto = (transporte: string | null) =>
  transporte?.match(/\/(\d+)$/)?.[1] ?? transporte ?? "-"

export type Grupo = { storeId: number | null; reglas: ReglaEnrutado[] }

// Una tienda por grupo (en orden de código) y la global al final, cada uno con
// sus reglas en el orden en que se prueban. La tabla numera con esto también.
export function agrupar(reglas: readonly ReglaEnrutado[]): Grupo[] {
  const tiendas = [...new Set(reglas.map((r) => r.storeId))].sort(
    (a, b) => (a ?? Infinity) - (b ?? Infinity)
  )
  return tiendas.map((storeId) => ({
    storeId,
    reglas: enOrden(reglas.filter((r) => r.storeId === storeId)),
  }))
}

export const tituloGrupo = (
  storeId: number | null,
  tiendas: readonly Tienda[]
) => (storeId === null ? "Global" : nombreTienda(storeId, tiendas))

export const condicion = (r: ReglaEnrutado) =>
  `${r.documentType ? (TIPOS_DOCUMENTO[r.documentType] ?? r.documentType) : "Cualquier tipo"} · ${r.channel ?? "cualquier canal"}`

// Por qué una regla no se aplicaría, o null si se aplica.
export function porQueNo(
  r: ReglaEnrutado,
  impresoras: readonly Impresora[],
  ahora = Date.now()
) {
  if (!r.isActive) return "Inactiva"
  if (nivelRegla(r) === 0) return "Global con tipo o canal: no se aplica nunca"
  if (impresoras.find((i) => i.printerId === r.printerId)?.isActive === false)
    return "Su impresora está inactiva"
  if (
    Date.parse(r.validFromUtc) > ahora ||
    (r.validToUtc !== null && Date.parse(r.validToUtc) < ahora)
  )
    return "Fuera de vigencia"
  return null
}

// «1 trabajo», «2 trabajos» (WEB-14).
export const plural = (n: number, uno: string, varios: string) =>
  `${n} ${n === 1 ? uno : varios}`
