import { lazy } from "react"
import { StoreIcon, UsersIcon } from "lucide-react"
import { PERMISOS_IMPRESION as P } from "@serca/contratos"
import type { ModuloWeb } from "@/workspace/modulo"

// Pantallas solo de la transición, fuera del módulo: en el ERP los usuarios
// son sus operadores (fdopera) y las tiendas, sus almacenes (fdalmacen), así
// que no van con él. Mientras tanto, en el mismo grupo que el resto.
export const transicion: ModuloWeb = {
  id: "transicion",
  permiso: P.administrar,
  paneles: {
    tiendas: {
      titulo: "Tiendas",
      icon: StoreIcon,
      componente: lazy(() => import("./Tiendas")),
    },
    usuarios: {
      titulo: "Usuarios",
      icon: UsersIcon,
      componente: lazy(() => import("./Usuarios")),
    },
  },
  menu: [
    { grupo: "Impresión", panel: "tiendas" },
    { grupo: "Impresión", panel: "usuarios" },
  ],
}
