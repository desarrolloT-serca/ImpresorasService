import { describe, expect, it } from "vitest"
import { ESTADOS_TRABAJO as E, type Impresora } from "@serca/contratos"
import {
  fechaHora,
  puedeCancelar,
  puedeReimprimir,
  puedeReintentar,
  puerto,
  resumen,
} from "@/modules/impresion/formato"

const impresora = (connectivitySeverity: Impresora["connectivitySeverity"]) =>
  ({ connectivitySeverity }) as Impresora

describe("resumen de una tienda", () => {
  it("manda el peor caso", () => {
    expect(resumen([impresora("healthy"), impresora("warning"), impresora("critical")])).toEqual({ tono: "error", texto: "1 con error" })
    expect(resumen([impresora("healthy"), impresora("warning")])).toEqual({ tono: "aviso", texto: "1 pendientes" })
    expect(resumen([impresora("healthy"), impresora("neutral")]).tono).toBe("ok")
    expect(resumen([]).texto).toBe("Sin impresoras")
  })
})

describe("acciones por estado (como el PHP)", () => {
  it("reintenta solo lo que no ha salido", () => {
    expect([E.pendiente, E.error].every(puedeReintentar)).toBe(true)
    expect([E.sinConfirmar, E.bloqueado, E.impreso, E.cancelado].some(puedeReintentar)).toBe(false)
  })
  it("reimprime solo lo que está sin confirmar", () => {
    expect(puedeReimprimir(E.sinConfirmar)).toBe(true)
    expect(puedeReimprimir(E.error)).toBe(false)
  })
  it("cancela pendientes, errores, sin confirmar y bloqueados", () => {
    expect([E.pendiente, E.error, E.sinConfirmar, E.bloqueado].every(puedeCancelar)).toBe(true)
    expect([E.impreso, E.cancelado, E.aceptado].some(puedeCancelar)).toBe(false)
  })
})

describe("formato", () => {
  it("da la fecha UTC en hora de Madrid, con y sin horario de verano", () => {
    expect(fechaHora("2026-07-01T10:05:00+00:00")).toBe("01/07/2026 12:05")
    expect(fechaHora("2026-12-01T23:30:00Z")).toBe("02/12/2026 00:30")
    expect(fechaHora(null)).toBe("-")
  })
  it("saca el puerto del transporte", () => {
    expect(puerto("tcp/9100")).toBe("9100")
    expect(puerto(null)).toBe("-")
  })
})
