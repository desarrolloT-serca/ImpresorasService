import { useMemo, useState } from "react"
import type { ColumnDef } from "@tanstack/react-table"
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
import { usePermiso } from "@/core/sesion"
import { cn } from "@/lib/utils"
import { useEsc, type PropsPanel } from "@/workspace/modulo"
import {
  useAccionTrabajo,
  useCola,
  useTiendas,
  type AccionTrabajo,
} from "./api"
import {
  CLASE_TONO,
  ESTADOS,
  documentoErp,
  fechaHora,
  fechaHoraCorta,
  nombreTienda,
  puedeCancelar,
  puedeReimprimir,
  puedeReintentar,
  TIPOS_DOCUMENTO,
} from "./formato"

const INICIAL: ConsultaCola = { pagina: 1, tamano: 50 }

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

const AVISO_REIMPRIMIR =
  "Este trabajo puede haberse impreso ya. Si lo reimprimes y sí había salido, el documento saldrá por duplicado. ¿Continuar?"

// Cola de impresión (cola.blade.php del PHP). La API ordena por fecha de
// creación, la más reciente primero, y filtra la tienda de quien no las ve
// todas.
export default function Cola({ clave }: PropsPanel) {
  const verTodas = usePermiso(P.todasLasTiendas)
  const gestionar = usePermiso(P.reimprimir)
  const [consulta, setConsulta] = useState(() => desdeClave(clave))
  const [reinicio, setReinicio] = useState(0)
  const cola = useCola(consulta)
  const tiendas = useTiendas().data ?? []
  const accion = useAccionTrabajo()
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
  }

  const columnas = useMemo(
    () => columnasCola(tiendas, gestionar, accion.isPending, ejecutar),
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

  return (
    <Panel className="[&_table]:min-w-[62rem] [&_table]:table-fixed">
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
                  onValueChange={(v) =>
                    filtrar({ tienda: v === "todas" ? undefined : Number(v) })
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

const columnaError: ColumnDef<TrabajoCola, unknown> = {
  id: "error",
  header: "Error",
  size: 150,
  cell: ({ row: { original: t } }) =>
    t.status === 8 && t.lastErrorCode ? (
      <span
        className="block truncate font-mono text-xs text-destructive"
        title={t.lastErrorMessage ?? undefined}
      >
        {t.lastErrorCode}
      </span>
    ) : null,
}

function columnasCola(
  tiendas: readonly Tienda[],
  gestionar: boolean,
  ocupado: boolean,
  ejecutar: (t: TrabajoCola[], tipo: AccionTrabajo) => void
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
      header: () => (
        <span className="tracking-normal normal-case">Intentos</span>
      ),
      size: 80,
      meta: { num: true },
      cell: ({ row: { original: t } }) => (
        <span className="block text-right tabular-nums">{t.attemptCount}</span>
      ),
    },
    {
      id: "creado",
      header: "Creado",
      size: 140,
      cell: ({ row: { original: t } }) => (
        <span title={fechaHora(t.createdAtUtc)}>
          {fechaHoraCorta(t.createdAtUtc)}
        </span>
      ),
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
              onClick={() =>
                window.confirm(AVISO_REIMPRIMIR) && ejecutar([t], "reintentar")
              }
            >
              Reimprimir
            </Button>
          )}
          {puedeCancelar(t.status) && (
            <Button
              size="xs"
              variant="destructive"
              disabled={ocupado}
              onClick={() =>
                window.confirm("¿Cancelar este trabajo?") &&
                ejecutar([t], "cancelar")
              }
            >
              Cancelar
            </Button>
          )}
        </div>
      ),
    },
  ]
}

// Reimprimir lo que está sin confirmar puede duplicarlo: solo de uno en uno.
function AccionesMasivas({
  filas,
  ocupado,
  onAccion,
}: {
  filas: TrabajoCola[]
  ocupado: boolean
  onAccion: (tipo: AccionTrabajo, aptos: TrabajoCola[]) => void
}) {
  const reintentables = filas.filter((t) => puedeReintentar(t.status))
  const cancelables = filas.filter((t) => puedeCancelar(t.status))
  return (
    <>
      <Button
        size="sm"
        variant="outline"
        disabled={ocupado || !reintentables.length}
        title="Solo los pendientes y los que dieron error"
        onClick={() =>
          window.confirm(`¿Reintentar ${reintentables.length} trabajos?`) &&
          onAccion("reintentar", reintentables)
        }
      >
        Reintentar ({reintentables.length})
      </Button>
      <Button
        size="sm"
        variant="destructive"
        disabled={ocupado || !cancelables.length}
        onClick={() =>
          window.confirm(`¿Cancelar ${cancelables.length} trabajos?`) &&
          onAccion("cancelar", cancelables)
        }
      >
        Cancelar ({cancelables.length})
      </Button>
    </>
  )
}
