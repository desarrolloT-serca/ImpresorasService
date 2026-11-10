import {
  BellRingIcon,
  ExternalLinkIcon,
  RefreshCwIcon,
  type LucideIcon,
} from "lucide-react"
import { toast } from "sonner"
import type { Impresora, Tienda } from "@serca/contratos"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogTitle,
} from "@/components/ui/dialog"
import { cn } from "@/lib/utils"
import { useState } from "react"
import { ChipEstado } from "./ChipEstado"
import { ColaDeImpresora } from "./ColaDeImpresora"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { mensajePanel } from "./estado-impresora"
import {
  avisoWeb,
  CLASE_TONO,
  COLOR_PUNTO,
  conNivel,
  fechaHora,
  hace,
  nombreTienda,
  puerto,
  TONO_SEVERIDAD,
  textoBandeja,
  textoPrevision,
  tonoToner,
  webImpresora,
  type UmbralToner,
} from "./formato"

// Una tarjeta de la ficha: título pequeño y su contenido.
function Tarjeta({
  titulo,
  children,
}: {
  titulo: string
  children: React.ReactNode
}) {
  return (
    <section className="flex min-w-0 flex-col gap-2 rounded-lg border bg-card p-3">
      <h3 className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
        {titulo}
      </h3>
      {children}
    </section>
  )
}

// Una fila «dato: valor» dentro de una tarjeta.
function Fila({
  dato,
  title,
  children,
}: {
  dato: string
  title?: string
  children: React.ReactNode
}) {
  return (
    <div className="flex items-baseline justify-between gap-3 text-sm">
      <span className="shrink-0 text-muted-foreground">{dato}</span>
      <span className="min-w-0 truncate text-right" title={title}>
        {children}
      </span>
    </div>
  )
}

// Botón de icono de la cabecera, con su nombre en el title y en aria-label.
function Accion({
  Icono,
  nombre,
  ocupado,
  disabled,
  onClick,
}: {
  Icono: LucideIcon
  nombre: string
  ocupado?: boolean
  disabled?: boolean
  onClick: () => void
}) {
  return (
    <Button
      size="icon-sm"
      variant="outline"
      title={nombre}
      aria-label={nombre}
      disabled={disabled || ocupado}
      onClick={onClick}
    >
      <Icono className={cn(ocupado && "animate-pulse")} />
    </Button>
  )
}

// Ficha de una impresora (docs/72, mejora 6; diseño del 10/11/2026: cabecera y
// tarjetas). Arriba lo que importa: estado, desde cuándo y las acciones. Debajo,
// por temas: tóner, papel, conexión y equipo.
export function FichaImpresora({
  impresora: i,
  tiendas,
  toner,
  gestionar,
  comprobando,
  identificando,
  onComprobar,
  onIdentificar,
  onCerrar,
}: {
  impresora: Impresora
  tiendas: readonly Tienda[]
  // Los de Reglas y avisos; sin ellos, los que manda la API con la impresora.
  toner?: UmbralToner
  gestionar: boolean
  comprobando: boolean
  identificando: boolean
  onComprobar: () => void
  onIdentificar: () => void
  onCerrar: () => void
}) {
  // Estado o la cola de la propia impresora (docs/72, R3): la cola solo se
  // pregunta al abrir su pestaña.
  const [pestana, setPestana] = useState<"estado" | "cola">("estado")
  const web = webImpresora(i)
  const consumibles = conNivel(i)
  const bandejas = i.ippTrays ?? []
  const prevision = textoPrevision(i.tonerDaysLeft, i.tonerDaysSource)
  const panel = mensajePanel(i)
  const aviso = toner?.aviso ?? i.tonerWarningPct ?? 20
  const critico = toner?.critico ?? i.tonerCriticalPct ?? 10
  return (
    <Dialog open onOpenChange={(open) => !open && onCerrar()}>
      <DialogContent className="gap-4 sm:max-w-2xl">
        {/* Cabecera: quién es, cómo está y qué se puede hacer. */}
        <div className="flex flex-wrap items-start gap-x-4 gap-y-2 pr-8">
          <div className="min-w-0 flex-1">
            <DialogTitle className="truncate text-lg">
              {i.printerName}
            </DialogTitle>
            <DialogDescription className="truncate">
              {nombreTienda(i.storeId, tiendas)} ·{" "}
              {i.ippMakeAndModel ?? "Modelo sin leer"}
            </DialogDescription>
          </div>
          <div className="flex items-center gap-1.5">
            {gestionar && (
              <>
                <Accion
                  Icono={RefreshCwIcon}
                  nombre="Comprobar conexión y estado"
                  ocupado={comprobando}
                  onClick={onComprobar}
                />
                <Accion
                  Icono={BellRingIcon}
                  nombre={
                    i.host
                      ? "Identificar: que pite o parpadee"
                      : "Sin IP no se puede identificar"
                  }
                  ocupado={identificando}
                  disabled={!i.host}
                  onClick={onIdentificar}
                />
              </>
            )}
            {web && (
              <Button
                size="icon-sm"
                variant="outline"
                title="Abrir la web de la impresora (solo desde un equipo con acceso a la red de su tienda)"
                aria-label="Abrir la web de la impresora"
                render={
                  <a
                    href={web}
                    target="_blank"
                    rel="noopener noreferrer"
                    onClick={() => {
                      const a = avisoWeb(i, tiendas)
                      toast.info(a.titulo, { description: a.detalle })
                    }}
                  />
                }
              >
                <ExternalLinkIcon />
              </Button>
            )}
          </div>
        </div>

        <div className="flex flex-col gap-1.5 rounded-lg border bg-muted/30 p-3">
          <div className="flex flex-wrap items-center gap-2">
            <ChipEstado impresora={i} umbral={toner} grande />
            {i.ippStateSinceUtc && (
              <span
                className="text-sm text-muted-foreground"
                title={fechaHora(i.ippStateSinceUtc)}
              >
                desde hace {hace(i.ippStateSinceUtc)}
              </span>
            )}
          </div>
          {i.ippStateText && i.ippSeverity !== "healthy" && (
            <p className="text-sm">{i.ippStateText}</p>
          )}
          {panel && (
            <p className="text-xs text-muted-foreground">Panel: {panel}</p>
          )}
          {i.ippSeverity === undefined && (
            <p className="text-xs text-warning">
              El servicio de impresión no está actualizado: estado, firmware,
              encendida, bandejas, previsión e Identificar no llegan hasta
              desplegar su última versión.
            </p>
          )}
        </div>

        <Tabs
          value={pestana}
          onValueChange={(v) => setPestana(v as "estado" | "cola")}
        >
          <TabsList variant="line" aria-label="Qué se ve de la impresora">
            <TabsTrigger value="estado">Estado</TabsTrigger>
            <TabsTrigger value="cola">
              Cola de la impresora
              {i.ippQueuedJobs ? (
                <span className="ml-1 rounded-full bg-muted px-1.5 text-xs tabular-nums">
                  {i.ippQueuedJobs}
                </span>
              ) : null}
            </TabsTrigger>
          </TabsList>
        </Tabs>

        {pestana === "cola" ? (
          <ColaDeImpresora printerId={i.printerId} />
        ) : (
          <div className="grid gap-3 sm:grid-cols-2">
            <Tarjeta titulo="Tóner">
              {consumibles.length ? (
                consumibles.map((c, n) => {
                  const tono = tonoToner(c.nivel, i, toner)
                  return (
                    <div key={n} className="flex flex-col gap-1">
                      <div className="flex items-center gap-2">
                        <span
                          aria-hidden
                          className="h-2.5 flex-1 overflow-hidden rounded-full bg-muted"
                        >
                          <span
                            className={cn("block h-full", COLOR_PUNTO[tono])}
                            style={{ width: `${c.nivel}%` }}
                          />
                        </span>
                        <span
                          className={cn(
                            "w-12 text-right text-sm font-semibold tabular-nums",
                            tono === "aviso" && "text-warning",
                            tono === "error" && "text-destructive"
                          )}
                        >
                          {c.nivel} %
                        </span>
                      </div>
                      <span className="truncate text-xs text-muted-foreground">
                        {c.nombre}
                      </span>
                    </div>
                  )
                })
              ) : (
                <span className="text-sm text-muted-foreground">Sin dato</span>
              )}
              <p className="text-xs text-muted-foreground">
                {prevision ?? "Sin previsión todavía"}
              </p>
              <p className="text-xs text-muted-foreground">
                Aviso al {aviso} %, crítico al {critico} % (Reglas y avisos)
              </p>
            </Tarjeta>

            <Tarjeta titulo="Papel">
              {bandejas.length ? (
                bandejas.map((b, n) => (
                  <Fila key={n} dato={b.nombre ?? `Bandeja ${n + 1}`}>
                    {textoBandeja(b)}
                  </Fila>
                ))
              ) : (
                <span className="text-sm text-muted-foreground">Sin dato</span>
              )}
              <Fila dato="En su cola">
                {i.ippQueuedJobs === null || i.ippQueuedJobs === undefined
                  ? "Sin dato"
                  : i.ippQueuedJobs === 0
                    ? "Nada pendiente"
                    : `${i.ippQueuedJobs} trabajos`}
              </Fila>
            </Tarjeta>

            <Tarjeta titulo="Conexión">
              <Fila dato="Estado">
                <span
                  className={cn(
                    "rounded px-1.5 py-0.5 text-xs",
                    CLASE_TONO[TONO_SEVERIDAD[i.connectivitySeverity]]
                  )}
                >
                  {i.connectivityLabel}
                </span>
              </Fila>
              <Fila dato="IP">
                <span className="font-mono">{i.host ?? "-"}</span>
              </Fila>
              <Fila dato="Responde por">
                {i.lastConnectionTransport
                  ? `puerto ${puerto(i.lastConnectionTransport)}`
                  : "-"}
              </Fila>
              <Fila dato="Leída" title={fechaHora(i.ippCheckedAtUtc ?? null)}>
                {i.ippCheckedAtUtc
                  ? `hace ${hace(i.ippCheckedAtUtc)}`
                  : "Nunca"}
              </Fila>
            </Tarjeta>

            <Tarjeta titulo="Equipo">
              <Fila dato="Modelo" title={i.ippMakeAndModel ?? undefined}>
                {i.ippMakeAndModel ?? "Sin dato"}
              </Fila>
              <Fila dato="Firmware" title={i.ippFirmware ?? undefined}>
                {i.ippFirmware ?? "Sin dato"}
              </Fila>
              <Fila
                dato="Encendida"
                title={i.ippUpSinceUtc ? fechaHora(i.ippUpSinceUtc) : undefined}
              >
                {i.ippUpSinceUtc ? `hace ${hace(i.ippUpSinceUtc)}` : "Sin dato"}
              </Fila>
              <Fila dato="Cola de Windows" title={i.spoolQueue ?? undefined}>
                {i.spoolQueue ?? "Solo IP"}
              </Fila>
            </Tarjeta>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
