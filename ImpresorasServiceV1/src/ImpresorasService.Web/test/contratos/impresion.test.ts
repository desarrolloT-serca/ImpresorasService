import { describe, expect, it } from "vitest"
import { DatosImpresora } from "@serca/contratos"

describe("DatosImpresora", () => {
  it("host vacío es null y los textos se recortan", () => {
    const d = DatosImpresora.parse({ printerName: " P1 ", spoolQueue: "\\\\srv\\p1", host: "  ", storeId: 1, isActive: true, capabilitiesJson: null })
    expect(d).toMatchObject({ printerName: "P1", host: null })
  })
})
