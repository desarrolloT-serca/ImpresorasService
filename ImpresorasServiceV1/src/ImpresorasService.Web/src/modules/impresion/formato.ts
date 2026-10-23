import {
  ESTADOS_TRABAJO as E,
  type Impresora,
  type Salud,
  type SeveridadConexion,
  type Tienda,
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

// Reintentar es seguro si el trabajo no ha salido (pendiente o error). Sin
// confirmar: el sistema no sabe si salió, reimprimir puede duplicarlo.
// Cancelar, en esos y en bloqueado (como el PHP).
export const puedeReintentar = (s: number) => s === E.pendiente || s === E.error
export const puedeReimprimir = (s: number) => s === E.sinConfirmar
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

// Cuánto hace, como el «Desde» del PHP: «ahora mismo», «12 min», «3 h 05 min»,
// «2 d 4 h». La fecha exacta va en el title.
export function hace(iso: string | null, ahora = Date.now()) {
  if (!iso) return "sin dato"
  const min = Math.floor((ahora - new Date(iso).getTime()) / 60_000)
  if (Number.isNaN(min)) return "sin dato"
  if (min < 1) return "ahora mismo"
  if (min < 60) return `${min} min`
  const h = Math.floor(min / 60)
  if (h < 24) return min % 60 ? `${h} h ${String(min % 60).padStart(2, "0")} min` : `${h} h`
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
  const errores = impresoras.filter((i) => i.connectivitySeverity === "critical").length
  const avisos = impresoras.filter((i) => i.connectivitySeverity === "warning").length
  if (!impresoras.length) return { tono: "neutro", texto: "Sin impresoras" }
  if (errores) return { tono: "error", texto: `${errores} con error` }
  // Aviso: sin host o sin comprobar.
  if (avisos) return { tono: "aviso", texto: `${avisos} pendientes` }
  return { tono: "ok", texto: "OK" }
}

// Tope del eje de la gráfica, redondeado al alza a un múltiplo del paso, como
// el PHP: 7 → 8, 47 → 50, 130 → 150, 430 → 500.
export function escala(maximo: number) {
  const bruto = Math.max(maximo, 1)
  const paso = bruto <= 10 ? 2 : bruto <= 50 ? 10 : bruto <= 200 ? 25 : 100
  return Math.max(paso, Math.ceil(bruto / paso) * paso)
}

// "tcp/9100" → "9100".
export const puerto = (transporte: string | null) =>
  transporte?.match(/\/(\d+)$/)?.[1] ?? transporte ?? "-"
