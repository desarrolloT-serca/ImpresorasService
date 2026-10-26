import { useEffect, useRef, useState } from "react"
import type { SerieDashboard } from "@serca/contratos"

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

type Clave = "queueDepth" | "received" | "printed" | "failed"
// Cola, la última y nunca por defecto (decisión del usuario, 26/10/2026).
const SERIES: { clave: Clave; etiqueta: string; unidad: string }[] = [
  { clave: "received", etiqueta: "Recibidos", unidad: "recibidos" },
  { clave: "printed", etiqueta: "Impresos", unidad: "impresos" },
  { clave: "failed", etiqueta: "Fallidos", unidad: "fallidos" },
  { clave: "queueDepth", etiqueta: "Cola", unidad: "en cola" },
]

const ALTO = 220
const M = { izq: 40, der: 12, arriba: 14, abajo: 26 }

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
const fmtDiaHora = new Intl.DateTimeFormat("es-ES", {
  timeZone: "Europe/Madrid",
  day: "2-digit",
  month: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
})

// Evolución del periodo (dashboard/partials/grafica-cola.blade.php del PHP):
// una serie cada vez, elegida en las pestañas, que explica en el tiempo las
// cifras de encima, por la franja elegida (automática según el periodo). Sale
// «Recibidos» (decisión del usuario, 26/10/2026). La línea del umbral crítico
// solo va en la cola: un «47 en cola» no dice si hay que actuar; cruzando la
// línea de 30, sí. Por días no se ofrece la cola: es una foto del instante y no
// se puede promediar por día.
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
  const minutos = serie.bucketMinutes ?? (serie.bucket === "hour" ? 60 : 1440)
  const porHoras = minutos < 1440
  const disponibles = porHoras
    ? SERIES
    : SERIES.filter((s) => s.clave !== "queueDepth")
  const [elegida, setElegida] = useState<Clave>("received")
  const actual = disponibles.find((s) => s.clave === elegida) ?? disponibles[0]
  const caja = useRef<HTMLDivElement>(null)
  const [ancho, setAncho] = useState(0)
  const [foco, setFoco] = useState<number>()

  useEffect(() => {
    const el = caja.current
    if (!el) return
    const ro = new ResizeObserver(([e]) => setAncho(e.contentRect.width))
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  const puntos = serie.points
  const valores = puntos.map((p) => p[actual.clave])
  const umbral = actual.clave === "queueDepth" ? umbralCritico : undefined
  const tope = escala(Math.max(...valores, umbral ?? 0))
  const anchoUtil = Math.max(ancho - M.izq - M.der, 1)
  const altoUtil = ALTO - M.arriba - M.abajo
  const x = (i: number) =>
    M.izq +
    (puntos.length < 2 ? anchoUtil / 2 : (i * anchoUtil) / (puntos.length - 1))
  const y = (v: number) => M.arriba + altoUtil - (v / tope) * altoUtil
  // Por horas en varios días (7 días de 4 en 4 h), con el día delante.
  const variosDias =
    puntos.length > 1 &&
    fmtDia.format(new Date(puntos[0].startUtc)) !==
      fmtDia.format(new Date(puntos[puntos.length - 1].startUtc))
  const etiqueta = (iso: string) =>
    (porHoras ? (variosDias ? fmtDiaHora : fmtHora) : fmtDia)
      .format(new Date(iso))
      .replace(",", "")
  const franjaActual =
    FRANJAS.find((f) => f.minutos === minutos)?.etiqueta ?? `${minutos} min`
  // Unas 6 etiquetas en el eje, nunca una por tramo.
  const cadaX = Math.max(1, Math.ceil(puntos.length / 6))

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        <ToggleGroup
          variant="outline"
          size="sm"
          value={[actual.clave]}
          onValueChange={(v) => v[0] && setElegida(v[0] as Clave)}
          aria-label="Serie de la gráfica"
        >
          {disponibles.map((s) => (
            <ToggleGroupItem key={s.clave} value={s.clave}>
              {s.etiqueta}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
        <Select
          items={franjas.map((f) => ({
            value: String(f.minutos),
            label:
              f.minutos === 0 ? `Automática (${franjaActual})` : f.etiqueta,
          }))}
          value={String(franja)}
          onValueChange={(v) => onFranja(Number(v))}
        >
          <SelectTrigger
            className="ml-auto w-40"
            aria-label="Franja de la gráfica"
          >
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {franjas.map((f) => (
              <SelectItem key={f.minutos} value={String(f.minutos)}>
                {f.minutos === 0 ? `Automática (${franjaActual})` : f.etiqueta}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div ref={caja} className="relative">
        {ancho > 0 && puntos.length > 0 && (
          <svg
            width={ancho}
            height={ALTO}
            role="img"
            aria-label={`${actual.etiqueta} por franjas de ${franjaActual} en el periodo`}
            className="block"
            onPointerLeave={() => setFoco(undefined)}
            onPointerMove={(e) => {
              const r = e.currentTarget.getBoundingClientRect()
              const i = Math.round(
                ((e.clientX - r.left - M.izq) / anchoUtil) * (puntos.length - 1)
              )
              setFoco(Math.min(Math.max(i, 0), puntos.length - 1))
            }}
          >
            {[0, tope / 2, tope].map((v) => (
              <g key={v}>
                <line
                  x1={M.izq}
                  x2={ancho - M.der}
                  y1={y(v)}
                  y2={y(v)}
                  stroke="var(--border)"
                />
                <text
                  x={M.izq - 6}
                  y={y(v)}
                  dy="0.32em"
                  textAnchor="end"
                  className="fill-muted-foreground text-[11px]"
                >
                  {num(v)}
                </text>
              </g>
            ))}
            {puntos.map((p, i) =>
              i % cadaX === 0 ? (
                <text
                  key={p.startUtc}
                  x={x(i)}
                  y={ALTO - 8}
                  textAnchor="middle"
                  className="fill-muted-foreground text-[11px]"
                >
                  {etiqueta(p.startUtc)}
                </text>
              ) : null
            )}
            {umbral !== undefined && (
              <g>
                <line
                  x1={M.izq}
                  x2={ancho - M.der}
                  y1={y(umbral)}
                  y2={y(umbral)}
                  stroke="var(--destructive)"
                  strokeWidth={1.5}
                  strokeDasharray="6 4"
                />
                <text
                  x={ancho - M.der}
                  y={y(umbral) - 5}
                  textAnchor="end"
                  className="fill-muted-foreground text-[11px]"
                >
                  Umbral crítico · {num(umbral)}
                </text>
              </g>
            )}
            <polyline
              fill="none"
              stroke="var(--chart-1)"
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
              points={valores.map((v, i) => `${x(i)},${y(v)}`).join(" ")}
            />
            {foco !== undefined && (
              <g>
                <line
                  x1={x(foco)}
                  x2={x(foco)}
                  y1={M.arriba}
                  y2={M.arriba + altoUtil}
                  stroke="var(--muted-foreground)"
                  strokeDasharray="2 3"
                />
                <circle
                  cx={x(foco)}
                  cy={y(valores[foco])}
                  r={4}
                  fill="var(--chart-1)"
                  stroke="var(--card)"
                  strokeWidth={2}
                />
              </g>
            )}
          </svg>
        )}
        {foco !== undefined && (
          <div
            className="pointer-events-none absolute top-0 rounded-md border bg-popover px-2 py-1 text-xs shadow-sm"
            style={{
              left: Math.min(x(foco) + 8, Math.max(ancho - 150, 0)),
            }}
          >
            <div className="text-muted-foreground">
              {porHoras
                ? `${etiqueta(puntos[foco].startUtc)} h`
                : etiqueta(puntos[foco].startUtc)}
            </div>
            <div className="font-semibold">
              {num(valores[foco])} {actual.unidad}
            </div>
          </div>
        )}
      </div>
      <details className="text-xs text-muted-foreground">
        <summary className="cursor-pointer">Ver como tabla</summary>
        <table className="mt-2 w-full max-w-xl text-right tabular-nums">
          <thead>
            <tr className="border-b">
              <th className="py-1 text-left font-medium">
                {porHoras ? "Hora" : "Día"}
              </th>
              {disponibles.map((s) => (
                <th key={s.clave} className="py-1 font-medium">
                  {s.etiqueta}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {puntos.map((p) => (
              <tr
                key={p.startUtc}
                className="border-b border-border/50 text-foreground"
              >
                <td className="py-0.5 text-left">{etiqueta(p.startUtc)}</td>
                {disponibles.map((s) => (
                  <td key={s.clave}>{num(p[s.clave])}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </div>
  )
}
