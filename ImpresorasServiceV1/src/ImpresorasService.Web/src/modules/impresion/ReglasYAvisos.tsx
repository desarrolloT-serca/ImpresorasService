import { useState, type ComponentType } from "react"

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
  C: ComponentType<PropsPanel>
}[] = [
  { id: "reglas", nombre: "Reglas de enrutado", C: Reglas },
  { id: "umbrales", nombre: "Umbrales de salud", C: Umbrales },
  { id: "telegram", nombre: "Alertas por Telegram", C: Telegram },
  { id: "pruebas", nombre: "Pruebas", C: Pruebas },
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
      {/* El subrayado de la sección activa cae sobre la línea de abajo. */}
      <div className="border-b px-4 pt-2">
        <TabsList variant="line" aria-label="Sección">
          {SECCIONES.map((s) => (
            <TabsTrigger key={s.id} value={s.id} className="px-2.5">
              {s.nombre}
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
