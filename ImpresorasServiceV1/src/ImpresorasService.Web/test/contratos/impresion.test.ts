import { describe, expect, it } from "vitest"
import { DatosImpresora } from "@serca/contratos"

const base = { printerName: " P1 ", storeId: 1, isActive: true, capabilitiesJson: null }

describe("DatosImpresora", () => {
  it("host vacío es null y los textos se recortan", () => {
    const d = DatosImpresora.parse({ ...base, spoolQueue: "\\\\srv\\p1", host: "  " })
    expect(d).toMatchObject({ printerName: "P1", host: null })
  })
  it("con IP, la cola de Windows es opcional", () => {
    expect(DatosImpresora.parse({ ...base, spoolQueue: " ", host: "10.0.0.9" })).toMatchObject({ spoolQueue: null, host: "10.0.0.9" })
  })
  it("sin IP ni cola, el error va en la IP", () => {
    const r = DatosImpresora.safeParse({ ...base, spoolQueue: "", host: "" })
    expect(r.success).toBe(false)
    expect(r.error?.issues[0]).toMatchObject({ path: ["host"], message: "Indica la IP o la cola de Windows" })
  })
})
