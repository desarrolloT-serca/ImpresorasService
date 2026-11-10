import type { Impresora } from "@serca/contratos"
import {
  conNivel,
  fechaHora,
  hace,
  tonoToner,
  type Tono,
  type UmbralToner,
} from "./formato"

// Tipos de estado de una impresora IPP (docs/72, 10/11/2026): una etiqueta corta
// por grupo de motivos (printer-state-reasons, RFC 8011 y PWG 5100.9) para que
// quepa en la tabla. El texto completo sigue en ippStateText (lo da la API).
export type ClaveEstado =
  | "lista"
  | "imprimiendo"
  | "sinPapel"
  | "papelBajo"
  | "atasco"
  | "tapa"
  | "bandeja"
  | "sinToner"
  | "tonerCritico"
  | "tonerBajo"
  | "tambor"
  | "residuos"
  | "mantenimiento"
  | "pausa"
  | "fueraDeLinea"
  | "parada"
  | "aviso"
  | "error"
  | "sinLectura"

export type TipoEstado = { clave: ClaveEstado; etiqueta: string; tono: Tono }

const ETIQUETA: Record<ClaveEstado, string> = {
  lista: "Lista",
  imprimiendo: "Imprimiendo",
  sinPapel: "Sin papel",
  papelBajo: "Papel bajo",
  atasco: "Atasco",
  tapa: "Tapa abierta",
  bandeja: "Bandeja",
  sinToner: "Sin tóner",
  tonerCritico: "Tóner crítico",
  tonerBajo: "Tóner bajo",
  tambor: "Tambor",
  residuos: "Residuos",
  mantenimiento: "Mantenimiento",
  pausa: "En pausa",
  fueraDeLinea: "Fuera de línea",
  parada: "Parada",
  aviso: "Aviso",
  error: "Error",
  sinLectura: "Sin lectura",
}

// Motivo sin sufijo → grupo. El orden importa: gana el primero que encaja.
const GRUPOS: [RegExp, ClaveEstado][] = [
  [/^(media-empty|media-needed)$/, "sinPapel"],
  [/^media-low$/, "papelBajo"],
  [/^media-jam/, "atasco"],
  [/^(door-open|cover-open|interlock-open)$/, "tapa"],
  [
    /^(input-tray-missing|output-tray-missing|output-area-(full|almost-full))$/,
    "bandeja",
  ],
  [/^(toner-empty|marker-supply-empty)$/, "sinToner"],
  [/^(toner-low|marker-supply-low)$/, "tonerBajo"],
  [/^opc-/, "tambor"],
  [/^marker-waste-/, "residuos"],
  [/^(fuser-|developer-|marker-|cleaner-|.*-life-)/, "mantenimiento"],
  [/^(paused|moving-to-paused)$/, "pausa"],
  [/^(offline|shutdown|connecting-to-device|timed-out)$/, "fueraDeLinea"],
]

const RANGO: Record<Tono, number> = {
  error: 0,
  aviso: 1,
  info: 2,
  ok: 3,
  neutro: 4,
}

// Gravedad por el sufijo (RFC 8011 §5.4.12), como IppEstadoImpresora.Describir:
// -error o sin sufijo, crítica; -warning, aviso; -report, nada.
function sufijo(motivo: string): { base: string; tono: Tono | null } {
  if (motivo.endsWith("-report")) return { base: motivo, tono: null }
  if (motivo.endsWith("-warning"))
    return { base: motivo.slice(0, -"-warning".length), tono: "aviso" }
  if (motivo.endsWith("-error"))
    return { base: motivo.slice(0, -"-error".length), tono: "error" }
  return { base: motivo, tono: "error" }
}

/**
 * Los tipos de estado de la impresora, del más grave al menos. Sin lectura
 * reciente (la API lo da como neutral), «Sin lectura»; sin problemas, «Lista» o
 * «Imprimiendo». Cada tipo sale una vez: media-empty y media-needed son los dos
 * «Sin papel».
 */
export function tiposEstado(i: Impresora, umbral?: UmbralToner): TipoEstado[] {
  if (!i.ippCheckedAtUtc || i.ippSeverity === "neutral")
    return [
      { clave: "sinLectura", etiqueta: ETIQUETA.sinLectura, tono: "neutro" },
    ]

  const tipos = new Map<ClaveEstado, Tono>()
  const anade = (clave: ClaveEstado, tono: Tono) => {
    const antes = tipos.get(clave)
    if (!antes || RANGO[tono] < RANGO[antes]) tipos.set(clave, tono)
  }
  for (const m of (i.ippStateReasons ?? "").split(",")) {
    const motivo = m.trim()
    if (!motivo || motivo === "none") continue
    const { base, tono } = sufijo(motivo)
    if (!tono) continue
    const grupo = GRUPOS.find(([re]) => re.test(base))?.[1]
    anade(grupo ?? (tono === "aviso" ? "aviso" : "error"), tono)
  }
  // El tóner por los umbrales de Reglas y avisos, aunque la impresora no avise.
  for (const c of conNivel(i)) {
    const tono = tonoToner(c.nivel, i, umbral)
    if (tono === "error") anade("tonerCritico", "error")
    else if (tono === "aviso") anade("tonerBajo", "aviso")
  }
  if (tipos.has("tonerCritico")) tipos.delete("tonerBajo")
  if (i.ippState === 5 && ![...tipos.values()].includes("error"))
    anade("parada", "error")

  if (!tipos.size)
    return [
      i.ippState === 4
        ? { clave: "imprimiendo", etiqueta: ETIQUETA.imprimiendo, tono: "info" }
        : { clave: "lista", etiqueta: ETIQUETA.lista, tono: "ok" },
    ]
  return [...tipos]
    .map(([clave, tono]) => ({ clave, etiqueta: ETIQUETA[clave], tono }))
    .sort((a, b) => RANGO[a.tono] - RANGO[b.tono])
}

// Lo que dice su panel, sin los estados normales que algunas Brother añaden
// («Espera», «Imprimiendo», «Ahorro», también en italiano): solo si aporta algo.
const NORMALES =
  /^(espera|imprimiendo|ahorro|modo ahorro|risparmio|stampa|attesa|lista|preparada|calentando|sleep|ready|printing|none)$/i
export const mensajePanel = (i: Impresora) => {
  const partes = (i.ippStateMessage ?? "")
    .split(",")
    .map((p) => p.trim())
    .filter((p) => p && !NORMALES.test(p))
  return partes.length ? partes.join(", ") : null
}

// Lo que va al pasar el ratón: todos los tipos, el texto completo de la API, el
// panel y cuándo se leyó.
export const detalleEstado = (i: Impresora, umbral?: UmbralToner) =>
  [
    tiposEstado(i, umbral)
      .map((t) => t.etiqueta)
      .join(" · "),
    i.ippStateText,
    mensajePanel(i) && `Panel: ${mensajePanel(i)}`,
    i.ippStateSinceUtc && `Desde ${fechaHora(i.ippStateSinceUtc)}`,
    i.ippCheckedAtUtc &&
      `Leído ${fechaHora(i.ippCheckedAtUtc)} (${hace(i.ippCheckedAtUtc)})`,
  ]
    .filter(Boolean)
    .join("\n")
