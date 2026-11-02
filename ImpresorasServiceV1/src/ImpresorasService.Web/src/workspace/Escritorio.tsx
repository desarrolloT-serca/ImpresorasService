import * as React from "react"
import {
  DockviewReact,
  themeLight,
  type DockviewApi,
  type IDockviewHeaderActionsProps,
  type IDockviewPanelHeaderProps,
  type IDockviewPanelProps,
} from "dockview-react"
import {
  FileQuestionIcon,
  Maximize2Icon,
  Minimize2Icon,
  XIcon,
} from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/components/ui/empty"
import { Kbd } from "@/components/ui/kbd"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { cn } from "@/lib/utils"
import {
  EscCtx,
  PanelIdCtx,
  useWorkspace,
  type IdPanel,
} from "@/workspace/modulo"
import { useImpresionPaneles } from "@/core/impresion-paneles"
import { PANELES } from "@/workspace/registro"
import { PanelSeguro } from "@/workspace/PanelSeguro"
import { cerrarPanelContiguo } from "./cerrar-panel"

type Params = { panel: IdPanel; clave?: string }

function Contenido({ panel, clave }: Params) {
  const def = PANELES.get(panel)
  if (!def)
    return (
      <Empty className="h-full">
        <EmptyHeader>
          <EmptyTitle>Pantalla no disponible</EmptyTitle>
          <EmptyDescription>
            «{panel}» ya no existe en esta versión. Puedes cerrar la pestaña.
          </EmptyDescription>
        </EmptyHeader>
      </Empty>
    )
  const C = def.componente
  return <C clave={clave ?? ""} />
}

// dockview recibe un único componente de panel; qué pantalla es va en los parámetros.
// Las pantallas toman el foco al activarse, para que Esc funcione sin clicar
// antes: en una de detalle (con padre) cierra la pestaña y vuelve a su
// listado; en las demás, hace lo que registren con useEsc. Solo si la tecla
// nace dentro del panel, no en un desplegable o diálogo encima.
export function PanelContenido({ params, api }: IDockviewPanelProps<Params>) {
  const { abrir } = useWorkspace()
  const padre = PANELES.get(params.panel)?.padre
  const ref = React.useRef<HTMLDivElement>(null)
  const esc = React.useRef<(() => void) | null>(null)
  React.useEffect(() => {
    // Si el foco ya está dentro (se ha pulsado un campo), se deja ahí.
    const enfocar = () => {
      if (!ref.current?.contains(document.activeElement))
        ref.current?.focus({ preventScroll: true })
    }
    if (api.isActive) enfocar()
    return api.onDidActiveChange((e) => e.isActive && enfocar()).dispose
  }, [api])
  const activar = () => {
    // setActive() del panel vuelve a insertar su contenido y pierde el foco
    // del input. Si ya está visible en su grupo, activar solo el grupo.
    if (api.group.activePanel?.id === api.id) {
      if (!api.isGroupActive) api.group.api.setActive()
    } else api.setActive()
  }
  return (
    <div
      ref={ref}
      tabIndex={-1}
      className="h-full bg-background outline-none"
      onPointerDownCapture={activar}
      onFocusCapture={activar}
      onKeyDown={(e) => {
        if (
          e.key !== "Escape" ||
          e.defaultPrevented ||
          !e.currentTarget.contains(e.target as Node)
        )
          return
        if (padre) {
          e.preventDefault()
          api.close()
          abrir(padre)
        } else if (esc.current) {
          e.preventDefault()
          esc.current()
          // Los listados remontan el buscador al reiniciar: tras el commit,
          // recuperar el foco permite seguir usando Esc sin volver a clicar.
          queueMicrotask(() => {
            if (api.isActive && !ref.current?.contains(document.activeElement))
              ref.current?.focus({ preventScroll: true })
          })
        }
      }}
    >
      <PanelIdCtx.Provider value={api.id}>
        <EscCtx.Provider value={esc}>
          <PanelSeguro>
            <Contenido {...params} />
          </PanelSeguro>
        </EscCtx.Provider>
      </PanelIdCtx.Provider>
    </div>
  )
}
const components = { panel: PanelContenido }

export function Pestana({
  api,
  params,
  tabLocation,
}: IDockviewPanelHeaderProps<Params>) {
  const Icon = PANELES.get(params.panel)?.icon ?? FileQuestionIcon
  // Se lee siempre el título actual: la ficha puede cambiarlo antes de que
  // se monte la pestaña y no debe quedarse el antiguo del layout guardado.
  const titulo = React.useSyncExternalStore(
    (cambio) => api.onDidTitleChange(cambio).dispose,
    () => api.title
  )
  // Un título nuevo cambia el ancho de la pestaña sin redimensionar la barra.
  React.useEffect(() => {
    if (tabLocation === "headerOverflow") return
    const id = requestAnimationFrame(() => api.group.header.refreshOverflow())
    return () => cancelAnimationFrame(id)
  }, [titulo, tabLocation, api])
  const activo = useImpresionPaneles((s) => s.panelActivo === api.id)
  const cerrar = (e: React.MouseEvent<HTMLElement>) => {
    e.stopPropagation()
    cerrarPanelContiguo(api)
    // El desplegable de pestañas que no caben es una copia que dockview no
    // repinta: se quita la fila de la cerrada y sigue abierto; si se queda
    // vacío, se cierra (con Escape, como lo cierra dockview).
    if (tabLocation !== "headerOverflow") return
    const fila = e.currentTarget.closest(".dv-tab")
    const lista = fila?.parentElement
    fila?.remove()
    if (lista && !lista.querySelector(".dv-tab"))
      lista.ownerDocument.defaultView?.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Escape" })
      )
  }
  return (
    <div
      className={cn(
        "group flex h-full items-center gap-1.5 pr-1 pl-3",
        activo && "serca-tab-activa"
      )}
      data-panel-activo={activo}
      onAuxClick={(e) => {
        if (e.button === 1) cerrar(e)
      }}
    >
      <Icon className="size-3.5 shrink-0 text-muted-foreground" />
      <span className="max-w-48 truncate">{titulo}</span>
      <button
        type="button"
        aria-label={`Cerrar ${titulo}`}
        className="ml-1 flex size-6 items-center justify-center rounded-sm opacity-0 group-hover:opacity-100 hover:bg-accent focus-visible:opacity-100 [.dv-active-tab_&]:opacity-60"
        // Dockview activa la pestaña al pulsar (antes del clic): así cerrar
        // otra no la enseña un instante. En captura, porque React delega en
        // la raíz y el aviso de dockview en la pestaña llegaría antes.
        onPointerDownCapture={(e) => e.preventDefault()}
        onClick={cerrar}
      >
        <XIcon className="size-3" />
      </button>
    </div>
  )
}

function AccionesGrupo({
  api,
  containerApi,
  panels,
  activePanel,
}: IDockviewHeaderActionsProps) {
  const [max, setMax] = React.useState(api.isMaximized())
  React.useEffect(
    () =>
      containerApi.onDidMaximizedGroupChange(() => setMax(api.isMaximized()))
        .dispose,
    [api, containerApi]
  )
  if (!panels.length) return null
  return (
    <div className="flex h-full items-center gap-0.5 px-1">
      <Tooltip>
        <TooltipTrigger
          render={
            <Button
              variant="ghost"
              size="icon-xs"
              onClick={() => (max ? api.exitMaximized() : api.maximize())}
            />
          }
        >
          {max ? <Minimize2Icon /> : <Maximize2Icon />}
        </TooltipTrigger>
        <TooltipContent>
          {max ? "Restaurar" : "Maximizar este grupo"}
        </TooltipContent>
      </Tooltip>
      <Tooltip>
        <TooltipTrigger
          render={
            <Button
              variant="ghost"
              size="icon-xs"
              // La visible, la última: cerrarla antes haría pintar cada
              // pestaña del grupo solo para cerrarla después.
              onClick={() =>
                [
                  ...panels.filter((p) => p !== activePanel),
                  ...(activePanel ? [activePanel] : []),
                ].forEach((p) => cerrarPanelContiguo(p.api))
              }
            />
          }
        >
          <XIcon />
        </TooltipTrigger>
        <TooltipContent>Cerrar todas las pestañas del grupo</TooltipContent>
      </Tooltip>
    </div>
  )
}

function Vacio() {
  return (
    <Empty className="h-full">
      <EmptyHeader>
        <EmptyTitle>No hay pestañas abiertas</EmptyTitle>
        <EmptyDescription>
          Abre un módulo desde el menú lateral o busca cualquier cosa.
        </EmptyDescription>
      </EmptyHeader>
      <EmptyContent className="flex-row justify-center gap-1 text-sm text-muted-foreground">
        <Kbd>Ctrl</Kbd> + <Kbd>K</Kbd> para buscar
      </EmptyContent>
    </Empty>
  )
}

// ponytail: el layout se guarda en este navegador por usuario. El plan pide
// guardarlo en servidor por usuario y puesto; cambiar solo leer/guardar.
// El almacenamiento puede estar lleno o bloqueado: nunca rompe el escritorio.
const leer = (clave: string) => {
  try {
    return JSON.parse(localStorage.getItem(clave) ?? "null")
  } catch {
    return null
  }
}

// Al arrastrar, el layout cambia decenas de veces por segundo: se guarda
// cuando se deja de mover.
function guardarConEspera(clave: string, api: DockviewApi) {
  let t: ReturnType<typeof setTimeout> | undefined
  return () => {
    clearTimeout(t)
    t = setTimeout(() => {
      try {
        localStorage.setItem(clave, JSON.stringify(api.toJSON()))
      } catch {
        // ponytail: sin aviso; si se pierde el layout se abre Inicio.
      }
    }, 300)
  }
}

const refrescarDesborde = (api: DockviewApi) =>
  api.groups.forEach((g) => g.header.refreshOverflow())

export function Escritorio({
  usuario,
  onReady,
  className,
}: {
  usuario: string
  onReady: (api: DockviewApi) => void
  className?: string
}) {
  const clave = `serca.escritorio.${usuario}`
  React.useEffect(() => () => useImpresionPaneles.getState().activar(null), [])
  return (
    <div className={cn("serca-dock min-h-0 flex-1", className)}>
      <DockviewReact
        theme={themeLight}
        components={components}
        defaultTabComponent={Pestana}
        rightHeaderActionsComponent={AccionesGrupo}
        watermarkComponent={Vacio}
        onReady={({ api }) => {
          const guardado = leer(clave)
          if (guardado)
            try {
              api.fromJSON(guardado)
            } catch {
              api.clear()
            }
          api.onDidLayoutChange(guardarConEspera(clave, api))
          // dockview solo recalcula las pestañas que no caben al redimensionar
          // o desplazar la barra: al abrir o cerrar una, el desplegable se
          // quedaba con la lista anterior y repetía pestañas visibles.
          // Un cambio dispara varios avisos: un solo recálculo por fotograma.
          let pendiente = 0
          api.onDidLayoutChange(() => {
            cancelAnimationFrame(pendiente)
            pendiente = requestAnimationFrame(() => refrescarDesborde(api))
          })
          const syncActivo = () =>
            useImpresionPaneles
              .getState()
              .activar(
                api.activePanel?.id ?? null,
                api.activePanel?.params?.panel ?? null
              )
          api.onDidActivePanelChange(syncActivo)
          api.onDidAddPanel(syncActivo)
          api.onDidRemovePanel(syncActivo)
          syncActivo()
          onReady(api)
        }}
      />
    </div>
  )
}
