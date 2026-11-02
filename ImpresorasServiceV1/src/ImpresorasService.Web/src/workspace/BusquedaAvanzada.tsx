import { useId, useRef, useState } from "react"
import { InfoIcon, ListFilterIcon, SearchIcon } from "lucide-react"
import { hoyMadrid } from "@serca/contratos"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { CampoFecha } from "@/components/campo-fecha"
import { FiltroLista } from "@/components/filtro-lista"
import {
  Dialog,
  DialogContent,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import {
  AVANZADOS,
  resumenAvanzados,
  useBusquedasAvanzadas,
  type BusquedaAvanzadaPanel,
  type Campo,
} from "@/core/avanzados"
import { useImpresionPaneles } from "@/core/impresion-paneles"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"

// Copia de la del ERP (apps/web/src/workspace/BusquedaAvanzada.tsx), sin el
// campo de cliente. Formulario de criterios de la pantalla activa, según sus
// campos en AVANZADOS.
function Formulario({
  cerrar,
  busqueda: { tipo, criterios, aplicar, campos: visibles, opciones },
}: {
  cerrar: () => void
  busqueda: BusquedaAvanzadaPanel
}) {
  const { esquema, campos: todos, ayuda: textoAyuda } = AVANZADOS[tipo]
  // Aquí solo hay los tipos de la cola: sin ensanchar, comparar con los demás
  // no compila.
  const campos = (todos as readonly Campo[]).filter(
    ([k]) => !visibles || visibles.includes(k)
  )
  const [valores, setValores] = useState<Record<string, unknown>>(() =>
    Object.fromEntries(campos.map(([k]) => [k, criterios[k] ?? ""]))
  )
  const [ayuda, setAyuda] = useState(false)
  const ayudaId = useId()
  const [error, setError] = useState("")
  const formulario = useRef<HTMLFormElement>(null)
  const cambiar = (k: string, v: unknown) =>
    setValores((s) => ({ ...s, [k]: v }))
  return (
    <form
      ref={formulario}
      onSubmit={(e) => {
        e.preventDefault()
        const r = esquema.safeParse(
          Object.fromEntries(
            Object.entries(valores).filter(([, v]) =>
              Array.isArray(v) ? v.length > 0 : String(v ?? "").length > 0
            )
          )
        )
        if (!r.success) {
          setError(
            r.error.issues
              .map(
                (i) =>
                  `${campos.find(([k]) => k === i.path[0])?.[1] ?? ""} ${i.message}`
              )
              .join(" · ")
          )
          return
        }
        aplicar(r.data)
        cerrar()
      }}
      className="space-y-4"
    >
      <div className="space-y-2">
        {campos.map(([k, label, , tipoCampo], i) => (
          <div
            key={k}
            className="grid grid-cols-[170px_minmax(0,1fr)] items-center gap-x-3 gap-y-1 max-[479px]:grid-cols-1"
          >
            {tipoCampo === "lista" ||
            tipoCampo === "textos" ||
            tipoCampo === "opcion" ||
            tipoCampo === "sn" ||
            tipoCampo === "fecha" ? (
              <span id={`avanzado-${k}-etiqueta`} className="whitespace-nowrap">
                {label}
              </span>
            ) : (
              <label htmlFor={`avanzado-${k}`} className="whitespace-nowrap">
                {label}
              </label>
            )}
            {tipoCampo === "lista" ||
            tipoCampo === "textos" ||
            tipoCampo === "opcion" ? (
              <FiltroLista
                id={`avanzado-${k}`}
                aria-labelledby={`avanzado-${k}-etiqueta`}
                opciones={opciones?.[k]}
                value={
                  tipoCampo !== "opcion"
                    ? Array.isArray(valores[k])
                      ? valores[k].map(String)
                      : []
                    : String(valores[k] ?? "")
                      ? [String(valores[k])]
                      : []
                }
                onChange={(v) =>
                  cambiar(
                    k,
                    tipoCampo === "lista"
                      ? v.map(Number)
                      : tipoCampo === "textos"
                        ? v
                        : (v[0] ?? "")
                  )
                }
                multiple={tipoCampo !== "opcion"}
                todas={
                  tipoCampo === "lista"
                    ? "Todas"
                    : tipoCampo === "textos"
                      ? "Todos"
                      : "Sin filtro"
                }
                plural="opciones"
                buscar="Código o nombre"
                className="w-full"
              />
            ) : tipoCampo === "sn" ? (
              <select
                id={`avanzado-${k}`}
                aria-labelledby={`avanzado-${k}-etiqueta`}
                value={String(valores[k] ?? "")}
                onChange={(e) => cambiar(k, e.target.value)}
                className="h-[30px] w-full rounded-md border border-input bg-background px-2 focus-visible:outline-ring"
              >
                <option value="">Todos</option>
                <option>S</option>
                <option>N</option>
              </select>
            ) : tipoCampo === "fecha" ? (
              <CampoFecha
                etiqueta={label.replace(/:$/, "")}
                permitirVacio
                max={hoyMadrid()}
                value={String(valores[k] ?? "")}
                onChange={(v) => cambiar(k, v)}
                className="w-full"
              />
            ) : (
              <Input
                id={`avanzado-${k}`}
                autoFocus={i === 0}
                value={String(valores[k] ?? "")}
                className="h-[30px] w-full rounded-md"
                onChange={(e) => cambiar(k, e.target.value)}
              />
            )}
          </div>
        ))}
      </div>
      {ayuda && (
        <Alert id={ayudaId} role="note">
          <InfoIcon />
          <AlertTitle>Cómo usar estos filtros</AlertTitle>
          <AlertDescription className="gap-3">
            {typeof textoAyuda === "string" ? (
              <p>{textoAyuda}</p>
            ) : (
              <dl className="flex flex-col gap-3">
                {textoAyuda.map(([nombre, explicacion]) => (
                  <div key={nombre} className="flex flex-col gap-0.5">
                    <dt className="font-medium text-foreground">{nombre}</dt>
                    <dd>{explicacion}</dd>
                  </div>
                ))}
              </dl>
            )}
            <p>
              Los campos vacíos no filtran. Deben cumplirse todos los filtros
              aplicados; en una lista, basta una de las opciones elegidas.
            </p>
          </AlertDescription>
        </Alert>
      )}
      <p aria-live="polite" className="text-xs text-destructive">
        {error}
      </p>
      <div className="flex flex-wrap justify-end gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="mr-auto"
          aria-expanded={ayuda}
          aria-controls={ayuda ? ayudaId : undefined}
          onClick={() => setAyuda(!ayuda)}
        >
          Ayuda
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => {
            setValores(Object.fromEntries(campos.map(([k]) => [k, ""])))
            setError("")
          }}
        >
          Limpiar
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={cerrar}>
          Cancelar
        </Button>
        <Button type="submit" size="sm">
          Aceptar
        </Button>
      </div>
    </form>
  )
}

// Solo se activa en la pestaña activa si esa pantalla registró su búsqueda
// avanzada (useBusquedaAvanzadaPanel); los criterios son de esa pestaña.
export function BusquedaAvanzada() {
  const panelActivo = useImpresionPaneles((s) => s.panelActivo)
  const activa = useBusquedasAvanzadas((s) =>
    panelActivo ? s.paneles[panelActivo] : undefined
  )
  const [abierto, setAbierto] = useState(false)
  const boton = useRef<HTMLButtonElement>(null)
  const resumen = activa
    ? resumenAvanzados(activa.tipo, activa.criterios, activa.opciones)
    : []
  const titulo = activa
    ? `Búsqueda avanzada (Ctrl+Shift+F)${resumen.length ? " · " + resumen.join(" · ") : ""}`
    : "Búsqueda avanzada: no disponible en esta pestaña"
  return (
    <Dialog
      open={abierto && !!activa}
      onOpenChange={(open, detalles) => {
        if (
          !open &&
          detalles.reason === "outside-press" &&
          !window.confirm("¿Descartar los cambios del formulario?")
        )
          return
        setAbierto(open)
      }}
    >
      <Tooltip>
        <TooltipTrigger
          render={
            <span
              tabIndex={!activa ? 0 : undefined}
              aria-label={!activa ? titulo : undefined}
            />
          }
        >
          <DialogTrigger
            render={
              <Button
                ref={boton}
                variant="outline"
                size="icon-sm"
                disabled={!activa}
                aria-label="Búsqueda avanzada"
                title={titulo}
                aria-keyshortcuts="Control+Shift+F Meta+Shift+F"
                data-busqueda-avanzada
                className="relative"
              />
            }
          >
            <SearchIcon />
            <ListFilterIcon className="absolute right-0.5 bottom-0.5 size-3 rounded-sm bg-background" />
            {resumen.length > 0 && (
              <span className="absolute -top-1 -right-1 rounded-full bg-primary px-1 text-[10px] text-primary-foreground">
                {resumen.length}
              </span>
            )}
          </DialogTrigger>
        </TooltipTrigger>
        <TooltipContent>{titulo}</TooltipContent>
      </Tooltip>
      <DialogContent
        scrollable
        className="sm:max-w-[460px]"
        showCloseButton={false}
        initialFocus={() =>
          activa &&
          document.getElementById(
            `avanzado-${activa.campos?.[0] ?? AVANZADOS[activa.tipo].campos[0][0]}`
          )
        }
        finalFocus={boton}
      >
        <DialogTitle>Criterio de selección</DialogTitle>
        {activa && (
          <Formulario
            key={`${panelActivo}:${activa.tipo}:${activa.campos?.join(",") ?? ""}`}
            busqueda={activa}
            cerrar={() => setAbierto(false)}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}
