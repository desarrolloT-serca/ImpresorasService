import { lazy } from "react"
import {
  FlaskConicalIcon,
  GaugeIcon,
  LayoutDashboardIcon,
  ListOrderedIcon,
  PrinterIcon,
  RouteIcon,
  SendIcon,
} from "lucide-react"
import { PERMISOS_IMPRESION as P } from "@serca/contratos"
import type { ModuloWeb } from "@/workspace/modulo"

// Cola de impresión de las tiendas. Escrito como un módulo del ERP
// (apps/web/src/modules): para integrarlo se copia esta carpeta y se registran
// sus dos manifiestos en su workspace/registro.ts. No importa nada de src/app.
// Todas sus pantallas van en el grupo «Impresión» del menú. Son dos
// manifiestos porque cada uno da un solo permiso: la operación, a quien lee;
// la configuración, solo a quien administra. El menú los junta por grupo.
// ponytail: sin programa del ERP antiguo hasta el día de integrarlo (su
// catálogo PANTALLAS aún no admite pantallas nuevas).
export const impresion: ModuloWeb = {
  id: "impresion",
  permiso: P.leer,
  paneles: {
    dashboard: {
      titulo: "Dashboard",
      icon: LayoutDashboardIcon,
      componente: lazy(() => import("./Dashboard")),
    },
    cola: {
      titulo: "Cola de impresión",
      icon: ListOrderedIcon,
      componente: lazy(() => import("./Cola")),
    },
    impresoras: {
      titulo: "Impresoras",
      icon: PrinterIcon,
      componente: lazy(() => import("./Impresoras")),
    },
  },
  menu: [
    { grupo: "Impresión", panel: "dashboard" },
    { grupo: "Impresión", panel: "cola" },
    { grupo: "Impresión", panel: "impresoras" },
  ],
}

export const impresionConfiguracion: ModuloWeb = {
  id: "impresionConfiguracion",
  permiso: P.administrar,
  paneles: {
    reglas: {
      titulo: "Reglas de enrutado",
      icon: RouteIcon,
      componente: lazy(() => import("./Reglas")),
    },
    umbrales: {
      titulo: "Umbrales de salud",
      icon: GaugeIcon,
      componente: lazy(() => import("./Umbrales")),
    },
    telegram: {
      titulo: "Alertas por Telegram",
      icon: SendIcon,
      componente: lazy(() => import("./Telegram")),
    },
    pruebas: {
      titulo: "Pruebas",
      icon: FlaskConicalIcon,
      componente: lazy(() => import("./Pruebas")),
    },
  },
  menu: [
    { grupo: "Impresión", panel: "reglas" },
    { grupo: "Impresión", panel: "umbrales" },
    { grupo: "Impresión", panel: "telegram" },
    { grupo: "Impresión", panel: "pruebas" },
  ],
}
