import {
  CircleCheckIcon,
  InfoIcon,
  RefreshCwIcon,
  UnplugIcon,
  type LucideIcon,
} from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Spinner } from "@/components/ui/spinner"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { cn } from "@/lib/utils"
import { useColaImpresora } from "./api"
import { CLASE_TONO, documentoErp, fechaHora, hace, type Tono } from "./formato"

// job-state de la impresora (RFC 8011 §5.3.7).
const ESTADO_TRABAJO: Record<number, { etiqueta: string; tono: Tono }> = {
  3: { etiqueta: "En espera", tono: "info" },
  4: { etiqueta: "Retenido", tono: "aviso" },
  5: { etiqueta: "Imprimiendo", tono: "ok" },
  6: { etiqueta: "Detenido", tono: "error" },
  7: { etiqueta: "Cancelado", tono: "neutro" },
  8: { etiqueta: "Abortado", tono: "error" },
  9: { etiqueta: "Terminado", tono: "ok" },
}

// Un mensaje con icono para cuando no hay lista que enseñar.
function Vacio({
  Icono,
  titulo,
  detalle,
  tono,
}: {
  Icono: LucideIcon
  titulo: string
  detalle?: string
  tono?: "ok" | "error"
}) {
  return (
    <div className="flex flex-col items-center gap-1.5 py-8 text-center">
      <Icono
        aria-hidden
        className={cn(
          "size-8",
          tono === "ok" && "text-success",
          tono === "error" && "text-destructive",
          !tono && "text-muted-foreground"
        )}
      />
      <p className="text-sm font-medium">{titulo}</p>
      {detalle && (
        <p className="max-w-sm text-xs text-muted-foreground">{detalle}</p>
      )}
    </div>
  )
}

/**
 * La cola de la propia impresora, en directo (docs/72, R3; rediseño del
 * 10/11/2026): resumen arriba (cuántos, detenidos, nuestros) y una tabla de
 * columnas fijas. No es la cola del programa: muchas impresoras solo enseñan
 * lo que mandó quien pregunta y no guardan lo ya impreso.
 */
export function ColaDeImpresora({ printerId }: { printerId: number }) {
  const cola = useColaImpresora(printerId)
  const actualizado = cola.dataUpdatedAt
    ? new Date(cola.dataUpdatedAt).toISOString()
    : null

  const cabecera = (resumen: React.ReactNode) => (
    <div className="flex flex-wrap items-center gap-2">
      {resumen}
      <span className="ml-auto flex items-center gap-1 text-xs text-muted-foreground">
        <span title="Algunas impresoras no enseñan los trabajos de otros equipos ni guardan lo ya impreso. Se pregunta cada 10 s mientras está abierta.">
          <InfoIcon aria-hidden className="size-3.5" />
        </span>
        {actualizado && `actualizada ${hace(actualizado)}`}
        <Button
          size="icon-xs"
          variant="ghost"
          title="Preguntar ahora"
          aria-label="Preguntar ahora a la impresora"
          disabled={cola.isFetching}
          onClick={() => void cola.refetch()}
        >
          <RefreshCwIcon className={cn(cola.isFetching && "animate-spin")} />
        </Button>
      </span>
    </div>
  )

  if (cola.isPending)
    return (
      <div className="flex items-center justify-center gap-2 py-8 text-sm text-muted-foreground">
        <Spinner /> Preguntando a la impresora…
      </div>
    )
  if (cola.isError || !cola.data)
    return (
      <Vacio
        Icono={UnplugIcon}
        tono="error"
        titulo="No se ha podido consultar"
        detalle="El servicio de impresión no ha contestado. Vuelve a intentarlo en un momento."
      />
    )
  const { respondio, mensaje, trabajos } = cola.data
  if (!respondio)
    return (
      <>
        {cabecera(null)}
        <Vacio
          Icono={UnplugIcon}
          tono="error"
          titulo="La impresora no contesta"
          detalle={mensaje ?? undefined}
        />
      </>
    )

  const detenidos = trabajos.filter((t) => t.estado === 6).length
  const nuestros = trabajos.filter((t) => t.nuestro).length
  return (
    <div className="flex flex-col gap-3">
      {cabecera(
        <>
          <span className="text-sm">
            <b className="font-semibold tabular-nums">{trabajos.length}</b>{" "}
            <span className="text-muted-foreground">
              {trabajos.length === 1
                ? "trabajo pendiente"
                : "trabajos pendientes"}
            </span>
          </span>
          {detenidos > 0 && (
            <Badge
              variant="outline"
              className={cn("border-transparent", CLASE_TONO.error)}
            >
              {detenidos} {detenidos === 1 ? "detenido" : "detenidos"}
            </Badge>
          )}
          {nuestros > 0 && (
            <Badge
              variant="outline"
              className={cn("border-transparent", CLASE_TONO.info)}
            >
              {nuestros} {nuestros === 1 ? "nuestro" : "nuestros"}
            </Badge>
          )}
        </>
      )}

      {trabajos.length === 0 ? (
        <Vacio
          Icono={CircleCheckIcon}
          tono="ok"
          titulo="No tiene nada pendiente"
          detalle="Todo lo que le ha llegado ya ha salido o se ha cancelado."
        />
      ) : (
        <div className="overflow-hidden rounded-md border">
          <Table className="table-fixed">
            <colgroup>
              <col />
              <col className="w-32" />
              <col className="w-28" />
            </colgroup>
            <TableHeader>
              <TableRow>
                <TableHead>Documento</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead className="text-right">Llegó</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {trabajos.map((t) => {
                const e =
                  t.estado === null ? undefined : ESTADO_TRABAJO[t.estado]
                const doc =
                  t.nuestro &&
                  documentoErp(t.nuestro.documentType, t.nuestro.externalJobId)
                return (
                  <TableRow key={t.jobId}>
                    <TableCell className="truncate">
                      {t.nuestro ? (
                        <span className="font-medium">
                          {doc
                            ? `${doc.nombre} ${doc.numero}`
                            : `${t.nuestro.documentType} ${t.nuestro.externalJobId}`}
                        </span>
                      ) : (
                        <span className="text-muted-foreground">
                          {t.nombre ?? "De otro equipo"}
                          {t.usuario && ` · ${t.usuario}`}
                        </span>
                      )}
                      <span className="ml-1.5 text-xs text-muted-foreground tabular-nums">
                        #{t.jobId}
                      </span>
                    </TableCell>
                    <TableCell>
                      <Badge
                        variant="outline"
                        className={cn(
                          "border-transparent",
                          CLASE_TONO[e?.tono ?? "neutro"]
                        )}
                        title={t.motivos ?? undefined}
                      >
                        {e?.etiqueta ?? "Sin estado"}
                      </Badge>
                    </TableCell>
                    <TableCell
                      className="text-right text-muted-foreground"
                      title={t.creadoUtc ? fechaHora(t.creadoUtc) : undefined}
                    >
                      {t.creadoUtc ? `hace ${hace(t.creadoUtc)}` : "-"}
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  )
}
