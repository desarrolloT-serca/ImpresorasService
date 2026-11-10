import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen } from "@testing-library/react"
import { afterEach, describe, expect, it, vi } from "vitest"
import type { PaginasImpresora, Tienda } from "@serca/contratos"
import { PaginasImpresas } from "@/modules/impresion/PaginasImpresas"
import {
  avisosPeriodo,
  avisosRango,
  diasPeriodo,
  tablaExportar,
} from "@/modules/impresion/paginas"

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const tiendas = [{ storeId: 6, name: "ELDA", isActive: true }] as Tienda[]
// Lecturas a las 00:00 de Madrid (23:00 UTC del día anterior en invierno).
const periodo = (from: string, to: string, pages: number | null) => ({
  from,
  to,
  pages,
  fromReadAtUtc: `${from}T00:00:00+01:00`,
  toReadAtUtc: `${to}T00:00:00+01:00`,
})
const mostrador: PaginasImpresora = {
  printerId: 12,
  printerName: "MOSTRADOR01",
  storeId: 6,
  isActive: true,
  pages: 75,
  source: "canon-113",
  periods: [
    periodo("2026-11-01", "2026-11-02", 30),
    periodo("2026-11-02", "2026-11-04", 45),
  ],
}

// docs/73 del ERP, opción E: las páginas salen de la lectura diaria del
// contador; lo que no encaja con las fechas pedidas se avisa.
describe("páginas impresas", () => {
  it("un periodo de un día es ese día; si falta una lectura, abarca más", () => {
    expect(diasPeriodo(mostrador.periods[0])).toBe("01/11/2026")
    expect(avisosPeriodo(mostrador.periods[0])).toEqual([])
    expect(diasPeriodo(mostrador.periods[1])).toBe("02/11/2026 – 03/11/2026")
    expect(avisosPeriodo(mostrador.periods[1])).toEqual([
      "Falta la lectura de algún día: suma 2 días",
    ])
  })

  it("avisa del contador que baja y de las lecturas tardías", () => {
    expect(
      avisosPeriodo({
        ...periodo("2026-11-01", "2026-11-02", null),
        fromReadAtUtc: "2026-11-01T14:02:00Z",
      })
    ).toEqual([
      "El contador bajó: ¿impresora cambiada o reiniciada?",
      "Empieza a las 15:02",
    ])
  })

  it("dice por qué lo leído no cubre justo las fechas pedidas", () => {
    expect(
      avisosRango(mostrador, "2026-11-01", "2026-11-03", "2026-11-10")
    ).toEqual(["1 periodo con avisos"])
    expect(
      avisosRango(mostrador, "2026-10-30", "2026-11-05", "2026-11-10")
    ).toEqual([
      "Desde el 01/11/2026: no hay lecturas anteriores",
      "Hasta el 03/11/2026: falta la lectura del 06/11/2026",
      "1 periodo con avisos",
    ])
    expect(
      avisosRango(mostrador, "2026-11-01", "2026-11-04", "2026-11-04")[0]
    ).toBe("Hasta el 03/11/2026: cada día se cuenta a las 00:00 del siguiente")
    expect(
      avisosRango(
        { ...mostrador, periods: [], pages: null, source: null },
        "2026-11-01",
        "2026-11-03",
        "2026-11-10"
      )
    ).toEqual(["Sin lecturas en estas fechas"])
    expect(
      avisosRango(
        { ...mostrador, periods: [], pages: null, source: "snmp" },
        "2026-11-10",
        "2026-11-10",
        "2026-11-10"
      )
    ).toEqual([
      "Solo hay una lectura: las páginas salen con la de las 00:00 siguientes",
    ])
  })

  it("exporta lo que se ve: una fila por impresora o una por día con el total", () => {
    const [cab, filas] = tablaExportar(
      [mostrador],
      tiendas,
      "2026-11-01",
      "2026-11-03",
      "2026-11-10",
      false
    )
    expect(cab).toEqual([
      "Tienda",
      "Impresora",
      "Desde",
      "Hasta",
      "Páginas",
      "Avisos",
    ])
    expect(filas).toEqual([
      [
        "6 · ELDA",
        "MOSTRADOR01",
        "01/11/2026",
        "03/11/2026",
        "75",
        "1 periodo con avisos",
      ],
    ])
    const [, dias] = tablaExportar(
      [mostrador],
      tiendas,
      "2026-11-01",
      "2026-11-03",
      "2026-11-10",
      true
    )
    expect(dias.map((f) => f.slice(2, 4))).toEqual([
      ["01/11/2026", "30"],
      ["02/11/2026 – 03/11/2026", "45"],
      ["Total", "75"],
    ])
  })

  it("pide las páginas del rango y enseña el total", async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () =>
      Response.json([
        mostrador,
        { ...mostrador, printerId: 13, printerName: "CALLCENTER01", pages: 5 },
      ])
    )
    vi.stubGlobal("fetch", fetch)
    render(
      <QueryClientProvider client={new QueryClient()}>
        <PaginasImpresas
          impresoras={[]}
          tiendas={tiendas}
          onCerrar={() => {}}
        />
      </QueryClientProvider>
    )

    expect(await screen.findByText("CALLCENTER01")).toBeTruthy()
    expect(screen.getByText("80")).toBeTruthy()
    expect(String(fetch.mock.calls[0][0])).toMatch(
      /^\/api\/impresion\/printers\/page-counts\?from=\d{4}-\d{2}-01&to=\d{4}-\d{2}-\d{2}$/
    )
  })
})
