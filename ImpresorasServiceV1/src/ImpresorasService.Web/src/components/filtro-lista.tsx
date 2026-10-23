import * as React from "react"
import { ChevronDownIcon } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Command,
  CommandEmpty,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { normalizar, num } from "@/lib/format"
import { cn } from "@/lib/utils"

// Filtro con su nombre encima: el mismo aspecto en todas las consultas.
export function CampoFiltro({
  etiqueta,
  className,
  children,
}: {
  etiqueta: string
  className?: string
  children: React.ReactNode
}) {
  return (
    <div className={cn("grid gap-1", className)}>
      <span className="text-[11px] font-medium text-muted-foreground">
        {etiqueta}
      </span>
      {children}
    </div>
  )
}

export type Opcion = { cod: string; nombre: string }

// ponytail: cmdk no virtualiza y con miles de opciones pintadas tardaba en
// abrir y en filtrar al teclear. Se pintan como mucho estas; si hace falta
// recorrer la lista entera con la rueda, virtualizar.
const MAX_OPCIONES = 100

// Selector múltiple con buscador por código o nombre (familias, marcas,
// proveedores…). Sin ninguna marcada no se filtra.
export function FiltroLista({
  id,
  "aria-labelledby": etiquetaId,
  opciones,
  value,
  onChange,
  todas,
  plural,
  buscar,
  className,
  multiple = true,
}: {
  id?: string
  "aria-labelledby"?: string
  opciones: Opcion[] | undefined
  value: string[]
  onChange: (v: string[]) => void
  // Texto sin nada marcado ("Todas las familias") y para varias ("familias").
  todas: string
  plural: string
  buscar: string
  className?: string
  multiple?: boolean
}) {
  const indice = React.useMemo(
    () =>
      (opciones ?? []).map((o) => ({
        ...o,
        clave: normalizar(`${o.cod} ${o.nombre}`),
      })),
    [opciones]
  )
  const [texto, setTexto] = React.useState("")
  const [abierto, setAbierto] = React.useState(false)
  // Las marcadas al abrir van arriba; no se reordenan mientras está abierto.
  const [arriba, setArriba] = React.useState<Set<string>>(new Set())
  const t = normalizar(texto.trim())
  const coincidencias = t
    ? indice.filter((o) => o.clave.includes(t))
    : [
        ...indice.filter((o) => arriba.has(o.cod)),
        ...indice.filter((o) => !arriba.has(o.cod)),
      ]
  const marcadas = new Set(value)
  const alternar = (cod: string) => {
    if (!multiple) {
      onChange(marcadas.has(cod) ? [] : [cod])
      setAbierto(false)
      return
    }
    onChange(
      marcadas.has(cod) ? value.filter((c) => c !== cod) : [...value, cod]
    )
  }
  const elegida =
    value.length === 1 ? indice.find((o) => o.cod === value[0]) : undefined
  const etiqueta =
    value.length === 0
      ? todas
      : value.length === 1
        ? !multiple && elegida && elegida.cod !== elegida.nombre
          ? `${elegida.cod} · ${elegida.nombre}`
          : (elegida?.nombre ?? value[0])
        : `${value.length} ${plural}`

  return (
    <Popover
      open={abierto}
      onOpenChange={(abierto) => {
        setAbierto(abierto)
        setTexto("")
        if (abierto) setArriba(new Set(value))
      }}
    >
      <PopoverTrigger
        render={
          <Button
            id={id}
            aria-labelledby={etiquetaId}
            variant="outline"
            className={cn(
              "w-48 justify-between font-normal",
              value.length > 0 && "border-primary/60",
              className
            )}
          />
        }
      >
        <span
          className={cn("truncate", !value.length && "text-muted-foreground")}
        >
          {etiqueta}
        </span>
        <ChevronDownIcon className="text-muted-foreground" />
      </PopoverTrigger>
      <PopoverContent align="start" className="w-96 p-0">
        <Command shouldFilter={false}>
          <CommandInput
            placeholder={buscar}
            value={texto}
            onValueChange={setTexto}
          />
          <CommandList>
            <CommandEmpty>Sin resultados.</CommandEmpty>
            {!t && (
              <CommandItem
                value="todas"
                data-checked={value.length === 0}
                onSelect={() => {
                  onChange([])
                  if (!multiple) setAbierto(false)
                }}
              >
                {todas}
              </CommandItem>
            )}
            {coincidencias.slice(0, MAX_OPCIONES).map((o) => (
              <CommandItem
                key={o.cod}
                value={o.cod}
                data-checked={marcadas.has(o.cod)}
                onSelect={() => alternar(o.cod)}
              >
                {o.cod !== o.nombre && (
                  <span className="font-mono text-muted-foreground">
                    {o.cod}
                  </span>
                )}
                <span className="truncate">{o.nombre}</span>
              </CommandItem>
            ))}
            {coincidencias.length > MAX_OPCIONES && (
              <p className="px-2 py-1.5 text-xs text-muted-foreground">
                {num(coincidencias.length - MAX_OPCIONES)} más: escribe un
                código o nombre para verlas.
              </p>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
