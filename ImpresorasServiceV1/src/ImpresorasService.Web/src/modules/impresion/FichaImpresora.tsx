import { ExternalLinkIcon } from "lucide-react"
import type { Impresora, Tienda } from "@serca/contratos"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from "@/components/ui/dialog"
import { cn } from "@/lib/utils"
import {
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
} from "./formato"

// Ficha de una impresora (docs/72, mejora 6): lo que dice de sí misma por IPP
// en la última lectura del Worker (o de «Comprobar»), más su conexión.
export function FichaImpresora({
  impresora: i,
  tiendas,
  gestionar,
  comprobando,
  identificando,
  onComprobar,
  onIdentificar,
  onCerrar,
}: {
  impresora: Impresora
  tiendas: readonly Tienda[]
  gestionar: boolean
  comprobando: boolean
  identificando: boolean
  onComprobar: () => void
  onIdentificar: () => void
  onCerrar: () => void
}) {
  const web = webImpresora(i)
  const consumibles = conNivel(i)
  const bandejas = i.ippTrays ?? []
  const prevision = textoPrevision(i.tonerDaysLeft, i.tonerDaysSource)
  return (
    <Dialog open onOpenChange={(open) => !open && onCerrar()}>
      <DialogContent className="sm:max-w-lg">
        <DialogTitle>{i.printerName}</DialogTitle>
        <DialogDescription>
          {nombreTienda(i.storeId, tiendas)} ·{" "}
          {i.ippMakeAndModel ?? "Modelo sin leer"}
        </DialogDescription>

        <dl className="grid grid-cols-[9rem_minmax(0,1fr)] gap-x-3 gap-y-2 text-sm">
          <dt className="text-muted-foreground">Estado</dt>
          <dd className="flex flex-wrap items-center gap-2">
            {i.ippStateText ? (
              <Badge
                variant="outline"
                className={cn(
                  "border-transparent",
                  CLASE_TONO[TONO_SEVERIDAD[i.ippSeverity ?? "neutral"]]
                )}
              >
                {i.ippStateText}
              </Badge>
            ) : (
              <span className="text-muted-foreground">Sin lectura</span>
            )}
            {i.ippStateSinceUtc && (
              <span
                className="text-xs text-muted-foreground"
                title={fechaHora(i.ippStateSinceUtc)}
              >
                desde hace {hace(i.ippStateSinceUtc)}
              </span>
            )}
          </dd>
          {i.ippStateMessage && (
            <>
              <dt className="text-muted-foreground">Mensaje del panel</dt>
              <dd>{i.ippStateMessage}</dd>
            </>
          )}

          <dt className="text-muted-foreground">Tóner</dt>
          <dd className="flex flex-col gap-1">
            {consumibles.length ? (
              consumibles.map((c, n) => {
                const tono = tonoToner(c.nivel, i)
                return (
                  <span key={n} className="flex items-center gap-2">
                    <span
                      aria-hidden
                      className="h-2 w-24 overflow-hidden rounded-full bg-muted"
                    >
                      <span
                        className={cn("block h-full", COLOR_PUNTO[tono])}
                        style={{ width: `${c.nivel}%` }}
                      />
                    </span>
                    <span className="tabular-nums">{c.nivel} %</span>
                    <span className="truncate text-xs text-muted-foreground">
                      {c.nombre}
                    </span>
                  </span>
                )
              })
            ) : (
              <span className="text-muted-foreground">Sin dato</span>
            )}
            <span className="text-xs text-muted-foreground">
              {prevision ??
                "Sin previsión todavía: hace falta que el nivel baje durante al menos un día"}
              {" · "}aviso al {i.tonerWarningPct ?? 20} %, crítico al{" "}
              {i.tonerCriticalPct ?? 10} %
            </span>
          </dd>

          {bandejas.length > 0 && (
            <>
              <dt className="text-muted-foreground">Bandejas</dt>
              <dd className="flex flex-col">
                {bandejas.map((b, n) => (
                  <span key={n}>
                    {b.nombre ?? `Bandeja ${n + 1}`}:{" "}
                    <span className="text-muted-foreground">
                      {textoBandeja(b)}
                    </span>
                  </span>
                ))}
              </dd>
            </>
          )}

          <dt className="text-muted-foreground">En su cola</dt>
          <dd>
            {i.ippQueuedJobs === null || i.ippQueuedJobs === undefined
              ? "Sin dato"
              : i.ippQueuedJobs === 0
                ? "Nada pendiente"
                : `${i.ippQueuedJobs} trabajos`}
          </dd>

          <dt className="text-muted-foreground">Firmware</dt>
          <dd>{i.ippFirmware ?? "Sin dato"}</dd>

          <dt className="text-muted-foreground">Encendida</dt>
          <dd title={i.ippUpSinceUtc ? fechaHora(i.ippUpSinceUtc) : undefined}>
            {i.ippUpSinceUtc ? `hace ${hace(i.ippUpSinceUtc)}` : "Sin dato"}
          </dd>

          <dt className="text-muted-foreground">Conexión</dt>
          <dd>
            {i.connectivityLabel}
            {i.host && (
              <span className="text-muted-foreground">
                {" "}
                · <span className="font-mono">{i.host}</span> · puerto{" "}
                {puerto(i.lastConnectionTransport)}
              </span>
            )}
          </dd>

          <dt className="text-muted-foreground">Última lectura</dt>
          <dd title={fechaHora(i.ippCheckedAtUtc ?? null)}>
            {i.ippCheckedAtUtc ? `hace ${hace(i.ippCheckedAtUtc)}` : "Nunca"}
          </dd>
        </dl>

        <DialogFooter className="gap-2 sm:justify-between">
          {web ? (
            <a
              href={web}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline"
            >
              <ExternalLinkIcon className="size-3.5" />
              Abrir la web de la impresora
            </a>
          ) : (
            <span />
          )}
          <span className="flex gap-2">
            {gestionar && (
              <>
                <Button
                  variant="outline"
                  disabled={!i.host || identificando}
                  title={
                    i.host
                      ? "Que pite o parpadee para encontrarla"
                      : "Sin IP no se puede identificar"
                  }
                  onClick={onIdentificar}
                >
                  {identificando ? "Identificando…" : "Identificar"}
                </Button>
                <Button
                  variant="outline"
                  disabled={comprobando}
                  onClick={onComprobar}
                >
                  {comprobando ? "Comprobando…" : "Comprobar"}
                </Button>
              </>
            )}
            <Button onClick={onCerrar}>Cerrar</Button>
          </span>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
