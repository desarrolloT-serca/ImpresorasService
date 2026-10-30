import type {
  Impresora,
  ResumenDashboard,
  Salud,
  TiendaDashboard,
} from "@serca/contratos"
import type { IdPanel } from "@/workspace/modulo"
import { plural } from "./formato"

export type Destino = {
  etiqueta: string
  panel: IdPanel
  clave: string
  titulo: string
}

export type Incidencia = {
  storeId: number
  storeName: string
  severidad: Salud
  tipo: string
  detalle: string
  resto: string
  desde: string | null
  // De menos a más compromiso: mirar, actuar.
  destinos: Destino[]
}

export const RANGO_SALUD: Record<Salud, number> = {
  critical: 0,
  warning: 1,
  info: 2,
  healthy: 3,
}

// Abrir la cola o las impresoras ya filtradas, en su propia pestaña.
const cola = (
  t: TiendaDashboard | { storeId: number; storeName: string },
  filtro: string,
  que: string
): Destino => ({
  etiqueta: que,
  panel: "impresion.cola",
  clave: `tienda=${t.storeId}${filtro ? `&${filtro}` : ""}`,
  titulo: `Cola · ${t.storeName}`,
})
const impresoras = (
  t: { storeId: number; storeName: string },
  que: string
): Destino => ({
  etiqueta: que,
  panel: "impresion.impresoras",
  clave: `tienda=${t.storeId}&conexion=problemas`,
  titulo: `Impresoras · ${t.storeName}`,
})

// Una fila por incidencia, como la tabla «Incidencias priorizadas» del PHP
// (dashboard/partials/operacion.blade.php). Las alertas vienen de la API (la
// misma fuente que Telegram) y se clasifican por su motivo; «sin impresora
// asignada» sale de unassignedQueueCurrent, que no genera alerta propia pero
// se puede resolver. Primero las críticas.
export function incidencias(
  resumen: ResumenDashboard,
  todasLasImpresoras: readonly Impresora[],
  {
    admin,
    umbralCola,
  }: { admin: boolean; umbralCola: (s: Salud) => number | undefined }
): Incidencia[] {
  const tiendas = new Map(resumen.stores.map((t) => [t.storeId, t]))
  const impresora = (storeId: number, estado: string) => {
    const activas = new Set(
      tiendas.get(storeId)?.printers.map((p) => p.printerId)
    )
    return todasLasImpresoras.find(
      (i) => activas.has(i.printerId) && i.connectivityStatus === estado
    )
  }
  const lista: Incidencia[] = resumen.alerts.map((a) => {
    const motivo = a.healthReason.toLowerCase()
    const t = tiendas.get(a.storeId)
    const base = {
      storeId: a.storeId,
      storeName: a.storeName,
      severidad: a.health,
    }
    if (
      motivo.includes("sin impresoras activas") ||
      motivo.includes("no hay impresoras activas")
    )
      return {
        ...base,
        tipo: "Impresoras",
        detalle: "Sin impresoras activas",
        resto:
          a.queuedCurrent > 0
            ? `${plural(a.queuedCurrent, "trabajo", "trabajos")} en cola sin salida posible`
            : "la tienda no puede imprimir",
        desde: t?.oldestQueuedAtUtc ?? null,
        destinos: [
          cola(a, "estado=0", "Ver los parados"),
          impresoras(a, "Ver impresoras"),
        ],
      }
    // Umbrales opcionales (27/10/2026). «Sin recibir trabajos…» lleva
    // «trabajos»: va antes que la cola para no confundirse con ella.
    if (motivo.includes("sin recibir")) {
      const minutos = t?.silentMinutes ?? null
      return {
        ...base,
        tipo: "Sin recibir",
        detalle: "No llegan trabajos",
        resto: "en horario de tienda: puede que el ERP no esté mandando",
        desde:
          minutos === null
            ? null
            : new Date(Date.now() - minutos * 60_000).toISOString(),
        destinos: [cola(a, "", "Ver la cola")],
      }
    }
    if (motivo.includes("reimpresiones"))
      return {
        ...base,
        tipo: "Reimpresiones",
        detalle: plural(
          t?.reprintsToday ?? 0,
          "reimpresión hoy",
          "reimpresiones hoy"
        ),
        resto: "posibles duplicados: revisa los motivos",
        desde: null,
        destinos: admin
          ? [
              {
                etiqueta: "Ver el registro",
                panel: "impresionControl.reimpresiones" as IdPanel,
                clave: `ventana=today&tienda=${a.storeId}`,
                titulo: `Reimpresiones · ${a.storeName}`,
              },
            ]
          : [cola(a, "", "Ver la cola")],
      }
    if (motivo.includes("sin host")) {
      const i = impresora(a.storeId, "no_host")
      return {
        ...base,
        tipo: "Sin host",
        detalle: `${i?.printerName ?? "Impresora"} sin host configurado`,
        resto: "no puede recibir trabajos",
        desde: i?.connectionFailingSinceUtc ?? null,
        destinos: [impresoras(a, "Configurar host")],
      }
    }
    if (motivo.includes("sin reenviar"))
      return {
        ...base,
        tipo: "Sin reenviar",
        detalle: plural(
          a.failedWithoutRetryCurrent,
          "trabajo sin reenviar",
          "trabajos sin reenviar"
        ),
        resto: "reintentos agotados y nadie los ha reenviado",
        desde: t?.oldestFailedWithoutRetryAtUtc ?? null,
        // El reenvío se confirma en la cola, no a ciegas desde aquí.
        destinos: [cola(a, "vista=sinReenviar", "Ver y reenviar")],
      }
    if (motivo.includes("conectividad") || motivo.includes("conexion")) {
      const i = impresora(a.storeId, "error")
      const racha = i?.connectionFailuresStreak ?? 0
      // Se comprueba cada 30 s: caída desde ayer son miles; pasado 100 basta con «muchos».
      const detalles = [
        racha > 100
          ? "+100 fallos seguidos"
          : racha > 0
            ? plural(racha, "fallo seguido", "fallos seguidos")
            : null,
        i?.lastConnectionTransport
          ? `transporte ${i.lastConnectionTransport}`
          : null,
      ].filter(Boolean)
      return {
        ...base,
        tipo: "Conexión",
        detalle: i
          ? `${i.printerName} sin conexión`
          : "Impresoras pendientes de comprobación",
        resto: detalles.length
          ? detalles.join(", ")
          : "revisar la conexión de la tienda",
        // Desde el primer fallo, no la última comprobación (cada 30 s: siempre
        // «ahora mismo»).
        desde: i?.connectionFailingSinceUtc ?? null,
        destinos: [impresoras(a, "Probar conexión")],
      }
    }
    if (motivo.includes("cola") || motivo.includes("trabajos")) {
      const umbral = umbralCola(a.health)
      return {
        ...base,
        tipo: "Cola",
        detalle: `Cola de ${plural(a.queuedCurrent, "trabajo", "trabajos")}`,
        resto: `por encima del umbral ${a.health === "critical" ? "crítico" : "de aviso"}${umbral === undefined ? "" : ` (${umbral})`}`,
        desde: t?.oldestQueuedAtUtc ?? null,
        destinos: [
          impresoras(a, "Ver impresoras"),
          cola(a, "estado=0", "Ver la cola"),
        ],
      }
    }
    return {
      ...base,
      tipo: "Otro",
      detalle: a.healthReason || "Incidencia sin clasificar",
      resto: "",
      desde: null,
      destinos: [
        a.failedWithoutRetryCurrent > 0
          ? cola(a, "vista=sinReenviar", "Ver los fallidos")
          : cola(a, "estado=0", "Ver la cola"),
      ],
    }
  })
  for (const t of resumen.stores)
    if (t.unassignedQueueCurrent > 0)
      lista.push({
        storeId: t.storeId,
        storeName: t.storeName,
        severidad: "warning",
        tipo: "Sin impresora",
        detalle: plural(
          t.unassignedQueueCurrent,
          "trabajo sin impresora",
          "trabajos sin impresora"
        ),
        resto: "ninguna regla de enrutado coincide",
        desde: t.oldestQueuedAtUtc,
        destinos: [
          cola(t, "vista=sinImpresora", "Ver los trabajos"),
          ...(admin
            ? [
                {
                  etiqueta: "Revisar reglas",
                  panel: "impresionConfiguracion.reglasYAvisos" as IdPanel,
                  clave: `seccion=reglas&tienda=${t.storeId}`,
                  titulo: `Reglas · ${t.storeName}`,
                },
              ]
            : []),
        ],
      })
  // sort es estable: dentro de cada severidad se mantiene el orden de la API.
  return lista.sort(
    (a, b) => RANGO_SALUD[a.severidad] - RANGO_SALUD[b.severidad]
  )
}
