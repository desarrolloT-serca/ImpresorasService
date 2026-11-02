import {
  Fragment,
  useEffectEvent,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type ReactNode,
} from "react"
import { CircleCheckIcon, CircleXIcon, FlaskConicalIcon } from "lucide-react"
import type { Impresora, ReglaEnrutado, Tienda } from "@serca/contratos"

import { CampoFiltro } from "@/components/filtro-lista"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { ErrorApi } from "@/core/api"
import { cn } from "@/lib/utils"
import { useSimularRegla } from "./api"
import {
  COLOR_PUNTO,
  condicion,
  NIVELES_REGLA,
  nivelRegla,
  nombreTienda,
  porQueNo,
  tituloGrupo,
  TIPOS_DOCUMENTO,
  TONO_SEVERIDAD,
  type Grupo,
} from "./formato"

// Las dos mitades de Reglas (tabla y esquema) son tarjetas con la misma
// estructura: título de una línea, barra de controles y contenido. Así sus
// partes quedan a la misma altura (decisión del usuario, 27/10/2026).
export function TarjetaReglas({
  titulo,
  descripcion,
  meta,
  children,
}: {
  titulo: string
  descripcion: string
  meta?: ReactNode
  children: ReactNode
}) {
  return (
    <section className="flex min-h-96 min-w-0 flex-col gap-3 rounded-lg border bg-card p-4">
      <div className="flex min-w-0 items-baseline gap-2">
        <h2 className="shrink-0 font-semibold">{titulo}</h2>
        <p
          className="truncate text-xs text-muted-foreground"
          title={descripcion}
        >
          {descripcion}
        </p>
        {meta && (
          <span className="ml-auto shrink-0 text-xs text-muted-foreground">
            {meta}
          </span>
        )}
      </div>
      {children}
    </section>
  )
}

type Resultado = { ruleId: number } | { ninguna: true } | { error: string }

// Esquema de las reglas (decisión del usuario, 27/10/2026): de cada tienda
// salen sus reglas en el orden en que se prueban y cada una llega a su
// impresora. El simulador pregunta a la API qué regla ganaría y la marca.
// Hecho a mano con CSS (ERP_SERCA/erp/README.md, «Reglas de la base»).
export function EsquemaReglas({
  grupos,
  tiendas,
  impresoras,
  tiendaFija,
  elegida,
  onElegir,
}: {
  grupos: Grupo[]
  tiendas: readonly Tienda[]
  impresoras: readonly Impresora[]
  // La tienda del filtro: el simulador la usa sin preguntar.
  tiendaFija?: number
  elegida?: number
  onElegir: (ruleId: number | undefined) => void
}) {
  const simular = useSimularRegla()
  const activas = tiendas.filter((t) => t.isActive)
  const [tienda, setTienda] = useState<string>()
  const [tipo, setTipo] = useState("TICKET")
  const [canal, setCanal] = useState("DEFAULT")
  const [resultado, setResultado] = useState<Resultado>()
  const lista = useId()
  const storeId =
    tiendaFija ?? Number(tienda ?? activas[0]?.storeId ?? Number.NaN)
  const marcada =
    elegida ??
    (resultado && "ruleId" in resultado ? resultado.ruleId : undefined)
  const numero = new Map(
    grupos.flatMap((g) => g.reglas.map((r, i) => [r.ruleId, i + 1] as const))
  )
  const tipos = [
    ...new Set([
      ...Object.keys(TIPOS_DOCUMENTO),
      ...grupos.flatMap((g) =>
        g.reglas.flatMap((r) => (r.documentType ? [r.documentType] : []))
      ),
    ]),
  ]
  const olvidar = () => setResultado(undefined)

  const probar = () => {
    onElegir(undefined)
    simular.mutate(
      {
        storeId,
        documentType: tipo.trim(),
        channel: canal.trim() || "DEFAULT",
      },
      {
        onSuccess: (r) => setResultado({ ruleId: r.ruleId }),
        onError: (e) =>
          setResultado(
            e instanceof ErrorApi && e.problema.status === 404
              ? { ninguna: true }
              : {
                  error:
                    e instanceof ErrorApi
                      ? e.problema.title
                      : "No se pudo comprobar",
                }
          ),
      }
    )
  }

  const ganadora =
    resultado && "ruleId" in resultado
      ? grupos
          .flatMap((g) => g.reglas)
          .find((r) => r.ruleId === resultado.ruleId)
      : undefined
  const impresoraGanadora = impresoras.find(
    (i) => i.printerId === ganadora?.printerId
  )

  return (
    <TarjetaReglas
      titulo="Recorrido"
      descripcion="De arriba abajo en cada tienda: gana la primera que encaja; si ninguna, la global."
    >
      {/* Misma forma que la barra de la tabla: queda a su altura. */}
      <form
        className="flex flex-wrap items-end gap-2 border-b pb-3"
        onSubmit={(e) => {
          e.preventDefault()
          probar()
        }}
      >
        {tiendaFija === undefined && (
          <CampoFiltro etiqueta="Tienda">
            <Select
              items={activas.map((t) => ({
                value: String(t.storeId),
                label: nombreTienda(t.storeId, tiendas),
              }))}
              value={Number.isNaN(storeId) ? null : String(storeId)}
              onValueChange={(v) => {
                setTienda(v as string)
                olvidar()
              }}
            >
              <SelectTrigger className="w-36" aria-label="Tienda">
                <SelectValue placeholder="Tienda" />
              </SelectTrigger>
              <SelectContent>
                {activas.map((t) => (
                  <SelectItem key={t.storeId} value={String(t.storeId)}>
                    {nombreTienda(t.storeId, tiendas)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </CampoFiltro>
        )}
        <CampoFiltro etiqueta="Tipo">
          <Input
            className="w-28 uppercase"
            list={lista}
            aria-label="Tipo de documento"
            value={tipo}
            onChange={(e) => {
              setTipo(e.target.value)
              olvidar()
            }}
          />
          <datalist id={lista}>
            {tipos.map((t) => (
              <option key={t} value={t}>
                {TIPOS_DOCUMENTO[t]}
              </option>
            ))}
          </datalist>
        </CampoFiltro>
        <CampoFiltro etiqueta="Canal">
          <Input
            className="w-24 uppercase"
            aria-label="Canal"
            value={canal}
            onChange={(e) => {
              setCanal(e.target.value)
              olvidar()
            }}
          />
        </CampoFiltro>
        <Button
          type="submit"
          variant="outline"
          className="ml-auto"
          title="Pregunta a la API a qué impresora iría un trabajo así"
          disabled={simular.isPending || !tipo.trim() || Number.isNaN(storeId)}
        >
          <FlaskConicalIcon data-icon="inline-start" />
          {simular.isPending ? "Probando…" : "Probar"}
        </Button>
      </form>

      {grupos.length === 0 ? (
        <p className="text-sm text-muted-foreground">No hay reglas.</p>
      ) : (
        <div className="min-h-0 flex-1 space-y-3 overflow-auto">
          {/* Nombres de las columnas, con las mismas medidas que las ramas. */}
          {/* Como la cabecera de la tabla de al lado: queda a su altura. */}
          <div
            className={cn(
              COLUMNAS,
              "sticky top-0 z-[1] h-8 items-center rounded-md border bg-muted px-2"
            )}
          >
            {["Tienda", "", "Condición", "", "Impresora"].map((t, i) => (
              <span
                key={i}
                className="text-xs font-semibold tracking-wide text-muted-foreground uppercase"
              >
                {t}
              </span>
            ))}
          </div>
          {resultado && (
            <div
              role="status"
              className={cn(
                "flex items-center gap-2 rounded-md border px-3 py-2 text-sm",
                "ruleId" in resultado
                  ? "border-primary/40 bg-primary/5"
                  : "border-destructive/40 bg-destructive/5 text-destructive"
              )}
            >
              {"ruleId" in resultado ? (
                <CircleCheckIcon className="size-4 shrink-0 text-success" />
              ) : (
                <CircleXIcon className="size-4 shrink-0" />
              )}
              <span className="min-w-0">
                {"ruleId" in resultado ? (
                  ganadora ? (
                    <>
                      Va a{" "}
                      <b className="font-semibold">
                        {ganadora.printer?.printerName ??
                          `Impresora ${ganadora.printerId}`}
                      </b>{" "}
                      por la regla #{numero.get(ganadora.ruleId)} de{" "}
                      {tituloGrupo(ganadora.storeId, tiendas)}
                      {impresoraGanadora &&
                        impresoraGanadora.connectivityStatus !== "ok" && (
                          <span className="text-warning">
                            {" "}
                            · ojo: {impresoraGanadora.connectivityLabel}
                          </span>
                        )}
                    </>
                  ) : (
                    "Gana una regla que no está en este filtro: elige «Todas» para verla."
                  )
                ) : "ninguna" in resultado ? (
                  "Ninguna regla sirve: el trabajo quedaría en Error (sin regla)."
                ) : (
                  resultado.error
                )}
              </span>
            </div>
          )}
          {grupos.map((g) => (
            <Rama
              key={g.storeId ?? "global"}
              titulo={tituloGrupo(g.storeId, tiendas)}
              reglas={g.reglas}
              impresoras={impresoras}
              marcada={marcada}
              onElegir={(id) => onElegir(id === elegida ? undefined : id)}
            />
          ))}
          <p className="flex items-center gap-2 border-t pt-2 text-xs text-muted-foreground">
            <span className="inline-block w-6 border-t border-dashed border-muted-foreground" />
            Borde discontinuo: la regla no se aplica (inactiva, fuera de
            vigencia o con la impresora inactiva).
          </p>
        </div>
      )}
    </TarjetaReglas>
  )
}

// Tienda, tronco, condición (ancho fijo: nunca necesita más), hueco para las
// líneas hasta las impresoras (se lleva lo que sobra) e impresora.
const COLUMNAS =
  "grid grid-cols-[minmax(0,7.5rem)_1rem_minmax(0,11rem)_minmax(2.5rem,1fr)_minmax(0,11rem)]"

type Linea = { ruleId: number; d: string; flecha: string; discontinua: boolean }

// Una tienda: su nodo a la izquierda, del que sale una rama por regla
// (condición). Cada impresora sale una sola vez aunque varias reglas vayan a
// ella (decisión del usuario, 27/10/2026): de cada regla parte una línea hasta
// su impresora. Las reglas siguen en el orden en que se prueban, así que las
// líneas pueden cruzarse; se dibujan en SVG midiendo dónde quedó cada caja.
function Rama({
  titulo,
  reglas,
  impresoras,
  marcada,
  onElegir,
}: {
  titulo: string
  reglas: ReglaEnrutado[]
  impresoras: readonly Impresora[]
  marcada?: number
  onElegir: (ruleId: number) => void
}) {
  const n = reglas.length
  const hayMarcada = reglas.some((r) => r.ruleId === marcada)
  const reglaMarcada = reglas.find((r) => r.ruleId === marcada)
  // Las impresoras del grupo, una vez cada una, en el orden de su primera regla.
  const destinos = [...new Set(reglas.map((r) => r.printerId))]
  // Tramo del tronco que lleva de la tienda (centrada en el grupo, a la altura
  // n/2 en filas) a la regla marcada (centro de su fila): se pinta en color.
  const iMarcada = reglas.findIndex((r) => r.ruleId === marcada)
  const [lo, hi] =
    iMarcada < 0
      ? [0, 0]
      : [Math.min(n / 2, iMarcada + 0.5), Math.max(n / 2, iMarcada + 0.5)]
  const enCamino = (desde: number, hasta: number) =>
    iMarcada >= 0 && desde >= lo && hasta <= hi

  const caja = useRef<HTMLDivElement>(null)
  const cajasRegla = useRef(new Map<number, HTMLElement>())
  const cajasImpresora = useRef(new Map<number, HTMLElement>())
  const [lineas, setLineas] = useState<Linea[]>([])
  const motivos = new Map(
    reglas.map((r) => [r.ruleId, porQueNo(r, impresoras)])
  )

  // Las líneas se calculan con las cajas ya pintadas y se rehacen si cambia el
  // tamaño (panel partido, ventana, textos).
  const medir = useEffectEvent(() => {
    const c = caja.current?.getBoundingClientRect()
    if (!c) return
    setLineas(
      reglas.flatMap((r) => {
        const a = cajasRegla.current.get(r.ruleId)?.getBoundingClientRect()
        const b = cajasImpresora.current
          .get(r.printerId)
          ?.getBoundingClientRect()
        if (!a || !b) return []
        const x1 = a.right - c.left
        const y1 = a.top + a.height / 2 - c.top
        const x2 = b.left - c.left - 1
        const y2 = b.top + b.height / 2 - c.top
        const xm = (x1 + x2) / 2
        return [
          {
            ruleId: r.ruleId,
            d: `M ${x1} ${y1} C ${xm} ${y1}, ${xm} ${y2}, ${x2} ${y2}`,
            flecha: `M ${x2 - 5} ${y2 - 4} L ${x2} ${y2} L ${x2 - 5} ${y2 + 4}`,
            discontinua: !!motivos.get(r.ruleId),
          },
        ]
      })
    )
  })
  useLayoutEffect(() => {
    medir()
    const ro = new ResizeObserver(() => medir())
    if (caja.current) ro.observe(caja.current)
    return () => ro.disconnect()
  }, [reglas, impresoras])

  return (
    <div ref={caja} className={cn(COLUMNAS, "relative px-2")}>
      <div
        style={{ gridRow: `1 / span ${n}` }}
        className={cn(
          "col-start-1 self-center rounded-md border bg-muted px-2.5 py-2 text-sm leading-tight font-medium break-words",
          hayMarcada && "border-primary"
        )}
        title={titulo}
      >
        {titulo}
      </div>
      {reglas.map((r, i) => {
        const fila: CSSProperties = { gridRow: i + 1 }
        const motivo = motivos.get(r.ruleId)
        const esta = r.ruleId === marcada
        return (
          <Fragment key={r.ruleId}>
            {/* Unión: el tronco en dos mitades (encima y debajo del centro
                de la fila, para pintar solo el tramo del camino) y el ramal
                horizontal hasta la regla. */}
            <div style={fila} aria-hidden className="relative col-start-2">
              {i > 0 && (
                <span
                  className={cn(
                    "absolute top-0 bottom-1/2 left-0 border-l",
                    enCamino(i, i + 0.5) ? "border-primary" : "border-border"
                  )}
                />
              )}
              {i < n - 1 && (
                <span
                  className={cn(
                    "absolute top-1/2 bottom-0 left-0 border-l",
                    enCamino(i + 0.5, i + 1)
                      ? "border-primary"
                      : "border-border"
                  )}
                />
              )}
              <span
                className={cn(
                  "absolute inset-x-0 top-1/2 border-t",
                  esta ? "border-primary" : "border-border"
                )}
              />
            </div>
            <button
              ref={(el) => {
                if (el) cajasRegla.current.set(r.ruleId, el)
                else cajasRegla.current.delete(r.ruleId)
              }}
              type="button"
              style={fila}
              onClick={() => onElegir(r.ruleId)}
              title={`${condicion(r)} · ${NIVELES_REGLA[nivelRegla(r)]} · prioridad ${r.priority}${motivo ? ` · ${motivo}` : ""}`}
              className={cn(
                "relative z-[1] col-start-3 my-1 flex min-w-0 flex-col rounded-md border bg-card px-2.5 py-1.5 text-left transition-colors hover:bg-accent",
                motivo && "border-dashed text-muted-foreground",
                esta && "border-primary bg-primary/5 ring-1 ring-primary/30",
                marcada !== undefined && !esta && "opacity-50"
              )}
            >
              <span className="flex min-w-0 items-center gap-1.5 text-sm">
                <span className="font-mono text-xs text-muted-foreground">
                  #{i + 1}
                </span>
                <span className="truncate font-medium">
                  {r.documentType
                    ? (TIPOS_DOCUMENTO[r.documentType] ?? r.documentType)
                    : "Cualquier tipo"}
                </span>
              </span>
              <span className="truncate text-xs text-muted-foreground">
                {r.channel ?? "cualquier canal"} · prio. {r.priority}
                {motivo && ` · ${motivo}`}
              </span>
            </button>
          </Fragment>
        )
      })}
      {/* Las impresoras, una vez cada una, repartidas a lo alto del grupo. */}
      <div
        style={{ gridRow: `1 / span ${n}` }}
        className="relative z-[1] col-start-5 flex flex-col justify-around gap-2 py-1"
      >
        {destinos.map((id) => {
          const impresora = impresoras.find((x) => x.printerId === id)
          const nombre =
            reglas.find((r) => r.printerId === id)?.printer?.printerName ??
            `Impresora ${id}`
          const esta = reglaMarcada?.printerId === id
          return (
            <div
              key={id}
              ref={(el) => {
                if (el) cajasImpresora.current.set(id, el)
                else cajasImpresora.current.delete(id)
              }}
              className={cn(
                "flex min-w-0 items-center gap-1.5 rounded-md border bg-card px-2.5 py-1.5 text-sm",
                esta && "border-primary",
                marcada !== undefined && !esta && "opacity-50"
              )}
              title={
                impresora
                  ? `${impresora.printerName} · ${impresora.connectivityLabel}`
                  : nombre
              }
            >
              <span
                aria-hidden
                className={cn(
                  "size-2 shrink-0 rounded-full",
                  COLOR_PUNTO[
                    impresora
                      ? TONO_SEVERIDAD[impresora.connectivitySeverity]
                      : "neutro"
                  ]
                )}
              />
              <span
                className={cn(
                  "truncate",
                  impresora?.isActive === false && "line-through"
                )}
              >
                {nombre}
              </span>
            </div>
          )
        })}
      </div>
      <svg
        aria-hidden
        className="pointer-events-none absolute inset-0 size-full overflow-visible"
      >
        {/* La marcada, la última: queda encima de las que se cruzan. */}
        {[...lineas]
          .sort(
            (a, b) =>
              Number(a.ruleId === marcada) - Number(b.ruleId === marcada)
          )
          .map((l) => {
            const esta = l.ruleId === marcada
            const color = esta
              ? "var(--primary)"
              : "color-mix(in oklab, var(--muted-foreground) 55%, transparent)"
            return (
              <g
                key={l.ruleId}
                fill="none"
                stroke={color}
                strokeWidth={esta ? 1.5 : 1}
                opacity={marcada !== undefined && !esta ? 0.5 : 1}
              >
                <path
                  d={l.d}
                  strokeDasharray={l.discontinua ? "4 3" : undefined}
                />
                <path d={l.flecha} />
              </g>
            )
          })}
      </svg>
    </div>
  )
}
