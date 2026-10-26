import { describe, expect, it } from "vitest"
import { ESTADOS_TRABAJO as E, type Impresora } from "@serca/contratos"
import {
  documentoErp,
  fechaHora,
  fechaHoraCorta,
  franjasDe,
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
  it("cada periodo ofrece las franjas con 2 a 500 tramos, y siempre la automática", () => {
    const minutos = (v: Parameters<typeof franjasDe>[0]) => franjasDe(v).map((f) => f.minutos)
    expect(minutos("today")).toEqual([0, 15, 30, 60, 120, 240, 720])
    expect(minutos("7d")).toEqual([0, 30, 60, 120, 240, 720, 1440])
    expect(minutos("30d")).toEqual([0, 120, 240, 720, 1440])
  })
  it("el documento del ERP sale del tipo y del número; las pruebas no tienen", () => {
    expect(documentoErp("A", "123456")).toEqual({ tipo: "A", nombre: "Albarán", numero: 123456 })
    expect(documentoErp("C", "98765-2")).toMatchObject({ nombre: "Contado", numero: 98765 }) // reimpresión
    expect(documentoErp("PRUEBA", "PRUEBA-IP-115904")).toBeNull()
    expect(documentoErp("A", "PRUEBA-IP-115904")).toBeNull()
    expect(documentoErp("A", "0")).toBeNull()
    expect(documentoErp("A", "99999999999")).toBeNull() // fuera de integer
  })
  it("lo de hoy en Madrid, solo con la hora", () => {
    const ahora = Date.parse("2026-10-26T15:00:00Z") // 16:00 en Madrid
    expect(fechaHoraCorta("2026-10-26T08:05:00Z", ahora)).toBe("09:05")
    expect(fechaHoraCorta("2026-10-25T23:30:00Z", ahora)).toBe("00:30") // ya es 26 en Madrid
    expect(fechaHoraCorta("2026-10-25T22:30:00Z", ahora)).toBe("25/10/2026 23:30")
    expect(fechaHoraCorta(null, ahora)).toBe("-")
  })
  it("saca el puerto del transporte", () => {
    expect(puerto("tcp/9100")).toBe("9100")
    expect(puerto(null)).toBe("-")
  })
})
