import { describe, expect, it } from "vitest"
import type { Impresora, ResumenDashboard, TiendaDashboard } from "@serca/contratos"
import { escala, hace } from "@/modules/impresion/formato"
import { incidencias } from "@/modules/impresion/incidencias"

const tienda = (storeId: number, extra: Partial<TiendaDashboard> = {}): TiendaDashboard => ({
  storeId, storeName: `T${storeId}`, connectedPrinters: 1, received: 0, printed: 0, failed: 0,
  queuedCurrent: 0, failedWithoutRetryCurrent: 0, health: "healthy", healthReason: "Operacion dentro de umbrales",
  unassignedQueueCurrent: 0, oldestQueuedAtUtc: null, oldestFailedWithoutRetryAtUtc: null,
  printers: [{ printerId: storeId * 10, queueCurrent: 0, failedWindow: 0, receivedWindow: 0 }], ...extra,
})
const alerta = (storeId: number, health: "critical" | "warning", healthReason: string) => ({
  storeId, storeName: `T${storeId}`, health, healthReason, queuedCurrent: 47, failedWithoutRetryCurrent: 3,
})
const resumen = (r: Partial<ResumenDashboard>): ResumenDashboard => ({
  generatedAtUtc: "2026-10-23T10:00:00Z",
  kpis: { received: 0, printed: 0, failed: 0, queueCurrent: 0, failedWithoutRetryCurrent: 0, activePrinters: 0, activeStores: 0 },
  alerts: [], stores: [], ...r,
})
const caida = { printerId: 10, printerName: "Mostrador", connectivityStatus: "error", connectionFailuresStreak: 250, lastConnectionTransport: "tcp/9100", lastConnectionCheckAtUtc: "2026-10-23T09:00:00Z" } as Impresora
const opciones = { admin: true, umbralCola: (s: string) => (s === "critical" ? 30 : 10) }

describe("incidencias (como operacion.blade.php)", () => {
  it("clasifica por el motivo de la API y nombra la impresora que falla", () => {
    const r = resumen({
      stores: [tienda(1), tienda(2)],
      alerts: [
        alerta(2, "warning", "Cola actual mayor o igual a 10 trabajos"),
        alerta(1, "critical", "Impresora(s) sin conexion (conectividad)"),
      ],
    })
    const [primera, segunda] = incidencias(r, [caida], opciones)
    expect(primera).toMatchObject({ storeId: 1, tipo: "Conexión", detalle: "Mostrador sin conexión", resto: "+100 fallos seguidos, transporte tcp/9100" })
    expect(segunda).toMatchObject({ tipo: "Cola", detalle: "Cola de 47 trabajos", resto: "por encima del umbral de aviso (10)" })
  })
  it("los fallos sin reenviar llevan a la cola ya filtrada, sin reenviar a ciegas", () => {
    const r = resumen({ stores: [tienda(3)], alerts: [alerta(3, "critical", "Critica: Acumula 5 o mas fallos sin reenviar")] })
    const [i] = incidencias(r, [], opciones)
    expect(i.destinos).toEqual([{ etiqueta: "Ver y reenviar", panel: "impresion.cola", clave: "tienda=3&vista=sinReenviar", titulo: "Cola · T3" }])
  })
  it("añade los trabajos sin impresora, con las reglas solo para quien administra", () => {
    const r = resumen({ stores: [tienda(4, { unassignedQueueCurrent: 2 })] })
    expect(incidencias(r, [], opciones)[0]).toMatchObject({ tipo: "Sin impresora", detalle: "2 trabajos sin impresora" })
    expect(incidencias(r, [], opciones)[0].destinos).toHaveLength(2)
    expect(incidencias(r, [], { ...opciones, admin: false })[0].destinos).toHaveLength(1)
  })
})

describe("formato del dashboard", () => {
  it("dice cuánto hace como el PHP", () => {
    const ahora = Date.parse("2026-10-23T12:00:00Z")
    expect(hace("2026-10-23T11:59:40Z", ahora)).toBe("ahora mismo")
    expect(hace("2026-10-23T11:48:00Z", ahora)).toBe("12 min")
    expect(hace("2026-10-23T08:55:00Z", ahora)).toBe("3 h 05 min")
    expect(hace("2026-10-21T08:00:00Z", ahora)).toBe("2 d 4 h")
    expect(hace(null)).toBe("sin dato")
  })
  it("redondea el eje al alza", () => {
    expect([0, 7, 47, 130, 430].map(escala)).toEqual([2, 8, 50, 150, 500])
  })
})
