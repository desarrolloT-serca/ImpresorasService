import { useMemo, useState } from "react"
import type { ColumnDef } from "@tanstack/react-table"
import {
  PencilIcon,
  PlusIcon,
  RefreshCwIcon,
  Trash2Icon,
} from "lucide-react"
import { toast } from "sonner"
import {
  PERMISOS_IMPRESION as P,
  type Impresora,
  type Tienda,
} from "@serca/contratos"

import { DataTable } from "@/components/data-table"
import { Panel } from "@/components/ficha"
import { CampoFiltro } from "@/components/filtro-lista"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
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
  useIdentificarImpresora,
  useImpresoras,
  useTiendas,
  useUmbrales,
} from "./api"
import { BarraToner, ChipEstado } from "./ChipEstado"
import { tiposEstado } from "./estado-impresora"
import { FichaImpresora } from "./FichaImpresora"
import { FormularioImpresora } from "./FormularioImpresora"
import {
  apiSinEstado,
  CLASE_TONO,
  COLOR_PUNTO,
  conProblema,
  FILTROS_ESTADO,
  fechaHora,
  nombreTienda,
  pasaFiltroEstado,
  resumen,
  TONO_SEVERIDAD,
  type FiltroEstado,
  type Tono,
  type UmbralToner,
} from "./formato"
import { SinActualizar } from "./formulario"

const Etiqueta = ({
  tono,
  title,
  className,
  children,
}: {
  tono: Tono
  title?: string
  className?: string
  children: React.ReactNode
}) => (
  <Badge
    variant="outline"
    className={cn("border-transparent", CLASE_TONO[tono], className)}
    title={title}
  >
    {children}
  </Badge>
)

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
  // Los de Reglas y avisos: la barra de tóner se pinta igual que se configura.
  const toner = useUmbrales().data?.toner
  const comprobar = useComprobarImpresora()
  const identificar = useIdentificarImpresora()
  const borrar = useBorrarImpresora()
  const [editando, setEditando] = useState<Impresora | "nueva">()
  // La ficha, por número: así se ve lo último que trae la lista (docs/72, mejora 6).
  const [ficha, setFicha] = useState<number>()
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
  // Lo que dice la propia impresora (docs/72, mejora 2).
  const [estado, setEstado] = useState<FiltroEstado>("todas")
  const [reinicio, setReinicio] = useState(0)
  // Esc: quita lo buscado; los filtros se quedan (regla del ERP).
  useEsc(() => setReinicio((n) => n + 1))

  const lista = impresoras.data ?? []
  const deTienda =
    tienda === TODAS ? lista : lista.filter((i) => String(i.storeId) === tienda)
  const visibles = (
    conexion === TODAS ? deTienda : deTienda.filter(conProblema)
  ).filter((i) => pasaFiltroEstado(i, estado, toner))
  const enFicha = lista.find((i) => i.printerId === ficha)

  // «Comprobar» lee también el estado IPP (docs/72, mejora 1): la lista se relee al acabar.
  const comprobarImpresora = (id: number) =>
    comprobar.mutate(id, {
      onSuccess: (r) =>
        r.reachable
          ? toast.success(
              `Conectada${r.transport ? ` por ${r.transport}` : ""}${r.latencyMs !== null ? ` en ${r.latencyMs} ms` : ""}`
            )
          : toast.error(`Sin conexión: ${r.connectivityLabel}`),
      onError: (e) =>
        toast.error(
          e instanceof ErrorApi ? e.problema.title : "No se pudo comprobar"
        ),
    })
  const identificarImpresora = (id: number) =>
    identificar.mutate(id, {
      onSuccess: (r) =>
        r.identified ? toast.success(r.message) : toast.warning(r.message),
      onError: (e) =>
        toast.error(
          e instanceof ErrorApi ? e.problema.title : "No se pudo identificar"
        ),
    })
  const total = resumen(deTienda)
  // «5 con error»: el número en negrita, como el total (decisión del usuario).
  const [problemas, ...queTienen] = total.texto.split(" ")
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
        toner,
        admin,
        gestionar,
        comprobando: comprobar.isPending ? comprobar.variables : undefined,
        onComprobar: comprobarImpresora,
        onFicha: setFicha,
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
    // comprobar.mutate, borrar.mutate y setFicha son estables.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [
      tiendas,
      toner,
      verTodas,
      tienda,
      admin,
      gestionar,
      comprobar.isPending,
      comprobar.variables,
    ]
  )

  return (
    <Panel className="[&_table]:min-w-[76rem] [&_table]:table-fixed">
      <SinActualizar consultas={[impresoras]} />
      {apiSinEstado(lista) && (
        <Alert>
          <AlertTitle>El servicio de impresión no está actualizado</AlertTitle>
          <AlertDescription>
            Su API es anterior al 10/11/2026: el estado, la ficha completa
            (firmware, encendida, bandejas, previsión) e Identificar no
            funcionarán hasta desplegar la última versión en su servidor.
          </AlertDescription>
        </Alert>
      )}
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
            : conexion === TODAS && estado === "todas"
              ? "No hay impresoras configuradas."
              : "Ninguna impresora con lo que se busca."
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
                              "size-2 shrink-0 self-center rounded-full",
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
              <CampoFiltro etiqueta="Estado">
                <Select
                  items={[...FILTROS_ESTADO]}
                  value={estado}
                  onValueChange={(v) => setEstado(v as FiltroEstado)}
                >
                  <SelectTrigger className="w-48">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {FILTROS_ESTADO.map((i) => (
                      <SelectItem key={i.value} value={i.value}>
                        {i.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </CampoFiltro>
              <span className="ml-auto flex h-8 items-center gap-2 rounded-lg border bg-card px-3 text-sm">
                <span>
                  <b className="font-semibold tabular-nums">
                    {deTienda.length}
                  </b>{" "}
                  <span className="text-muted-foreground">impresoras</span>
                </span>
                {total.tono !== "ok" && total.tono !== "neutro" && (
                  <span
                    className={
                      total.tono === "error"
                        ? "text-destructive"
                        : "text-warning"
                    }
                  >
                    <span className="text-muted-foreground">·</span>{" "}
                    <b className="font-semibold tabular-nums">{problemas}</b>{" "}
                    {queTienen.join(" ")}
                  </span>
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
      {enFicha && (
        <FichaImpresora
          impresora={enFicha}
          tiendas={tiendas}
          toner={toner}
          gestionar={gestionar}
          comprobando={
            comprobar.isPending && comprobar.variables === enFicha.printerId
          }
          identificando={identificar.isPending}
          onComprobar={() => comprobarImpresora(enFicha.printerId)}
          onIdentificar={() => identificarImpresora(enFicha.printerId)}
          onCerrar={() => setFicha(undefined)}
        />
      )}
    </Panel>
  )
}

function columnasImpresoras({
  tiendas,
  conTienda,
  toner,
  admin,
  gestionar,
  comprobando,
  onComprobar,
  onFicha,
  onEditar,
  onBorrar,
}: {
  tiendas: readonly Tienda[]
  conTienda: boolean
  toner: UmbralToner | undefined
  admin: boolean
  gestionar: boolean
  comprobando: number | undefined
  onComprobar: (id: number) => void
  onFicha: (id: number) => void
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
      // Fijo, no elástico: lo que sobra se reparte entre todas las columnas
      // (decisión del usuario, 27/10/2026).
      size: 180,
      accessorKey: "printerName",
      // El nombre abre la ficha: subrayado de puntos, discreto (decisión del
      // usuario, 10/11/2026).
      cell: ({ row: { original: i } }) => (
        <button
          type="button"
          className="block max-w-full cursor-pointer truncate text-left font-medium underline decoration-muted-foreground/40 decoration-dotted underline-offset-4 hover:decoration-foreground"
          title={`Ver la ficha${i.ippMakeAndModel ? ` · ${i.ippMakeAndModel}` : ""}`}
          onClick={() => onFicha(i.printerId)}
        >
          {i.printerName}
        </button>
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
      // Lo que dice la impresora por IPP, tipificado (docs/72, 10/11/2026): el
      // tipo más grave con su icono y «+N»; el texto completo en el title.
      id: "estado",
      header: "Estado",
      size: 150,
      accessorFn: (i) =>
        tiposEstado(i, toner)
          .map((t) => t.etiqueta)
          .join(" "),
      cell: ({ row: { original: i } }) => (
        <ChipEstado impresora={i} umbral={toner} />
      ),
    },
    {
      id: "toner",
      header: "Tóner",
      size: 110,
      cell: ({ row: { original: i } }) => (
        <BarraToner impresora={i} umbral={toner} />
      ),
    },
    {
      id: "conexion",
      header: "Conexión",
      size: 130,
      cell: ({ row: { original: i } }) => (
        <Etiqueta
          tono={TONO_SEVERIDAD[i.connectivitySeverity]}
          title={
            [
              i.connectivityDetail ?? i.lastConnectionError,
              i.lastConnectionTransport &&
                `Responde por ${i.lastConnectionTransport}`,
              i.lastConnectionCheckAtUtc &&
                `Último chequeo: ${fechaHora(i.lastConnectionCheckAtUtc)}`,
            ]
              .filter(Boolean)
              .join("\n") || undefined
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
      id: "ipp",
      header: "IPP",
      size: 70,
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
    {
      id: "activa",
      header: "Activa",
      size: 70,
      cell: ({ row: { original: i } }) => (
        <Etiqueta tono={i.isActive ? "ok" : "error"}>
          {i.isActive ? "Sí" : "No"}
        </Etiqueta>
      ),
    },
  ]
  if (!gestionar) return columnas
  // Iconos en vez de botones con texto (docs/72, 10/11/2026): el nombre va en
  // el title y en aria-label.
  return [
    ...columnas,
    {
      id: "acciones",
      header: "",
      size: admin ? 100 : 44,
      cell: ({ row: { original: i } }) => (
        <div className="flex justify-end gap-0.5">
          <Button
            size="icon-xs"
            variant="ghost"
            disabled={comprobando === i.printerId}
            title="Comprobar conexión y estado"
            aria-label={`Comprobar ${i.printerName}`}
            onClick={() => onComprobar(i.printerId)}
          >
            <RefreshCwIcon
              className={cn(comprobando === i.printerId && "animate-spin")}
            />
          </Button>
          {admin && (
            <>
              <Button
                size="icon-xs"
                variant="ghost"
                title="Editar"
                aria-label={`Editar ${i.printerName}`}
                onClick={() => onEditar(i)}
              >
                <PencilIcon />
              </Button>
              <Button
                size="icon-xs"
                variant="ghost"
                className="text-destructive hover:text-destructive"
                title="Eliminar"
                aria-label={`Eliminar ${i.printerName}`}
                onClick={() => onBorrar(i)}
              >
                <Trash2Icon />
              </Button>
            </>
          )}
        </div>
      ),
    },
  ]
}
