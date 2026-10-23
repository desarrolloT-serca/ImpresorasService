import * as React from "react"
import type { DockviewApi } from "dockview-react"
import {
  ChevronsUpDownIcon,
  LogOutIcon,
  MoonIcon,
  SunIcon,
} from "lucide-react"

import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarRail,
  SidebarTrigger,
} from "@/components/ui/sidebar"
import { Toaster } from "@/components/ui/sonner"
import { TooltipProvider } from "@/components/ui/tooltip"
import { useTheme } from "@/components/theme-provider"
import { useImpresionPaneles } from "@/core/impresion-paneles"
import { ROLES, useCerrarSesion, useSesion } from "@/core/sesion"
import { Escritorio } from "@/workspace/Escritorio"
import { WorkspaceCtx, type Abrir, type IdPanel } from "@/workspace/modulo"
import { PANELES, menu } from "@/workspace/registro"

// Contenedor propio de la aplicación, copiado del shell del ERP (app/App.tsx)
// sin lo que aquí no aplica: sociedad, almacén, perfil, buscador, fichar.
// Dentro del ERP no se usa: el módulo se abre en su escritorio.
export function App() {
  const sesion = useSesion()
  const cerrarSesion = useCerrarSesion()
  const apiRef = React.useRef<DockviewApi | null>(null)
  const activo = useImpresionPaneles((s) => s.pantallaActiva)
  const [pestanas, setPestanas] = React.useState(0)
  const { theme, setTheme } = useTheme()
  const grupos = menu(sesion.permisos)
  const tienda =
    sesion.storeId === null ? "Todas las tiendas" : `Tienda ${sesion.storeId}`

  const abrir = React.useCallback<Abrir>((panel, clave, opts = {}) => {
    const api = apiRef.current
    const def = PANELES.get(panel)
    if (!api || !def) return
    const id = clave ? `${panel}:${clave}` : panel
    const existente = api.getPanel(id)
    if (existente) {
      existente.api.setActive()
      return
    }
    const ref = api.activePanel
    api.addPanel({
      id,
      component: "panel",
      title: opts.titulo ?? (clave ? `${def.titulo} ${clave}` : def.titulo),
      params: { panel, clave },
      ...(opts.derecha && ref
        ? { position: { referencePanel: ref, direction: "right" as const } }
        : {}),
    })
  }, [])

  const renombrar = React.useCallback(
    (panel: IdPanel, clave: string, titulo: string) => {
      const id = clave ? `${panel}:${clave}` : panel
      const existente = apiRef.current?.getPanel(id)
      if (existente && existente.api.title !== titulo)
        existente.api.setTitle(titulo)
    },
    []
  )

  // Estable: un valor nuevo en cada render volvería a pintar todas las pantallas.
  const workspace = React.useMemo(
    () => ({ abrir, renombrar }),
    [abrir, renombrar]
  )

  const onReady = (api: DockviewApi) => {
    apiRef.current = api
    const sync = () => setPestanas(api.panels.length)
    api.onDidAddPanel(sync)
    api.onDidRemovePanel(sync)
    sync()
    const primera = grupos[0]?.items[0]?.id
    if (!api.panels.length && primera) abrir(primera)
  }

  return (
    <WorkspaceCtx.Provider value={workspace}>
      <TooltipProvider>
        <SidebarProvider className="h-svh overflow-hidden">
          <Sidebar collapsible="icon">
            <SidebarHeader>
              <SidebarMenu>
                <SidebarMenuItem>
                  <SidebarMenuButton size="lg">
                    <div className="grid flex-1 gap-1 text-left leading-tight">
                      {/* Plegada, el recorte del botón deja ver solo «ad» */}
                      <img
                        src="/ad-serca.png"
                        alt="AD Serca"
                        className="h-[22px] w-auto max-w-none group-data-[collapsible=icon]:h-[18px]"
                      />
                      <span className="truncate text-xs group-data-[collapsible=icon]:hidden">
                        Impresión
                      </span>
                    </div>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              </SidebarMenu>
            </SidebarHeader>
            <SidebarContent>
              {grupos.map((g) => (
                <SidebarGroup key={g.grupo}>
                  <SidebarGroupLabel>{g.grupo}</SidebarGroupLabel>
                  <SidebarGroupContent>
                    <SidebarMenu>
                      {g.items.map(({ id, titulo, icon: Icon }) => (
                        <SidebarMenuItem key={id}>
                          <SidebarMenuButton
                            isActive={activo === id}
                            tooltip={titulo}
                            onClick={(e) =>
                              abrir(id, undefined, {
                                derecha: e.ctrlKey || e.metaKey,
                              })
                            }
                          >
                            <Icon />
                            <span>{titulo}</span>
                          </SidebarMenuButton>
                        </SidebarMenuItem>
                      ))}
                    </SidebarMenu>
                  </SidebarGroupContent>
                </SidebarGroup>
              ))}
            </SidebarContent>
            <SidebarFooter>
              <SidebarMenu>
                <SidebarMenuItem>
                  <DropdownMenu>
                    <DropdownMenuTrigger
                      render={<SidebarMenuButton size="lg" />}
                    >
                      <div className="grid flex-1 text-left leading-tight group-data-[collapsible=icon]:hidden">
                        <span className="truncate font-medium text-sidebar-accent-foreground">
                          {sesion.displayName}
                        </span>
                        <span className="truncate text-xs">{tienda}</span>
                      </div>
                      <ChevronsUpDownIcon className="ml-auto group-data-[collapsible=icon]:mx-auto" />
                    </DropdownMenuTrigger>
                    <DropdownMenuContent
                      side="right"
                      align="end"
                      className="w-56"
                    >
                      <DropdownMenuGroup>
                        <DropdownMenuLabel>
                          {sesion.displayName} · {ROLES[sesion.role]}
                        </DropdownMenuLabel>
                      </DropdownMenuGroup>
                      <DropdownMenuSeparator />
                      <DropdownMenuGroup>
                        <DropdownMenuItem
                          onClick={() =>
                            setTheme(theme === "dark" ? "light" : "dark")
                          }
                        >
                          {theme === "dark" ? <SunIcon /> : <MoonIcon />}
                          {theme === "dark" ? "Tema claro" : "Tema oscuro"}
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          disabled={cerrarSesion.isPending}
                          onClick={() => cerrarSesion.mutate()}
                        >
                          <LogOutIcon />
                          Cerrar sesión
                        </DropdownMenuItem>
                      </DropdownMenuGroup>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </SidebarMenuItem>
              </SidebarMenu>
            </SidebarFooter>
            <SidebarRail />
          </Sidebar>

          <SidebarInset className="min-h-0 overflow-hidden">
            <header className="toolbar-principal flex h-12 shrink-0 items-center gap-2 border-b bg-card px-3">
              <SidebarTrigger />
            </header>

            <Escritorio usuario={sesion.login} onReady={onReady} />

            <footer className="flex h-7 shrink-0 items-center gap-4 border-t bg-muted px-3 text-xs text-muted-foreground">
              <span>{pestanas} pestañas</span>
              <span className="ml-auto hidden md:inline">
                Esc en una ficha vuelve a su listado · Ctrl + clic abre a la
                derecha · arrastra una pestaña para dividir · clic central
                cierra
              </span>
            </footer>
          </SidebarInset>
        </SidebarProvider>
        <Toaster position="bottom-right" />
      </TooltipProvider>
    </WorkspaceCtx.Provider>
  )
}
