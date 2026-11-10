import { describe, expect, it } from "vitest"
import type {
  Impresora,
  ResumenDashboard,
  TiendaDashboard,
} from "@serca/contratos"
import { escala, hace } from "@/modules/impresion/formato"
import { incidencias } from "@/modules/impresion/incidencias"

const tienda = (
  storeId: number,
  extra: Partial<TiendaDashboard> = {}
): TiendaDashboard => ({
  storeId,
  storeName: `T${storeId}`,
  connectedPrinters: 1,
  received: 0,
  printed: 0,
  failed: 0,
  queuedCurrent: 0,
  failedWithoutRetryCurrent: 0,
  health: "healthy",
  healthReason: "Operacion dentro de umbrales",
  unassignedQueueCurrent: 0,
  oldestQueuedAtUtc: null,
  oldestFailedWithoutRetryAtUtc: null,
  printers: [
    {
      printerId: storeId * 10,
      queueCurrent: 0,
      failedWindow: 0,
      receivedWindow: 0,
    },
  ],
  ...extra,
})
const alerta = (
  storeId: number,
  health: "critical" | "warning",
  healthReason: string
) => ({
  storeId,
  storeName: `T${storeId}`,
  health,
  healthReason,
  queuedCurrent: 47,
  failedWithoutRetryCurrent: 3,
})
const resumen = (r: Partial<ResumenDashboard>): ResumenDashboard => ({
  generatedAtUtc: "2026-10-23T10:00:00Z",
  kpis: {
    received: 0,
    printed: 0,
    failed: 0,
    queueCurrent: 0,
    failedWithoutRetryCurrent: 0,
    activePrinters: 0,
    activeStores: 0,
  },
  alerts: [],
  stores: [],
  ...r,
})
const caida = {
  printerId: 10,
  printerName: "Mostrador",
  connectivityStatus: "error",
  connectionFailuresStreak: 250,
  lastConnectionTransport: "tcp/9100",
  lastConnectionCheckAtUtc: "2026-10-23T09:00:00Z",
} as Impresora
const opciones = {
  admin: true,
  umbralCola: (s: string) => (s === "critical" ? 30 : 10),
}

// docs/72, R2 y mejoras 3 y 5, con lo que mandaron de verdad el 10/11/2026.
describe("incidencias de la propia impresora", () => {
  const leida = {
    connectivityStatus: "ok",
    ippCheckedAtUtc: "2026-11-10T11:20:00Z",
    ippState: 3,
    ippMarkers: [
      { nombre: "BK", color: null, tipo: "toner", nivel: 60, bajo: 9 },
    ],
  }
  const atalayas = {
    ...leida,
    printerId: 30,
    printerName: "Informatica",
    ippState: 5,
    ippSeverity: "critical",
    ippStateReasons: "media-empty-error, media-needed-error",
    ippStateText:
      "Sin papel en la bandeja. Se necesita cargar papel. BK al 20 %",
    ippStateMessage: "Sin papel B1, Imprimiendo",
    ippStateSinceUtc: "2026-11-10T11:13:31Z",
    ippMarkers: [
      { nombre: "BK", color: null, tipo: "toner", nivel: 20, bajo: 9 },
    ],
  } as Impresora
  const tambor = {
    ...leida,
    printerId: 20,
    printerName: "Almacen",
    ippSeverity: "warning",
    ippStateReasons: "opc-life-over-warning",
    ippStateText:
      "El cartucho fotoconductor ha llegado al final de su vida útil",
    ippStateMessage: "Cambie tambor, Espera",
  } as Impresora

  it("una fila por impresora activa con problema, tipificada y con su «Desde»", () => {
    const r = resumen({ stores: [tienda(2), tienda(3)] })
    const lista = incidencias(
      r,
      [
        tambor,
        atalayas,
        { ...atalayas, printerId: 99 } as Impresora, // no está en el resumen: inactiva
        { ...leida, printerId: 31, ippSeverity: "neutral" } as Impresora,
      ],
      opciones
    )
    expect(lista).toHaveLength(2)
    expect(lista[0]).toMatchObject({
      storeId: 3,
      severidad: "critical",
      tipo: "Impresora",
      // Sin papel una vez (media-empty y media-needed), y el tóner al 20 %.
      detalle: "Informatica: Sin papel, Tóner bajo",
      // «Imprimiendo» es su estado normal: no va.
      resto: "Panel: Sin papel B1",
      desde: "2026-11-10T11:13:31Z",
    })
    expect(lista[1]).toMatchObject({
      storeId: 2,
      severidad: "warning",
      detalle: "Almacen: Tambor",
      resto: "Panel: Cambie tambor",
    })
    expect(lista[1].destinos[0].clave).toBe("tienda=2&conexion=problemas")
  })

  it("no repite la alerta de tienda que manda la API por el estado de la impresora", () => {
    const r = resumen({
      stores: [tienda(2)],
      alerts: [
        alerta(
          2,
          "warning",
          "Impresora(s) con avisos (estado de la impresora)"
        ),
      ],
    })
    const lista = incidencias(r, [tambor], opciones)
    expect(lista).toHaveLength(1)
    expect(lista[0].detalle).toBe("Almacen: Tambor")
  })
})

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
    expect(primera).toMatchObject({
      storeId: 1,
      tipo: "Conexión",
      detalle: "Mostrador sin conexión",
      resto: "+100 fallos seguidos, transporte tcp/9100",
    })
    expect(segunda).toMatchObject({
      tipo: "Cola",
      detalle: "Cola de 47 trabajos",
      resto: "por encima del umbral de aviso (10)",
    })
  })
  it("los fallos sin reenviar llevan a la cola ya filtrada, sin reenviar a ciegas", () => {
    const r = resumen({
      stores: [tienda(3)],
      alerts: [
        alerta(3, "critical", "Critica: Acumula 5 o mas fallos sin reenviar"),
      ],
    })
    const [i] = incidencias(r, [], opciones)
    expect(i.destinos).toEqual([
      {
        etiqueta: "Ver y reenviar",
        panel: "impresion.cola",
        clave: "tienda=3&vista=sinReenviar",
        titulo: "Cola · T3",
      },
    ])
  })
  it("sin recibir y reimpresiones tienen su tipo (no se confunden con la cola)", () => {
    const r = resumen({
      stores: [
        tienda(5, { silentMinutes: 90 }),
        tienda(6, { reprintsToday: 7 }),
      ],
      alerts: [
        alerta(
          5,
          "warning",
          "Sin recibir trabajos desde hace 90 min en horario de tienda"
        ),
        alerta(6, "critical", "7 reimpresiones hoy"),
      ],
    })
    const [reimp, silencio] = incidencias(r, [], opciones)
    expect(reimp).toMatchObject({
      tipo: "Reimpresiones",
      detalle: "7 reimpresiones hoy",
    })
    expect(reimp.destinos[0]).toMatchObject({
      panel: "impresionControl.reimpresiones",
      clave: "ventana=today&tienda=6",
    })
    expect(silencio).toMatchObject({
      tipo: "Sin recibir",
      detalle: "No llegan trabajos",
    })
    expect(silencio.destinos[0].clave).toBe("tienda=5")
  })
  it("añade los trabajos sin impresora, con las reglas solo para quien administra", () => {
    const r = resumen({ stores: [tienda(4, { unassignedQueueCurrent: 2 })] })
    expect(incidencias(r, [], opciones)[0]).toMatchObject({
      tipo: "Sin impresora",
      detalle: "2 trabajos sin impresora",
    })
    expect(incidencias(r, [], opciones)[0].destinos).toHaveLength(2)
    expect(
      incidencias(r, [], { ...opciones, admin: false })[0].destinos
    ).toHaveLength(1)
  })
})

// Se clasifican por el texto de StoreHealthEvaluator (API .NET): si cambia la
// redacción allí, la incidencia caería en «Otro» sin que nadie lo note (WEB-5).
describe("cada motivo que manda la API tiene su tipo", () => {
  it.each([
    "Impresora(s) sin conexion (conectividad)",
    "Impresora(s) pendientes de comprobacion (conectividad)",
    "Impresora(s) con fallos de conexion (conectividad)",
    "Hay cola pero no hay impresoras activas",
    "Sin impresoras activas en la tienda",
    "Acumula 5 o mas fallos sin reenviar",
    "Tiene fallos recientes sin reenviar",
    "Cola actual mayor o igual a 10 trabajos",
    "Impresora(s) sin host configurado",
    "Info: Impresora(s) sin host configurado",
    "Sin recibir trabajos desde hace 45 min en horario de tienda",
    "3 reimpresiones hoy",
  ])("%s", (motivo) => {
    const r = resumen({
      stores: [tienda(1)],
      alerts: [alerta(1, "warning", motivo)],
    })
    expect(incidencias(r, [caida], opciones)[0].tipo).not.toBe("Otro")
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
