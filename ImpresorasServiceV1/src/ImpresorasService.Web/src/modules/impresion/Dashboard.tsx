import { useMemo, useState, type ReactNode } from "react"
import type { ColumnDef } from "@tanstack/react-table"
import {
  PERMISOS_IMPRESION as P,
  type Impresora,
  type ReglasUmbral,
  type TiendaDashboard,
  type VentanaDashboard,
} from "@serca/contratos"

import { DataTable } from "@/components/data-table"
import { Panel } from "@/components/ficha"
import { CampoFiltro } from "@/components/filtro-lista"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Spinner } from "@/components/ui/spinner"
import { usePermiso } from "@/core/sesion"
import { num } from "@/lib/format"
import { cn } from "@/lib/utils"
import { useWorkspace } from "@/workspace/modulo"
import {
  useImpresoras,
  useResumen,
  useSerie,
  useTiendas,
  useUmbrales,
} from "./api"
import {
  CLASE_TONO,
  fechaHora,
  hace,
  nombreTienda,
  SALUD,
  TONO_SEVERIDAD,
  type Tono,
} from "./formato"
import { Grafica } from "./Grafica"
import { incidencias, RANGO_SALUD, type Destino, type Incidencia } from "./incidencias"

const VENTANAS: { value: VentanaDashboard; label: string }[] = [
  { value: "today", label: "Hoy" },
  { value: "7d", label: "7 días" },
  { value: "30d", label: "30 días" },
]
const TODAS = "todas"
const FILAS_SALUD = 6
const SIN_IMPRESORAS: Impresora[] = []

const umbral = (r: ReglasUmbral | undefined, m: keyof ReglasUmbral, s: string) =>
  r?.[m].find((u) => u.severity === s)?.min

const Etiqueta = ({ tono, children }: { tono: Tono; children: ReactNode }) => (
  <Badge variant="outline" className={cn("border-transparent", CLASE_TONO[tono])}>
    {children}
  </Badge>
)

function Seccion({ titulo, meta, children }: { titulo: string; meta?: ReactNode; children: ReactNode }) {
  return (
    <section className="space-y-3 rounded-lg border bg-card p-4">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <h2 className="font-semibold">{titulo}</h2>
        {meta && <div className="flex items-center gap-2 text-xs text-muted-foreground">{meta}</div>}
      </div>
      {children}
    </section>
  )
}

function Cifra({ etiqueta, valor, nota, alerta }: { etiqueta: string; valor: string; nota?: string; alerta?: boolean }) {
  return (
    <div className={cn("rounded-md border p-3", alerta && "border-destructive/40 bg-destructive/5")}>
      <div className="text-[11px] font-medium tracking-wide text-muted-foreground uppercase">{etiqueta}</div>
      <div className={cn("text-2xl font-semibold tabular-nums", alerta && "text-destructive")}>{valor}</div>
      {nota && <div className="truncate text-xs text-muted-foreground" title={nota}>{nota}</div>}
    </div>
  )
}

// Dashboard operativo (dashboard.blade.php y dashboard/partials/operacion del
// PHP), en este orden a propósito: lo que está roto ahora (estado actual e
// incidencias, que no dependen del periodo) y después el balance del periodo.
// Quien ve todas las tiendas tiene además la salud por tienda; el resto, las
// impresoras de la suya. Se relee cada 30 s.
export default function Dashboard() {
  const verTodas = usePermiso(P.todasLasTiendas)
  const admin = usePermiso(P.administrar)
  const { abrir } = useWorkspace()
  const [ventana, setVentana] = useState<VentanaDashboard>("today")
  const [tienda, setTienda] = useState<number>()
  const [todasLasFilas, setTodasLasFilas] = useState(false)
  const resumen = useResumen(ventana, tienda)
  const serie = useSerie(ventana, tienda)
  const impresoras = useImpresoras().data ?? SIN_IMPRESORAS
  const tiendas = useTiendas().data ?? []
  const umbrales = useUmbrales().data
  const ir = (d: Destino) => abrir(d.panel, d.clave, { titulo: d.titulo })

  const datos = resumen.data
  // Las impresoras que cuenta el dashboard: las activas de sus tiendas.
  const enAmbito = useMemo(() => {
    const ids = new Set(datos?.stores.flatMap((t) => t.printers.map((p) => p.printerId)))
    return impresoras.filter((i) => ids.has(i.printerId))
  }, [datos, impresoras])
  const lista = useMemo(
    () =>
      datos
        ? incidencias(datos, impresoras, {
            admin,
            umbralCola: (s) => umbral(umbrales, "queue", s),
          })
        : [],
    [datos, impresoras, admin, umbrales]
  )
  const columnasIncidencias = useMemo(
    () => columnasDeIncidencias(verTodas, ir),
    // ir solo usa abrir, estable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [verTodas]
  )
  const columnasSalud = useMemo(
    () => columnasDeSalud(impresoras, ir),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [impresoras]
  )

  if (!datos)
    return (
      <Panel className="items-center justify-center">
        <Spinner />
      </Panel>
    )

  const k = datos.kpis
  const sinImpresora = datos.stores.reduce((s, t) => s + t.unassignedQueueCurrent, 0)
  const porEstado = (e: string) => enAmbito.filter((i) => i.connectivityStatus === e).length
  const sinConexion = porEstado("error")
  const sinHost = porEstado("no_host")
  const sinComprobar = enAmbito.length - sinConexion - sinHost - porEstado("ok")
  const criticas = datos.stores.filter((t) => t.health === "critical").length
  const enAviso = datos.stores.filter((t) => t.health === "warning").length
  const tasa = k.received > 0 ? Math.round((k.printed / k.received) * 100) : null
  const ordenadas = [...datos.stores].sort(
    (a, b) =>
      RANGO_SALUD[a.health] - RANGO_SALUD[b.health] ||
      b.failedWithoutRetryCurrent - a.failedWithoutRetryCurrent ||
      b.queuedCurrent - a.queuedCurrent
  )
  const tiendaItems = [
    { value: TODAS, label: "Todas" },
    ...tiendas.filter((t) => t.isActive).map((t) => ({ value: String(t.storeId), label: nombreTienda(t.storeId, tiendas) })),
  ]

  return (
    <Panel>
      <div className="flex flex-wrap items-end gap-2 border-b pb-3">
        <CampoFiltro etiqueta="Periodo">
          <Select items={VENTANAS} value={ventana} onValueChange={(v) => setVentana(v as VentanaDashboard)}>
            <SelectTrigger className="w-32">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {VENTANAS.map((v) => (
                <SelectItem key={v.value} value={v.value}>
                  {v.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </CampoFiltro>
        {verTodas && (
          <CampoFiltro etiqueta="Tienda">
            <Select
              items={tiendaItems}
              value={tienda === undefined ? TODAS : String(tienda)}
              onValueChange={(v) => setTienda(v === TODAS ? undefined : Number(v))}
            >
              <SelectTrigger className="w-56">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {tiendaItems.map((i) => (
                  <SelectItem key={i.value} value={i.value}>
                    {i.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </CampoFiltro>
        )}
        <span className="ml-auto self-center text-xs text-muted-foreground" title="Se actualiza cada 30 s">
          {resumen.isFetching ? "Actualizando…" : `Actualizado ${fechaHora(datos.generatedAtUtc).slice(11)}`}
        </span>
      </div>

      <Seccion
        titulo="Estado actual de la operación"
        meta={
          criticas > 0 ? (
            <Etiqueta tono="error">{criticas === 1 ? "1 tienda crítica" : `${criticas} tiendas críticas`}</Etiqueta>
          ) : enAviso > 0 ? (
            <Etiqueta tono="aviso">{enAviso === 1 ? "1 tienda en aviso" : `${enAviso} tiendas en aviso`}</Etiqueta>
          ) : (
            <Etiqueta tono="ok">Operación normal</Etiqueta>
          )
        }
      >
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <Cifra etiqueta="En cola" valor={num(k.queueCurrent)} nota="trabajos esperando" />
          <Cifra etiqueta="Sin reenviar" valor={num(k.failedWithoutRetryCurrent)} nota="necesitan seguimiento" alerta={k.failedWithoutRetryCurrent > 0} />
          <Cifra
            etiqueta="Impresoras con problema"
            valor={num(sinConexion + sinHost + sinComprobar)}
            nota={
              [
                sinConexion && `${sinConexion} sin conexión`,
                sinHost && `${sinHost} sin host`,
                sinComprobar && `${sinComprobar} sin comprobar`,
              ]
                .filter(Boolean)
                .join(" · ") || "todas responden"
            }
            alerta={sinConexion > 0}
          />
          <Cifra etiqueta="Sin impresora" valor={num(sinImpresora)} nota="trabajos sin asignar" />
        </div>
      </Seccion>

      <Seccion
        titulo="Incidencias, de más a menos grave"
        meta={
          <>
            {lista.length === 1 ? "1 abierta" : `${lista.length} abiertas`}
            {umbrales && (
              <span>
                · umbrales: cola {umbral(umbrales, "queue", "warning") ?? "-"} / {umbral(umbrales, "queue", "critical") ?? "-"} · sin reenviar{" "}
                {umbral(umbrales, "failed", "warning") ?? "-"} / {umbral(umbrales, "failed", "critical") ?? "-"}
              </span>
            )}
          </>
        }
      >
        {lista.length ? (
          <div className="[&_table]:min-w-[56rem] [&_table]:table-fixed">
            <DataTable columns={columnasIncidencias} data={lista} paginar={false} />
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">Sin incidencias abiertas.</p>
        )}
      </Seccion>

      <Seccion
        titulo={`Rendimiento del periodo · ${VENTANAS.find((v) => v.value === ventana)?.label}`}
        meta={k.received > 0 ? `porcentajes sobre ${num(k.received)} recibidos` : "sin trabajos recibidos en el periodo"}
      >
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <Cifra etiqueta="Recibidos" valor={num(k.received)} />
          <Cifra etiqueta="Impresos" valor={num(k.printed)} />
          <Cifra etiqueta="Fallidos" valor={num(k.failed)} alerta={k.failed > 0} />
          <Cifra etiqueta="Tasa de éxito" valor={tasa === null ? "—" : `${tasa} %`} />
        </div>
        {serie.data && serie.data.points.length > 1 && (
          <Grafica serie={serie.data} umbralCritico={umbral(umbrales, "queue", "critical")} />
        )}
      </Seccion>

      {verTodas && tienda === undefined ? (
        <Seccion
          titulo="Salud por tienda, ahora"
          meta={`las que tienen problemas, primero · ${todasLasFilas ? ordenadas.length : Math.min(FILAS_SALUD, ordenadas.length)} de ${ordenadas.length}`}
        >
          <div className="[&_table]:min-w-[56rem] [&_table]:table-fixed">
            <DataTable
              columns={columnasSalud}
              data={todasLasFilas ? ordenadas : ordenadas.slice(0, FILAS_SALUD)}
              paginar={false}
            />
          </div>
          {ordenadas.length > FILAS_SALUD && (
            <Button variant="ghost" size="sm" onClick={() => setTodasLasFilas((v) => !v)}>
              {todasLasFilas ? "Ver solo las primeras" : `Ver las ${ordenadas.length} tiendas`}
            </Button>
          )}
        </Seccion>
      ) : (
        <Seccion titulo="Impresoras de la tienda">
          <ImpresorasDeTienda tienda={datos.stores[0]} impresoras={enAmbito} admin={admin} />
        </Seccion>
      )}
    </Panel>
  )
}

function columnasDeIncidencias(conTienda: boolean, ir: (d: Destino) => void): ColumnDef<Incidencia, unknown>[] {
  return [
    ...(conTienda
      ? [{
          id: "tienda",
          header: "Tienda",
          size: 160,
          cell: ({ row: { original: i } }) => <span className="block truncate">{`${i.storeId} · ${i.storeName}`}</span>,
        } satisfies ColumnDef<Incidencia, unknown>]
      : []),
    {
      id: "severidad",
      header: "Gravedad",
      size: 100,
      cell: ({ row: { original: i } }) => <Etiqueta tono={SALUD[i.severidad].tono}>{SALUD[i.severidad].etiqueta}</Etiqueta>,
    },
    {
      id: "tipo",
      header: "Tipo",
      size: 120,
      cell: ({ row: { original: i } }) => <span className="text-xs font-medium uppercase">{i.tipo}</span>,
    },
    {
      id: "detalle",
      header: "Detalle",
      size: 0,
      cell: ({ row: { original: i } }) => (
        <span className="block truncate" title={i.resto ? `${i.detalle} — ${i.resto}` : i.detalle}>
          <b className="font-medium">{i.detalle}</b>
          {i.resto && <span className="text-muted-foreground"> — {i.resto}</span>}
        </span>
      ),
    },
    {
      id: "desde",
      header: "Desde",
      size: 100,
      cell: ({ row: { original: i } }) => (
        <span title={i.desde ? fechaHora(i.desde) : "Sin dato de inicio"}>{hace(i.desde)}</span>
      ),
    },
    {
      id: "acciones",
      header: "",
      size: 260,
      cell: ({ row: { original: i } }) => (
        <div className="flex justify-end gap-1">
          {i.destinos.map((d, n) => (
            <Button
              key={d.etiqueta}
              size="xs"
              variant={n === i.destinos.length - 1 && i.severidad === "critical" ? "destructive" : "outline"}
              onClick={() => ir(d)}
            >
              {d.etiqueta}
            </Button>
          ))}
        </div>
      ),
    },
  ]
}

function columnasDeSalud(impresoras: readonly Impresora[], ir: (d: Destino) => void): ColumnDef<TiendaDashboard, unknown>[] {
  const ok = (t: TiendaDashboard) =>
    t.printers.filter((p) => impresoras.find((i) => i.printerId === p.printerId)?.connectivityStatus === "ok").length
  return [
    {
      id: "tienda",
      header: "Tienda",
      size: 190,
      cell: ({ row: { original: t } }) => <span className="block truncate">{`${t.storeId} · ${t.storeName}`}</span>,
    },
    {
      id: "salud",
      header: "Salud",
      size: 100,
      cell: ({ row: { original: t } }) => <Etiqueta tono={SALUD[t.health].tono}>{SALUD[t.health].etiqueta}</Etiqueta>,
    },
    {
      id: "motivo",
      header: "Motivo",
      size: 0,
      cell: ({ row: { original: t } }) => (
        <span className="block truncate text-muted-foreground" title={t.healthReason}>{t.healthReason}</span>
      ),
    },
    {
      id: "cola",
      header: () => <span className="tracking-normal normal-case">Cola</span>,
      size: 70,
      meta: { num: true },
      cell: ({ row: { original: t } }) => <span className="block text-right tabular-nums">{num(t.queuedCurrent)}</span>,
    },
    {
      id: "sinReenviar",
      header: () => <span className="tracking-normal normal-case">Sin reenviar</span>,
      size: 100,
      meta: { num: true },
      cell: ({ row: { original: t } }) => (
        <span className={cn("block text-right tabular-nums", t.failedWithoutRetryCurrent > 0 && "text-destructive")}>
          {num(t.failedWithoutRetryCurrent)}
        </span>
      ),
    },
    {
      id: "impresoras",
      header: "Impresoras",
      size: 100,
      cell: ({ row: { original: t } }) => {
        const bien = ok(t)
        return (
          <span className={cn("tabular-nums", bien < t.printers.length && "text-destructive")}>
            {bien} / {t.printers.length} OK
          </span>
        )
      },
    },
    {
      id: "ir",
      header: "",
      size: 170,
      cell: ({ row: { original: t } }) => (
        // Cada enlace llega ya filtrado por lo que falla en la tienda.
        <div className="flex justify-end gap-1">
          <Button
            size="xs"
            variant="outline"
            onClick={() =>
              ir({
                etiqueta: "Cola",
                panel: "impresion.cola",
                clave: `tienda=${t.storeId}${t.failedWithoutRetryCurrent > 0 ? "&vista=sinReenviar" : t.queuedCurrent > 0 ? "&estado=0" : ""}`,
                titulo: `Cola · ${t.storeName}`,
              })
            }
          >
            Cola
          </Button>
          <Button
            size="xs"
            variant="outline"
            onClick={() =>
              ir({
                etiqueta: "Impresoras",
                panel: "impresion.impresoras",
                clave: `tienda=${t.storeId}${ok(t) < t.printers.length ? "&conexion=problemas" : ""}`,
                titulo: `Impresoras · ${t.storeName}`,
              })
            }
          >
            Impresoras
          </Button>
        </div>
      ),
    },
  ]
}

// Vista de una tienda: sus impresoras con lo que tienen en cola y lo del periodo.
function ImpresorasDeTienda({
  tienda,
  impresoras,
  admin,
}: {
  tienda?: TiendaDashboard
  impresoras: readonly Impresora[]
  admin: boolean
}) {
  if (!tienda) return <p className="text-sm text-muted-foreground">Sin datos de la tienda.</p>
  if (!tienda.printers.length)
    return <p className="text-sm text-muted-foreground">La tienda no tiene impresoras activas.</p>
  return (
    <ul className="divide-y rounded-md border">
      {tienda.printers.map((p) => {
        const i = impresoras.find((x) => x.printerId === p.printerId)
        return (
          <li key={p.printerId} className="flex flex-wrap items-center gap-x-4 gap-y-1 px-3 py-2 text-sm">
            <span className="min-w-40 flex-1 font-medium">{i?.printerName ?? `Impresora ${p.printerId}`}</span>
            {i && (
              <Etiqueta tono={TONO_SEVERIDAD[i.connectivitySeverity]}>
                {i.connectivityStatus === "error" && !admin ? "Sin conexión" : i.connectivityLabel}
              </Etiqueta>
            )}
            <span className="tabular-nums text-muted-foreground">{num(p.queueCurrent)} en cola</span>
            <span className="tabular-nums text-muted-foreground">
              {num(p.receivedWindow)} recibidos · {num(p.failedWindow)} fallidos en el periodo
            </span>
          </li>
        )
      })}
    </ul>
  )
}
