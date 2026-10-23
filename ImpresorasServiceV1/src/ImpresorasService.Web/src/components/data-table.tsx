import * as React from "react"
import {
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type RowSelectionState,
  type SortingState,
} from "@tanstack/react-table"
import {
  ArrowDownIcon,
  ArrowUpIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  SearchIcon,
} from "lucide-react"
import { TAMANOS_PAGINA } from "@serca/contratos"

import { cn, siguienteOrden } from "@/lib/utils"
import { CampoFiltro } from "@/components/filtro-lista"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from "@/components/ui/input-group"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableFooter,
  TableRow,
} from "@/components/ui/table"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/components/ui/empty"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { num } from "@/lib/format"

type Teclas = { ctrlKey: boolean; metaKey: boolean }

type Props<T> = {
  columns: ColumnDef<T, unknown>[]
  data: T[]
  buscar?: string
  // Nombre encima del buscador, como el resto de filtros (CampoFiltro).
  buscarEtiqueta?: string
  // Ancho del buscador si no vale el de por defecto (w-72).
  buscarClassName?: string
  buscarInputProps?: Pick<
    React.ComponentProps<"input">,
    "form" | "name" | "type" | "inputMode" | "maxLength"
  >
  // Consulta directa por número, independiente de los filtros del listado.
  onBuscarIntro?: (texto: string, teclas: Teclas) => void
  // Clic en una fila, o Intro en el buscador (ver exacta). Con Ctrl, a la
  // derecha (derecha() de workspace).
  onRowClick?: (row: T, e: Teclas) => void
  // Intro en el buscador elige la fila que coincide exacta con el texto
  // (referencia, EAN, código…), aunque haya otras que empiezan igual. Si hay
  // varias exactas se dejan solo esas; si no hay ninguna, se avisa.
  exacta?: (row: T, q: string) => boolean
  // Lo elegido, dentro del buscador tras el texto (marca, descripción…).
  buscarDetalle?: React.ReactNode
  emptyContent?: React.ReactNode
  toolbar?: React.ReactNode
  // Encabezado de la tabla, pegado encima de las columnas dentro del mismo
  // marco: no se desplaza con las filas (lo que se ve, botones de la tabla).
  cabecera?: React.ReactNode
  // Usa los footer de las columnas; permite totales de servidor alineados.
  pie?: boolean
  seleccion?: (filas: T[], limpiar: () => void) => React.ReactNode
  // Impresión: identidad de la fila para la selección. Sin ella, la selección
  // va por posición y, al refrescar o cambiar de página, apunta a otras filas.
  getRowId?: (row: T) => string
  pageSize?: number
  // Detalles ya cargados completos: todas las filas, sin pie de paginación.
  // Las listas de servidor conservan siempre su paginación.
  paginar?: boolean
  sorting?: SortingState
  rowClassName?: (row: T) => string | undefined
  // Aún no se ha buscado nada: la tabla y el pie no se ven y el buscador se
  // queda en su sitio (sin saltos al aparecer los resultados).
  busquedaPendiente?: boolean
  // Modo servidor: la API filtra, ordena y pagina; la tabla solo pinta una
  // página. Obligatorio en tablas grandes. Se pueden ordenar las columnas
  // cuyo id está en servidor.campos (los campos de orden del contrato); las
  // numéricas (meta.num) empiezan por la mayor.
  servidor?: Servidor
}

// Paginación por desplazamiento: total = filas que cumplen los filtros.
// Por cursor (con haySiguiente): total si la API las ha contado, masDe si
// pasan de su tope. Filtros, búsqueda y orden los aplica la API a todas las
// páginas, no solo a la que se ve.
// campos: los campos de orden del contrato (ORDEN_X); una columna que no esté
// no se puede pulsar, así que nunca se pide un orden que la API rechazaría.
// ordenables: de esos, los que se pueden usar ahora (sin él, todos); el resto
// se podrá al filtrar más.
export type Servidor = {
  campos: readonly string[]
  total?: number
  masDe?: number
  haySiguiente?: boolean
  pagina: number
  tamano: number
  orden?: string
  q?: string
  cargando?: boolean
  ordenables?: string[]
  onCambio: (c: {
    pagina?: number
    orden?: string
    q?: string
    tamano?: number
  }) => void
}

// Con etiqueta, el buscador lleva su nombre encima como los demás filtros.
const CampoBuscar = ({
  etiqueta,
  children,
}: {
  etiqueta?: string
  children: React.ReactNode
}) =>
  etiqueta ? (
    <CampoFiltro etiqueta={etiqueta}>{children}</CampoFiltro>
  ) : (
    children
  )

// Búsqueda en servidor: con menos de 2 caracteres no se consulta (casi todo
// coincide y cada pulsación sería una consulta a la base).
const MIN_BUSQUEDA = 2
const qValida = (s: string) => {
  const t = s.trim()
  return t.length >= MIN_BUSQUEDA ? t : ""
}

const deOrden = (o?: string): SortingState =>
  o ? [{ id: o.replace(/^-/, ""), desc: o.startsWith("-") }] : []

export function DataTable<T>({
  columns,
  data,
  buscar,
  buscarEtiqueta,
  buscarClassName,
  buscarInputProps,
  onBuscarIntro,
  buscarDetalle,
  onRowClick,
  exacta,
  toolbar,
  cabecera,
  pie = false,
  emptyContent,
  seleccion,
  getRowId,
  pageSize = 50,
  paginar = true,
  sorting: sort0 = [],
  rowClassName,
  busquedaPendiente,
  servidor,
}: Props<T>) {
  const [sorting, setSorting] = React.useState<SortingState>(sort0)
  // Orden con el que abre la tabla. Quitarlo o buscar otro texto vuelve a él:
  // con otro orden puesto, lo buscado no sale arriba y parece que el buscador
  // no filtra («1364» ordenado por descripción).
  const [ordenInicial] = React.useState(servidor?.orden)
  // Columna que el usuario ha ordenado (no la del orden inicial): se resalta
  // en amarillo suave para que se vea que no es el orden de siempre.
  const columnaOrdenada =
    servidor?.orden && servidor.orden !== ordenInicial
      ? servidor.orden.replace(/^-/, "")
      : undefined
  // Cabecera de texto de la columna; si es un componente, su id.
  const nombreColumna = (id: string) => {
    const h = columns.find(
      (c) => c.id === id || ("accessorKey" in c && c.accessorKey === id)
    )?.header
    return typeof h === "string" ? h : id
  }
  const buscarTexto = (q: string | undefined) =>
    servidor?.onCambio({
      q,
      pagina: 1,
      ...(servidor.orden !== ordenInicial ? { orden: ordenInicial } : {}),
    })
  const [globalFilter, setGlobalFilter] = React.useState(servidor?.q ?? "")
  // Intro pulsado: se elige cuando llegan los resultados de ese texto.
  const [intro, setIntro] = React.useState<{
    q: string
    teclas: Teclas
  } | null>(null)
  // Texto con varias exactas: mientras siga buscado, solo se ven esas.
  const [soloExactas, setSoloExactas] = React.useState<string | null>(null)
  const [aviso, setAviso] = React.useState("")
  const qActual = servidor ? (servidor.q ?? "") : qValida(globalFilter)
  const filtrando = !!exacta && soloExactas !== null && soloExactas === qActual
  const filas = filtrando ? data.filter((f) => exacta(f, qActual)) : data
  const listo = intro !== null && intro.q === qActual && !servidor?.cargando
  const elegirConIntro = React.useEffectEvent(() => {
    if (!intro) return
    setIntro(null)
    if (!exacta) return
    const exactas = data.filter((f) => exacta(f, intro.q))
    if (exactas.length === 1) onRowClick?.(exactas[0], intro.teclas)
    else if (exactas.length > 1) setSoloExactas(intro.q)
    else setAviso("Sin coincidencia exacta: sigue escribiendo o pulsa una fila")
  })
  React.useEffect(() => {
    if (listo) elegirConIntro()
  }, [listo])
  const [rowSelection, setRowSelection] = React.useState<RowSelectionState>({})

  const cols = React.useMemo<ColumnDef<T, unknown>[]>(() => {
    // Sentido del primer clic fijo: TanStack lo deduce de la primera fila y,
    // con la tabla vacía, empezaba por el mayor también en las de texto.
    const ordenables = columns.map((c) => ({
      sortDescFirst: !!(c.meta as { num?: boolean } | undefined)?.num,
      ...c,
    }))
    if (!seleccion) return ordenables
    return [
      {
        id: "_sel",
        size: 32,
        enableSorting: false,
        header: ({ table }) => (
          <Checkbox
            aria-label="Seleccionar todo"
            checked={table.getIsAllPageRowsSelected()}
            indeterminate={table.getIsSomePageRowsSelected()}
            onCheckedChange={(v) => table.toggleAllPageRowsSelected(!!v)}
          />
        ),
        cell: ({ row }) => (
          <Checkbox
            aria-label="Seleccionar fila"
            checked={row.getIsSelected()}
            onCheckedChange={(v) => row.toggleSelected(!!v)}
            onClick={(e) => e.stopPropagation()}
          />
        ),
      },
      ...ordenables,
    ]
  }, [columns, seleccion])

  // En modo servidor la búsqueda espera a que se deje de teclear (300 ms):
  // una consulta por pausa, no por pulsación.
  const buscarEnServidor = React.useEffectEvent((q: string) =>
    buscarTexto(q || undefined)
  )
  const enServidor = servidor !== undefined
  const qServidor = servidor ? (servidor.q ?? "") : globalFilter
  React.useEffect(() => {
    if (!enServidor || qValida(globalFilter) === qServidor) return
    const t = setTimeout(() => buscarEnServidor(qValida(globalFilter)), 300)
    return () => clearTimeout(t)
  }, [globalFilter, qServidor, enServidor])

  const modoServidor = servidor
    ? {
        manualPagination: true,
        manualSorting: true,
        manualFiltering: true,
        ...(servidor.haySiguiente !== undefined
          ? { pageCount: servidor.pagina + (servidor.haySiguiente ? 1 : 0) }
          : { rowCount: servidor.total ?? 0 }),
        onPaginationChange: (
          u: React.SetStateAction<{ pageIndex: number; pageSize: number }>
        ) => {
          const prev = {
            pageIndex: servidor.pagina - 1,
            pageSize: servidor.tamano,
          }
          const p = typeof u === "function" ? u(prev) : u
          servidor.onCambio({ pagina: p.pageIndex + 1 })
        },
      }
    : {}

  const table = useReactTable({
    data: filas,
    columns: cols,
    state: {
      sorting: servidor ? deOrden(servidor.orden) : sorting,
      globalFilter,
      rowSelection,
      ...(servidor && {
        pagination: {
          pageIndex: servidor.pagina - 1,
          pageSize: servidor.tamano,
        },
      }),
    },
    onSortingChange: setSorting,
    onGlobalFilterChange: setGlobalFilter,
    onRowSelectionChange: setRowSelection,
    getRowId: getRowId && ((row) => getRowId(row)),
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getPaginationRowModel:
      paginar || servidor ? getPaginationRowModel() : undefined,
    globalFilterFn: (row, _id, value: string) => {
      const q = value.toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "")
      return row.getAllCells().some((c) =>
        String(c.getValue() ?? "")
          .toLowerCase()
          .normalize("NFD")
          .replace(/[̀-ͯ]/g, "")
          .includes(q)
      )
    },
    initialState: { pagination: { pageSize } },
    ...modoServidor,
  })

  const seleccionadas = table.getSelectedRowModel().rows.map((r) => r.original)
  const total = servidor
    ? servidor.total
    : table.getFilteredRowModel().rows.length
  const { pageIndex, pageSize: tamano } = table.getState().pagination
  const paginas =
    total === undefined ? undefined : Math.max(1, Math.ceil(total / tamano))
  const vacia = table.getRowModel().rows.length === 0

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-2">
      {/* La línea separa los filtros de los resultados. */}
      {(buscar !== undefined ||
        !!toolbar ||
        (!!seleccion && seleccionadas.length > 0)) && (
        <div className="mb-1 flex flex-wrap items-end gap-2 border-b pb-3">
          {buscar !== undefined && (
            <CampoBuscar etiqueta={buscarEtiqueta}>
              <InputGroup className={cn("table-search w-72", buscarClassName)}>
                <InputGroupAddon>
                  <SearchIcon />
                </InputGroupAddon>
                <InputGroupInput
                  {...buscarInputProps}
                  // Con lo elegido detrás, el texto ocupa solo lo que mide (en
                  // monoespaciada, 1ch por carácter más el relleno) y el resto
                  // queda para el detalle.
                  className={cn(
                    buscarDetalle && "max-w-1/2 flex-none font-mono"
                  )}
                  style={
                    buscarDetalle
                      ? {
                          width: `calc(${Math.max(globalFilter.length, 3)}ch + 1rem)`,
                        }
                      : undefined
                  }
                  placeholder={buscar}
                  autoComplete="off"
                  aria-label={buscarEtiqueta ?? buscar}
                  value={globalFilter}
                  onChange={(e) => {
                    const q = e.target.value
                    setGlobalFilter(q)
                    setAviso("")
                    // Al borrar, los resultados se vacían sin esperar.
                    if (servidor && !q.trim()) buscarTexto("")
                  }}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" && onBuscarIntro) {
                      e.preventDefault()
                      onBuscarIntro(globalFilter.trim(), e)
                      return
                    }
                    // Intro busca sin esperar (con el mínimo de caracteres) y,
                    // al llegar los resultados, elige la fila exacta.
                    const q = qValida(globalFilter)
                    if (e.key === "Enter" && q) {
                      e.preventDefault()
                      setAviso("")
                      setIntro({
                        q,
                        teclas: { ctrlKey: e.ctrlKey, metaKey: e.metaKey },
                      })
                      if (servidor) buscarTexto(q)
                    }
                  }}
                />
                {buscarDetalle && (
                  <InputGroupAddon
                    align="inline-end"
                    className="min-w-0 flex-1 justify-start font-normal"
                  >
                    {buscarDetalle}
                  </InputGroupAddon>
                )}
              </InputGroup>
            </CampoBuscar>
          )}
          {toolbar}
          {seleccion && seleccionadas.length > 0 && (
            <div className="ml-auto flex items-center gap-2">
              <span className="text-sm text-muted-foreground">
                {seleccionadas.length} seleccionadas
              </span>
              {seleccion(seleccionadas, () => setRowSelection({}))}
            </div>
          )}
        </div>
      )}
      {cabecera && (
        // -mb-2 anula el hueco del contenedor: forma un marco con la tabla.
        <div
          className={cn(
            "-mb-2 rounded-t-lg border border-b-0 bg-card px-2 py-1.5",
            busquedaPendiente && "invisible"
          )}
        >
          {cabecera}
        </div>
      )}
      <div
        aria-busy={servidor?.cargando}
        // El contenedor del Table no hace scroll propio: la barra horizontal
        // es la de este marco y se ve siempre, sin bajar al final de la tabla.
        className={cn(
          "min-h-0 flex-1 overflow-auto border bg-card transition-opacity [&>[data-slot=table-container]]:overflow-visible",
          cabecera ? "rounded-b-lg" : "rounded-lg",
          servidor?.cargando && "opacity-60",
          busquedaPendiente && "invisible"
        )}
      >
        <Table className="pointer-coarse:text-base">
          <TableHeader className="sticky top-0 z-[1] bg-muted">
            {table.getHeaderGroups().map((hg) => (
              <TableRow key={hg.id} className="hover:bg-transparent">
                {hg.headers.map((h) => {
                  const dir = h.column.getIsSorted()
                  const meta = h.column.columnDef.meta as
                    { num?: boolean } | undefined
                  const restringida =
                    servidor?.ordenables !== undefined &&
                    servidor.campos.includes(h.column.id) &&
                    !servidor.ordenables.includes(h.column.id)
                  const ordenable =
                    h.column.getCanSort() &&
                    !restringida &&
                    (!servidor || servidor.campos.includes(h.column.id))
                  return (
                    <TableHead
                      key={h.id}
                      style={{
                        width: h.column.columnDef.size
                          ? h.getSize()
                          : undefined,
                      }}
                      className={cn(
                        "h-8 text-xs font-semibold tracking-wide text-muted-foreground uppercase",
                        meta?.num && "text-right",
                        h.column.id === columnaOrdenada &&
                          "bg-ring/15 text-foreground shadow-[inset_0_-2px_0_var(--ring)]"
                      )}
                    >
                      {h.isPlaceholder ? null : ordenable ? (
                        <button
                          type="button"
                          className={cn(
                            "inline-flex items-center gap-1 hover:text-foreground",
                            meta?.num && "flex-row-reverse"
                          )}
                          onClick={
                            servidor
                              ? () =>
                                  servidor.onCambio({
                                    orden: siguienteOrden(
                                      h.column.id,
                                      !!meta?.num,
                                      servidor.orden
                                    ),
                                    pagina: 1,
                                  })
                              : h.column.getToggleSortingHandler()
                          }
                        >
                          {flexRender(
                            h.column.columnDef.header,
                            h.getContext()
                          )}
                          {dir === "asc" ? (
                            <ArrowUpIcon className="size-3" />
                          ) : dir === "desc" ? (
                            <ArrowDownIcon className="size-3" />
                          ) : null}
                        </button>
                      ) : (
                        <span
                          title={
                            restringida
                              ? "Filtra la tabla para poder ordenar por esta columna"
                              : undefined
                          }
                        >
                          {flexRender(
                            h.column.columnDef.header,
                            h.getContext()
                          )}
                        </span>
                      )}
                    </TableHead>
                  )
                })}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {table.getRowModel().rows.map((row) => (
              <TableRow
                key={row.id}
                data-state={row.getIsSelected() ? "selected" : undefined}
                className={cn(
                  "h-[34px] pointer-coarse:h-[52px]",
                  onRowClick && "cursor-pointer",
                  rowClassName?.(row.original)
                )}
                onClick={(e) => onRowClick?.(row.original, e)}
              >
                {row.getVisibleCells().map((cell) => {
                  const meta = cell.column.columnDef.meta as
                    { num?: boolean } | undefined
                  return (
                    <TableCell
                      key={cell.id}
                      className={cn(
                        "py-1.5",
                        meta?.num && "text-right",
                        cell.column.id === columnaOrdenada && "bg-ring/5"
                      )}
                    >
                      {flexRender(
                        cell.column.columnDef.cell,
                        cell.getContext()
                      )}
                    </TableCell>
                  )
                })}
              </TableRow>
            ))}
          </TableBody>
          {pie && (
            <TableFooter className="sticky bottom-0 z-[1] bg-muted">
              {table.getFooterGroups().map((grupo) => (
                <TableRow key={grupo.id}>
                  {grupo.headers.map((h) => (
                    <TableCell
                      key={h.id}
                      className={cn(
                        "font-semibold",
                        (
                          h.column.columnDef.meta as
                            { num?: boolean } | undefined
                        )?.num && "text-right"
                      )}
                    >
                      {h.isPlaceholder
                        ? null
                        : flexRender(h.column.columnDef.footer, h.getContext())}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableFooter>
          )}
        </Table>
        {vacia && (
          <Empty className="py-10">
            {emptyContent ?? (
              <EmptyHeader>
                <EmptyTitle>Sin resultados</EmptyTitle>
                <EmptyDescription>
                  Prueba con otra búsqueda o quita filtros.
                </EmptyDescription>
              </EmptyHeader>
            )}
          </Empty>
        )}
      </div>
      {(paginar || servidor) && (
        <div
          className={cn(
            "flex items-center gap-4 text-sm text-muted-foreground",
            busquedaPendiente && "invisible"
          )}
        >
          <span className="mr-auto" role="status" aria-live="polite">
            {servidor?.cargando ? (
              "Buscando…"
            ) : aviso ? (
              <span className="text-warning">{aviso}</span>
            ) : filtrando ? (
              <>
                {filas.length} coincidencias exactas ·{" "}
                <button
                  type="button"
                  className="underline underline-offset-2 hover:text-foreground"
                  onClick={() => setSoloExactas(null)}
                >
                  Ver todas
                </button>
              </>
            ) : (
              <>
                {total !== undefined
                  ? `${num(total)} ${total === 1 ? "registro" : "registros"}`
                  : servidor?.masDe !== undefined
                    ? `Más de ${num(servidor.masDe)} registros`
                    : `${data.length} registros en esta página`}
                {busquedaPendiente !== undefined &&
                  (total !== undefined || servidor?.masDe !== undefined) &&
                  " encontrados"}
                {servidor?.ordenables &&
                  " · filtra la tabla para ordenar por más columnas"}
              </>
            )}
          </span>
          {servidor && columnaOrdenada && (
            <span>
              Ordenado por {nombreColumna(columnaOrdenada).toLowerCase()}{" "}
              {servidor.orden?.startsWith("-") ? "↓" : "↑"} ·{" "}
              <button
                type="button"
                className="underline underline-offset-2 hover:text-foreground"
                onClick={() =>
                  servidor.onCambio({ orden: ordenInicial, pagina: 1 })
                }
              >
                Quitar orden
              </button>
            </span>
          )}
          <div className="flex items-center gap-2">
            <span>Filas por página</span>
            <Select
              items={TAMANOS_PAGINA.map((t) => ({
                value: t,
                label: String(t),
              }))}
              value={table.getState().pagination.pageSize}
              onValueChange={(t) =>
                servidor
                  ? servidor.onCambio({ tamano: Number(t), pagina: 1 })
                  : table.setPageSize(Number(t))
              }
            >
              <SelectTrigger
                size="sm"
                className="w-20"
                aria-label="Filas por página"
              >
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TAMANOS_PAGINA.map((t) => (
                  <SelectItem key={t} value={t}>
                    {t}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          {table.getPageCount() > 1 && (
            <div className="flex items-center gap-2">
              <span>
                Página {pageIndex + 1}
                {paginas !== undefined && ` de ${paginas}`}
              </span>
              <Button
                variant="outline"
                size="icon-sm"
                aria-label="Página anterior"
                disabled={!table.getCanPreviousPage()}
                onClick={() => table.previousPage()}
              >
                <ChevronLeftIcon />
              </Button>
              <Button
                variant="outline"
                size="icon-sm"
                aria-label="Página siguiente"
                disabled={servidor?.cargando || !table.getCanNextPage()}
                onClick={() => table.nextPage()}
              >
                <ChevronRightIcon />
              </Button>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
