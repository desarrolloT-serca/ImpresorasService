import { useId, useState } from "react"
import type { SerieDashboard } from "@serca/contratos"
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  LabelList,
  ReferenceArea,
  ReferenceLine,
  XAxis,
  YAxis,
} from "recharts"

import {
  ChartContainer,
  ChartTooltip,
  type ChartConfig,
} from "@/components/ui/chart"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { num } from "@/lib/format"
import { escala, FRANJAS } from "./formato"

type Vista = "trabajos" | "cola"
type Punto = SerieDashboard["points"][number]

// Colores solo con tokens del ERP.
const CONFIG = {
  received: { label: "Recibidos", color: "var(--chart-1)" },
  printed: { label: "Impresos", color: "var(--success)" },
  cancelled: { label: "Cancelados", color: "var(--chart-4)" },
  active: { label: "En curso", color: "var(--chart-3)" },
  failed: { label: "Fallidos sin resolver", color: "var(--destructive)" },
  queueDepth: { label: "Máx. en cola", color: "var(--chart-3)" },
} satisfies ChartConfig
// Lo que llegó en cada franja, por cómo está ahora, de abajo arriba: lo
// resuelto abajo y los fallos sin resolver encima, que es lo que hay que ver.
const RESULTADOS = ["printed", "cancelled", "active", "failed"] as const
// Rápida: al cambiar de franja o de tienda las barras no deben hacerse esperar.
const ANIMACION = {
  animationDuration: 250,
  animationEasing: "ease-out",
} as const

const fmtHora = new Intl.DateTimeFormat("es-ES", {
  timeZone: "Europe/Madrid",
  hour: "2-digit",
  minute: "2-digit",
})
const fmtDia = new Intl.DateTimeFormat("es-ES", {
  timeZone: "Europe/Madrid",
  day: "2-digit",
  month: "2-digit",
})
// Por horas, solo de 07:30 a 20:00 (hora de Madrid) salvo con «24 h»: de noche
// no hay actividad y solo ocupaba sitio (decisión del usuario, 27/10/2026).
const JORNADA = { desde: 7 * 60 + 30, hasta: 20 * 60, texto: "07:30–20:00" }
const fmtMinuto = new Intl.DateTimeFormat("en-GB", {
  timeZone: "Europe/Madrid",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
})
const minutoDelDia = (iso: string) => {
  const [h, m] = fmtMinuto.format(new Date(iso)).split(":").map(Number)
  return h * 60 + m
}

// El detalle de una franja, con todas sus cifras.
function Detalle({
  titulo,
  filas,
}: {
  titulo: string
  filas: { clave: keyof typeof CONFIG; valor: number; total?: boolean }[]
}) {
  return (
    <div className="grid min-w-36 gap-1.5 rounded-lg border border-border/50 bg-background px-2.5 py-1.5 text-xs shadow-xl">
      <div className="font-medium">{titulo}</div>
      {filas.map((f) => (
        <div key={f.clave} className="flex items-center gap-2">
          <span
            aria-hidden
            className="size-2.5 shrink-0 rounded-[2px]"
            style={{
              background: f.total ? "transparent" : CONFIG[f.clave].color,
            }}
          />
          <span className="text-muted-foreground">{CONFIG[f.clave].label}</span>
          <span className="ml-auto pl-3 font-medium text-foreground tabular-nums">
            {num(f.valor)}
          </span>
        </div>
      ))}
    </div>
  )
}

// Evolución del periodo (dashboard/partials/grafica-cola.blade.php del PHP),
// con shadcn/ui chart (Recharts), por la franja elegida (automática según el
// periodo). Dos vistas:
// - Trabajos: una barra por franja con lo que llegó, partida por cómo está
//   ahora (outcome de la API): impreso, cancelado, en curso y fallido sin
//   resolver. Cada trabajo cuenta una vez, en la franja en que llegó, y un fallo
//   sigue en rojo hasta que se resuelve; al reimprimirlo y salir pasa a verde
//   (decisión del usuario, 27/10/2026: con los fallos por la hora del evento,
//   los que nadie resolvía se perdían de vista).
// - Cola: la profundidad con el umbral crítico y la zona por encima en rojo:
//   un «47 en cola» no dice si hay que actuar; cruzando la línea de 30, sí. Por
//   días no se ofrece: es una foto del instante y no se puede promediar por día.
// El detalle de la franja va fijo arriba y sin animar: siguiendo al cursor
// saltaba de un lado a otro.
export function Grafica({
  serie,
  umbralCritico,
  franja,
  franjas,
  onFranja,
}: {
  serie: SerieDashboard
  umbralCritico?: number
  franja: number
  franjas: typeof FRANJAS
  onFranja: (minutos: number) => void
}) {
  const id = useId().replace(/:/g, "")
  const minutos = serie.bucketMinutes ?? (serie.bucket === "hour" ? 60 : 1440)
  const porHoras = minutos < 1440
  const [vistaElegida, setVista] = useState<Vista>("trabajos")
  const vista = porHoras ? vistaElegida : "trabajos"
  const [todoElDia, setTodoElDia] = useState(false)

  // Las franjas que tocan la jornada; por días, todas.
  // En cola, la mayor de cada franja: un trabajo espera segundos y la del
  // final casi siempre es 0. Sin queuePeak (API anterior), la del final.
  const conPico = serie.points.map((p) => ({
    ...p,
    queueDepth: p.queuePeak ?? p.queueDepth,
  }))
  const puntos =
    porHoras && !todoElDia
      ? conPico.filter((p) => {
          const inicio = minutoDelDia(p.startUtc)
          return inicio < JORNADA.hasta && inicio + minutos > JORNADA.desde
        })
      : conPico
  const recibidos = (ps: readonly Punto[]) =>
    ps.reduce((n, p) => n + p.received, 0)
  const fuera = recibidos(serie.points) - recibidos(puntos)
  // Sin outcome (API anterior al 27/10/2026), solo lo recibido, sin desglose:
  // pintarlo todo como «en curso» lo tapaba todo y no era verdad.
  const sinResultado = !serie.points.some((p) => p.outcome)
  const desglose = sinResultado ? [] : RESULTADOS
  const series: readonly (keyof typeof CONFIG)[] = sinResultado
    ? ["received"]
    : RESULTADOS
  const datos = puntos.map((p) => ({
    ...p,
    ...(p.outcome ?? {
      printed: 0,
      failed: 0,
      cancelled: 0,
      active: 0,
    }),
  }))
  // Por horas en varios días (7 días de 4 en 4 h), con el día delante.
  const variosDias =
    puntos.length > 1 &&
    fmtDia.format(new Date(puntos[0].startUtc)) !==
      fmtDia.format(new Date(puntos[puntos.length - 1].startUtc))
  const hora = (ms: number) => fmtHora.format(new Date(ms))
  const etiqueta = (iso: string) =>
    porHoras
      ? `${variosDias ? `${fmtDia.format(new Date(iso))} ` : ""}${hora(Date.parse(iso))}`
      : fmtDia.format(new Date(iso))
  // En el detalle, la franja entera: «13:00 – 13:30».
  const rango = (iso: string) =>
    porHoras
      ? `${etiqueta(iso)} – ${hora(Date.parse(iso) + minutos * 60_000)}`
      : etiqueta(iso)
  const franjaActual =
    FRANJAS.find((f) => f.minutos === minutos)?.etiqueta ?? `${minutos} min`

  // El pico de recibidos, con su cifra encima: la única escrita en la gráfica.
  const pico = Math.max(...puntos.map((p) => p.received))
  const umbral = vista === "cola" ? umbralCritico : undefined
  // Con aire por encima del umbral, para que se vea la zona roja.
  const topeCola = escala(
    Math.max(...puntos.map((p) => p.queueDepth), (umbral ?? 0) * 1.25)
  )

  const comunes = (
    <>
      <CartesianGrid vertical={false} strokeDasharray="3 3" />
      <XAxis
        dataKey="startUtc"
        tickFormatter={etiqueta}
        tickLine={false}
        axisLine={false}
        tickMargin={8}
        minTickGap={28}
      />
      <ChartTooltip
        isAnimationActive={false}
        position={{ y: 0 }}
        content={({ active, payload, label }) => {
          const p = payload?.[0]?.payload as (typeof datos)[number] | undefined
          if (!active || !p) return null
          return (
            <Detalle
              titulo={rango(String(label))}
              filas={
                vista === "cola"
                  ? [{ clave: "queueDepth", valor: p.queueDepth }]
                  : [
                      {
                        clave: "received",
                        valor: p.received,
                        total: !sinResultado,
                      },
                      ...[...desglose]
                        .reverse()
                        .map((c) => ({ clave: c, valor: p[c] })),
                    ]
              }
            />
          )
        }}
      />
    </>
  )

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        {porHoras && (
          <ToggleGroup
            variant="outline"
            size="sm"
            value={[vista]}
            onValueChange={(v) => v[0] && setVista(v[0] as Vista)}
            aria-label="Qué se ve en la gráfica"
          >
            <ToggleGroupItem value="trabajos">Trabajos</ToggleGroupItem>
            <ToggleGroupItem value="cola">Cola</ToggleGroupItem>
          </ToggleGroup>
        )}
        {vista === "trabajos" && (
          <ul className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
            {series.map((c) => (
              <li key={c} className="flex items-center gap-1.5">
                <span
                  aria-hidden
                  className="size-2.5 rounded-[2px]"
                  style={{ background: CONFIG[c].color }}
                />
                {CONFIG[c].label}
              </li>
            ))}
          </ul>
        )}
        <div className="ml-auto flex items-center gap-2">
          {porHoras && (
            <ToggleGroup
              variant="outline"
              size="sm"
              value={[todoElDia ? "24h" : "jornada"]}
              onValueChange={(v) => v[0] && setTodoElDia(v[0] === "24h")}
              aria-label="Horas de la gráfica"
            >
              <ToggleGroupItem value="jornada">{JORNADA.texto}</ToggleGroupItem>
              <ToggleGroupItem value="24h">24 h</ToggleGroupItem>
            </ToggleGroup>
          )}
          <Select
            items={franjas.map((f) => ({
              value: String(f.minutos),
              label:
                f.minutos === 0 ? `Automática (${franjaActual})` : f.etiqueta,
            }))}
            value={String(franja)}
            onValueChange={(v) => onFranja(Number(v))}
          >
            <SelectTrigger className="w-48" aria-label="Franja de la gráfica">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {franjas.map((f) => (
                <SelectItem key={f.minutos} value={String(f.minutos)}>
                  {f.minutos === 0
                    ? `Automática (${franjaActual})`
                    : f.etiqueta}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>
      <ChartContainer
        config={CONFIG}
        role="img"
        aria-label={
          vista === "cola"
            ? `Trabajos en cola por franjas de ${franjaActual}${umbral === undefined ? "" : `, con el umbral crítico en ${umbral}`}`
            : `Lo recibido por franjas de ${franjaActual}, según cómo está ahora: impreso, cancelado, en curso o fallido sin resolver`
        }
        className="aspect-auto h-[280px] w-full"
      >
        {vista === "cola" ? (
          <AreaChart data={puntos} margin={{ top: 16, right: 12 }}>
            <defs>
              <linearGradient id={`${id}-cola`} x1="0" y1="0" x2="0" y2="1">
                <stop
                  offset="5%"
                  stopColor="var(--color-queueDepth)"
                  stopOpacity={0.3}
                />
                <stop
                  offset="95%"
                  stopColor="var(--color-queueDepth)"
                  stopOpacity={0.02}
                />
              </linearGradient>
            </defs>
            {comunes}
            <YAxis
              allowDecimals={false}
              tickLine={false}
              axisLine={false}
              width={36}
              tickFormatter={(v: number) => num(v)}
              domain={[0, topeCola]}
              // Con el tope fijo, Recharts reparte marcas desiguales (0, 8, 16, 24, 30).
              ticks={[0, topeCola / 2, topeCola]}
            />
            {umbral !== undefined && (
              <ReferenceArea
                y1={umbral}
                y2={topeCola}
                fill="var(--destructive)"
                fillOpacity={0.07}
              />
            )}
            {umbral !== undefined && (
              <ReferenceLine
                y={umbral}
                stroke="var(--destructive)"
                strokeWidth={1.5}
                strokeDasharray="6 4"
                label={{
                  value: `Umbral crítico · ${num(umbral)}`,
                  position: "insideTopRight",
                  fill: "var(--muted-foreground)",
                  fontSize: 11,
                }}
              />
            )}
            <Area
              dataKey="queueDepth"
              type="monotone"
              stroke="var(--color-queueDepth)"
              strokeWidth={2}
              fill={`url(#${id}-cola)`}
              activeDot={{ r: 4, strokeWidth: 2, stroke: "var(--card)" }}
              {...ANIMACION}
            />
          </AreaChart>
        ) : (
          <BarChart
            data={datos}
            margin={{ top: 20, right: 12 }}
            barCategoryGap="18%"
          >
            {comunes}
            <YAxis
              allowDecimals={false}
              tickLine={false}
              axisLine={false}
              width={36}
              tickFormatter={(v: number) => num(v)}
            />
            {series.map((c, i) => (
              <Bar
                key={c}
                dataKey={c}
                stackId="recibidos"
                fill={`var(--color-${c})`}
                fillOpacity={0.85}
                // Un filo del color de la tarjeta separa los trozos.
                stroke="var(--card)"
                strokeWidth={1}
                radius={2}
                maxBarSize={32}
                activeBar={{ fillOpacity: 1 }}
                {...ANIMACION}
              >
                {/* Sobre el último trozo (en lo alto de la barra, aunque valga
                    0), el total del pico. Por valor: con empate, todas. */}
                {i === series.length - 1 && (
                  <LabelList
                    dataKey="received"
                    content={({ x, y, width, value }) =>
                      pico > 0 && Number(value) === pico ? (
                        <text
                          x={Number(x) + Number(width) / 2}
                          y={Number(y) - 6}
                          textAnchor="middle"
                          className="fill-foreground text-[11px] font-medium"
                        >
                          {num(Number(value))}
                        </text>
                      ) : null
                    }
                  />
                )}
              </Bar>
            ))}
          </BarChart>
        )}
      </ChartContainer>
      {vista === "trabajos" && sinResultado && (
        <p className="text-xs text-muted-foreground">
          La API instalada todavía no da cómo acabó lo recibido: hasta
          desplegarla, solo se ven los recibidos.
        </p>
      )}
      {fuera > 0 && (
        <p className="text-xs text-muted-foreground">
          {fuera === 1 ? "1 recibido" : `${num(fuera)} recibidos`} fuera de{" "}
          {JORNADA.texto}: con «24 h» se ven.
        </p>
      )}
      <details className="text-xs text-muted-foreground">
        <summary className="cursor-pointer">Ver como tabla</summary>
        <table className="mt-2 w-full max-w-xl text-right tabular-nums">
          <thead>
            <tr className="border-b">
              <th className="py-1 text-left font-medium">
                {porHoras ? "Hora" : "Día"}
              </th>
              <th className="py-1 font-medium">Recibidos</th>
              {desglose.map((c) => (
                <th key={c} className="py-1 font-medium">
                  {CONFIG[c].label}
                </th>
              ))}
              {porHoras && <th className="py-1 font-medium">Máx. en cola</th>}
            </tr>
          </thead>
          <tbody>
            {datos.map((p) => (
              <tr
                key={p.startUtc}
                className="border-b border-border/50 text-foreground"
              >
                <td className="py-0.5 text-left">{etiqueta(p.startUtc)}</td>
                <td>{num(p.received)}</td>
                {desglose.map((c) => (
                  <td key={c}>{num(p[c])}</td>
                ))}
                {porHoras && <td>{num(p.queueDepth)}</td>}
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </div>
  )
}
