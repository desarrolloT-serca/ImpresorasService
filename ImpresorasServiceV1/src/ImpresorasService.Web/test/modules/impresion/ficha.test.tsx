import { cleanup, render, screen } from "@testing-library/react"
import { afterEach, describe, expect, it } from "vitest"
import type { Impresora } from "@serca/contratos"
import { FichaImpresora } from "@/modules/impresion/FichaImpresora"

afterEach(cleanup)

const brother = {
  printerId: 3,
  printerName: "Almacen",
  storeId: 25,
  host: null,
  connectivityLabel: "OK",
  lastConnectionTransport: "tcp/515",
  ippSeverity: "warning",
  ippStateText: "El cartucho fotoconductor ha llegado al final de su vida útil",
  ippStateMessage: "Cambie tambor, Espera",
  ippStateSinceUtc: "2026-11-10T08:44:00Z",
  ippMakeAndModel: "Brother MFC-L2710DW series",
  ippMoreInfoUrl: "http://192.42.225.115/net/net/airprint.html",
  ippMarkers: [
    { nombre: "BK", color: null, tipo: "toner", nivel: 14, bajo: 9 },
  ],
  ippTrays: [{ nombre: "TRAY1", capacidad: 250, nivel: -3 }],
  ippQueuedJobs: 0,
  ippFirmware: "R2007171728:BC14, 1.04",
  ippUpSinceUtc: "2026-10-27T00:00:00Z",
  ippCheckedAtUtc: "2026-11-10T10:00:00Z",
  tonerWarningPct: 20,
  tonerCriticalPct: 10,
  tonerDaysLeft: 20,
} as Impresora

// docs/72, mejora 6: la ficha enseña lo que dice la impresora de sí misma.
describe("ficha de la impresora", () => {
  const ficha = (gestionar: boolean) =>
    render(
      <FichaImpresora
        impresora={brother}
        tiendas={[]}
        gestionar={gestionar}
        comprobando={false}
        identificando={false}
        onComprobar={() => {}}
        onIdentificar={() => {}}
        onCerrar={() => {}}
      />
    )

  it("enseña estado, mensaje, tóner con previsión, bandejas, firmware y su web", () => {
    ficha(true)
    expect(screen.getByText(/fotoconductor/)).toBeTruthy()
    expect(screen.getByText("Cambie tambor, Espera")).toBeTruthy()
    expect(screen.getByText("14 %")).toBeTruthy()
    expect(screen.getByText(/Llega al crítico en unos 20 días/)).toBeTruthy()
    expect(screen.getByText("Con papel")).toBeTruthy()
    expect(screen.getByText("R2007171728:BC14, 1.04")).toBeTruthy()
    expect(
      screen.getByRole("link", { name: /Abrir la web/ }).getAttribute("href")
    ).toBe("http://192.42.225.115/")
    // Sin IP no se puede identificar.
    expect(
      (screen.getByRole("button", { name: "Identificar" }) as HTMLButtonElement)
        .disabled
    ).toBe(true)
  })

  it("sin poder gestionar no salen Identificar ni Comprobar", () => {
    ficha(false)
    expect(screen.queryByRole("button", { name: "Identificar" })).toBeNull()
    expect(screen.queryByRole("button", { name: "Comprobar" })).toBeNull()
  })
})
