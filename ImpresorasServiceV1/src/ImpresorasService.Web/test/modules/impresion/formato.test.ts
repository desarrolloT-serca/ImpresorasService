import { describe, expect, it } from "vitest"
import { ESTADOS_TRABAJO as E, type Impresora } from "@serca/contratos"
import {
  autor,
  documentoErp,
  enOrden,
  nivelRegla,
  faltaParaReimprimir,
  fechaHora,
  fechaHoraCorta,
  franjasDe,
  horaTrabajo,
  puedeCancelar,
  puedeReimprimir,
  puedeReintentar,
  puerto,
  resumen,
} from "@/modules/impresion/formato"

const impresora = (connectivitySeverity: Impresora["connectivitySeverity"]) =>
  ({ connectivitySeverity }) as Impresora

describe("orden de las reglas (RoutingResolver)", () => {
  const regla = (
    ruleId: number,
    storeId: number | null,
    documentType: string | null,
    channel: string | null,
    priority = 10
  ) =>
    ({ ruleId, storeId, documentType, channel, priority }) as Parameters<
      typeof enOrden
    >[0][number]

  it("nivel por especificidad; la global con tipo no se aplica nunca", () => {
    expect(nivelRegla(regla(1, 6, "TICKET", "WEB"))).toBe(5)
    expect(nivelRegla(regla(1, 6, "TICKET", null))).toBe(4)
    expect(nivelRegla(regla(1, 6, null, "WEB"))).toBe(3)
    expect(nivelRegla(regla(1, 6, " ", null))).toBe(2)
    expect(nivelRegla(regla(1, null, null, null))).toBe(1)
    expect(nivelRegla(regla(1, null, "TICKET", null))).toBe(0)
  })

  it("gana la más específica y, entre iguales, la de menor prioridad y menor número", () => {
    const orden = enOrden([
      regla(1, 6, null, null, 1),
      regla(2, 6, "TICKET", null, 50),
      regla(3, 6, "TICKET", null, 5),
      regla(4, null, null, null, 0),
      regla(5, 6, "TICKET", null, 5),
    ])
    expect(orden.map((r) => r.ruleId)).toEqual([3, 5, 2, 1, 4])
  })
})

describe("resumen de una tienda", () => {
  it("manda el peor caso", () => {
    expect(
      resumen([
        impresora("healthy"),
        impresora("warning"),
        impresora("critical"),
      ])
    ).toEqual({ tono: "error", texto: "1 con error" })
    expect(resumen([impresora("healthy"), impresora("warning")])).toEqual({
      tono: "aviso",
      texto: "1 pendiente",
    })
    expect(resumen([impresora("healthy"), impresora("neutral")]).tono).toBe(
      "ok"
    )
    expect(resumen([]).texto).toBe("Sin impresoras")
  })
})

describe("acciones por estado (como el PHP)", () => {
  it("reintenta solo lo que no ha salido", () => {
    expect([E.pendiente, E.error].every(puedeReintentar)).toBe(true)
    expect(
      [E.sinConfirmar, E.bloqueado, E.impreso, E.cancelado].some(
        puedeReintentar
      )
    ).toBe(false)
  })
  it("reimprime solo lo que está sin confirmar", () => {
    expect(puedeReimprimir(E.sinConfirmar)).toBe(true)
    expect(puedeReimprimir(E.error)).toBe(false)
  })
  it("cancela pendientes, errores, sin confirmar y bloqueados", () => {
    expect(
      [E.pendiente, E.error, E.sinConfirmar, E.bloqueado].every(puedeCancelar)
    ).toBe(true)
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
    const minutos = (v: Parameters<typeof franjasDe>[0]) =>
      franjasDe(v).map((f) => f.minutos)
    expect(minutos("today")).toEqual([0, 15, 30, 60, 120, 240, 720])
    expect(minutos("7d")).toEqual([0, 30, 60, 120, 240, 720, 1440])
    expect(minutos("30d")).toEqual([0, 120, 240, 720, 1440])
  })
  it("el documento del ERP sale del tipo y del número; las pruebas no tienen", () => {
    expect(documentoErp("A", "123456")).toEqual({
      tipo: "A",
      nombre: "Albarán",
      numero: 123456,
    })
    expect(documentoErp("C", "98765-2")).toMatchObject({
      nombre: "Contado",
      numero: 98765,
    }) // reimpresión
    expect(documentoErp("PRUEBA", "PRUEBA-IP-115904")).toBeNull()
    expect(documentoErp("A", "PRUEBA-IP-115904")).toBeNull()
    expect(documentoErp("A", "0")).toBeNull()
    expect(documentoErp("A", "99999999999")).toBeNull() // fuera de integer
  })
  it("lo de hoy en Madrid, solo con la hora", () => {
    const ahora = Date.parse("2026-10-26T15:00:00Z") // 16:00 en Madrid
    expect(fechaHoraCorta("2026-10-26T08:05:00Z", ahora)).toBe("09:05")
    expect(fechaHoraCorta("2026-10-25T23:30:00Z", ahora)).toBe("00:30") // ya es 26 en Madrid
    expect(fechaHoraCorta("2026-10-25T22:30:00Z", ahora)).toBe(
      "25/10/2026 23:30"
    )
    expect(fechaHoraCorta(null, ahora)).toBe("-")
  })
  it("saca el puerto del transporte", () => {
    expect(puerto("tcp/9100")).toBe("9100")
    expect(puerto(null)).toBe("-")
  })
})

describe("hora de un trabajo reintentado o reimpreso", () => {
  const ahora = Date.parse("2026-10-26T15:00:00Z")
  const llego = "2026-10-26T09:15:00Z"
  const salio = "2026-10-26T11:55:00Z"

  it("sin reintentos, la de llegada y sin marca", () => {
    expect(horaTrabajo({ createdAtUtc: llego }, ahora)).toEqual({
      marca: null,
      hora: "10:15",
      detalle: "Llegó 26/10/2026 10:15",
    })
  })

  it("reintentado tras un error, la de la nueva salida", () => {
    const h = horaTrabajo(
      { createdAtUtc: llego, sentAtUtc: salio, retries: 1, reprints: 0 },
      ahora
    )
    expect(h.marca).toBe("reintento")
    expect(h.hora).toBe("12:55")
    expect(h.detalle).toBe(
      "Llegó 26/10/2026 10:15\nReintentado tras un error 1 vez\nSalió de nuevo 26/10/2026 12:55"
    )
  })

  it("la reimpresión, que puede duplicar, manda sobre el reintento", () => {
    expect(
      horaTrabajo(
        { createdAtUtc: llego, sentAtUtc: salio, retries: 1, reprints: 2 },
        ahora
      ).marca
    ).toBe("reimpresion")
  })

  it("si aún no ha vuelto a salir, la de llegada y lo dice", () => {
    const h = horaTrabajo(
      { createdAtUtc: llego, sentAtUtc: null, reprints: 1 },
      ahora
    )
    expect(h.hora).toBe("10:15")
    expect(h.detalle).toContain("Aún no ha vuelto a salir")
  })
})

describe("reimprimir lo que ya salió", () => {
  it("solo lo impreso y lo sin confirmar; lo que espera confirmación, no", () => {
    expect(puedeReimprimir(E.impreso)).toBe(true)
    expect(puedeReimprimir(E.sinConfirmar)).toBe(true)
    expect(puedeReimprimir(E.aceptado)).toBe(false)
    expect(puedeReimprimir(E.error)).toBe(false)
  })

  it("pide motivo, explicación con Otro y aceptar el riesgo", () => {
    const ok = { motivo: "PERDIDO" as const, nota: "", entiendo: true }
    expect(faltaParaReimprimir(ok)).toBeNull()
    expect(faltaParaReimprimir({ ...ok, motivo: "" })).toBe("Elige el motivo")
    expect(faltaParaReimprimir({ ...ok, motivo: "OTRO", nota: "  " })).toBe(
      "Explica el motivo"
    )
    expect(
      faltaParaReimprimir({
        ...ok,
        motivo: "OTRO",
        nota: "lo pidió el cliente",
      })
    ).toBeNull()
    expect(faltaParaReimprimir({ ...ok, entiendo: false })).toMatch(/duplicado/)
    expect(faltaParaReimprimir({ ...ok, nota: "x".repeat(501) })).toMatch(/500/)
  })
})

describe("quién hizo una acción", () => {
  it("del ERP, código y nombre por separado; del PHP, su login de nombre", () => {
    expect(autor("ERP 123 Ana López")).toEqual({
      codigo: "123",
      nombre: "Ana López",
    })
    expect(autor("ERP 123")).toEqual({ codigo: "123", nombre: null })
    expect(autor("jefe.alicante")).toEqual({
      codigo: null,
      nombre: "jefe.alicante",
    })
    expect(autor(null)).toEqual({ codigo: null, nombre: null })
  })
})
