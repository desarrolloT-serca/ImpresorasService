import { useState } from "react"
import {
  CopyIcon,
  InboxIcon,
  ListOrderedIcon,
  ServerOffIcon,
  TriangleAlertIcon,
  UnplugIcon,
  type LucideIcon,
} from "lucide-react"
import { toast } from "sonner"
import type {
  HorarioTienda,
  ReglasUmbral,
  SeveridadUmbral,
} from "@serca/contratos"

import { Panel } from "@/components/ficha"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { Spinner } from "@/components/ui/spinner"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { ErrorApi } from "@/core/api"
import { cn } from "@/lib/utils"
import { useGuardarUmbrales, useUmbrales } from "./api"
import { CLASE_TONO, COLOR_PUNTO, SALUD } from "./formato"

type Metrica = Exclude<keyof ReglasUmbral, "horario">
// Como ajustes.blade.php del PHP, más dos opcionales (27/10/2026): salen
// desactivadas y, al activar un nivel vacío, se rellena con su sugerencia.
const METRICAS: {
  clave: Metrica
  titulo: string
  ayuda: string
  unidad: string
  Icono: LucideIcon
  opcional?: boolean
  sugerida?: Partial<Record<SeveridadUmbral, number>>
}[] = [
  {
    clave: "queue",
    titulo: "Cola de impresión",
    ayuda: "Trabajos en cola ahora mismo",
    unidad: "trabajos",
    Icono: ListOrderedIcon,
  },
  {
    clave: "failed",
    titulo: "Fallos sin reenviar",
    ayuda: "Errores finales sin reintentar",
    unidad: "fallos",
    Icono: TriangleAlertIcon,
  },
  {
    clave: "missingHost",
    titulo: "Impresoras sin host",
    ayuda: "Impresoras sin host ni ruta UNC",
    unidad: "impresoras",
    Icono: ServerOffIcon,
  },
  {
    clave: "conn",
    titulo: "Fallos de conexión",
    ayuda: "Fallos seguidos al comprobar una impresora",
    unidad: "seguidos",
    Icono: UnplugIcon,
  },
  {
    clave: "silent",
    titulo: "Tienda sin recibir",
    ayuda:
      "Minutos sin que llegue ningún trabajo, dentro del horario de tienda: el ERP puede haber dejado de mandar",
    unidad: "min",
    Icono: InboxIcon,
    opcional: true,
    sugerida: { info: 30, warning: 60, critical: 120 },
  },
  {
    clave: "reprints",
    titulo: "Reimpresiones del día",
    ayuda: "Reimpresiones de hoy en la tienda: posibles duplicados",
    unidad: "reimpr.",
    Icono: CopyIcon,
    opcional: true,
    sugerida: { info: 3, warning: 5, critical: 15 },
  },
]
// De menos a más grave: cada nivel activo empieza en una cifra mayor que el
// anterior (la API lo exige).
const NIVELES: SeveridadUmbral[] = ["info", "warning", "critical"]
const DIAS = ["L", "M", "X", "J", "V", "S", "D"]
const NOMBRE_DIA = [
  "lunes",
  "martes",
  "miércoles",
  "jueves",
  "viernes",
  "sábado",
  "domingo",
]

// Cada métrica tiene sus tres niveles fijos, activos o no, en vez de filas que
// se añaden y se quitan (decisión del usuario, 27/10/2026: así no hay dos
// «Aviso» ni órdenes imposibles). Los valores se editan como texto.
type Nivel = { activo: boolean; min: string }
type Edicion = Record<Metrica, Record<SeveridadUmbral, Nivel>>

const aEdicion = (r: ReglasUmbral): Edicion =>
  Object.fromEntries(
    METRICAS.map(({ clave }) => [
      clave,
      Object.fromEntries(
        NIVELES.map((s) => {
          const u = r[clave].find((x) => x.severity === s)
          return [s, { activo: !!u, min: u ? String(u.min) : "" }]
        })
      ),
    ])
  ) as Edicion

const aReglas = (e: Edicion, horario: HorarioTienda): ReglasUmbral => ({
  ...(Object.fromEntries(
    METRICAS.map(({ clave }) => [
      clave,
      NIVELES.filter((s) => e[clave][s].activo).map((s) => ({
        min: Number(e[clave][s].min),
        severity: s,
      })),
    ])
  ) as Omit<ReglasUmbral, "horario">),
  horario,
})

const entero = (v: string) => /^\d{1,9}$/.test(v.trim())
const HORA = /^([01]\d|2[0-3]):[0-5]\d$/

// Qué falla en cada nivel (lo mismo que comprueba la API), o nada.
function errores(niveles: Record<SeveridadUmbral, Nivel>, opcional?: boolean) {
  const malos: Partial<Record<SeveridadUmbral, string>> = {}
  let anterior: { s: SeveridadUmbral; min: number } | undefined
  for (const s of NIVELES) {
    const n = niveles[s]
    if (!n.activo) continue
    if (!entero(n.min)) {
      malos[s] = "Pon un número entero"
      continue
    }
    const min = Number(n.min)
    if (anterior && min <= anterior.min)
      malos[s] = `Mayor que ${SALUD[anterior.s].etiqueta} (${anterior.min})`
    anterior = { s, min }
  }
  const general =
    opcional || NIVELES.some((s) => niveles[s].activo)
      ? undefined
      : "Activa al menos un nivel"
  return { malos, general }
}

const errorHorario = (h: HorarioTienda) =>
  !h.dias.length
    ? "Elige al menos un día"
    : !HORA.test(h.desde) || !HORA.test(h.hasta) || h.desde >= h.hasta
      ? "La hora de cierre tiene que ser posterior a la de apertura"
      : undefined

// Umbrales de salud (ajustes.blade.php del PHP): de qué cifra en adelante una
// tienda pasa a info, aviso o crítica. Los usan el dashboard y las alertas de
// Telegram. La API comprueba que crezcan con la severidad.
export default function Umbrales() {
  const umbrales = useUmbrales()
  if (!umbrales.data)
    return (
      <Panel className="items-center justify-center">
        <Spinner />
      </Panel>
    )
  return <Editor inicial={umbrales.data} />
}

function Editor({ inicial }: { inicial: ReglasUmbral }) {
  const guardar = useGuardarUmbrales()
  const [edicion, setEdicion] = useState(() => aEdicion(inicial))
  const [horario, setHorario] = useState(inicial.horario)
  const [mensaje, setMensaje] = useState("")
  const cambiado =
    JSON.stringify(aReglas(edicion, horario)) !==
    JSON.stringify(aReglas(aEdicion(inicial), inicial.horario))
  const fallos = Object.fromEntries(
    METRICAS.map(({ clave, opcional }) => [
      clave,
      errores(edicion[clave], opcional),
    ])
  ) as Record<Metrica, ReturnType<typeof errores>>
  const fallaHorario = errorHorario(horario)
  const hayFallos =
    !!fallaHorario ||
    METRICAS.some(
      ({ clave }) =>
        fallos[clave].general || Object.keys(fallos[clave].malos).length
    )
  const cambiar = (m: Metrica, s: SeveridadUmbral, n: Partial<Nivel>) => {
    setEdicion((e) => ({
      ...e,
      [m]: { ...e[m], [s]: { ...e[m][s], ...n } },
    }))
    setMensaje("")
  }

  function enviar() {
    guardar.mutate(aReglas(edicion, horario), {
      onSuccess: (r) => {
        setEdicion(aEdicion(r))
        setHorario(r.horario)
        toast.success("Umbrales guardados")
      },
      onError: (e) =>
        setMensaje(
          e instanceof ErrorApi ? e.problema.title : "No se pudieron guardar"
        ),
    })
  }

  return (
    <Panel className="@container">
      {/* Cabecera con las acciones a la vista, sin bajar al final. */}
      <div className="flex flex-wrap items-center gap-3 border-b pb-3">
        <div className="min-w-0">
          <h2 className="font-semibold">Umbrales de salud</h2>
          <p className="text-xs text-muted-foreground">
            Desde qué cifra una tienda pasa a info, aviso o crítica en el
            dashboard y en las alertas de Telegram.
          </p>
        </div>
        <p
          role="status"
          aria-live="polite"
          className={cn(
            "ml-auto text-sm",
            mensaje || hayFallos ? "text-destructive" : "text-warning"
          )}
        >
          {mensaje ||
            (hayFallos
              ? "Corrige lo marcado para guardar"
              : cambiado
                ? "Cambios sin guardar"
                : "")}
        </p>
        <Button
          variant="outline"
          disabled={!cambiado || guardar.isPending}
          onClick={() => {
            setEdicion(aEdicion(inicial))
            setHorario(inicial.horario)
            setMensaje("")
          }}
        >
          Deshacer
        </Button>
        <Button
          disabled={!cambiado || hayFallos || guardar.isPending}
          onClick={enviar}
        >
          {guardar.isPending ? "Guardando…" : "Guardar umbrales"}
        </Button>
      </div>

      <div className="grid gap-4 @4xl:grid-cols-2">
        {METRICAS.map((m) => {
          const niveles = edicion[m.clave]
          const { malos, general } = fallos[m.clave]
          const apagada = !NIVELES.some((s) => niveles[s].activo)
          return (
            <section
              key={m.clave}
              className="flex flex-col gap-4 rounded-lg border bg-card p-4"
            >
              <div className="flex items-center gap-3">
                <span className="flex size-9 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
                  <m.Icono className="size-4" />
                </span>
                <div className="min-w-0">
                  <h3 className="font-semibold">{m.titulo}</h3>
                  <p className="text-xs text-muted-foreground">{m.ayuda}</p>
                </div>
                {m.opcional && apagada && (
                  <Badge
                    variant="outline"
                    className={cn("ml-auto shrink-0", CLASE_TONO.neutro)}
                  >
                    Desactivado
                  </Badge>
                )}
              </div>

              {apagada ? (
                <p className="rounded-md border border-dashed px-3 py-2 text-xs text-muted-foreground">
                  No avisa. Activa un nivel para usarlo.
                </p>
              ) : (
                <Escala niveles={niveles} unidad={m.unidad} />
              )}

              <div className="divide-y rounded-md border">
                {NIVELES.map((s) => {
                  const n = niveles[s]
                  const id = `umbral-${m.clave}-${s}`
                  return (
                    <div
                      key={s}
                      className={cn(
                        "grid grid-cols-[7rem_auto_5rem_minmax(0,1fr)] items-center gap-2 px-3 py-2",
                        !n.activo && "bg-muted/40"
                      )}
                    >
                      <label className="flex items-center gap-2 text-sm font-medium">
                        <Checkbox
                          checked={n.activo}
                          disabled={guardar.isPending}
                          onCheckedChange={(v) =>
                            cambiar(m.clave, s, {
                              activo: !!v,
                              ...(v &&
                                !n.min &&
                                m.sugerida?.[s] !== undefined && {
                                  min: String(m.sugerida[s]),
                                }),
                            })
                          }
                        />
                        <span
                          aria-hidden
                          className={cn(
                            "size-2 rounded-full",
                            COLOR_PUNTO[SALUD[s].tono]
                          )}
                        />
                        {SALUD[s].etiqueta}
                      </label>
                      <label
                        htmlFor={id}
                        className={cn(
                          "text-sm",
                          n.activo
                            ? "text-muted-foreground"
                            : "text-muted-foreground/50"
                        )}
                      >
                        desde
                      </label>
                      <Input
                        id={id}
                        inputMode="numeric"
                        className="tabular-nums"
                        value={n.min}
                        disabled={!n.activo || guardar.isPending}
                        aria-invalid={!!malos[s]}
                        onChange={(e) =>
                          cambiar(m.clave, s, { min: e.target.value })
                        }
                      />
                      <span className="flex min-w-0 items-baseline gap-2 text-sm">
                        <span
                          className={cn(
                            "text-muted-foreground",
                            !n.activo && "opacity-50"
                          )}
                        >
                          {m.unidad}
                        </span>
                        {malos[s] && (
                          <span className="truncate text-xs text-destructive">
                            {malos[s]}
                          </span>
                        )}
                        {!n.activo && (
                          <span className="truncate text-xs text-muted-foreground">
                            desactivado
                          </span>
                        )}
                      </span>
                    </div>
                  )
                })}
              </div>
              {general && <p className="text-xs text-destructive">{general}</p>}

              {m.clave === "silent" && (
                <Horario
                  horario={horario}
                  error={fallaHorario}
                  deshabilitado={guardar.isPending}
                  onCambio={(h) => {
                    setHorario(h)
                    setMensaje("")
                  }}
                />
              )}
            </section>
          )
        })}
      </div>
    </Panel>
  )
}

// Horario de tienda común: fuera de él «sin recibir» no cuenta (si no,
// saltaría cada noche). Hora de negocio (Madrid).
function Horario({
  horario,
  error,
  deshabilitado,
  onCambio,
}: {
  horario: HorarioTienda
  error?: string
  deshabilitado: boolean
  onCambio: (h: HorarioTienda) => void
}) {
  return (
    <div className="space-y-2 rounded-md border bg-muted/30 p-3">
      <div className="flex flex-wrap items-center gap-3">
        <span className="text-sm font-medium">Horario de tienda</span>
        <ToggleGroup
          multiple
          variant="outline"
          size="sm"
          aria-label="Días de apertura"
          disabled={deshabilitado}
          value={horario.dias.map(String)}
          onValueChange={(v) =>
            onCambio({
              ...horario,
              dias: (v as string[]).map(Number).sort((a, b) => a - b),
            })
          }
        >
          {DIAS.map((d, i) => (
            <ToggleGroupItem
              key={d}
              value={String(i + 1)}
              aria-label={NOMBRE_DIA[i]}
              title={NOMBRE_DIA[i]}
              className="w-8 aria-pressed:bg-primary aria-pressed:text-primary-foreground"
            >
              {d}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
        <span className="flex items-center gap-2 text-sm text-muted-foreground">
          de
          <Input
            type="time"
            aria-label="Hora de apertura"
            className="w-28 tabular-nums"
            value={horario.desde}
            disabled={deshabilitado}
            onChange={(e) => onCambio({ ...horario, desde: e.target.value })}
          />
          a
          <Input
            type="time"
            aria-label="Hora de cierre"
            className="w-28 tabular-nums"
            value={horario.hasta}
            disabled={deshabilitado}
            onChange={(e) => onCambio({ ...horario, hasta: e.target.value })}
          />
        </span>
      </div>
      <p
        className={cn(
          "text-xs",
          error ? "text-destructive" : "text-muted-foreground"
        )}
      >
        {error ??
          "Fuera de este horario no avisa. Si lo último llegó antes de abrir, cuenta desde la apertura."}
      </p>
    </div>
  )
}

// Cómo queda la escala con lo que hay escrito: de 0 hasta el primer nivel
// activo, normal; después, cada tramo con el color de su gravedad. Se
// actualiza al teclear; los niveles sin número válido no cuentan.
function Escala({
  niveles,
  unidad,
}: {
  niveles: Record<SeveridadUmbral, Nivel>
  unidad: string
}) {
  const validos = NIVELES.filter(
    (s) => niveles[s].activo && entero(niveles[s].min)
  )
    .map((s) => ({ min: Number(niveles[s].min), s }))
    .sort((a, b) => a.min - b.min)
  const tramos = [
    ...(validos[0]?.min !== 0
      ? [
          {
            desde: 0,
            hasta: validos[0]?.min,
            etiqueta: "Normal",
            tono: "ok" as const,
          },
        ]
      : []),
    ...validos.map((v, i) => ({
      desde: v.min,
      hasta: validos[i + 1]?.min,
      etiqueta: SALUD[v.s].etiqueta,
      tono: SALUD[v.s].tono,
    })),
  ]
  return (
    <div
      role="img"
      aria-label={`Escala: ${tramos.map((t) => `${t.etiqueta} ${rango(t.desde, t.hasta)}`).join(", ")} ${unidad}`}
      className="flex overflow-hidden rounded-md border text-xs"
    >
      {tramos.map((t, i) => (
        <div
          key={i}
          className={cn(
            "flex min-w-0 flex-1 flex-col px-2.5 py-1.5",
            i > 0 && "border-l",
            CLASE_TONO[t.tono]
          )}
        >
          <span className="truncate font-semibold">{t.etiqueta}</span>
          <span className="truncate tabular-nums">
            {rango(t.desde, t.hasta)} {unidad}
          </span>
        </div>
      ))}
    </div>
  )
}

const rango = (desde: number, hasta?: number) =>
  hasta === undefined
    ? `${desde} o más`
    : hasta - 1 <= desde
      ? `${desde}`
      : `${desde} a ${hasta - 1}`
