import type { DockviewPanelApi } from "dockview-react"
import { useImpresionPaneles } from "@/core/impresion-paneles"

// Dockview elige por uso reciente al cerrar. Aquí se conserva la contigüidad
// visual: siguiente a la derecha, o anterior si era la última del grupo.
export function cerrarPanelContiguo(api: DockviewPanelApi) {
  const activo = useImpresionPaneles.getState().panelActivo === api.id
  const paneles = api.group.panels
  const indice = paneles.findIndex((p) => p.id === api.id)
  const contiguo =
    indice < 0 ? undefined : (paneles[indice + 1] ?? paneles[indice - 1])
  // Se activa antes de cerrar: si no, Dockview pinta primero la pestaña usada
  // más recientemente y luego se cambia a la contigua (dos repintados).
  if (activo) contiguo?.api.setActive()
  api.close()
  // Sin contiguo, Dockview activa el grupo restante al retirar el grupo vacío.
}
