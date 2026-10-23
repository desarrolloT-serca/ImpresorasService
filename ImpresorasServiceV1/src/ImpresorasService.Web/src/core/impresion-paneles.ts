import { create } from "zustand"
import type { IdPanel } from "@/workspace/modulo"

export type CapacidadImpresion = {
  imprimir: () => void
  disabled: boolean
  motivo: string
  mensaje: string
}
// Panel activo del escritorio: su id de dockview (único por pestaña) y qué
// pantalla es. Lo mantiene Escritorio; lo leen la barra y la impresión.
type ImpresionPaneles = {
  panelActivo: string | null
  pantallaActiva: IdPanel | null
  capacidades: Record<string, CapacidadImpresion>
  activar: (id: string | null, pantalla?: IdPanel | null) => void
  registerPrintHandler: (
    id: string,
    capacidad: CapacidadImpresion
  ) => () => void
}
// Estado transitorio: ni funciones ni ids de pestañas se guardan en localStorage.
export const useImpresionPaneles = create<ImpresionPaneles>((set) => ({
  panelActivo: null,
  pantallaActiva: null,
  capacidades: {},
  activar: (panelActivo, pantallaActiva = null) =>
    set({ panelActivo, pantallaActiva }),
  registerPrintHandler: (id, capacidad) => {
    set((s) => ({ capacidades: { ...s.capacidades, [id]: capacidad } }))
    return () =>
      set((s) => {
        if (s.capacidades[id] !== capacidad) return s
        const capacidades = { ...s.capacidades }
        delete capacidades[id]
        return { capacidades }
      })
  },
}))
// Único mapa: conserva Ctrl+K, Ctrl+clic y Alt+flechas.
const ATAJOS = new Set(["mod+l", "mod+shift+y"])
export function ejecutarAtajoImpresion(e: KeyboardEvent) {
  if (
    !(e.ctrlKey || e.metaKey) ||
    e.altKey ||
    !ATAJOS.has(`mod+${e.shiftKey ? "shift+" : ""}${e.key.toLowerCase()}`)
  )
    return
  const { panelActivo, capacidades } = useImpresionPaneles.getState()
  const capacidad = panelActivo ? capacidades[panelActivo] : undefined
  if (!capacidad) return
  e.preventDefault()
  e.stopPropagation()
  if (!e.repeat && !capacidad.disabled) capacidad.imprimir()
}
