import type { LucideIcon } from "lucide-react"
import { tienePermiso } from "@serca/contratos"

import type { DefPanel, IdPanel, ModuloWeb } from "@/workspace/modulo"
import { transicion } from "@/app/transicion"
import { impresion, impresionConfiguracion, impresionControl } from "@/modules/impresion"

// Módulos de esta aplicación. Dentro del ERP se registran los dos de
// impresión en su workspace/registro.ts; transicion y este fichero se tiran.
export const MODULOS: ModuloWeb[] = [impresion, impresionControl, impresionConfiguracion, transicion]

export const PANELES = new Map<IdPanel, DefPanel>(
  MODULOS.flatMap((m) =>
    Object.entries(m.paneles).map(
      ([id, def]) => [`${m.id}.${id}` as IdPanel, def] as const
    )
  )
)

export type EntradaMenu = { id: IdPanel; titulo: string; icon: LucideIcon }

// Pantallas que el usuario puede abrir, por grupo y en orden de registro.
// Sin las pendientes ni los programas del ERP antiguo: aquí no los hay.
export function menu(
  permisos: readonly string[]
): { grupo: string; items: EntradaMenu[] }[] {
  const items = MODULOS.filter(
    (m) => !m.permiso || tienePermiso(permisos, m.permiso)
  ).flatMap((m) =>
    (m.menu ?? []).map((i) => ({
      grupo: i.grupo as string,
      id: `${m.id}.${i.panel}` as IdPanel,
      titulo: m.paneles[i.panel].titulo,
      icon: m.paneles[i.panel].icon,
    }))
  )
  return [...new Set(items.map((i) => i.grupo))].map((grupo) => ({
    grupo,
    items: items
      .filter((i) => i.grupo === grupo)
      .map(({ id, titulo, icon }) => ({ id, titulo, icon })),
  }))
}
