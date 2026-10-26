import { useMemo, useState } from "react"
import type { ColumnDef } from "@tanstack/react-table"
import { PlusIcon } from "lucide-react"
import { toast } from "sonner"
import {
  PERMISOS_IMPRESION as P,
  type Impresora,
  type Tienda,
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
  useBorrarImpresora,
  useComprobarImpresora,
  useImpresoras,
  useTiendas,
} from "./api"
import { FormularioImpresora } from "./FormularioImpresora"
import {
  CLASE_TONO,
  conProblema,
  fechaHora,
  nombreTienda,
  puerto,
  resumen,
  TONO_SEVERIDAD,
  type Tono,
} from "./formato"

const Etiqueta = ({
  tono,
  title,
  children,
}: {
  tono: Tono
  title?: string
  children: React.ReactNode
}) => (
  <Badge
    variant="outline"
    className={cn("border-transparent", CLASE_TONO[tono])}
    title={title}
  >
    {children}
  </Badge>
)

const COLOR_PUNTO: Record<Tono, string> = {
  ok: "bg-success",
  aviso: "bg-warning",
  error: "bg-destructive",
  info: "bg-secondary-foreground",
  neutro: "bg-muted-foreground/40",
}

const TODAS = "todas"
const CONEXION_ITEMS = [
  { value: TODAS, label: "Todas" },
  { value: "problemas", label: "Con problemas" },
]

// Impresoras (impresoras/index.blade.php del PHP). Quien ve todas las tiendas
// elige la tienda en un selector, como en la cola, con el estado de cada una
// al lado; «Todas» las junta en una tabla con su tienda. La conexión la
// comprueba el Worker cada 30 s; la pantalla relee la lista con ese ritmo y
// «Comprobar» lo hace al momento. Alta, edición y baja, para quien administra.
export default function Impresoras({ clave }: PropsPanel) {
  const verTodas = usePermiso(P.todasLasTiendas)
  const admin = usePermiso(P.administrar)
  const gestionar = usePermiso(P.reimprimir)
  const impresoras = useImpresoras()
  const tiendas = useTiendas().data ?? []
  const comprobar = useComprobarImpresora()
  const borrar = useBorrarImpresora()
  const [editando, setEditando] = useState<Impresora | "nueva">()
  // Abierta desde el dashboard, la clave trae los filtros: «tienda=12&conexion=problemas».
  const [tienda, setTienda] = useState(
    () =>
      new URLSearchParams(clave).get("tienda")?.match(/^\d{1,9}$/)?.[0] ?? TODAS
  )
  const [conexion, setConexion] = useState(() =>
    new URLSearchParams(clave).get("conexion") === "problemas"
      ? "problemas"
      : TODAS
  )
  const [reinicio, setReinicio] = useState(0)
  // Esc: quita lo buscado; los filtros se quedan (regla del ERP).
  useEsc(() => setReinicio((n) => n + 1))

  const lista = impresoras.data ?? []
  const deTienda =
    tienda === TODAS ? lista : lista.filter((i) => String(i.storeId) === tienda)
  const visibles = conexion === TODAS ? deTienda : deTienda.filter(conProblema)
  const total = resumen(deTienda)
  const tiendaItems = [
    { value: TODAS, label: "Todas" },
    // Tiendas activas en orden de código, también las que no tienen impresoras.
    ...tiendas
      .filter((t) => t.isActive)
      .map((t) => ({
        value: String(t.storeId),
        label: nombreTienda(t.storeId, tiendas),
      })),
  ]

  const columnas = useMemo(
    () =>
      columnasImpresoras({
        tiendas,
        conTienda: verTodas && tienda === TODAS,
        admin,
        gestionar,
        comprobando: comprobar.isPending ? comprobar.variables : undefined,
        onComprobar: (id) =>
          comprobar.mutate(id, {
            onSuccess: (r) =>
              r.reachable
                ? toast.success(
                    `Conectada${r.transport ? ` por ${r.transport}` : ""}${r.latencyMs !== null ? ` en ${r.latencyMs} ms` : ""}`
                  )
                : toast.error(`Sin conexión: ${r.connectivityLabel}`),
            onError: (e) =>
              toast.error(
                e instanceof ErrorApi
                  ? e.problema.title
                  : "No se pudo comprobar"
              ),
          }),
        onEditar: setEditando,
        onBorrar: (i) =>
          window.confirm(
            `Se eliminará la impresora ${i.printerName}. Esta acción no se puede deshacer.`
          ) &&
          borrar.mutate(i.printerId, {
            onSuccess: () => toast.success("Impresora eliminada"),
            onError: (e) =>
              toast.error(
                e instanceof ErrorApi ? e.problema.title : "No se pudo eliminar"
              ),
          }),
      }),
    // comprobar.mutate y borrar.mutate son estables.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [
      tiendas,
      verTodas,
      tienda,
      admin,
      gestionar,
      comprobar.isPending,
      comprobar.variables,
    ]
  )

  return (
    <Panel className="[&_table]:min-w-[60rem] [&_table]:table-fixed">
      <DataTable
        key={reinicio}
        columns={columnas}
        data={visibles}
        paginar={false}
        buscar={verTodas ? "Nombre, cola o host" : undefined}
        buscarEtiqueta="Buscar impresora"
        emptyContent={
          impresoras.isPending
            ? undefined
            : conexion === TODAS
              ? "No hay impresoras configuradas."
              : "Ninguna impresora con problemas."
        }
        toolbar={
          verTodas && (
            <>
              <CampoFiltro etiqueta="Tienda">
                <Select
                  items={tiendaItems}
                  value={tienda}
                  onValueChange={(v) => setTienda(v as string)}
                >
                  <SelectTrigger className="w-64">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {tiendaItems.map((i) => {
                      const r = resumen(
                        i.value === TODAS
                          ? lista
                          : lista.filter((x) => String(x.storeId) === i.value)
                      )
                      return (
                        <SelectItem key={i.value} value={i.value}>
                          <span
                            aria-hidden
                            className={cn(
                              "size-2 shrink-0 rounded-full",
                              COLOR_PUNTO[r.tono]
                            )}
                          />
                          <span className="truncate">{i.label}</span>
                          {r.tono !== "ok" && (
                            <span className="ml-auto pl-3 text-xs text-muted-foreground">
                              {r.texto}
                            </span>
                          )}
                        </SelectItem>
                      )
                    })}
                  </SelectContent>
                </Select>
              </CampoFiltro>
              <CampoFiltro etiqueta="Conexión">
                <Select
                  items={CONEXION_ITEMS}
                  value={conexion}
                  onValueChange={(v) => setConexion(v as string)}
                >
                  <SelectTrigger className="w-40">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {CONEXION_ITEMS.map((i) => (
                      <SelectItem key={i.value} value={i.value}>
                        {i.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </CampoFiltro>
              <span className="ml-auto flex items-center gap-2 self-center text-xs text-muted-foreground">
                {deTienda.length} impresoras
                {total.tono !== "ok" && total.tono !== "neutro" && (
                  <Etiqueta tono={total.tono}>{total.texto}</Etiqueta>
                )}
              </span>
              {admin && (
                <Button size="sm" onClick={() => setEditando("nueva")}>
                  <PlusIcon data-icon="inline-start" />
                  Nueva impresora
                </Button>
              )}
            </>
          )
        }
      />
      {editando && (
        <FormularioImpresora
          impresora={editando === "nueva" ? undefined : editando}
          tienda={tienda === TODAS ? undefined : Number(tienda)}
          tiendas={tiendas}
          onCerrar={() => setEditando(undefined)}
        />
      )}
    </Panel>
  )
}

function columnasImpresoras({
  tiendas,
  conTienda,
  admin,
  gestionar,
  comprobando,
  onComprobar,
  onEditar,
  onBorrar,
}: {
  tiendas: readonly Tienda[]
  conTienda: boolean
  admin: boolean
  gestionar: boolean
  comprobando: number | undefined
  onComprobar: (id: number) => void
  onEditar: (i: Impresora) => void
  onBorrar: (i: Impresora) => void
}): ColumnDef<Impresora, unknown>[] {
  // Con accesor: el buscador y el orden por columna miran ese valor.
  const columnas: ColumnDef<Impresora, unknown>[] = [
    ...(conTienda
      ? [
          {
            id: "tienda",
            header: "Tienda",
            size: 170,
            accessorFn: (i: Impresora) => nombreTienda(i.storeId, tiendas),
            cell: ({ getValue }) => (
              <span className="block truncate">{getValue<string>()}</span>
            ),
          } satisfies ColumnDef<Impresora, unknown>,
        ]
      : []),
    {
      id: "nombre",
      header: "Nombre",
      size: 0,
      accessorKey: "printerName",
      cell: ({ row: { original: i } }) => (
        <span className="block truncate font-medium">{i.printerName}</span>
      ),
    },
    {
      id: "cola",
      header: "Cola",
      size: 200,
      accessorFn: (i) => i.spoolQueue ?? "",
      cell: ({ row: { original: i } }) =>
        i.spoolQueue ? (
          <span className="block truncate font-mono" title={i.spoolQueue}>
            {i.spoolQueue}
          </span>
        ) : (
          <span className="text-muted-foreground">Solo IP</span>
        ),
    },
    {
      id: "host",
      header: "Host",
      size: 150,
      accessorFn: (i) => i.host ?? "",
      cell: ({ row: { original: i } }) =>
        i.host ? (
          <span className="block truncate font-mono" title={i.host}>
            {i.host}
          </span>
        ) : (
          <span className="text-muted-foreground">-</span>
        ),
    },
    {
      id: "activa",
      header: "Activa",
      size: 80,
      cell: ({ row: { original: i } }) => (
        <Etiqueta tono={i.isActive ? "ok" : "error"}>
          {i.isActive ? "Sí" : "No"}
        </Etiqueta>
      ),
    },
    {
      id: "conexion",
      header: "Conexión",
      size: 140,
      cell: ({ row: { original: i } }) => (
        <Etiqueta
          tono={TONO_SEVERIDAD[i.connectivitySeverity]}
          title={
            i.connectivityDetail ??
            i.lastConnectionError ??
            (i.lastConnectionCheckAtUtc
              ? `Último chequeo: ${fechaHora(i.lastConnectionCheckAtUtc)}`
              : undefined)
          }
        >
          {/* Como el PHP: a quien no administra no se le da el detalle. */}
          {i.connectivityStatus === "error" && !admin
            ? "Sin conexión"
            : i.connectivityLabel}
        </Etiqueta>
      ),
    },
    {
      id: "puerto",
      header: "Puerto",
      size: 80,
      cell: ({ row: { original: i } }) => (
        <span className="font-mono">{puerto(i.lastConnectionTransport)}</span>
      ),
    },
    {
      id: "ipp",
      header: "IPP",
      size: 80,
      cell: ({ row: { original: i } }) =>
        i.ippSupported === null ? (
          <Etiqueta tono="aviso" title="Compatibilidad IPP sin comprobar">
            IPP ?
          </Etiqueta>
        ) : i.ippSupported ? (
          <Etiqueta tono="ok" title="Soporta IPP">
            IPP ✓
          </Etiqueta>
        ) : (
          <Etiqueta tono="neutro" title="No soporta IPP">
            IPP ✗
          </Etiqueta>
        ),
    },
  ]
  if (!gestionar) return columnas
  return [
    ...columnas,
    {
      id: "acciones",
      header: "",
      size: admin ? 250 : 110,
      cell: ({ row: { original: i } }) => (
        <div className="flex justify-end gap-1">
          <Button
            size="xs"
            variant="outline"
            disabled={comprobando === i.printerId}
            onClick={() => onComprobar(i.printerId)}
          >
            {comprobando === i.printerId ? "Comprobando…" : "Comprobar"}
          </Button>
          {admin && (
            <>
              <Button size="xs" variant="outline" onClick={() => onEditar(i)}>
                Editar
              </Button>
              <Button
                size="xs"
                variant="destructive"
                onClick={() => onBorrar(i)}
              >
                Eliminar
              </Button>
            </>
          )}
        </div>
      ),
    },
  ]
}
