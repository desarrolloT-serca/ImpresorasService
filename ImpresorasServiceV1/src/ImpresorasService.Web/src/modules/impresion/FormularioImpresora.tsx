import { DatosImpresora, type Impresora, type Tienda } from "@serca/contratos"

import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useGuardarImpresora } from "./api"
import { nombreTienda } from "./formato"
import { Campo, DialogoFormulario } from "./formulario"

// Alta o edición de una impresora (impresoras/form.blade.php del PHP).
export function FormularioImpresora({
  impresora,
  tienda,
  tiendas,
  onCerrar,
}: {
  // Sin impresora, alta; en la tienda elegida si la hay.
  impresora?: Impresora
  tienda?: number
  tiendas: readonly Tienda[]
  onCerrar: () => void
}) {
  const guardar = useGuardarImpresora()
  const activas = tiendas.filter(
    (t) => t.isActive || t.storeId === impresora?.storeId
  )
  const items = activas.map((t) => ({
    value: String(t.storeId),
    label: nombreTienda(t.storeId, tiendas),
  }))
  return (
    <DialogoFormulario
      titulo={impresora ? `Editar ${impresora.printerName}` : "Nueva impresora"}
      aviso={impresora ? "Impresora guardada" : "Impresora creada"}
      esquema={DatosImpresora}
      inicial={{
        printerName: impresora?.printerName ?? "",
        spoolQueue: impresora?.spoolQueue ?? "",
        host: impresora?.host ?? "",
        storeId: impresora?.storeId ?? tienda,
        isActive: impresora?.isActive ?? true,
        capabilitiesJson: impresora?.capabilitiesJson ?? null,
      }}
      guardar={(datos) =>
        guardar.mutateAsync({ id: impresora?.printerId, datos })
      }
      onCerrar={onCerrar}
    >
      {({ valores, errores, enviando, cambiar, id }) => (
        <>
          <Campo
            id={id("printerName")}
            etiqueta="Nombre"
            error={errores.printerName}
          >
            <Input
              id={id("printerName")}
              autoFocus
              value={valores.printerName}
              disabled={enviando}
              aria-invalid={!!errores.printerName}
              onChange={(e) => cambiar("printerName", e.target.value)}
            />
          </Campo>
          <Campo id={id("storeId")} etiqueta="Tienda" error={errores.storeId}>
            <Select
              items={items}
              value={
                valores.storeId === undefined ? null : String(valores.storeId)
              }
              disabled={enviando}
              onValueChange={(v) => cambiar("storeId", Number(v))}
            >
              <SelectTrigger
                id={id("storeId")}
                className="w-full"
                aria-invalid={!!errores.storeId}
              >
                <SelectValue placeholder="Elige una tienda" />
              </SelectTrigger>
              <SelectContent>
                {items.map((i) => (
                  <SelectItem key={i.value} value={i.value}>
                    {i.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Campo>
          <Campo
            id={id("host")}
            etiqueta="IP"
            error={errores.host}
            ayuda="Se imprime primero por aquí (IPP), sin cola ni driver. También sirve para comprobar la conexión."
          >
            <Input
              id={id("host")}
              className="font-mono"
              value={valores.host}
              disabled={enviando}
              aria-invalid={!!errores.host}
              onChange={(e) => cambiar("host", e.target.value)}
            />
          </Campo>
          <Campo
            id={id("spoolQueue")}
            etiqueta="Cola de Windows (alternativa)"
            error={errores.spoolQueue}
            ayuda="Opcional si hay IP: se usa cuando la impresora no acepta PDF por IP o no responde. Nombre exacto en el servidor del Worker (Get-Printer)."
          >
            <Input
              id={id("spoolQueue")}
              className="font-mono"
              value={valores.spoolQueue}
              disabled={enviando}
              aria-invalid={!!errores.spoolQueue}
              onChange={(e) => cambiar("spoolQueue", e.target.value)}
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
