import { cleanup, render, screen } from "@testing-library/react"
import { afterEach, describe, expect, it, vi } from "vitest"
import type { ColaImpresora } from "@serca/contratos"

let datos: ColaImpresora
vi.mock("@/modules/impresion/api", () => ({
  useColaImpresora: () => ({
    isPending: false,
    isError: false,
    isFetching: false,
    data: datos,
    dataUpdatedAt: Date.now(),
    refetch: () => {},
  }),
}))
const { ColaDeImpresora } = await import("@/modules/impresion/ColaDeImpresora")

afterEach(cleanup)

// docs/72, R3: la cola de la propia impresora, rediseñada el 10/11/2026.
describe("cola de la impresora", () => {
  it("resume cuántos hay, los detenidos y los nuestros, y los pone en tabla", () => {
    datos = {
      respondio: true,
      mensaje: null,
      trabajos: [
        {
          jobId: 160,
          nombre: "A 125",
          estado: 6,
          motivos: "job-stopped",
          usuario: "ImpresorasService",
          creadoUtc: "2026-11-10T11:12:59Z",
          nuestro: { externalJobId: "125", documentType: "A", status: 9 },
        },
        {
          jobId: 161,
          nombre: null,
          estado: 3,
          motivos: null,
          usuario: null,
          creadoUtc: null,
          nuestro: null,
        },
      ],
    }
    render(<ColaDeImpresora printerId={1} />)
    expect(screen.getByText("trabajos pendientes")).toBeTruthy()
    expect(screen.getByText("1 detenido")).toBeTruthy()
    expect(screen.getByText("1 nuestro")).toBeTruthy()
    expect(screen.getByText("Detenido")).toBeTruthy()
    expect(screen.getByText("En espera")).toBeTruthy()
    expect(screen.getByText("De otro equipo")).toBeTruthy()
    for (const c of ["Documento", "Estado", "Llegó"])
      expect(screen.getByRole("columnheader", { name: c })).toBeTruthy()
  })

  it("sin nada pendiente lo dice claro", () => {
    datos = { respondio: true, mensaje: null, trabajos: [] }
    render(<ColaDeImpresora printerId={1} />)
    expect(screen.getByText("No tiene nada pendiente")).toBeTruthy()
  })

  it("si la impresora no contesta, lo dice con su motivo", () => {
    datos = {
      respondio: false,
      mensaje: "La impresora no responde por IPP.",
      trabajos: [],
    }
    render(<ColaDeImpresora printerId={1} />)
    expect(screen.getByText("La impresora no contesta")).toBeTruthy()
    expect(screen.getByText("La impresora no responde por IPP.")).toBeTruthy()
  })
})
