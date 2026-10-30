import { useState, type ComponentType } from "react"
import {
  FlaskConicalIcon,
  GaugeIcon,
  RouteIcon,
  SendIcon,
  type LucideIcon,
} from "lucide-react"

import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import type { PropsPanel } from "@/workspace/modulo"
import Pruebas from "./Pruebas"
import Reglas from "./Reglas"
import Telegram from "./Telegram"
import Umbrales from "./Umbrales"

// Lo que solo toca quien administra, en una pestaña del escritorio en vez de
// cuatro (decisión del usuario, 26/10/2026). No se llama «Configuración» para
// no confundirla con la del ERP; el nombre lo eligió el usuario. La clave elige la sección y pasa el resto:
// «seccion=reglas&tienda=12» abre las reglas de la tienda 12.
const SECCIONES: {
  id: string
  nombre: string
  Icono: LucideIcon
  C: ComponentType<PropsPanel>
}[] = [
  { id: "reglas", nombre: "Reglas de enrutado", Icono: RouteIcon, C: Reglas },
  {
    id: "umbrales",
    nombre: "Umbrales de salud",
    Icono: GaugeIcon,
    C: Umbrales,
  },
  {
    id: "telegram",
    nombre: "Alertas por Telegram",
    Icono: SendIcon,
    C: Telegram,
  },
  { id: "pruebas", nombre: "Pruebas", Icono: FlaskConicalIcon, C: Pruebas },
]

export default function ReglasYAvisos({ clave }: PropsPanel) {
  const [seccion, setSeccion] = useState(() => {
    const s = new URLSearchParams(clave).get("seccion")
    return SECCIONES.some((x) => x.id === s) ? s! : SECCIONES[0].id
  })
  // keepMounted: cambiar de sección no pierde lo que se está editando ni una
  // prueba en marcha.
  return (
    <Tabs
      value={seccion}
      onValueChange={setSeccion}
      className="h-full min-h-0 gap-0"
    >
      {/* Pestañas en cápsula, como las del Extracto contable del ERP
          (decisión del usuario, 27/10/2026: el subrayado no parecía un menú). */}
      <div className="border-b px-4 py-2">
        <TabsList aria-label="Sección">
          {SECCIONES.map(({ id, nombre, Icono }) => (
            <TabsTrigger key={id} value={id} className="px-3">
              <Icono />
              {nombre}
            </TabsTrigger>
          ))}
        </TabsList>
      </div>
      {SECCIONES.map(({ id, C }) => (
        <TabsContent key={id} value={id} keepMounted className="min-h-0">
          <C clave={clave} />
        </TabsContent>
      ))}
    </Tabs>
  )
}
