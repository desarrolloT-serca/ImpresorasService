import { useMemo, useState } from "react"
import type { ColumnDef } from "@tanstack/react-table"
import { CopyIcon, RotateCcwIcon } from "lucide-react"
import { toast } from "sonner"
import {
  PERMISOS_IMPRESION as P,
  VistaCola,
  type ConsultaCola,
  type Tienda,
  type TrabajoCola,
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
import { ErrorApi } from "@/core/api"
import { useBusquedaAvanzadaPanel } from "@/core/avanzados"
import { usePermiso } from "@/core/sesion"
import { cn } from "@/lib/utils"
import { useEsc, type PropsPanel } from "@/workspace/modulo"
import {
  useAccionTrabajo,
  useCola,
  useImpresoras,
  useTiendas,
  type AccionTrabajo,
} from "./api"
import {
  CLASE_TONO,
  ESTADOS,
  documentoErp,
  horaTrabajo,
  nombreTienda,
  puedeCancelar,
  puedeReimprimir,
  puedeReintentar,
  TIPOS_DOCUMENTO,
  plural,
} from "./formato"
import { SinActualizar } from "./formulario"
import { DialogoReimprimir } from "./Reimprimir"

const INICIAL: ConsultaCola = { pagina: 1, tamano: 50 }
const SIN_TIENDAS: Tienda[] = []

// Abierta desde el dashboard, la clave trae los filtros: «tienda=12&vista=sinReenviar».
const desdeClave = (clave: string): ConsultaCola => {
  const p = new URLSearchParams(clave)
  const n = (k: string) =>
    /^\d{1,9}$/.test(p.get(k) ?? "") ? Number(p.get(k)) : undefined
  const vista = VistaCola.safeParse(p.get("vista"))
  return {
    ...INICIAL,
    tienda: n("tienda"),
    estado: n("estado"),
    vista: vista.data,
  }
}

// Los mismos que el PHP: sin Aceptado ni Impreso, que no piden nada.
const ESTADO_ITEMS = [
  { value: "todos", label: "Todos" },
  { value: "sinReenviar", label: "Sin reenviar" },
  { value: "sinImpresora", label: "Sin impresora" },
  ...[0, 1, 2, 5, 6, 7, 8, 9].map((s) => ({
    value: String(s),
    label: ESTADOS[s].etiqueta,
  })),
]
const VISTAS: readonly string[] = ["sinReenviar", "sinImpresora"]
const TIPO_OPCIONES = Object.entries(TIPOS_DOCUMENTO).map(([cod, nombre]) => ({
  cod,
  nombre,
}))
// Al cancelar, lo que deja la impresora sin nada pendiente; lo demás pide
// mirarla (si no, el documento sale al recuperarse y duplica en silencio).
const IMPRESORA_EN_ORDEN = new Set(["Cancelado", "YaNoSaldra", "NoLoTiene"])

// Cola de impresión (cola.blade.php del PHP). La API ordena por la última vez
// que se mandó imprimir (llegada, reintento o reimpresión), lo más reciente
// primero, y filtra la tienda de quien no las ve todas.
export default function Cola({ clave }: PropsPanel) {
  const verTodas = usePermiso(P.todasLasTiendas)
  const gestionar = usePermiso(P.reimprimir)
  const [consulta, setConsulta] = useState(() => desdeClave(clave))
  const [reinicio, setReinicio] = useState(0)
  const cola = useCola(consulta)
  const tiendas = useTiendas().data ?? SIN_TIENDAS
  const impresoras = useImpresoras().data
  const accion = useAccionTrabajo()
  // Reimprimir lo que ya salió abre su ventana de aviso (motivo y riesgo).
  const [aReimprimir, setAReimprimir] = useState<TrabajoCola>()
  // Cualquier filtro vuelve a la primera página.
  const filtrar = (f: Partial<ConsultaCola>) =>
    setConsulta((c) => ({ ...c, pagina: 1, ...f }))
  // Esc: quita lo buscado y lo elegido; los filtros se quedan (regla del ERP).
  useEsc(() => {
    filtrar({ q: undefined })
    setReinicio((n) => n + 1)
  })

  async function ejecutar(
    trabajos: TrabajoCola[],
    tipo: AccionTrabajo,
    limpiar?: () => void
  ) {
    const resultados = await Promise.allSettled(
      trabajos.map((t) => accion.mutateAsync({ id: t.jobId, accion: tipo }))
    )
    limpiar?.()
    const fallos = resultados.flatMap((r) =>
      r.status === "rejected"
        ? [r.reason instanceof ErrorApi ? r.reason.problema.title : "Error"]
        : r.value.status === "ErrorFinal"
          ? [
              `Sin impresora para el trabajo (${r.value.errorCode ?? "sin regla"})`,
            ]
          : []
    )
    const hechos = trabajos.length - fallos.length
    const verbo = tipo === "reintentar" ? "reenviado" : "cancelado"
    if (hechos)
      toast.success(
        hechos === 1 ? `Trabajo ${verbo}` : `${hechos} trabajos ${verbo}s`
      )
    if (fallos.length)
      toast.error(
        trabajos.length === 1
          ? fallos[0]
          : `${fallos.length} no se pudieron: ${[...new Set(fallos)].join("; ")}`
      )
    // Qué pasó en la cola de la impresora al cancelar (decisión del usuario,
    // 27/10/2026). Si hay que mirarla, el aviso se queda hasta cerrarlo.
    resultados.forEach((r, i) => {
      if (r.status !== "fulfilled" || !r.value.printerMessage) return
      const texto =
        trabajos.length === 1
          ? r.value.printerMessage
          : `${trabajos[i].externalJobId}: ${r.value.printerMessage}`
      if (IMPRESORA_EN_ORDEN.has(r.value.printerCancel ?? "")) toast.info(texto)
      else toast.warning(texto, { duration: Infinity, closeButton: true })
    })
  }

  const columnas = useMemo(
    () =>
      columnasCola(
        tiendas,
        gestionar,
        accion.isPending,
        ejecutar,
        setAReimprimir
      ),
    // ejecutar solo usa accion.mutateAsync, estable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [tiendas, gestionar, accion.isPending]
  )
  const tiendaItems = [
    { value: "todas", label: "Todas" },
    ...tiendas.map((t) => ({
      value: String(t.storeId),
      label: nombreTienda(t.storeId, tiendas),
    })),
  ]
  // Impresoras, tipos y fechas van en la búsqueda avanzada (botón de arriba a
  // la derecha): se usan poco (decisión del usuario, 27/10/2026). Las
  // impresoras, las de la tienda elegida; con todas, cada una con su tienda.
  const avanzados = useMemo(
    () => ({
      impresoras: consulta.impresoras,
      tipos: consulta.tipos,
      desde: consulta.desde,
      hasta: consulta.hasta,
    }),
    [consulta.impresoras, consulta.tipos, consulta.desde, consulta.hasta]
  )
  const opcionesAvanzadas = useMemo(
    () => ({
      impresoras: (impresoras ?? [])
        .filter(
          (i) => consulta.tienda === undefined || i.storeId === consulta.tienda
        )
        .map((i) => ({
          cod: String(i.printerId),
          nombre:
            verTodas && consulta.tienda === undefined
              ? `${i.printerName} · ${nombreTienda(i.storeId, tiendas)}`
              : i.printerName,
        })),
      tipos: TIPO_OPCIONES,
    }),
    [impresoras, consulta.tienda, verTodas, tiendas]
  )
  useBusquedaAvanzadaPanel(
    "cola",
    avanzados,
    (c) =>
      filtrar({
        impresoras: c.impresoras,
        tipos: c.tipos,
        desde: c.desde,
        hasta: c.hasta,
      }),
    undefined,
    opcionesAvanzadas
  )

  return (
    <Panel className="[&_table]:min-w-[62rem] [&_table]:table-fixed">
      <SinActualizar consultas={[cola]} />
      {aReimprimir && (
        <DialogoReimprimir
          trabajo={aReimprimir}
          tiendas={tiendas}
          onCerrar={() => setAReimprimir(undefined)}
        />
      )}
      <DataTable
        key={reinicio}
        columns={columnas}
        data={cola.data?.value ?? []}
        getRowId={(t) => t.jobId}
        buscar="Documento"
        buscarEtiqueta="Buscar trabajo"
        emptyContent={
          cola.isPending ? undefined : "No hay trabajos con estos filtros."
        }
        toolbar={
          <>
            {verTodas && (
              <CampoFiltro etiqueta="Tienda">
                <Select
                  items={tiendaItems}
                  value={
                    consulta.tienda === undefined
                      ? "todas"
                      : String(consulta.tienda)
                  }
                  // Las impresoras elegidas son de la tienda anterior.
                  onValueChange={(v) =>
                    filtrar({
                      tienda: v === "todas" ? undefined : Number(v),
                      impresoras: undefined,
                    })
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
            <CampoFiltro etiqueta="Estado">
              <Select
                items={ESTADO_ITEMS}
                value={
                  consulta.vista ??
                  (consulta.estado === undefined
                    ? "todos"
                    : String(consulta.estado))
                }
                onValueChange={(v) =>
                  filtrar(
                    v === "todos"
                      ? { estado: undefined, vista: undefined }
                      : VISTAS.includes(v as string)
                        ? { estado: undefined, vista: v as VistaCola }
                        : { estado: Number(v), vista: undefined }
                  )
                }
              >
                <SelectTrigger className="w-48">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {ESTADO_ITEMS.map((i) => (
                    <SelectItem key={i.value} value={i.value}>
                      {i.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </CampoFiltro>
          </>
        }
        seleccion={
          gestionar
            ? (filas, limpiar) => (
                <AccionesMasivas
                  filas={filas}
                  ocupado={accion.isPending}
                  onAccion={(tipo, aptos) => ejecutar(aptos, tipo, limpiar)}
                  onReimprimir={(t) => {
                    limpiar()
                    setAReimprimir(t)
                  }}
                />
              )
            : undefined
        }
        servidor={{
          // La API solo ordena por fecha: ninguna columna se puede ordenar.
          campos: [],
          total: cola.data?.count,
          pagina: consulta.pagina,
          tamano: consulta.tamano,
          q: consulta.q,
          cargando: cola.isFetching,
          onCambio: (c) =>
            setConsulta((x) => ({
              ...x,
              ...("q" in c ? { ...c, q: c.q || undefined } : c),
            })),
        }}
      />
    </Panel>
  )
}

// El error para la gente (ErroresTrabajo, en la API), no solo en «Error»:
// también bloqueado, sin confirmar o reintentando. Al pasar el ratón, el código,
// el detalle y qué hacer. Con una API anterior al 26/10/2026, el código.
const columnaError: ColumnDef<TrabajoCola, unknown> = {
  id: "error",
  header: "Error",
  size: 220,
  cell: ({ row: { original: t } }) =>
    t.lastErrorCode ? (
      <span
        className={cn(
          "block truncate",
          t.status === 8 ? "text-destructive" : "text-muted-foreground"
        )}
        title={[t.lastErrorCode, t.lastErrorMessage, t.lastErrorHint]
          .filter(Boolean)
          .join("\n")}
      >
        {t.lastErrorText ?? t.lastErrorCode}
      </span>
    ) : null,
}

function columnasCola(
  tiendas: readonly Tienda[],
  gestionar: boolean,
  ocupado: boolean,
  ejecutar: (t: TrabajoCola[], tipo: AccionTrabajo) => void,
  reimprimir: (t: TrabajoCola) => void
): ColumnDef<TrabajoCola, unknown>[] {
  const columnas: ColumnDef<TrabajoCola, unknown>[] = [
    {
      id: "trabajo",
      header: "Documento",
      size: 0,
      cell: ({ row: { original: t } }) => {
        const d = documentoErp(t.documentType, t.externalJobId)
        return (
          <span
            className="block truncate font-mono"
            title={d ? `${d.nombre} ${d.numero}` : t.externalJobId}
          >
            {t.externalJobId}
          </span>
        )
      },
    },
    {
      id: "tienda",
      header: "Tienda",
      size: 170,
      cell: ({ row: { original: t } }) => (
        <span className="block truncate">
          {nombreTienda(t.storeId, tiendas)}
        </span>
      ),
    },
    {
      id: "tipo",
      header: "Tipo",
      size: 120,
      cell: ({ row: { original: t } }) => (
        <span className="block truncate" title={t.documentType}>
          {TIPOS_DOCUMENTO[t.documentType] ?? t.documentType}
        </span>
      ),
    },
    {
      id: "estado",
      header: "Estado",
      size: 170,
      cell: ({ row: { original: t } }) => {
        const e = ESTADOS[t.status]
        return (
          <Badge
            variant="outline"
            className={cn("border-transparent", CLASE_TONO[e.tono])}
            title={
              t.status >= 8 ? (t.lastErrorMessage ?? undefined) : undefined
            }
          >
            {e.etiqueta}
          </Badge>
        )
      },
    },
    {
      id: "impresora",
      header: "Impresora",
      size: 160,
      cell: ({ row: { original: t } }) =>
        t.printerName || t.printerId ? (
          <span className="block truncate">
            {t.printerName ?? `Impresora ${t.printerId}`}
          </span>
        ) : (
          <span className="text-muted-foreground">Sin asignar</span>
        ),
    },
    {
      id: "intentos",
      header: "Intentos",
      size: 96,
      meta: { num: true },
      cell: ({ row: { original: t } }) => (
        <span className="block text-right tabular-nums">{t.attemptCount}</span>
      ),
    },
    {
      id: "creado",
      header: "Hora",
      size: 140,
      // Un renglón: la hora (de llegada o de la última salida) y, si se
      // reintentó o reimprimió, su icono. El detalle, en el title.
      cell: ({ row: { original: t } }) => {
        const { marca, hora, detalle } = horaTrabajo(t)
        const Icono = marca === "reimpresion" ? CopyIcon : RotateCcwIcon
        return (
          <span title={detalle} className="inline-flex items-center gap-1">
            {hora}
            {marca && (
              <>
                <Icono
                  aria-hidden
                  className={cn(
                    "size-3.5 shrink-0",
                    marca === "reimpresion"
                      ? "text-warning"
                      : "text-muted-foreground"
                  )}
                />
                <span className="sr-only">
                  {marca === "reimpresion" ? "Reimpreso" : "Reintentado"}
                </span>
              </>
            )}
          </span>
        )
      },
    },
  ]
  // Error, la última columna de datos (casi siempre está vacía) y las acciones,
  // a la derecha del todo (decisión del usuario, 26/10/2026).
  if (!gestionar) return [...columnas, columnaError]
  return [
    ...columnas,
    columnaError,
    {
      id: "acciones",
      header: "",
      size: 190,
      cell: ({ row: { original: t } }) => (
        <div className="flex justify-end gap-1">
          {/* Cancelar delante: así Reintentar y Reimprimir quedan siempre a la
              derecha, alineados entre filas (decisión del usuario, 26/10/2026). */}
          {puedeCancelar(t.status) && (
            <Button
              size="xs"
              variant="destructive"
              disabled={ocupado}
              onClick={() =>
                window.confirm(
                  "¿Cancelar este trabajo? Si sigue en la cola de la impresora, se cancela también allí."
                ) && ejecutar([t], "cancelar")
              }
            >
              Cancelar
            </Button>
          )}
          {puedeReintentar(t.status) && (
            <Button
              size="xs"
              variant="outline"
              disabled={ocupado}
              onClick={() => ejecutar([t], "reintentar")}
            >
              Reintentar
            </Button>
          )}
          {puedeReimprimir(t.status) && (
            <Button
              size="xs"
              variant="outline"
              disabled={ocupado}
              onClick={() => reimprimir(t)}
            >
              Reimprimir
            </Button>
          )}
        </div>
      ),
    },
  ]
}

// Reimprimir lo que ya salió puede duplicarlo: solo de uno en uno, con su
// ventana de motivo (la misma que el botón de la fila).
function AccionesMasivas({
  filas,
  ocupado,
  onAccion,
  onReimprimir,
}: {
  filas: TrabajoCola[]
  ocupado: boolean
  onAccion: (tipo: AccionTrabajo, aptos: TrabajoCola[]) => void
  onReimprimir: (t: TrabajoCola) => void
}) {
  const reintentables = filas.filter((t) => puedeReintentar(t.status))
  const reimprimibles = filas.filter((t) => puedeReimprimir(t.status))
  const cancelables = filas.filter((t) => puedeCancelar(t.status))
  return (
    <>
      <Button
        size="sm"
        variant="outline"
        disabled={ocupado || !reintentables.length}
        title={
          reimprimibles.length && !reintentables.length
            ? "Lo que ya salió no se reintenta: se reimprime"
            : "Solo los pendientes y los que dieron error"
        }
        onClick={() =>
          window.confirm(
            `¿Reintentar ${plural(reintentables.length, "trabajo", "trabajos")}?`
          ) && onAccion("reintentar", reintentables)
        }
      >
        Reintentar ({reintentables.length})
      </Button>
      {reimprimibles.length > 0 && (
        <Button
          size="sm"
          variant="outline"
          disabled={ocupado || reimprimibles.length > 1}
          title={
            reimprimibles.length > 1
              ? "Reimprimir va de uno en uno: puede salir duplicado"
              : "Lo que ya salió, con su motivo"
          }
          onClick={() => onReimprimir(reimprimibles[0])}
        >
          Reimprimir ({reimprimibles.length})
        </Button>
      )}
      <Button
        size="sm"
        variant="destructive"
        disabled={ocupado || !cancelables.length}
        onClick={() =>
          window.confirm(
            `¿Cancelar ${plural(cancelables.length, "trabajo", "trabajos")}?`
          ) && onAccion("cancelar", cancelables)
        }
      >
        Cancelar ({cancelables.length})
      </Button>
    </>
  )
}
