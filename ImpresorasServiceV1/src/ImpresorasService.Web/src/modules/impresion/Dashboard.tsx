import { useMemo, useState, type ReactNode } from "react"
import type { ColumnDef } from "@tanstack/react-table"
import {
  CircleCheckIcon,
  OctagonAlertIcon,
  TriangleAlertIcon,
} from "lucide-react"
import { Line, LineChart, YAxis } from "recharts"
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
import { ChartContainer } from "@/components/ui/chart"
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
import { useWorkspace, type IdPanel } from "@/workspace/modulo"
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
  franjasDe,
  hace,
  nombreTienda,
  SALUD,
  TONO_SEVERIDAD,
  type Tono,
} from "./formato"
import { SinActualizar } from "./formulario"
import { Grafica } from "./Grafica"
import {
  incidencias,
  RANGO_SALUD,
  type Destino,
  type Incidencia,
} from "./incidencias"

const VENTANAS: { value: VentanaDashboard; label: string }[] = [
  { value: "today", label: "Hoy" },
  { value: "7d", label: "7 días" },
  { value: "30d", label: "30 días" },
]
const TODAS = "todas"
const FILAS_SALUD = 6
const SIN_IMPRESORAS: Impresora[] = []

const umbral = (
  r: ReglasUmbral | undefined,
  m: Exclude<keyof ReglasUmbral, "horario">,
  s: string
) => r?.[m].find((u) => u.severity === s)?.min

const Etiqueta = ({ tono, children }: { tono: Tono; children: ReactNode }) => (
  <Badge
    variant="outline"
    className={cn("border-transparent", CLASE_TONO[tono])}
  >
    {children}
  </Badge>
)

// Estado general, lo primero que se ve: una frase que vale igual para una
// tienda que para todas («1 tienda crítica» con una tienda elegida no dice nada;
// decisión del usuario, 27/10/2026) y, debajo, el porqué.
const ESTADO_GENERAL = {
  error: {
    titulo: "Acción urgente requerida",
    Icono: OctagonAlertIcon,
    clase: "border-destructive/40 bg-destructive/10 text-destructive",
  },
  aviso: {
    titulo: "Operación con avisos",
    Icono: TriangleAlertIcon,
    clase: "border-warning/40 bg-warning/10 text-warning",
  },
  ok: {
    titulo: "Operación normal",
    Icono: CircleCheckIcon,
    clase: "border-success/40 bg-success/10 text-success",
  },
} as const

function EstadoGeneral({
  tono,
  detalle,
}: {
  tono: keyof typeof ESTADO_GENERAL
  detalle: string
}) {
  const { titulo, Icono, clase } = ESTADO_GENERAL[tono]
  return (
    <div
      role="status"
      className={cn(
        "flex items-center gap-3 rounded-lg border px-4 py-3",
        clase
      )}
    >
      <Icono aria-hidden className="size-7 shrink-0" />
      <div className="min-w-0">
        <p className="text-lg leading-tight font-semibold">{titulo}</p>
        <p className="text-sm text-foreground/80">{detalle}</p>
      </div>
    </div>
  )
}

function Seccion({
  titulo,
  meta,
  children,
}: {
  titulo: string
  meta?: ReactNode
  children: ReactNode
}) {
  return (
    <section className="space-y-3 rounded-lg border bg-card p-4">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <h2 className="font-semibold">{titulo}</h2>
        {meta && (
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            {meta}
          </div>
        )}
      </div>
      {children}
    </section>
  )
}

// Las cifras de una sección en una franja: una celda por cifra, separadas por
// una línea (el gap-px deja ver el borde del fondo), en vez de cuadros sueltos
// con hueco dentro (decisión del usuario, 27/10/2026). Se reparten por el ancho
// del panel (@container), no de la ventana.
const Cifras = ({ children }: { children: ReactNode }) => (
  <div className="grid grid-cols-2 gap-px overflow-hidden rounded-md border bg-border @3xl:auto-cols-fr @3xl:grid-flow-col @3xl:grid-cols-none">
    {children}
  </div>
)

function Cifra({
  etiqueta,
  valor,
  nota,
  alerta,
  tendencia,
  color = "var(--chart-1)",
  onClick,
}: {
  etiqueta: string
  valor: string
  nota?: string
  alerta?: boolean
  // Valores del periodo por franja: se dibujan como una mini-gráfica.
  tendencia?: number[]
  color?: string
  // Con onClick es un botón: abre el detalle de la cifra.
  onClick?: () => void
}) {
  const Caja = onClick ? "button" : "div"
  return (
    <Caja
      {...(onClick && { type: "button" as const, onClick })}
      className={cn(
        "flex min-w-0 flex-col gap-1 bg-card px-4 py-3 text-left",
        onClick && "transition-colors hover:bg-accent",
        // Opaco: con transparencia se ve el gris de las líneas de la franja.
        alerta && "bg-[color-mix(in_oklab,var(--destructive)_6%,var(--card))]"
      )}
    >
      <div className="truncate text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
        {etiqueta}
      </div>
      <div className="flex min-w-0 items-baseline gap-2">
        <span
          className={cn(
            "text-2xl font-semibold tabular-nums",
            alerta && "text-destructive"
          )}
        >
          {valor}
        </span>
        {nota && (
          <span className="truncate text-xs text-muted-foreground" title={nota}>
            {nota}
          </span>
        )}
      </div>
      {tendencia && tendencia.length > 1 && (
        <Tendencia
          valores={tendencia}
          color={alerta ? "var(--destructive)" : color}
        />
      )}
    </Caja>
  )
}

// Mini-gráfica sin ejes: solo la forma del periodo. Decorativa: el dato está
// en la gráfica grande y en «Ver como tabla»; sin ratón (Recharts pintaba un
// punto bajo el cursor al pasar por la cifra).
function Tendencia({ valores, color }: { valores: number[]; color: string }) {
  return (
    <ChartContainer
      aria-hidden
      config={{ v: { color } }}
      className="pointer-events-none aspect-auto h-8 w-full"
    >
      <LineChart
        data={valores.map((v, i) => ({ i, v }))}
        margin={{ top: 2, right: 0, bottom: 2, left: 0 }}
      >
        {/* De 0 a, como poco, 1: todo a cero queda como una línea abajo. */}
        <YAxis hide domain={[0, (max: number) => Math.max(max, 1)]} />
        <Line
          dataKey="v"
          type="monotone"
          stroke="var(--color-v)"
          strokeWidth={1.5}
          dot={false}
          activeDot={false}
          isAnimationActive={false}
        />
      </LineChart>
    </ChartContainer>
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
  const verReimpresiones = usePermiso(P.reimprimir)
  const { abrir } = useWorkspace()
  const [ventana, setVentana] = useState<VentanaDashboard>("today")
  const [tienda, setTienda] = useState<number>()
  const [todasLasFilas, setTodasLasFilas] = useState(false)
  const resumen = useResumen(ventana, tienda)
  // Franja de la gráfica (0 = automática); si el periodo nuevo no la admite, la automática.
  const [franjaElegida, setFranja] = useState(0)
  const franjas = franjasDe(ventana)
  const franja = franjas.some((f) => f.minutos === franjaElegida)
    ? franjaElegida
    : 0
  const serie = useSerie(ventana, tienda, franja)
  const impresoras = useImpresoras().data ?? SIN_IMPRESORAS
  const tiendas = useTiendas().data ?? []
  const umbrales = useUmbrales().data
  const ir = (d: Destino) => abrir(d.panel, d.clave, { titulo: d.titulo })

  const datos = resumen.data
  // Las impresoras que cuenta el dashboard: las activas de sus tiendas.
  const enAmbito = useMemo(() => {
    const ids = new Set(
      datos?.stores.flatMap((t) => t.printers.map((p) => p.printerId))
    )
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
  const sinImpresora = datos.stores.reduce(
    (s, t) => s + t.unassignedQueueCurrent,
    0
  )
  const porEstado = (e: string) =>
    enAmbito.filter((i) => i.connectivityStatus === e).length
  const sinConexion = porEstado("error")
  const sinHost = porEstado("no_host")
  const sinComprobar = enAmbito.length - sinConexion - sinHost - porEstado("ok")
  const criticas = datos.stores.filter((t) => t.health === "critical").length
  const enAviso = datos.stores.filter((t) => t.health === "warning").length
  const estado = criticas > 0 ? "error" : enAviso > 0 ? "aviso" : "ok"
  // Con una sola tienda (elegida o la del usuario), su motivo; con varias,
  // cuántas hay en cada estado.
  const unaTienda = datos.stores.length === 1 ? datos.stores[0] : undefined
  const detalleEstado = unaTienda
    ? estado === "ok"
      ? `${unaTienda.storeName}: sin incidencias`
      : `${unaTienda.storeName}: ${unaTienda.healthReason || `${lista.length} incidencias abiertas`}`
    : estado === "ok"
      ? datos.stores.length === 0
        ? "No hay tiendas activas"
        : `Las ${datos.stores.length} tiendas funcionan con normalidad`
      : [
          criticas > 0 &&
            (criticas === 1
              ? "1 tienda crítica"
              : `${criticas} tiendas críticas`),
          enAviso > 0 &&
            (enAviso === 1
              ? "1 tienda en aviso"
              : `${enAviso} tiendas en aviso`),
          `${datos.stores.length - criticas - enAviso} sin problemas`,
        ]
          .filter(Boolean)
          .join(" · ")
  const tasa =
    k.received > 0 ? Math.round((k.printed / k.received) * 100) : null
  const ordenadas = [...datos.stores].sort(
    (a, b) =>
      RANGO_SALUD[a.health] - RANGO_SALUD[b.health] ||
      b.failedWithoutRetryCurrent - a.failedWithoutRetryCurrent ||
      b.queuedCurrent - a.queuedCurrent
  )
  const tiendaItems = [
    { value: TODAS, label: "Todas" },
    ...tiendas
      .filter((t) => t.isActive)
      .map((t) => ({
        value: String(t.storeId),
        label: nombreTienda(t.storeId, tiendas),
      })),
  ]

  // Valores de la serie del periodo, para las mini-gráficas de las cifras.
  const tramos = serie.data?.points ?? []
  const de = (c: "received" | "printed" | "failed") => tramos.map((p) => p[c])
  // En cola, la mayor de cada tramo (sin queuePeak, API anterior: la del final).
  const picosCola = tramos.map((p) => p.queuePeak ?? p.queueDepth)
  const picoCola = picosCola.length ? Math.max(...picosCola) : 0
  // La cola es una foto del instante: por días no se puede dibujar.
  const colaPorTramos =
    (serie.data?.bucketMinutes ?? (serie.data?.bucket === "hour" ? 60 : 1440)) <
    1440

  return (
    <Panel className="@container">
      <div className="flex flex-wrap items-end gap-2 border-b pb-3">
        <CampoFiltro etiqueta="Periodo">
          <Select
            items={VENTANAS}
            value={ventana}
            onValueChange={(v) => setVentana(v as VentanaDashboard)}
          >
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
              onValueChange={(v) =>
                setTienda(v === TODAS ? undefined : Number(v))
              }
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
        <span
          className="ml-auto self-center text-xs text-muted-foreground"
          title="Se actualiza cada 30 s"
        >
          {resumen.isFetching
            ? "Actualizando…"
            : `Actualizado ${fechaHora(datos.generatedAtUtc).slice(11)}`}
        </span>
      </div>
      <SinActualizar consultas={[resumen, serie]} />

      <EstadoGeneral tono={estado} detalle={detalleEstado} />

      <Seccion titulo="Estado actual de la operación">
        <Cifras>
          <Cifra
            etiqueta="En cola"
            valor={num(k.queueCurrent)}
            nota={
              colaPorTramos && picoCola > 0
                ? `máx. ${num(picoCola)} en el periodo`
                : "trabajos esperando"
            }
            color="var(--chart-3)"
            tendencia={colaPorTramos ? picosCola : undefined}
          />
          <Cifra
            etiqueta="Sin reenviar"
            valor={num(k.failedWithoutRetryCurrent)}
            nota="necesitan seguimiento"
            alerta={k.failedWithoutRetryCurrent > 0}
          />
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
          <Cifra
            etiqueta="Sin impresora"
            valor={num(sinImpresora)}
            nota="trabajos sin asignar"
          />
        </Cifras>
      </Seccion>

      <Seccion
        titulo="Incidencias, de más a menos grave"
        meta={
          <>
            {lista.length === 1 ? "1 abierta" : `${lista.length} abiertas`}
            {umbrales && (
              <span>
                · umbrales: cola {umbral(umbrales, "queue", "warning") ?? "-"} /{" "}
                {umbral(umbrales, "queue", "critical") ?? "-"} · sin reenviar{" "}
                {umbral(umbrales, "failed", "warning") ?? "-"} /{" "}
                {umbral(umbrales, "failed", "critical") ?? "-"}
              </span>
            )}
          </>
        }
      >
        {lista.length ? (
          <div className="[&_table]:min-w-[56rem] [&_table]:table-fixed">
            <DataTable
              columns={columnasIncidencias}
              data={lista}
              paginar={false}
            />
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">
            Sin incidencias abiertas.
          </p>
        )}
      </Seccion>

      <Seccion
        titulo={`Rendimiento del periodo · ${VENTANAS.find((v) => v.value === ventana)?.label}`}
        meta={
          k.received > 0
            ? `porcentajes sobre ${num(k.received)} recibidos`
            : "sin trabajos recibidos en el periodo"
        }
      >
        <Cifras>
          <Cifra
            etiqueta="Recibidos"
            valor={num(k.received)}
            tendencia={de("received")}
          />
          <Cifra
            etiqueta="Impresos"
            valor={num(k.printed)}
            tendencia={de("printed")}
            color="var(--success)"
          />
          <Cifra
            etiqueta="Fallidos"
            valor={num(k.failed)}
            alerta={k.failed > 0}
            tendencia={de("failed")}
            color="var(--destructive)"
          />
          <Cifra
            etiqueta="Tasa de éxito"
            valor={tasa === null ? "—" : `${tasa} %`}
          />
          {/* Reimpresiones de lo que ya había salido: el control de que los
              duplicados no se disparen (sin dato con una API anterior). */}
          {k.reprints !== undefined && (
            <Cifra
              etiqueta="Reimpresiones"
              valor={num(k.reprints)}
              nota={
                verReimpresiones ? "ver el registro" : "copias de lo ya impreso"
              }
              onClick={
                verReimpresiones
                  ? // La misma pestaña que el menú, sin clave: con el periodo y
                    // la tienda en la clave abría otra «Reimpresiones» igual
                    // (decisión del usuario, 27/10/2026).
                    () => abrir("impresionControl.reimpresiones" as IdPanel)
                  : undefined
              }
            />
          )}
        </Cifras>
        {serie.data && serie.data.points.length > 1 && (
          <Grafica
            serie={serie.data}
            umbralCritico={umbral(umbrales, "queue", "critical")}
            franja={franja}
            franjas={franjas}
            onFranja={setFranja}
          />
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
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setTodasLasFilas((v) => !v)}
            >
              {todasLasFilas
                ? "Ver solo las primeras"
                : `Ver las ${ordenadas.length} tiendas`}
            </Button>
          )}
        </Seccion>
      ) : (
        <Seccion titulo="Impresoras de la tienda">
          <ImpresorasDeTienda
            tienda={datos.stores[0]}
            impresoras={enAmbito}
            admin={admin}
          />
        </Seccion>
      )}
    </Panel>
  )
}

function columnasDeIncidencias(
  conTienda: boolean,
  ir: (d: Destino) => void
): ColumnDef<Incidencia, unknown>[] {
  return [
    ...(conTienda
      ? [
          {
            id: "tienda",
            header: "Tienda",
            size: 160,
            cell: ({ row: { original: i } }) => (
              <span className="block truncate">{`${i.storeId} · ${i.storeName}`}</span>
            ),
          } satisfies ColumnDef<Incidencia, unknown>,
        ]
      : []),
    {
      id: "severidad",
      header: "Gravedad",
      size: 100,
      cell: ({ row: { original: i } }) => (
        <Etiqueta tono={SALUD[i.severidad].tono}>
          {SALUD[i.severidad].etiqueta}
        </Etiqueta>
      ),
    },
    {
      id: "tipo",
      header: "Tipo",
      size: 120,
      cell: ({ row: { original: i } }) => (
        <span className="text-xs font-medium uppercase">{i.tipo}</span>
      ),
    },
    {
      id: "detalle",
      header: "Detalle",
      size: 0,
      cell: ({ row: { original: i } }) => (
        <span
          className="block truncate"
          title={i.resto ? `${i.detalle} — ${i.resto}` : i.detalle}
        >
          <b className="font-medium">{i.detalle}</b>
          {i.resto && (
            <span className="text-muted-foreground"> — {i.resto}</span>
          )}
        </span>
      ),
    },
    {
      id: "desde",
      header: "Desde",
      size: 100,
      cell: ({ row: { original: i } }) => (
        <span title={i.desde ? fechaHora(i.desde) : "Sin dato de inicio"}>
          {hace(i.desde)}
        </span>
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
              variant={
                n === i.destinos.length - 1 && i.severidad === "critical"
                  ? "destructive"
                  : "outline"
              }
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

function columnasDeSalud(
  impresoras: readonly Impresora[],
  ir: (d: Destino) => void
): ColumnDef<TiendaDashboard, unknown>[] {
  const ok = (t: TiendaDashboard) =>
    t.printers.filter(
      (p) =>
        impresoras.find((i) => i.printerId === p.printerId)
          ?.connectivityStatus === "ok"
    ).length
  return [
    {
      id: "tienda",
      header: "Tienda",
      size: 190,
      cell: ({ row: { original: t } }) => (
        <span className="block truncate">{`${t.storeId} · ${t.storeName}`}</span>
      ),
    },
    {
      id: "salud",
      header: "Salud",
      size: 100,
      cell: ({ row: { original: t } }) => (
        <Etiqueta tono={SALUD[t.health].tono}>
          {SALUD[t.health].etiqueta}
        </Etiqueta>
      ),
    },
    {
      id: "motivo",
      header: "Motivo",
      size: 0,
      cell: ({ row: { original: t } }) => (
        <span
          className="block truncate text-muted-foreground"
          title={t.healthReason}
        >
          {t.healthReason}
        </span>
      ),
    },
    {
      id: "cola",
      header: () => <span className="tracking-normal normal-case">Cola</span>,
      size: 70,
      meta: { num: true },
      cell: ({ row: { original: t } }) => (
        <span className="block text-right tabular-nums">
          {num(t.queuedCurrent)}
        </span>
      ),
    },
    {
      id: "sinReenviar",
      header: () => (
        <span className="tracking-normal normal-case">Sin reenviar</span>
      ),
      size: 100,
      meta: { num: true },
      cell: ({ row: { original: t } }) => (
        <span
          className={cn(
            "block text-right tabular-nums",
            t.failedWithoutRetryCurrent > 0 && "text-destructive"
          )}
        >
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
          <span
            className={cn(
              "tabular-nums",
              bien < t.printers.length && "text-destructive"
            )}
          >
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
  if (!tienda)
    return (
      <p className="text-sm text-muted-foreground">Sin datos de la tienda.</p>
    )
  if (!tienda.printers.length)
    return (
      <p className="text-sm text-muted-foreground">
        La tienda no tiene impresoras activas.
      </p>
    )
  return (
    <ul className="divide-y rounded-md border">
      {tienda.printers.map((p) => {
        const i = impresoras.find((x) => x.printerId === p.printerId)
        return (
          <li
            key={p.printerId}
            className="flex flex-wrap items-center gap-x-4 gap-y-1 px-3 py-2 text-sm"
          >
            <span className="min-w-40 flex-1 font-medium">
              {i?.printerName ?? `Impresora ${p.printerId}`}
            </span>
            {i && (
              <Etiqueta tono={TONO_SEVERIDAD[i.connectivitySeverity]}>
                {i.connectivityStatus === "error" && !admin
                  ? "Sin conexión"
                  : i.connectivityLabel}
              </Etiqueta>
            )}
            <span className="text-muted-foreground tabular-nums">
              {num(p.queueCurrent)} en cola
            </span>
            <span className="text-muted-foreground tabular-nums">
              {num(p.receivedWindow)} recibidos · {num(p.failedWindow)} fallidos
              en el periodo
            </span>
          </li>
        )
      })}
    </ul>
  )
}
