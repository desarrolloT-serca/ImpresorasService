import { useMemo, useState } from "react"
import type { ColumnDef } from "@tanstack/react-table"
import { PlusIcon } from "lucide-react"
import { toast } from "sonner"
import { z } from "zod"

import { DataTable } from "@/components/data-table"
import { Panel } from "@/components/ficha"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { ErrorApi } from "@/core/api"
import { cn } from "@/lib/utils"
import { useEsc } from "@/workspace/modulo"
import { CLASE_TONO } from "@/modules/impresion/formato"
import { Campo, DialogoFormulario } from "@/modules/impresion/formulario"
import { useBorrarTienda, useGuardarTienda, useTiendasResumen } from "./api"
import { DatosTienda, type TiendaResumen } from "./contratos"

const error = (e: unknown, defecto: string) =>
  toast.error(e instanceof ErrorApi ? e.problema.title : defecto)

// Tiendas (tiendas/index.blade.php del PHP). El número es el código de
// almacén del ERP (cod_alm). Desactivar conserva el histórico y desactiva sus
// impresoras; eliminar es definitivo y borra también su histórico.
// Solo durante la transición: en el ERP las tiendas son sus almacenes.
export default function Tiendas() {
  const tiendas = useTiendasResumen()
  const guardar = useGuardarTienda()
  const borrar = useBorrarTienda()
  const [editando, setEditando] = useState<TiendaResumen | "nueva">()
  const [reinicio, setReinicio] = useState(0)
  useEsc(() => setReinicio((n) => n + 1))

  const columnas = useMemo<ColumnDef<TiendaResumen, unknown>[]>(
    () => [
      {
        id: "numero",
        header: "Nº",
        size: 70,
        accessorKey: "storeId",
        meta: { num: true },
        cell: ({ row: { original: t } }) => (
          <span className="block text-right font-mono">{t.storeId}</span>
        ),
      },
      {
        id: "nombre",
        header: "Nombre",
        size: 0,
        accessorKey: "name",
        cell: ({ row: { original: t } }) => (
          <span className="block truncate">{t.name}</span>
        ),
      },
      {
        id: "estado",
        header: "Estado",
        size: 100,
        cell: ({ row: { original: t } }) => (
          <Badge
            variant="outline"
            className={cn(
              "border-transparent",
              CLASE_TONO[t.isActive ? "ok" : "neutro"]
            )}
          >
            {t.isActive ? "Activa" : "Inactiva"}
          </Badge>
        ),
      },
      {
        id: "impresoras",
        header: () => <span className="tracking-normal normal-case">Impresoras</span>,
        size: 100,
        accessorKey: "printersCount",
        meta: { num: true },
        cell: ({ row: { original: t } }) => (
          <span className="block text-right tabular-nums">{t.printersCount}</span>
        ),
      },
      {
        id: "usuarios",
        header: () => <span className="tracking-normal normal-case">Usuarios</span>,
        size: 100,
        accessorKey: "usersCount",
        meta: { num: true },
        cell: ({ row: { original: t } }) => (
          <span className="block text-right tabular-nums">{t.usersCount}</span>
        ),
      },
      {
        id: "acciones",
        header: "",
        size: 260,
        enableSorting: false,
        cell: ({ row: { original: t } }) => (
          <div className="flex justify-end gap-1">
            <Button size="xs" variant="outline" onClick={() => setEditando(t)}>
              Editar
            </Button>
            {t.isActive ? (
              <Button
                size="xs"
                variant="outline"
                onClick={() =>
                  window.confirm(
                    `Se desactivará ${t.name} y sus impresoras. Se conservan los registros y el histórico.`
                  ) &&
                  borrar.mutate(
                    { id: t.storeId, definitivo: false },
                    {
                      onSuccess: (r) => toast.success(r.message),
                      onError: (e) => error(e, "No se pudo desactivar"),
                    }
                  )
                }
              >
                Desactivar
              </Button>
            ) : (
              <Button
                size="xs"
                variant="outline"
                onClick={() =>
                  window.confirm(`${t.name} volverá a estar disponible para enrutado.`) &&
                  guardar.mutate(
                    { nueva: false, datos: { ...t, isActive: true } },
                    {
                      onSuccess: () => toast.success("Tienda activada"),
                      onError: (e) => error(e, "No se pudo activar"),
                    }
                  )
                }
              >
                Activar
              </Button>
            )}
            <Button
              size="xs"
              variant="destructive"
              onClick={() =>
                // Como el PHP: hay que escribir el número para confirmar.
                window.prompt(
                  `Esto borrará también el histórico de impresión de ${t.name} (#${t.storeId}) y no se puede deshacer. Escribe ${t.storeId} para confirmar.`
                ) === String(t.storeId) &&
                borrar.mutate(
                  { id: t.storeId, definitivo: true },
                  {
                    onSuccess: (r) => toast.success(r.message),
                    onError: (e) => error(e, "No se pudo eliminar"),
                  }
                )
              }
            >
              Eliminar
            </Button>
          </div>
        ),
      },
    ],
    // guardar.mutate y borrar.mutate son estables.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    []
  )

  return (
    <Panel className="[&_table]:min-w-[44rem] [&_table]:table-fixed">
      <DataTable
        key={reinicio}
        columns={columnas}
        data={tiendas.data ?? []}
        paginar={false}
        buscar="Número o nombre"
        buscarEtiqueta="Buscar tienda"
        emptyContent={tiendas.isPending ? undefined : "No hay tiendas."}
        toolbar={
          <Button
            size="sm"
            className="ml-auto"
            onClick={() => setEditando("nueva")}
          >
            <PlusIcon data-icon="inline-start" />
            Nueva tienda
          </Button>
        }
      />
      {editando && (
        <FormularioTienda
          tienda={editando === "nueva" ? undefined : editando}
          onCerrar={() => setEditando(undefined)}
        />
      )}
    </Panel>
  )
}

// El número se escribe como texto y se convierte al validar: solo dígitos
// («1e3» no es 1000).
const FormTienda = DatosTienda.extend({
  storeId: z
    .string()
    .trim()
    .regex(/^\d{1,9}$/, "Solo dígitos")
    .transform(Number),
})

function FormularioTienda({
  tienda,
  onCerrar,
}: {
  tienda?: TiendaResumen
  onCerrar: () => void
}) {
  const guardar = useGuardarTienda()
  return (
    <DialogoFormulario
      titulo={tienda ? `Editar ${tienda.name}` : "Nueva tienda"}
      aviso={tienda ? "Tienda guardada" : "Tienda creada"}
      esquema={FormTienda}
      inicial={{
        storeId: tienda ? String(tienda.storeId) : "",
        name: tienda?.name ?? "",
        isActive: tienda?.isActive ?? true,
      }}
      guardar={(datos) => guardar.mutateAsync({ nueva: !tienda, datos })}
      onCerrar={onCerrar}
    >
      {({ valores, errores, enviando, cambiar, id }) => (
        <>
          <Campo
            id={id("storeId")}
            etiqueta="Número"
            error={errores.storeId}
            ayuda={tienda ? undefined : "El código de almacén del ERP (cod_alm). No se puede cambiar después."}
          >
            <Input
              id={id("storeId")}
              inputMode="numeric"
              className="w-32 font-mono"
              autoFocus={!tienda}
              disabled={enviando || !!tienda}
              value={valores.storeId}
              aria-invalid={!!errores.storeId}
              onChange={(e) => cambiar("storeId", e.target.value)}
            />
          </Campo>
          <Campo id={id("name")} etiqueta="Nombre" error={errores.name}>
            <Input
              id={id("name")}
              autoFocus={!!tienda}
              value={valores.name}
              disabled={enviando}
              aria-invalid={!!errores.name}
              onChange={(e) => cambiar("name", e.target.value)}
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
      )}
    </DialogoFormulario>
  )
}
