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
  ippState: 3,
  ippStateReasons: "opc-life-over-warning",
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

  it("cabecera con estado y acciones, y tarjetas de tóner, papel, conexión y equipo", () => {
    ficha(true)
    expect(screen.getByText("Tambor")).toBeTruthy()
    expect(screen.getByText(/fotoconductor/)).toBeTruthy()
    // El panel, sin «Espera».
    expect(screen.getByText("Panel: Cambie tambor")).toBeTruthy()
    expect(screen.getByText("14 %")).toBeTruthy()
    expect(
      screen.getByText("Se agota en unos 20 días (según la impresora)")
    ).toBeTruthy()
    expect(screen.getByText(/Aviso al 20 %, crítico al 10 %/)).toBeTruthy()
    expect(screen.getByText("Con papel")).toBeTruthy()
    expect(screen.getByText("R2007171728:BC14, 1.04")).toBeTruthy()
    for (const t of ["Tóner", "Papel", "Conexión", "Equipo"])
      expect(screen.getByRole("heading", { name: t })).toBeTruthy()
    expect(
      screen
        .getByRole("link", { name: "Abrir la web de la impresora" })
        .getAttribute("href")
    ).toBe("http://192.42.225.115/")
    // Sin IP no se puede identificar.
    expect(
      (
        screen.getByRole("button", {
          name: "Sin IP no se puede identificar",
        }) as HTMLButtonElement
      ).disabled
    ).toBe(true)
  })

  it("los umbrales del tóner son los de Reglas y avisos", () => {
    render(
      <FichaImpresora
        impresora={brother}
        tiendas={[]}
        toner={{ aviso: 30, critico: 15 }}
        gestionar={false}
        comprobando={false}
        identificando={false}
        onComprobar={() => {}}
        onIdentificar={() => {}}
        onCerrar={() => {}}
      />
    )
    expect(screen.getByText(/Aviso al 30 %, crítico al 15 %/)).toBeTruthy()
    expect(screen.getByText("Tóner crítico")).toBeTruthy()
  })

  it("la cola de la impresora va en su pestaña, con lo que tiene en cola", () => {
    ficha(false)
    const pestana = screen.getByRole("tab", { name: /Cola de la impresora/ })
    expect(pestana.getAttribute("aria-selected")).toBe("false")
    expect(
      screen.getByRole("tab", { name: "Estado" }).getAttribute("aria-selected")
    ).toBe("true")
  })

  it("sin poder gestionar no salen Comprobar ni Identificar", () => {
    ficha(false)
    expect(
      screen.queryByRole("button", { name: "Comprobar conexión y estado" })
    ).toBeNull()
    expect(
      screen.queryByRole("button", { name: /Identificar|identificar/ })
    ).toBeNull()
  })
})
