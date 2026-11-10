import {
  CircleAlertIcon,
  CircleCheckIcon,
  CircleHelpIcon,
  CirclePauseIcon,
  DoorOpenIcon,
  BatteryLowIcon,
  BatteryWarningIcon,
  FileXIcon,
  PowerOffIcon,
  PrinterIcon,
  TriangleAlertIcon,
  WrenchIcon,
  type LucideIcon,
} from "lucide-react"
import type { Impresora } from "@serca/contratos"

import { Badge } from "@/components/ui/badge"
import { cn } from "@/lib/utils"
import {
  detalleEstado,
  tiposEstado,
  type ClaveEstado,
} from "./estado-impresora"
import {
  CLASE_TONO,
  COLOR_PUNTO,
  conNivel,
  textoPrevision,
  tonoToner,
  type UmbralToner,
} from "./formato"

const ICONO: Record<ClaveEstado, LucideIcon> = {
  lista: CircleCheckIcon,
  imprimiendo: PrinterIcon,
  sinPapel: FileXIcon,
  papelBajo: FileXIcon,
  atasco: TriangleAlertIcon,
  tapa: DoorOpenIcon,
  bandeja: TriangleAlertIcon,
  sinToner: BatteryWarningIcon,
  tonerCritico: BatteryWarningIcon,
  tonerBajo: BatteryLowIcon,
  tambor: WrenchIcon,
  residuos: WrenchIcon,
  mantenimiento: WrenchIcon,
  pausa: CirclePauseIcon,
  fueraDeLinea: PowerOffIcon,
  parada: CircleAlertIcon,
  aviso: TriangleAlertIcon,
  error: CircleAlertIcon,
  sinLectura: CircleHelpIcon,
}

/**
 * El estado de la impresora en poco sitio (docs/72, 10/11/2026): el tipo más
 * grave con su icono y «+N» si hay más; el resto, en el title.
 */
export function ChipEstado({
  impresora,
  umbral,
  grande,
}: {
  impresora: Impresora
  umbral?: UmbralToner
  grande?: boolean
}) {
  const [peor, ...resto] = tiposEstado(impresora, umbral)
  const Icono = ICONO[peor.clave]
  return (
    <Badge
      variant="outline"
      className={cn(
        "max-w-full gap-1 border-transparent",
        CLASE_TONO[peor.tono],
        grande && "h-7 px-2.5 text-sm"
      )}
      title={detalleEstado(impresora, umbral)}
    >
      <Icono aria-hidden className={grande ? "size-4" : "size-3.5"} />
      <span className="truncate">{peor.etiqueta}</span>
      {resto.length > 0 && (
        <span
          className="shrink-0 opacity-70"
          aria-label={`y ${resto.length} más`}
        >
          +{resto.length}
        </span>
      )}
    </Badge>
  )
}

/**
 * El tóner en poco sitio: barra y % con los colores de Reglas y avisos; el nombre
 * del consumible y la previsión, en el title. En la tabla de Impresoras y en las
 * impresoras de la tienda del dashboard.
 */
export function BarraToner({
  impresora: i,
  umbral,
}: {
  impresora: Impresora
  umbral?: UmbralToner
}) {
  const consumibles = conNivel(i)
  if (!consumibles.length)
    return <span className="text-muted-foreground">-</span>
  return (
    <span className="flex flex-col gap-0.5">
      {consumibles.map((c, n) => {
        const tono = tonoToner(c.nivel, i, umbral)
        return (
          <span
            key={n}
            className="flex items-center gap-1.5"
            title={
              [c.nombre, textoPrevision(i.tonerDaysLeft, i.tonerDaysSource)]
                .filter(Boolean)
                .join("\n") || undefined
            }
          >
            <span
              aria-hidden
              className="h-1.5 w-12 overflow-hidden rounded-full bg-muted"
            >
              <span
                className={cn("block h-full rounded-full", COLOR_PUNTO[tono])}
                style={{ width: `${c.nivel}%` }}
              />
            </span>
            <span
              className={cn(
                "text-xs tabular-nums",
                tono === "aviso" && "font-semibold text-warning",
                tono === "error" && "font-semibold text-destructive"
              )}
            >
              {c.nivel} %
            </span>
          </span>
        )
      })}
    </span>
  )
}
