import { useState } from "react"
import type { ColumnDef } from "@tanstack/react-table"
import { PlayIcon, PlusIcon, SaveIcon, XIcon } from "lucide-react"
import { toast } from "sonner"
import { ORIGEN_PRUEBA, type TrabajoCola } from "@serca/contratos"

import { DataTable } from "@/components/data-table"
import { Panel } from "@/components/ficha"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { ErrorApi } from "@/core/api"
import { cn } from "@/lib/utils"
import { crearTrabajoPrueba, useCancelarPruebas, useTiendas, useTrabajosPrueba } from "./api"
import { CLASE_TONO, ESTADOS, nombreTienda } from "./formato"

type Lote = { tienda: string; tipo: string; canal: string; cantidad: string }
type Escenario = { nombre: string; lotes: Lote[] }

const LOTE: Lote = { tienda: "", tipo: "TICKET", canal: "DEFAULT", cantidad: "1" }
const MAX_PDF = 20 * 1024 * 1024

// ponytail: los escenarios se guardan en este navegador (el PHP los guardaba
// en su disco, que desaparece con él). Si hace falta compartirlos entre
// administradores, irán a la API.
const CLAVE = "impresion.pruebas.escenarios"
const leerEscenarios = (): Escenario[] => {
  try {
    return JSON.parse(localStorage.getItem(CLAVE) ?? "[]")
  } catch {
    return []
  }
}
const guardarEscenarios = (e: Escenario[]) => {
  try {
    localStorage.setItem(CLAVE, JSON.stringify(e))
  } catch {
    // Almacenamiento lleno o bloqueado: el escenario se pierde al recargar.
  }
}

const enBase64 = async (f: File) => {
  const bytes = new Uint8Array(await f.arrayBuffer())
  let s = ""
  for (let i = 0; i < bytes.length; i += 0x8000)
    s += String.fromCharCode(...bytes.subarray(i, i + 0x8000))
  return btoa(s)
}

// «PRUEBA-NOCHE-20261023143005»: el id de cada trabajo empieza por él, y así
// se buscan y se cancelan los de esta prueba.
const idPrueba = (nombre: string) => {
  const d = new Date()
  const dos = (n: number) => String(n).padStart(2, "0")
  const slug = nombre.toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 30) || ORIGEN_PRUEBA
  return `${ORIGEN_PRUEBA}-${slug}-${d.getFullYear()}${dos(d.getMonth() + 1)}${dos(d.getDate())}${dos(d.getHours())}${dos(d.getMinutes())}${dos(d.getSeconds())}`
}

const errorDeLote = (l: Lote) =>
  !l.tienda
    ? "elige la tienda"
    : !/^\d{1,2}$/.test(l.cantidad) || +l.cantidad < 1 || +l.cantidad > 50
      ? "cantidad de 1 a 50"
      : !l.tipo.trim() || l.tipo.trim().length > 80
        ? "tipo obligatorio (máx. 80)"
        : l.canal.trim().length > 40
          ? "canal de máx. 40"
          : null

// Pruebas (pruebas/index.blade.php del PHP): crea trabajos de prueba por la
// misma vía que los del ERP para comprobar enrutado, impresión y alertas.
// Se imprimen de verdad.
export default function Pruebas() {
  const tiendas = (useTiendas().data ?? []).filter((t) => t.isActive)
  const cancelar = useCancelarPruebas()
  const [nombre, setNombre] = useState("Prueba")
  const [lotes, setLotes] = useState<Lote[]>([LOTE])
  const [pdf, setPdf] = useState<{ nombre: string; base64: string }>()
  const [escenarios, setEscenarios] = useState(leerEscenarios)
  const [prueba, setPrueba] = useState<string>()
  const [progreso, setProgreso] = useState<{ hechos: number; fallos: number; total: number }>()
  const trabajos = useTrabajosPrueba(prueba)
  const lanzando = !!progreso && progreso.hechos + progreso.fallos < progreso.total
  const total = lotes.reduce((s, l) => s + (Number(l.cantidad) || 0), 0)
  const tiendaItems = tiendas.map((t) => ({ value: String(t.storeId), label: nombreTienda(t.storeId, tiendas) }))
  const cambiarLote = (i: number, c: Partial<Lote>) =>
    setLotes((ls) => ls.map((l, j) => (j === i ? { ...l, ...c } : l)))

  async function lanzar() {
    const malos = lotes.map(errorDeLote).map((e, i) => (e ? `lote ${i + 1}: ${e}` : null)).filter(Boolean)
    if (malos.length) {
      toast.error(`Revisa ${malos.join("; ")}`)
      return
    }
    if (!window.confirm(`Se crearán ${total} trabajos de prueba. Entran por la misma vía que los del ERP y se imprimen de verdad en las impresoras de esas tiendas.`))
      return
    const id = idPrueba(nombre)
    setPrueba(id)
    setProgreso({ hechos: 0, fallos: 0, total })
    let n = 0
    let fallos = 0
    for (const l of lotes)
      for (let k = 0; k < Number(l.cantidad); k++) {
        n++
        try {
          await crearTrabajoPrueba({
            sourceSystem: ORIGEN_PRUEBA,
            externalJobId: `${id}-${String(n).padStart(3, "0")}`,
            storeId: Number(l.tienda),
            documentType: l.tipo.trim(),
            channel: l.canal.trim() || "DEFAULT",
            ...(pdf && { pdfBlob: pdf.base64 }),
          })
        } catch {
          fallos++
        }
        setProgreso({ hechos: n - fallos, fallos, total })
      }
    if (fallos) toast.error(`${fallos} de ${total} no se pudieron crear`)
    else toast.success(`${total} trabajos de prueba creados`)
  }

  const resumen = Object.entries(
    (trabajos.data?.value ?? []).reduce<Record<string, number>>((acc, t) => {
      const e = ESTADOS[t.status].etiqueta
      acc[e] = (acc[e] ?? 0) + 1
      return acc
    }, {})
  )

  return (
    <Panel className="[&_table]:min-w-[44rem] [&_table]:table-fixed">
      <section className="space-y-3 rounded-lg border bg-card p-4">
        <div className="flex flex-wrap items-end gap-3">
          <label className="grid gap-1 text-sm">
            <span className="text-[11px] font-medium text-muted-foreground">Nombre de la prueba</span>
            <Input className="w-56" value={nombre} disabled={lanzando} onChange={(e) => setNombre(e.target.value)} />
          </label>
          <label className="grid gap-1 text-sm">
            <span className="text-[11px] font-medium text-muted-foreground">PDF (opcional, máx. 20 MB)</span>
            <Input
              type="file"
              accept="application/pdf"
              className="w-72"
              disabled={lanzando}
              onChange={async (e) => {
                const f = e.target.files?.[0]
                if (!f) return setPdf(undefined)
                if (f.size > MAX_PDF) {
                  toast.error("El PDF pasa de 20 MB")
                  e.target.value = ""
                  return
                }
                setPdf({ nombre: f.name, base64: await enBase64(f) })
              }}
            />
          </label>
          {!pdf && <span className="self-center text-xs text-muted-foreground">Sin PDF, la API manda uno mínimo.</span>}
        </div>

        <div className="space-y-2">
          {lotes.map((l, i) => (
            <div key={i} className="flex flex-wrap items-center gap-2">
              <Select items={tiendaItems} value={l.tienda || null} disabled={lanzando} onValueChange={(v) => cambiarLote(i, { tienda: v as string })}>
                <SelectTrigger className="w-60" aria-label="Tienda">
                  <SelectValue placeholder="Tienda" />
                </SelectTrigger>
                <SelectContent>
                  {tiendaItems.map((t) => (
                    <SelectItem key={t.value} value={t.value}>
                      {t.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Input className="w-36" aria-label="Tipo de documento" value={l.tipo} disabled={lanzando} onChange={(e) => cambiarLote(i, { tipo: e.target.value })} />
              <Input className="w-32" aria-label="Canal" value={l.canal} disabled={lanzando} onChange={(e) => cambiarLote(i, { canal: e.target.value })} />
              <Input className="w-20" aria-label="Cantidad" inputMode="numeric" value={l.cantidad} disabled={lanzando} onChange={(e) => cambiarLote(i, { cantidad: e.target.value })} />
              <span className="text-xs text-muted-foreground">trabajos</span>
              <Button variant="ghost" size="icon-sm" aria-label="Quitar lote" disabled={lanzando || lotes.length === 1} onClick={() => setLotes((ls) => ls.filter((_, j) => j !== i))}>
                <XIcon />
              </Button>
            </div>
          ))}
          <Button variant="outline" size="xs" disabled={lanzando} onClick={() => setLotes((ls) => [...ls, LOTE])}>
            <PlusIcon data-icon="inline-start" />
            Añadir lote
          </Button>
        </div>

        <div className="flex flex-wrap items-center gap-2 border-t pt-3">
          <Button
            variant="outline"
            size="sm"
            disabled={lanzando || !nombre.trim()}
            onClick={() => {
              const nuevos = [...escenarios.filter((e) => e.nombre !== nombre.trim()), { nombre: nombre.trim(), lotes }]
              setEscenarios(nuevos)
              guardarEscenarios(nuevos)
              toast.success("Escenario guardado en este navegador")
            }}
          >
            <SaveIcon data-icon="inline-start" />
            Guardar escenario
          </Button>
          {escenarios.map((e) => (
            <span key={e.nombre} className="flex items-center rounded-md border">
              <button type="button" className="px-2 py-1 text-xs hover:bg-accent" disabled={lanzando} onClick={() => { setNombre(e.nombre); setLotes(e.lotes) }}>
                {e.nombre}
              </button>
              <button
                type="button"
                aria-label={`Borrar el escenario ${e.nombre}`}
                className="border-l px-1.5 py-1 text-muted-foreground hover:bg-accent"
                onClick={() => {
                  const nuevos = escenarios.filter((x) => x.nombre !== e.nombre)
                  setEscenarios(nuevos)
                  guardarEscenarios(nuevos)
                }}
              >
                <XIcon className="size-3" />
              </button>
            </span>
          ))}
          <Button size="sm" className="ml-auto" disabled={lanzando} onClick={lanzar}>
            <PlayIcon data-icon="inline-start" />
            {lanzando ? `Creando ${progreso!.hechos + progreso!.fallos} de ${progreso!.total}…` : `Lanzar ${total} trabajos`}
          </Button>
        </div>
      </section>

      {prueba && (
        <section className="space-y-3 rounded-lg border bg-card p-4">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="font-semibold">Prueba</h2>
            <span className="font-mono text-xs text-muted-foreground">{prueba}</span>
            <span className="text-xs text-muted-foreground">
              {resumen.map(([e, n]) => `${n} ${e.toLowerCase()}`).join(" · ") || "sin trabajos todavía"}
            </span>
            <Button
              variant="outline"
              size="xs"
              className="ml-auto"
              disabled={cancelar.isPending}
              onClick={() =>
                cancelar.mutate(prueba, {
                  onSuccess: (r) => toast.success(`${r.cancelled} trabajos cancelados`),
                  onError: (e) => toast.error(e instanceof ErrorApi ? e.problema.title : "No se pudo cancelar"),
                })
              }
            >
              Cancelar esta prueba
            </Button>
          </div>
          <DataTable columns={columnas} data={trabajos.data?.value ?? []} paginar={false} emptyContent="Aún no ha entrado ningún trabajo: el Worker los recoge cada 2 s." />
        </section>
      )}

      <div>
        <Button
          variant="ghost"
          size="sm"
          disabled={cancelar.isPending}
          onClick={() =>
            window.confirm("Se cancelarán todos los trabajos de prueba que sigan pendientes, de cualquier prueba.") &&
            cancelar.mutate(undefined, {
              onSuccess: (r) => toast.success(`${r.cancelled} trabajos cancelados`),
              onError: (e) => toast.error(e instanceof ErrorApi ? e.problema.title : "No se pudo cancelar"),
            })
          }
        >
          Cancelar todas las pruebas pendientes
        </Button>
      </div>
    </Panel>
  )
}

const columnas: ColumnDef<TrabajoCola, unknown>[] = [
  {
    id: "trabajo",
    header: "Id. externo",
    size: 0,
    cell: ({ row: { original: t } }) => <span className="block truncate font-mono">{t.externalJobId}</span>,
  },
  { id: "tienda", header: "Tienda", size: 80, cell: ({ row: { original: t } }) => t.storeId },
  {
    id: "estado",
    header: "Estado",
    size: 170,
    cell: ({ row: { original: t } }) => (
      <Badge
        variant="outline"
        className={cn("border-transparent", CLASE_TONO[ESTADOS[t.status].tono])}
        title={t.lastErrorMessage ?? undefined}
      >
        {ESTADOS[t.status].etiqueta}
      </Badge>
    ),
  },
  {
    id: "impresora",
    header: "Impresora",
    size: 170,
    cell: ({ row: { original: t } }) => t.printerName ?? <span className="text-muted-foreground">Sin asignar</span>,
  },
  {
    id: "intentos",
    header: () => <span className="tracking-normal normal-case">Intentos</span>,
    size: 80,
    meta: { num: true },
    cell: ({ row: { original: t } }) => <span className="block text-right tabular-nums">{t.attemptCount}</span>,
  },
]
