import { describe, expect, it } from "vitest"
import type { Impresora } from "@serca/contratos"
import { mensajePanel, tiposEstado } from "@/modules/impresion/estado-impresora"

// docs/72, 10/11/2026: tipos de estado de una impresora IPP, cortos para la tabla.
const leida = (extra: Partial<Impresora>) =>
  ({
    ippCheckedAtUtc: "2026-11-10T11:20:00Z",
    ippSeverity: "healthy",
    ippState: 3,
    ippMarkers: [
      { nombre: "BK", color: null, tipo: "toner", nivel: 80, bajo: 7 },
    ],
    ...extra,
  }) as Impresora
const etiquetas = (i: Impresora, umbral?: { aviso: number; critico: number }) =>
  tiposEstado(i, umbral).map((t) => `${t.etiqueta}:${t.tono}`)

describe("tipos de estado", () => {
  it("sin problemas: Lista o Imprimiendo; sin lectura, Sin lectura", () => {
    expect(etiquetas(leida({}))).toEqual(["Lista:ok"])
    expect(etiquetas(leida({ ippState: 4 }))).toEqual(["Imprimiendo:info"])
    expect(etiquetas(leida({ ippCheckedAtUtc: null }))).toEqual([
      "Sin lectura:neutro",
    ])
    expect(etiquetas(leida({ ippSeverity: "neutral" }))).toEqual([
      "Sin lectura:neutro",
    ])
  })

  it("agrupa los motivos y no los repite (ATALAYAS, 10/11/2026)", () => {
    const i = leida({
      ippState: 5,
      ippStateReasons: "media-empty-error, media-needed-error",
      ippMarkers: [
        { nombre: "BK", color: null, tipo: "toner", nivel: 20, bajo: 9 },
      ],
    })
    expect(etiquetas(i)).toEqual(["Sin papel:error", "Tóner bajo:aviso"])
  })

  it("gravedad por el sufijo: -warning aviso, sin sufijo error, -report nada", () => {
    expect(
      etiquetas(leida({ ippStateReasons: "opc-life-over-warning" }))
    ).toEqual(["Tambor:aviso"])
    expect(etiquetas(leida({ ippStateReasons: "media-jam" }))).toEqual([
      "Atasco:error",
    ])
    expect(etiquetas(leida({ ippStateReasons: "toner-low-report" }))).toEqual([
      "Lista:ok",
    ])
    expect(etiquetas(leida({ ippStateReasons: "cosa-rara-warning" }))).toEqual([
      "Aviso:aviso",
    ])
    expect(
      etiquetas(
        leida({ ippStateReasons: "door-open-error, media-low-warning" })
      )
    ).toEqual(["Tapa abierta:error", "Papel bajo:aviso"])
  })

  it("parada sin motivo, y el tóner por los umbrales de Reglas y avisos", () => {
    expect(etiquetas(leida({ ippState: 5 }))).toEqual(["Parada:error"])
    const i = leida({
      ippStateReasons: "toner-low-warning",
      ippMarkers: [
        { nombre: "BK", color: null, tipo: "toner", nivel: 8, bajo: 7 },
      ],
    })
    // El crítico quita el «bajo»: no se dice dos veces.
    expect(etiquetas(i)).toEqual(["Tóner crítico:error"])
    expect(etiquetas(leida({}), { aviso: 90, critico: 50 })).toEqual([
      "Tóner bajo:aviso",
    ])
  })
})

describe("mensaje del panel", () => {
  it("quita los estados normales que añaden las Brother", () => {
    expect(
      mensajePanel(leida({ ippStateMessage: "Sin papel B1, Imprimiendo" }))
    ).toBe("Sin papel B1")
    expect(
      mensajePanel(leida({ ippStateMessage: "Sostit. tamburo, Risparmio" }))
    ).toBe("Sostit. tamburo")
    expect(mensajePanel(leida({ ippStateMessage: "Espera" }))).toBe(null)
    expect(mensajePanel(leida({ ippStateMessage: null }))).toBe(null)
  })
})
