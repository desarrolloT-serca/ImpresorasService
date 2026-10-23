import { createContext, useContext, useEffect, type RefObject } from "react"
import type { ComponentType, LazyExoticComponent } from "react"
import type { LucideIcon } from "lucide-react"

// Contrato que cumple cada módulo del frontend. El shell solo conoce esto:
// añadir un módulo es crear su carpeta y registrarlo en registro.ts.
export type PropsPanel = { clave: string }

export type DefPanel = {
  titulo: string
  icon: LucideIcon
  // Carga diferida: el código de un módulo solo se descarga al abrirlo.
  componente: LazyExoticComponent<ComponentType<PropsPanel>>
  // Pantalla de detalle de una consulta: Esc la cierra y vuelve a esta. Sin
  // padre, Esc llama a lo que la pantalla registre con useEsc.
  padre?: IdPanel
}

// Aportación de un módulo al buscador universal (Ctrl + K). Recibe el texto
// ya con espera aplicada y pinta sus resultados como grupo de la paleta.
export type PropsBuscador = {
  q: string
  ir: (panel: IdPanel, clave: string, titulo: string) => void
}

// Grupos del menú: las ventanas de «Selección de procesos» del ERP actual.
// "Consultas": pantallas de búsqueda y consulta (tablas con DataTable).
// Impresión: el grupo «Impresión» reúne todas las pantallas de ese módulo
// (decisión del usuario, 23/10/2026); al integrarlo, el ERP lo añade aquí.
export type GrupoMenu =
  | "General"
  | "Consultas"
  | "Ventas"
  | "Inventarios"
  | "Compras / Pedidos"
  | "Impresión"

export type ModuloWeb = {
  id: string
  // Permiso necesario para ver el módulo en el menú y el buscador.
  permiso?: string
  paneles: Record<string, DefPanel>
  // programa: "aplicación/programa" del ERP actual (admon.permisos) al que
  // sustituye la entrada; sin él (Inicio) la ven todos.
  menu?: { grupo: GrupoMenu; panel: string; programa?: string }[]
  buscador?: LazyExoticComponent<ComponentType<PropsBuscador>>
}

// Un panel se identifica como "<módulo>.<panel>", por ejemplo "clientes.ficha".
export type IdPanel = `${string}.${string}`

export type AbrirOpts = { titulo?: string; derecha?: boolean }
export type Abrir = (panel: IdPanel, clave?: string, opts?: AbrirOpts) => void

export const WorkspaceCtx = createContext<{
  abrir: Abrir
  renombrar?: (panel: IdPanel, clave: string, titulo: string) => void
}>({ abrir: () => {} })
export const useWorkspace = () => useContext(WorkspaceCtx)
// Id único de Dockview (incluye la clave de la ficha); permite registrar capacidades.
export const PanelIdCtx = createContext<string | null>(null)

// Ctrl/Cmd + clic abre en un panel a la derecha (comparar dos fichas, etc.)
export const derecha = (e: { ctrlKey: boolean; metaKey: boolean }) => ({
  derecha: e.ctrlKey || e.metaKey,
})

// Esc en una pantalla sin padre: vuelve a empezar en la misma pestaña (quita
// lo elegido y los resultados; los filtros se quedan), sin abrir otra.
export const EscCtx = createContext<RefObject<(() => void) | null> | null>(null)
export function useEsc(alPulsar: () => void) {
  const ref = useContext(EscCtx)
  // Cada render deja la versión actual (con el estado de ese render).
  useEffect(() => {
    if (!ref) return
    ref.current = alPulsar
    return () => {
      ref.current = null
    }
  })
}
