import { useState } from "react"
import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { DownloadIcon } from "lucide-react"
import { z } from "zod"
import { PaginasImpresora, type Impresora, type Tienda } from "@serca/contratos"

import { CampoFecha } from "@/components/campo-fecha"
import { CampoFiltro } from "@/components/filtro-lista"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { api, ErrorApi, params } from "@/core/api"
import { guardarFichero } from "@/core/exportar"
import { fechaIso, num } from "@/lib/format"
import { nombreTienda } from "./formato"
import {
  avisosPeriodo,
  avisosRango,
  diasPeriodo,
  isoLocal,
  tablaExportar,
} from "./paginas"

// Páginas impresas por impresora y rango de fechas (docs/73 del ERP, opción
// E). Solo administración: lo abre el botón de la pestaña Impresoras. Los
// cálculos y los avisos, en paginas.ts.

const TODAS = "todas"

export function PaginasImpresas({
  impresoras,
  tiendas,
  onCerrar,
}: {
  impresoras: readonly Impresora[]
  tiendas: readonly Tienda[]
  onCerrar: () => void
}) {
  const hoy = isoLocal(new Date())
  const [desde, setDesde] = useState(() => `${hoy.slice(0, 8)}01`)
  const [hasta, setHasta] = useState(hoy)
  const [impresora, setImpresora] = useState(TODAS)
  const valido = Boolean(desde && hasta && desde <= hasta)
  const consulta = useQuery({
    queryKey: ["impresion", "paginas", desde, hasta],
    queryFn: ({ signal }) =>
      api(
        `/impresion/printers/page-counts?${params({ from: desde, to: hasta })}`,
        z.array(PaginasImpresora),
        { signal }
      ),
    enabled: valido,
    placeholderData: keepPreviousData,
  })
  const todas = consulta.data ?? []
  const detalle = impresora !== TODAS
  const lista = detalle
    ? todas.filter((i) => String(i.printerId) === impresora)
    : todas
  const total = lista.reduce((s, i) => s + (i.pages ?? 0), 0)
  const items = [
    { value: TODAS, label: "Todas" },
    ...[...impresoras]
      .sort(
        (a, b) =>
          a.storeId - b.storeId || a.printerName.localeCompare(b.printerName)
      )
      .map((i) => ({
        value: String(i.printerId),
        label: `${nombreTienda(i.storeId, tiendas)} · ${i.printerName}`,
      })),
  ]
  const elegida = items.find((i) => i.value === impresora)?.label ?? ""
  const exportar = () =>
    guardarFichero(
      `Páginas impresas ${detalle ? `${elegida} ` : ""}${fechaIso(desde).replaceAll("/", "-")} a ${fechaIso(hasta).replaceAll("/", "-")}`,
      () => tablaExportar(lista, tiendas, desde, hasta, hoy, detalle)
    )

  return (
    <Dialog open onOpenChange={(open) => !open && onCerrar()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-3xl">
        <DialogTitle>Páginas impresas</DialogTitle>
        <DialogDescription>
          Cada noche, a las 00:00, se guarda el contador de cada impresora: las
          páginas de un día salen de esa lectura y la del día siguiente. En las
          Canon cuenta impresión, copias e informes.
        </DialogDescription>
        <div className="flex flex-wrap items-end gap-3">
          <CampoFiltro etiqueta="Impresora">
            <Select
              items={items}
              value={impresora}
              onValueChange={(v) => setImpresora(v as string)}
            >
              <SelectTrigger className="w-72">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {items.map((i) => (
                  <SelectItem key={i.value} value={i.value}>
                    {i.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </CampoFiltro>
          <CampoFiltro etiqueta="Desde">
            <CampoFecha
              etiqueta="Desde"
              className="w-40"
              max={hoy}
              value={desde}
              onChange={setDesde}
            />
          </CampoFiltro>
          <CampoFiltro etiqueta="Hasta">
            <CampoFecha
              etiqueta="Hasta"
              className="w-40"
              value={hasta}
              onChange={setHasta}
            />
          </CampoFiltro>
          <Button
            variant="outline"
            className="ml-auto"
            disabled={!lista.length || consulta.isFetching}
            onClick={exportar}
          >
            <DownloadIcon data-icon="inline-start" />
            Exportar
          </Button>
        </div>
        {!valido ? (
          <p className="text-sm text-muted-foreground">
            Elija las dos fechas; la primera no puede ser posterior.
          </p>
        ) : consulta.isError ? (
          <p className="text-sm text-destructive">
            {consulta.error instanceof ErrorApi
              ? consulta.error.problema.title
              : "No se pudieron leer las páginas"}
          </p>
        ) : consulta.isPending ? (
          <p className="text-sm text-muted-foreground">Cargando…</p>
        ) : detalle ? (
          <Detalle
            impresora={lista[0]}
            avisos={lista[0] ? avisosRango(lista[0], desde, hasta, hoy) : []}
          />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Tienda</TableHead>
                <TableHead>Impresora</TableHead>
                <TableHead className="text-right">Páginas</TableHead>
                <TableHead>Avisos</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {lista.map((i) => (
                <TableRow key={i.printerId}>
                  <TableCell>{nombreTienda(i.storeId, tiendas)}</TableCell>
                  <TableCell>
                    <button
                      type="button"
                      className="text-left underline-offset-2 hover:underline"
                      title="Ver por días"
                      onClick={() => setImpresora(String(i.printerId))}
                    >
                      {i.printerName}
                    </button>
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {i.pages === null ? "-" : num(i.pages)}
                  </TableCell>
                  <TableCell className="text-xs whitespace-normal text-muted-foreground">
                    {avisosRango(i, desde, hasta, hoy).join(". ")}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
            <TableFooter>
              <TableRow>
                <TableCell colSpan={2}>Total</TableCell>
                <TableCell className="text-right tabular-nums">
                  {num(total)}
                </TableCell>
                <TableCell />
              </TableRow>
            </TableFooter>
          </Table>
        )}
      </DialogContent>
    </Dialog>
  )
}

function Detalle({
  impresora: i,
  avisos,
}: {
  impresora: PaginasImpresora | undefined
  avisos: string[]
}) {
  if (!i)
    return (
      <p className="text-sm text-muted-foreground">
        Sin lecturas de esta impresora en estas fechas.
      </p>
    )
  return (
    <>
      {avisos.length > 0 && (
        <p className="text-xs text-muted-foreground">{avisos.join(". ")}</p>
      )}
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Día</TableHead>
            <TableHead className="text-right">Páginas</TableHead>
            <TableHead>Avisos</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {i.periods.map((p) => (
            <TableRow key={p.from}>
              <TableCell className="tabular-nums">{diasPeriodo(p)}</TableCell>
              <TableCell className="text-right tabular-nums">
                {p.pages === null ? "-" : num(p.pages)}
              </TableCell>
              <TableCell className="text-xs whitespace-normal text-muted-foreground">
                {avisosPeriodo(p).join(". ")}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
        <TableFooter>
          <TableRow>
            <TableCell>Total</TableCell>
            <TableCell className="text-right tabular-nums">
              {i.pages === null ? "-" : num(i.pages)}
            </TableCell>
            <TableCell />
          </TableRow>
        </TableFooter>
      </Table>
    </>
  )
}
