import { useMemo, useState } from "react"
import type { ColumnDef } from "@tanstack/react-table"
import { PlusIcon } from "lucide-react"
import { toast } from "sonner"
import { z } from "zod"
import { DatosRegla, type ReglaEnrutado, type Tienda } from "@serca/contratos"

import { DataTable } from "@/components/data-table"
import { Panel } from "@/components/ficha"
import { CampoFiltro } from "@/components/filtro-lista"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
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
import { useEsc, type PropsPanel } from "@/workspace/modulo"
import {
  useBorrarRegla,
  useGuardarRegla,
  useImpresoras,
  useReglas,
  useTiendas,
} from "./api"
import { CLASE_TONO, nombreTienda } from "./formato"
import { Campo, DialogoFormulario } from "./formulario"

const TODAS = "todas"
const GLOBAL = "global"
const nombreDe = (storeId: number | null, tiendas: readonly Tienda[]) =>
  storeId === null ? "Global" : nombreTienda(storeId, tiendas)

// Reglas de enrutado (reglas/index.blade.php del PHP): a qué impresora va
// cada trabajo según tienda, tipo de documento y canal. La tienda se elige en
// un selector, como en Impresoras; «Globales» son las que valen para todas.
export default function Reglas({ clave }: PropsPanel) {
  const reglas = useReglas()
  const tiendas = useTiendas().data ?? []
  const borrar = useBorrarRegla()
  // Abierta desde el dashboard, la clave trae la tienda: «tienda=12».
  const [tienda, setTienda] = useState(
    () =>
      new URLSearchParams(clave).get("tienda")?.match(/^\d{1,9}$/)?.[0] ?? TODAS
  )
  const [editando, setEditando] = useState<ReglaEnrutado | "nueva">()
  const [reinicio, setReinicio] = useState(0)
  useEsc(() => setReinicio((n) => n + 1))

  const lista = reglas.data ?? []
  const visibles =
    tienda === TODAS
      ? lista
      : lista.filter((r) => String(r.storeId ?? GLOBAL) === tienda)
  const items = [
    { value: TODAS, label: "Todas" },
    { value: GLOBAL, label: "Globales" },
    ...tiendas
      .filter((t) => t.isActive)
      .map((t) => ({
        value: String(t.storeId),
        label: nombreTienda(t.storeId, tiendas),
      })),
  ]

  const columnas = useMemo<ColumnDef<ReglaEnrutado, unknown>[]>(
    () => [
      ...(tienda === TODAS
        ? [
            {
              id: "tienda",
              header: "Tienda",
              size: 170,
              accessorFn: (r: ReglaEnrutado) => nombreDe(r.storeId, tiendas),
              cell: ({ getValue }) => (
                <span className="block truncate">{getValue<string>()}</span>
              ),
            } satisfies ColumnDef<ReglaEnrutado, unknown>,
          ]
        : []),
      {
        id: "prioridad",
        header: () => (
          <span className="tracking-normal normal-case">Prioridad</span>
        ),
        size: 90,
        accessorKey: "priority",
        meta: { num: true },
        cell: ({ row: { original: r } }) => (
          <span className="block text-right tabular-nums">{r.priority}</span>
        ),
      },
      {
        id: "tipo",
        header: "Tipo de documento",
        size: 160,
        accessorFn: (r) => r.documentType ?? "",
        cell: ({ row: { original: r } }) =>
          r.documentType ?? (
            <span className="text-muted-foreground">Cualquiera</span>
          ),
      },
      {
        id: "canal",
        header: "Canal",
        size: 130,
        accessorFn: (r) => r.channel ?? "",
        cell: ({ row: { original: r } }) =>
          r.channel ?? (
            <span className="text-muted-foreground">Cualquiera</span>
          ),
      },
      {
        id: "impresora",
        header: "Impresora",
        size: 0,
        accessorFn: (r) => r.printer?.printerName ?? `Impresora ${r.printerId}`,
        cell: ({ getValue }) => (
          <span className="block truncate">{getValue<string>()}</span>
        ),
      },
      {
        id: "activa",
        header: "Activa",
        size: 80,
        cell: ({ row: { original: r } }) => (
          <Badge
            variant="outline"
            className={cn(
              "border-transparent",
              CLASE_TONO[r.isActive ? "ok" : "neutro"]
            )}
          >
            {r.isActive ? "Sí" : "No"}
          </Badge>
        ),
      },
      {
        id: "acciones",
        header: "",
        size: 150,
        enableSorting: false,
        cell: ({ row: { original: r } }) => (
          <div className="flex justify-end gap-1">
            <Button size="xs" variant="outline" onClick={() => setEditando(r)}>
              Editar
            </Button>
            <Button
              size="xs"
              variant="destructive"
              onClick={() =>
                window.confirm(
                  `Se eliminará la regla de prioridad ${r.priority} (${r.documentType ?? "cualquier tipo"}, ${r.channel ?? "cualquier canal"}).`
                ) &&
                borrar.mutate(r.ruleId, {
                  onSuccess: () => toast.success("Regla eliminada"),
                  onError: (e) =>
                    toast.error(
                      e instanceof ErrorApi
                        ? e.problema.title
                        : "No se pudo eliminar"
                    ),
                })
              }
            >
              Eliminar
            </Button>
          </div>
        ),
      },
    ],
    // borrar.mutate es estable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [tienda, tiendas]
  )

  return (
    <Panel className="[&_table]:min-w-[56rem] [&_table]:table-fixed">
      <DataTable
        key={reinicio}
        columns={columnas}
        data={visibles}
        paginar={false}
        buscar="Tipo, canal o impresora"
        buscarEtiqueta="Buscar regla"
        emptyContent={reglas.isPending ? undefined : "No hay reglas."}
        toolbar={
          <>
            <CampoFiltro etiqueta="Tienda">
              <Select
                items={items}
                value={tienda}
                onValueChange={(v) => setTienda(v as string)}
              >
                <SelectTrigger className="w-64">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {items.map((i) => (
                    <SelectItem key={i.value} value={i.value}>
                      {i.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </CampoFiltro>
            <span className="ml-auto self-center text-xs text-muted-foreground">
              Gana la regla más específica; entre iguales, la de menor
              prioridad.
            </span>
            <Button size="sm" onClick={() => setEditando("nueva")}>
              <PlusIcon data-icon="inline-start" />
              Nueva regla
            </Button>
          </>
        }
      />
      {editando && (
        <FormularioRegla
          regla={editando === "nueva" ? undefined : editando}
          tienda={
            tienda === TODAS
              ? undefined
              : tienda === GLOBAL
                ? null
                : Number(tienda)
          }
          tiendas={tiendas}
          onCerrar={() => setEditando(undefined)}
        />
      )}
    </Panel>
  )
}

// Prioridad como texto mientras se escribe; se convierte al validar.
const FormRegla = DatosRegla.extend({
  priority: z
    .string()
    .trim()
    .regex(/^\d{1,9}$/, "Solo dígitos")
    .transform(Number),
})

function FormularioRegla({
  regla,
  tienda,
  tiendas,
  onCerrar,
}: {
  regla?: ReglaEnrutado
  // Para una nueva: la del selector (null = global).
  tienda?: number | null
  tiendas: readonly Tienda[]
  onCerrar: () => void
}) {
  const guardar = useGuardarRegla()
  const impresoras = useImpresoras().data ?? []
  const tiendaItems = [
    { value: GLOBAL, label: "Global (todas las tiendas)" },
    ...tiendas
      .filter((t) => t.isActive || t.storeId === regla?.storeId)
      .map((t) => ({
        value: String(t.storeId),
        label: nombreTienda(t.storeId, tiendas),
      })),
  ]
  return (
    <DialogoFormulario
      titulo={regla ? "Editar regla" : "Nueva regla"}
      aviso={regla ? "Regla guardada" : "Regla creada"}
      esquema={FormRegla}
      inicial={{
        priority: String(regla?.priority ?? 0),
        storeId: regla ? regla.storeId : (tienda ?? null),
        documentType: regla?.documentType ?? "",
        channel: regla?.channel ?? "",
        printerId: regla?.printerId as number | undefined,
        isActive: regla?.isActive ?? true,
      }}
      guardar={(datos) => guardar.mutateAsync({ regla, datos })}
      onCerrar={onCerrar}
    >
      {({ valores, errores, enviando, cambiar, id }) => {
        // Con tienda, solo sus impresoras (la API lo exige); global, todas.
        const disponibles = impresoras.filter(
          (i) => valores.storeId === null || i.storeId === valores.storeId
        )
        const impresoraItems = disponibles.map((i) => ({
          value: String(i.printerId),
          label:
            valores.storeId === null
              ? `${i.printerName} · ${nombreTienda(i.storeId, tiendas)}`
              : i.printerName,
        }))
        return (
          <>
            <Campo id={id("storeId")} etiqueta="Tienda" error={errores.storeId}>
              <Select
                items={tiendaItems}
                value={
                  valores.storeId === null ? GLOBAL : String(valores.storeId)
                }
                disabled={enviando}
                onValueChange={(v) => {
                  cambiar("storeId", v === GLOBAL ? null : Number(v))
                  cambiar("printerId", undefined)
                }}
              >
                <SelectTrigger id={id("storeId")} className="w-full">
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
            </Campo>
            <Campo
              id={id("printerId")}
              etiqueta="Impresora"
              error={errores.printerId}
            >
              <Select
                items={impresoraItems}
                value={
                  valores.printerId === undefined
                    ? null
                    : String(valores.printerId)
                }
                disabled={enviando}
                onValueChange={(v) => cambiar("printerId", Number(v))}
              >
                <SelectTrigger
                  id={id("printerId")}
                  className="w-full"
                  aria-invalid={!!errores.printerId}
                >
                  <SelectValue
                    placeholder={
                      disponibles.length
                        ? "Elige una impresora"
                        : "Esta tienda no tiene impresoras"
                    }
                  />
                </SelectTrigger>
                <SelectContent>
                  {impresoraItems.map((i) => (
                    <SelectItem key={i.value} value={i.value}>
                      {i.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Campo>
            <Campo
              id={id("documentType")}
              etiqueta="Tipo de documento"
              error={errores.documentType}
              ayuda="Vacío = cualquier tipo. Ej.: ALBARAN, TICKET, FACTURA."
            >
              <Input
                id={id("documentType")}
                value={valores.documentType}
                disabled={enviando}
                aria-invalid={!!errores.documentType}
                onChange={(e) => cambiar("documentType", e.target.value)}
              />
            </Campo>
            <Campo
              id={id("channel")}
              etiqueta="Canal"
              error={errores.channel}
              ayuda="Vacío = cualquier canal. Ej.: DEFAULT, MOSTRADOR, WEB."
            >
              <Input
                id={id("channel")}
                value={valores.channel}
                disabled={enviando}
                aria-invalid={!!errores.channel}
                onChange={(e) => cambiar("channel", e.target.value)}
              />
            </Campo>
            <Campo
              id={id("priority")}
              etiqueta="Prioridad"
              error={errores.priority}
              ayuda="Menor número = mayor prioridad."
            >
              <Input
                id={id("priority")}
                inputMode="numeric"
                className="w-32"
                value={valores.priority}
                disabled={enviando}
                aria-invalid={!!errores.priority}
                onChange={(e) => cambiar("priority", e.target.value)}
              />
            </Campo>
            <label className="flex items-center gap-2 text-sm">
              <Checkbox
                checked={valores.isActive}
                disabled={enviando}
                onCheckedChange={(v) => cambiar("isActive", !!v)}
              />
              Activa
            </label>
          </>
        )
      }}
    </DialogoFormulario>
  )
}
