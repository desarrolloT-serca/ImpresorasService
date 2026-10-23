import { useId, useRef, useState, type ReactNode } from "react"
import { toast } from "sonner"
import type { z } from "zod"

import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog"
import { ErrorApi } from "@/core/api"

export type Errores = Record<string, string>

// Alta o edición en un diálogo, con el patrón de Cambiar contraseña del ERP
// (workspace/CambiarContrasena.tsx): valores en estado, Zod antes de enviar,
// errores por campo, un solo envío a la vez y aviso al descartar lo escrito.
// El padre lo monta para abrirlo y lo desmonta en onCerrar.
export function DialogoFormulario<V extends Record<string, unknown>, S extends z.ZodType>({
  titulo,
  inicial,
  esquema,
  guardar,
  aviso,
  onCerrar,
  children,
}: {
  titulo: string
  inicial: V
  esquema: S
  guardar: (datos: z.output<S>) => Promise<unknown>
  // Mensaje al guardar bien.
  aviso: string
  onCerrar: () => void
  children: (f: {
    valores: V
    errores: Errores
    enviando: boolean
    cambiar: <K extends keyof V>(clave: K, valor: V[K]) => void
    id: (clave: keyof V) => string
  }) => ReactNode
}) {
  const [valores, setValores] = useState(inicial)
  const [errores, setErrores] = useState<Errores>({})
  const [mensaje, setMensaje] = useState("")
  const [enviando, setEnviando] = useState(false)
  const bloqueo = useRef(false)
  const base = useId()
  const cambiado = JSON.stringify(valores) !== JSON.stringify(inicial)
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (open || bloqueo.current) return
        if (cambiado && !window.confirm("¿Descartar los cambios?")) return
        onCerrar()
      }}
    >
      <DialogContent className="sm:max-w-lg">
        <DialogTitle>{titulo}</DialogTitle>
        <form
          noValidate
          className="space-y-4"
          onSubmit={async (e) => {
            e.preventDefault()
            if (bloqueo.current) return
            const r = esquema.safeParse(valores)
            if (!r.success) {
              const nuevos: Errores = {}
              for (const i of r.error.issues) nuevos[String(i.path[0])] ??= i.message
              setErrores(nuevos)
              return
            }
            bloqueo.current = true
            setEnviando(true)
            setMensaje("")
            try {
              await guardar(r.data)
              toast.success(aviso)
              onCerrar()
            } catch (error) {
              setMensaje(
                error instanceof ErrorApi
                  ? error.problema.title
                  : "Error de red. Puedes reintentar"
              )
            } finally {
              bloqueo.current = false
              setEnviando(false)
            }
          }}
        >
          <div className="space-y-3">
            {children({
              valores,
              errores,
              enviando,
              cambiar: (clave, valor) => {
                setValores((v) => ({ ...v, [clave]: valor }))
                setErrores((v) => ({ ...v, [clave as string]: "" }))
              },
              id: (clave) => `${base}-${String(clave)}`,
            })}
          </div>
          <p role="status" aria-live="polite" className="text-sm text-destructive">
            {mensaje}
          </p>
          <div className="flex justify-end gap-2">
            <Button type="submit" size="sm" disabled={enviando}>
              {enviando ? "Guardando…" : "Guardar"}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={enviando}
              onClick={() =>
                (!cambiado || window.confirm("¿Descartar los cambios?")) &&
                onCerrar()
              }
            >
              Cancelar
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  )
}

// Etiqueta encima, el control y su error debajo.
export function Campo({
  id,
  etiqueta,
  error,
  ayuda,
  children,
}: {
  id: string
  etiqueta: string
  error?: string
  ayuda?: string
  children: ReactNode
}) {
  return (
    <div className="grid gap-1">
      <label htmlFor={id} className="text-sm font-medium">
        {etiqueta}
      </label>
      {children}
      {error ? (
        <p aria-live="polite" className="text-xs text-destructive">
          {error}
        </p>
      ) : (
        ayuda && <p className="text-xs text-muted-foreground">{ayuda}</p>
      )}
    </div>
  )
}
