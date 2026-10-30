import { useMemo, useState } from "react"
import type { ColumnDef } from "@tanstack/react-table"
import {
  MOTIVOS_REIMPRESION,
  PERMISOS_IMPRESION as P,
  type Reimpresion,
  TAMANOS_PAGINA,
  type Tienda,
  type VentanaDashboard,
} from "@serca/contratos"

import { DataTable } from "@/components/data-table"
import { Panel } from "@/components/ficha"
import { CampoFiltro } from "@/components/filtro-lista"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { usePermiso } from "@/core/sesion"
import type { PropsPanel } from "@/workspace/modulo"
import { useReimpresiones, useTiendas } from "./api"
import {
  ESTADOS,
  autor,
  documentoErp,
  fechaHora,
  nombreTienda,
  plural,
} from "./formato"
import { SinActualizar } from "./formulario"

// Los del dashboard; el inicio de cada uno lo calcula la API.
const PERIODOS: { value: VentanaDashboard; label: string }[] = [
  { value: "today", label: "Hoy" },
  { value: "7d", label: "7 días" },
  { value: "30d", label: "30 días" },
]
const SIN_TIENDAS: Tienda[] = []
const TODAS = "todas"
const TODOS = "todos"
const MOTIVOS = [
  { value: TODOS, label: "Todos" },
  ...Object.entries(MOTIVOS_REIMPRESION).map(([value, label]) => ({
    value,
    label,
  })),
]

// Registro de reimpresiones (decisión del usuario, 26/10/2026): cada copia de
// algo que ya había salido, con quién, cuándo y por qué, para que
// administración y los jefes de tienda comprueben que todo está en orden. La API
// da a cada uno su tienda; quien ve todas, todas o la del filtro. Abierto desde
// el dashboard, la clave trae su periodo y su tienda: «ventana=7d&tienda=12».
export default function Reimpresiones({ clave }: PropsPanel) {
  const verTodas = usePermiso(P.todasLasTiendas)
  const inicial = new URLSearchParams(clave)
  const [periodo, setPeriodo] = useState(
    () =>
      PERIODOS.find((p) => p.value === inicial.get("ventana")) ?? PERIODOS[0]
  )
  const [tienda, setTienda] = useState(() =>
    /^\d{1,9}$/.test(inicial.get("tienda") ?? "")
      ? Number(inicial.get("tienda"))
      : undefined
  )
  const [motivo, setMotivo] = useState(TODOS)
  // Por páginas en la API, como el resto de consultas del ERP (WEB-2). Al
  // cambiar un filtro se vuelve a la primera.
  const [pagina, setPagina] = useState(1)
  const [tamano, setTamano] = useState<number>(TAMANOS_PAGINA[0])
  const registro = useReimpresiones({
    tienda,
    ventana: periodo.value,
    motivo: motivo === TODOS ? undefined : motivo,
    pagina,
    tamano,
  })
  const tiendas = useTiendas().data ?? SIN_TIENDAS
  const filas = registro.data?.value ?? []
  const total = registro.data?.count ?? 0
  // De todo el periodo, aunque se filtre por motivo.
  const porMotivo = Object.entries(registro.data?.byReason ?? {}).map(
    ([cod, n]) =>
      [
        (MOTIVOS_REIMPRESION as Record<string, string>)[cod] ?? "Sin motivo",
        n,
      ] as const
  )
  const columnas = useMemo(() => columnasDe(tiendas), [tiendas])
  const tiendaItems = [
    { value: TODAS, label: "Todas" },
    ...tiendas
      .filter((t) => t.isActive)
      .map((t) => ({
        value: String(t.storeId),
        label: nombreTienda(t.storeId, tiendas),
      })),
  ]

  return (
    <Panel className="[&_table]:min-w-[86rem] [&_table]:table-fixed">
      <SinActualizar consultas={[registro]} />
      <div className="flex flex-wrap items-end gap-2 border-b pb-3">
        <CampoFiltro etiqueta="Periodo">
          <Select
            items={PERIODOS}
            value={periodo.value}
            onValueChange={(v) => {
              setPeriodo(PERIODOS.find((p) => p.value === v) ?? PERIODOS[0])
              setPagina(1)
            }}
          >
            <SelectTrigger className="w-32">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {PERIODOS.map((p) => (
                <SelectItem key={p.value} value={p.value}>
                  {p.label}
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
              onValueChange={(v) => {
                setTienda(v === TODAS ? undefined : Number(v))
                setPagina(1)
              }}
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
        <CampoFiltro etiqueta="Motivo">
          <Select
            items={MOTIVOS}
            value={motivo}
            onValueChange={(v) => {
              setMotivo(v as string)
              setPagina(1)
            }}
          >
            <SelectTrigger className="w-44">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {MOTIVOS.map((m) => (
                <SelectItem key={m.value} value={m.value}>
                  {m.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </CampoFiltro>
        <span className="ml-auto self-center text-xs text-muted-foreground">
          {plural(total, "reimpresión", "reimpresiones")}
          {porMotivo.length > 0 &&
            ` · ${porMotivo.map(([m, n]) => `${m} ${n}`).join(" · ")}`}
        </span>
      </div>
      <DataTable
        columns={columnas}
        data={filas}
        getRowId={(r) => String(r.eventId)}
        servidor={{
          // La API solo ordena por fecha: ninguna columna se puede ordenar.
          campos: [],
          total,
          pagina,
          tamano,
          cargando: registro.isFetching,
          onCambio: (c) => {
            if (c.tamano) {
              setTamano(c.tamano)
              setPagina(1)
            } else if (c.pagina) setPagina(c.pagina)
          },
        }}
        emptyContent={
          registro.isLoading
            ? "Cargando…"
            : "Ninguna reimpresión en el periodo."
        }
      />
    </Panel>
  )
}

function columnasDe(
  tiendas: readonly Tienda[]
): ColumnDef<Reimpresion, unknown>[] {
  return [
    {
      id: "cuando",
      header: "Cuándo",
      size: 140,
      cell: ({ row: { original: r } }) => fechaHora(r.occurredAtUtc),
    },
    {
      id: "documento",
      header: "Documento",
      size: 260,
      cell: ({ row: { original: r } }) => {
        const d = documentoErp(r.documentType, r.externalJobId)
        return (
          <span className="block truncate" title={r.externalJobId}>
            {d ? (
              `${d.nombre} ${r.externalJobId}`
            ) : (
              <span className="font-mono">{r.externalJobId}</span>
            )}
          </span>
        )
      },
    },
    {
      id: "tienda",
      header: "Tienda",
      size: 130,
      cell: ({ row: { original: r } }) => (
        <span className="block truncate">
          {nombreTienda(r.storeId, tiendas)}
        </span>
      ),
    },
    {
      // Elástica: el motivo se lleva el ancho que sobra y su nota, hasta tres
      // líneas, se lee sin abrir nada (decisión del usuario, 27/10/2026).
      id: "motivo",
      header: "Motivo",
      size: 0,
      cell: ({ row: { original: r } }) => (
        <span
          className="line-clamp-3 break-words whitespace-normal"
          title={r.note ?? undefined}
        >
          {r.reasonText ?? "—"}
          {r.note && <span className="text-muted-foreground"> · {r.note}</span>}
        </span>
      ),
    },
    {
      id: "operador",
      header: "Operador",
      size: 90,
      meta: { num: true },
      cell: ({ row: { original: r } }) => (
        <span
          className="block text-right tabular-nums"
          title={r.actor ?? undefined}
        >
          {autor(r.actor).codigo ?? "—"}
        </span>
      ),
    },
    {
      id: "quien",
      header: "Nombre",
      size: 180,
      cell: ({ row: { original: r } }) => {
        const nombre = autor(r.actor).nombre
        return (
          <span className="block truncate" title={nombre ?? undefined}>
            {nombre ?? "—"}
          </span>
        )
      },
    },
    {
      id: "antes",
      header: "Estaba",
      size: 110,
      cell: ({ row: { original: r } }) =>
        r.previousStatus === null ? "—" : ESTADOS[r.previousStatus].etiqueta,
    },
    {
      id: "impresora",
      header: "Impresora",
      size: 200,
      cell: ({ row: { original: r } }) => (
        <span className="block truncate">{r.printerName ?? "—"}</span>
      ),
    },
  ]
}
