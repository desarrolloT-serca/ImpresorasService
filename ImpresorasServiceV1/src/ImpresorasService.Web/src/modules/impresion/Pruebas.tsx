import { useEffect, useRef, useState } from "react"
import { useQueries, type UseQueryResult } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import {
  FileUpIcon,
  MinusIcon,
  PlayIcon,
  PlusIcon,
  SaveIcon,
  SquareIcon,
  TriangleAlertIcon,
  XIcon,
} from "lucide-react"
import { toast } from "sonner"
import {
  ORIGEN_PRUEBA,
  type Impresora,
  type ResolucionRegla,
  type TrabajoCola,
} from "@serca/contratos"

import { DataTable } from "@/components/data-table"
import { Panel } from "@/components/ficha"
import { CampoFiltro, FiltroLista } from "@/components/filtro-lista"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ButtonGroup } from "@/components/ui/button-group"
import { Input } from "@/components/ui/input"
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
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { ErrorApi } from "@/core/api"
import { cn } from "@/lib/utils"
import {
  consultaDestino,
  crearTrabajoPrueba,
  useCancelarPruebas,
  useImpresoras,
  useReglas,
  useTiendas,
  useTrabajosPrueba,
} from "./api"
import {
  CLASE_TONO,
  COLOR_PUNTO,
  ESTADOS,
  plural,
  TIPOS_DOCUMENTO,
  TONO_SEVERIDAD,
} from "./formato"

type Lote = { tienda: string; tipo: string; canal: string; cantidad: string }
type Escenario = { nombre: string; lotes: Lote[] }

const LOTE: Lote = {
  tienda: "",
  tipo: "A",
  canal: "DEFAULT",
  cantidad: "1",
}
const MAX_PDF = 20 * 1024 * 1024
// Los tipos del ERP y, con «Otro…», cualquier texto (escenarios antiguos con
// TICKET, FACTURA…).
const OTRO = "otro"
const OPCIONES_TIPO = [
  ...Object.entries(TIPOS_DOCUMENTO).map(([value, label]) => ({
    value,
    label,
  })),
  { value: OTRO, label: "Otro…" },
]
const esTipoErp = (tipo: string) => Object.hasOwn(TIPOS_DOCUMENTO, tipo)

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
  const slug =
    nombre
      .toUpperCase()
      .replace(/[^A-Z0-9]/g, "")
      .slice(0, 30) || ORIGEN_PRUEBA
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

// Lo que se pregunta al simulador de reglas: sin tienda o sin tipo, nada.
const trabajoDeLote = (l: Lote) =>
  l.tienda && l.tipo.trim()
    ? {
        storeId: Number(l.tienda),
        documentType: l.tipo.trim(),
        channel: l.canal.trim() || "DEFAULT",
      }
    : undefined

// Por dónde saldrá un lote, con las reglas de ahora.
type Destino =
  | { tipo: "incompleto" }
  | { tipo: "buscando" }
  | { tipo: "fallo" }
  | { tipo: "sinRegla" }
  | { tipo: "impresora"; nombre: string; ruleId: number; impresora?: Impresora }

function destinoDe(
  l: Lote,
  q: UseQueryResult<ResolucionRegla | null>,
  impresoras: readonly Impresora[]
): Destino {
  if (!trabajoDeLote(l)) return { tipo: "incompleto" }
  if (q.isPending) return { tipo: "buscando" }
  if (q.isError) return { tipo: "fallo" }
  const r = q.data
  if (r === null) return { tipo: "sinRegla" }
  const impresora = impresoras.find((x) => x.printerId === r.printerId)
  return {
    tipo: "impresora",
    nombre: impresora?.printerName ?? `Impresora ${r.printerId}`,
    ruleId: r.ruleId,
    impresora,
  }
}

// Pruebas (pruebas/index.blade.php del PHP): crea trabajos de prueba por la
// misma vía que los del ERP para comprobar enrutado, impresión y alertas.
// Se imprimen de verdad.
export default function Pruebas() {
  const tiendas = (useTiendas().data ?? []).filter((t) => t.isActive)
  const reglas = useReglas().data ?? []
  const cancelar = useCancelarPruebas()
  const [nombre, setNombre] = useState("Prueba")
  const [lotes, setLotes] = useState<Lote[]>([LOTE])
  const [pdf, setPdf] = useState<{ nombre: string; base64: string }>()
  const [pdfUrl, setPdfUrl] = useState("")
  const url = pdfUrl.trim()
  const [escenarios, setEscenarios] = useState(leerEscenarios)
  const [prueba, setPrueba] = useState<string>()
  const [progreso, setProgreso] = useState<{
    hechos: number
    fallos: number
    total: number
  }>()
  // Pausa entre un trabajo y el siguiente, para probar carga poco a poco, y
  // poder parar a medias (decisión del usuario, 27/10/2026).
  const [pausa, setPausa] = useState("0")
  const detener = useRef(false)
  // Al cerrar el panel a media prueba, se para: si no, seguiría creando
  // trabajos que se imprimen de verdad sin que nadie lo vea (WEB-4).
  useEffect(
    () => () => {
      detener.current = true
    },
    []
  )
  const impresoras = useImpresoras().data ?? []
  const trabajos = useTrabajosPrueba(prueba)
  const destinos = useQueries({
    queries: lotes.map((l) => consultaDestino(trabajoDeLote(l))),
  }).map((q, i) => destinoDe(lotes[i], q, impresoras))
  const lanzando =
    !!progreso && progreso.hechos + progreso.fallos < progreso.total
  const total = lotes.reduce((s, l) => s + (Number(l.cantidad) || 0), 0)
  const opcionesTienda = tiendas.map((t) => ({
    cod: String(t.storeId),
    nombre: t.name,
  }))
  // DEFAULT y los canales que usan las reglas; el del lote, aunque no esté.
  const canales = [
    ...new Set([
      "DEFAULT",
      ...reglas.flatMap((r) => (r.channel ? [r.channel] : [])),
      ...lotes.map((l) => l.canal.trim()).filter(Boolean),
    ]),
  ]
  const cambiarLote = (i: number, c: Partial<Lote>) =>
    setLotes((ls) => ls.map((l, j) => (j === i ? { ...l, ...c } : l)))

  // Cuántos trabajos van a cada impresora (y cuántos quedarán en Error).
  const reparto = new Map<string, { n: number; error: boolean }>()
  lotes.forEach((l, i) => {
    const d = destinos[i]
    const clave =
      d.tipo === "impresora"
        ? d.impresora
          ? `${d.nombre} (${d.impresora.connectivityLabel.toLowerCase()})`
          : d.nombre
        : d.tipo === "sinRegla"
          ? ""
          : null
    if (clave === null) return
    const r = reparto.get(clave) ?? { n: 0, error: clave === "" }
    r.n += Number(l.cantidad) || 0
    reparto.set(clave, r)
  })

  async function elegirPdf(f?: File) {
    if (!f) return
    if (f.type !== "application/pdf" && !/\.pdf$/i.test(f.name)) {
      toast.error("Tiene que ser un PDF")
      return
    }
    if (f.size > MAX_PDF) {
      toast.error("El PDF pasa de 20 MB")
      return
    }
    setPdf({ nombre: f.name, base64: await enBase64(f) })
  }

  async function lanzar() {
    const malos = lotes
      .map(errorDeLote)
      .map((e, i) => (e ? `lote ${i + 1}: ${e}` : null))
      .filter(Boolean)
    if (malos.length) {
      toast.error(`Revisa ${malos.join("; ")}`)
      return
    }
    if (url && !/^https:\/\/\S+$/i.test(url)) {
      toast.error("La URL del PDF tiene que empezar por https://")
      return
    }
    if (!/^\d{1,2}$/.test(pausa.trim())) {
      toast.error("La pausa es un número de segundos, de 0 a 99")
      return
    }
    if (
      !window.confirm(
        `Se crearán ${plural(total, "trabajo", "trabajos")} de prueba. Entran por la misma vía que los del ERP y se imprimen de verdad en las impresoras de esas tiendas.`
      )
    )
      return
    detener.current = false
    const id = idPrueba(nombre)
    setPrueba(id)
    setProgreso({ hechos: 0, fallos: 0, total })
    let n = 0
    let fallos = 0
    lanzamiento: for (const l of lotes)
      for (let k = 0; k < Number(l.cantidad); k++) {
        if (n > 0 && Number(pausa) > 0)
          await new Promise((r) => setTimeout(r, Number(pausa) * 1000))
        if (detener.current) break lanzamiento
        n++
        try {
          await crearTrabajoPrueba({
            sourceSystem: ORIGEN_PRUEBA,
            externalJobId: `${id}-${String(n).padStart(3, "0")}`,
            storeId: Number(l.tienda),
            documentType: l.tipo.trim(),
            channel: l.canal.trim() || "DEFAULT",
            ...(url ? { pdfUrl: url } : pdf && { pdfBlob: pdf.base64 }),
          })
        } catch {
          fallos++
        }
        setProgreso({ hechos: n - fallos, fallos, total })
      }
    // Al parar a medias, el total pasa a ser lo que se llegó a crear.
    if (detener.current) {
      setProgreso({ hechos: n - fallos, fallos, total: n })
      toast.info(
        `Prueba detenida: ${n} de ${plural(total, "trabajo creado", "trabajos creados")}`
      )
    } else if (fallos) toast.error(`${fallos} de ${total} no se pudieron crear`)
    else
      toast.success(
        `${plural(total, "trabajo", "trabajos")} de prueba ${total === 1 ? "creado" : "creados"}`
      )
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
      <section className="rounded-lg border bg-card">
        {/* Mientras se lanza no se cambia nada (el fieldset desactiva todos
            sus controles); Detener queda fuera. */}
        <fieldset disabled={lanzando} className="min-w-0">
          <div className="flex flex-wrap items-end gap-3 p-4">
            <CampoFiltro etiqueta="Nombre de la prueba">
              <Input
                className="w-56"
                value={nombre}
                onChange={(e) => setNombre(e.target.value)}
              />
            </CampoFiltro>
            <CampoFiltro etiqueta="Pausa entre trabajos">
              <Contador
                etiqueta="Pausa entre trabajos, en segundos"
                value={pausa}
                onChange={setPausa}
                min={0}
                max={99}
                unidad="s"
              />
            </CampoFiltro>
            <CampoFiltro etiqueta="PDF (opcional, máx. 20 MB)">
              <div className="flex items-center gap-1">
                <label
                  className={cn(
                    "flex h-8 w-72 cursor-pointer items-center gap-2 rounded-lg border border-dashed px-2.5 text-sm hover:bg-accent",
                    pdf ? "text-foreground" : "text-muted-foreground",
                    (lanzando || !!url) && "pointer-events-none opacity-50"
                  )}
                  onDragOver={(e) => e.preventDefault()}
                  onDrop={(e) => {
                    e.preventDefault()
                    if (!url) elegirPdf(e.dataTransfer.files[0])
                  }}
                >
                  <FileUpIcon className="size-4 shrink-0" />
                  <span className="truncate">
                    {pdf?.nombre ?? "Arrastra un PDF o elígelo"}
                  </span>
                  <input
                    type="file"
                    accept="application/pdf"
                    className="sr-only"
                    disabled={!!url}
                    onChange={(e) => {
                      elegirPdf(e.target.files?.[0])
                      e.target.value = ""
                    }}
                  />
                </label>
                {pdf && (
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label="Quitar el PDF"
                    onClick={() => setPdf(undefined)}
                  >
                    <XIcon />
                  </Button>
                )}
              </div>
            </CampoFiltro>
            <CampoFiltro etiqueta="o URL del PDF (la descarga el Worker, como la del ERP)">
              <Input
                type="url"
                className="w-96"
                placeholder="https://facturas.repuestos-serca.es/generate.php?id=…"
                value={pdfUrl}
                disabled={!!pdf}
                onChange={(e) => setPdfUrl(e.target.value)}
              />
            </CampoFiltro>
            {!pdf && !url && (
              <span className="pb-2 text-xs text-muted-foreground">
                Sin PDF, la API manda uno mínimo.
              </span>
            )}
          </div>

          {escenarios.length > 0 && (
            <div className="flex flex-wrap items-center gap-2 px-4 pb-3">
              <span className="text-[11px] font-medium text-muted-foreground">
                Escenarios guardados
              </span>
              {escenarios.map((e) => (
                <span
                  key={e.nombre}
                  className="flex items-center rounded-md border"
                >
                  <button
                    type="button"
                    className="px-2 py-1 text-xs hover:bg-accent"
                    onClick={() => {
                      setNombre(e.nombre)
                      setLotes(e.lotes)
                    }}
                  >
                    {e.nombre}
                  </button>
                  <button
                    type="button"
                    aria-label={`Borrar el escenario ${e.nombre}`}
                    className="border-l px-1.5 py-1 text-muted-foreground hover:bg-accent"
                    onClick={() => {
                      const nuevos = escenarios.filter(
                        (x) => x.nombre !== e.nombre
                      )
                      setEscenarios(nuevos)
                      guardarEscenarios(nuevos)
                    }}
                  >
                    <XIcon className="size-3" />
                  </button>
                </span>
              ))}
            </div>
          )}

          <div className="border-t px-4 pt-2 pb-3">
            <Table>
              <TableHeader>
                <TableRow className="hover:bg-transparent">
                  <TableHead className="w-60">Tienda</TableHead>
                  <TableHead className="w-60">Documento</TableHead>
                  <TableHead className="w-40">Canal</TableHead>
                  <TableHead className="w-32">Cantidad</TableHead>
                  <TableHead>Saldrá por</TableHead>
                  <TableHead className="w-10" />
                </TableRow>
              </TableHeader>
              <TableBody>
                {lotes.map((l, i) => (
                  <TableRow key={i} className="hover:bg-transparent">
                    <TableCell>
                      <FiltroLista
                        multiple={false}
                        // Sin el borde de «filtro activo»: aquí es un campo.
                        className="w-full border-input"
                        opciones={opcionesTienda}
                        value={l.tienda ? [l.tienda] : []}
                        onChange={(v) => cambiarLote(i, { tienda: v[0] ?? "" })}
                        todas="Elige la tienda"
                        plural="tiendas"
                        buscar="Buscar tienda…"
                      />
                    </TableCell>
                    <TableCell>
                      <div className="flex gap-1.5">
                        <Select
                          items={OPCIONES_TIPO}
                          value={esTipoErp(l.tipo) ? l.tipo : OTRO}
                          onValueChange={(v) =>
                            cambiarLote(i, {
                              tipo: v === OTRO ? "" : (v as string),
                            })
                          }
                        >
                          <SelectTrigger
                            aria-label="Documento"
                            className={esTipoErp(l.tipo) ? "w-full" : "w-24"}
                          >
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {OPCIONES_TIPO.map((o) => (
                              <SelectItem key={o.value} value={o.value}>
                                {o.label}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                        {!esTipoErp(l.tipo) && (
                          <Input
                            autoFocus={!l.tipo}
                            aria-label="Otro tipo de documento"
                            placeholder="Escribe el tipo"
                            value={l.tipo}
                            onChange={(e) =>
                              cambiarLote(i, { tipo: e.target.value })
                            }
                          />
                        )}
                      </div>
                    </TableCell>
                    <TableCell>
                      <Select
                        items={canales.map((c) => ({ value: c, label: c }))}
                        value={l.canal.trim() || "DEFAULT"}
                        onValueChange={(v) =>
                          cambiarLote(i, { canal: v as string })
                        }
                      >
                        <SelectTrigger aria-label="Canal" className="w-full">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          {canales.map((c) => (
                            <SelectItem key={c} value={c}>
                              {c}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </TableCell>
                    <TableCell>
                      <Contador
                        etiqueta="Cantidad de trabajos"
                        value={l.cantidad}
                        onChange={(cantidad) => cambiarLote(i, { cantidad })}
                        min={1}
                        max={50}
                      />
                    </TableCell>
                    <TableCell>
                      <SaldraPor destino={destinos[i]} />
                    </TableCell>
                    <TableCell>
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        aria-label="Quitar lote"
                        disabled={lotes.length === 1}
                        onClick={() =>
                          setLotes((ls) => ls.filter((_, j) => j !== i))
                        }
                      >
                        <XIcon />
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <Button
              variant="outline"
              size="xs"
              className="mt-2"
              onClick={() => setLotes((ls) => [...ls, LOTE])}
            >
              <PlusIcon data-icon="inline-start" />
              Añadir lote
            </Button>
          </div>
        </fieldset>

        <div className="flex flex-wrap items-center gap-2 border-t px-4 py-3">
          <p className="text-sm">
            <span className="font-medium tabular-nums">
              {plural(total, "trabajo", "trabajos")}
            </span>
            {reparto.size > 0 && ": "}
            {[...reparto].map(([clave, r], k) => (
              <span key={clave}>
                {k > 0 && " · "}
                {r.error ? (
                  <span className="text-destructive">{r.n} en Error</span>
                ) : (
                  `${r.n} a ${clave}`
                )}
              </span>
            ))}
          </p>
          <Button
            variant="outline"
            size="sm"
            className="ml-auto"
            disabled={lanzando || !nombre.trim()}
            onClick={() => {
              const nuevos = [
                ...escenarios.filter((e) => e.nombre !== nombre.trim()),
                { nombre: nombre.trim(), lotes },
              ]
              setEscenarios(nuevos)
              guardarEscenarios(nuevos)
              toast.success("Escenario guardado en este navegador")
            }}
          >
            <SaveIcon data-icon="inline-start" />
            Guardar escenario
          </Button>
          {lanzando && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => (detener.current = true)}
            >
              <SquareIcon data-icon="inline-start" />
              Detener
            </Button>
          )}
          <Button size="sm" disabled={lanzando} onClick={lanzar}>
            <PlayIcon data-icon="inline-start" />
            {lanzando
              ? `Creando ${progreso!.hechos + progreso!.fallos} de ${progreso!.total}…`
              : `Lanzar ${plural(total, "trabajo", "trabajos")}`}
          </Button>
        </div>
      </section>

      {prueba && (
        <section className="space-y-3 rounded-lg border bg-card p-4">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="font-semibold">Prueba</h2>
            <span className="font-mono text-xs text-muted-foreground">
              {prueba}
            </span>
            <span className="text-xs text-muted-foreground">
              {resumen.map(([e, n]) => `${n} ${e.toLowerCase()}`).join(" · ") ||
                "sin trabajos todavía"}
              {mediaHastaSalir(trabajos.data?.value ?? []) &&
                ` · salen en ${mediaHastaSalir(trabajos.data?.value ?? [])} de media`}
            </span>
            <Button
              variant="outline"
              size="xs"
              className="ml-auto"
              disabled={cancelar.isPending}
              onClick={() =>
                cancelar.mutate(prueba, {
                  onSuccess: (r) =>
                    toast.success(
                      plural(
                        r.cancelled,
                        "trabajo cancelado",
                        "trabajos cancelados"
                      )
                    ),
                  onError: (e) =>
                    toast.error(
                      e instanceof ErrorApi
                        ? e.problema.title
                        : "No se pudo cancelar"
                    ),
                })
              }
            >
              Cancelar esta prueba
            </Button>
          </div>
          <DataTable
            columns={columnas}
            data={trabajos.data?.value ?? []}
            paginar={false}
            emptyContent="Aún no ha entrado ningún trabajo: el Worker los recoge cada 2 s."
          />
        </section>
      )}

      <div>
        <Button
          variant="ghost"
          size="sm"
          disabled={cancelar.isPending}
          onClick={() =>
            window.confirm(
              "Se cancelarán todos los trabajos de prueba que sigan pendientes, de cualquier prueba."
            ) &&
            cancelar.mutate(undefined, {
              onSuccess: (r) =>
                toast.success(
                  plural(
                    r.cancelled,
                    "trabajo cancelado",
                    "trabajos cancelados"
                  )
                ),
              onError: (e) =>
                toast.error(
                  e instanceof ErrorApi
                    ? e.problema.title
                    : "No se pudo cancelar"
                ),
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
    cell: ({ row: { original: t } }) => (
      <span className="block truncate font-mono">{t.externalJobId}</span>
    ),
  },
  {
    id: "tienda",
    header: "Tienda",
    size: 80,
    cell: ({ row: { original: t } }) => t.storeId,
  },
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
    cell: ({ row: { original: t } }) =>
      t.printerName ?? (
        <span className="text-muted-foreground">Sin asignar</span>
      ),
  },
  {
    // De entrar en la cola a salir hacia la impresora.
    id: "tardo",
    header: "Tardó",
    size: 80,
    meta: { num: true },
    cell: ({ row: { original: t } }) => (
      <span className="block text-right tabular-nums">
        {duracion(segundosHastaSalir(t))}
      </span>
    ),
  },
  {
    id: "intentos",
    header: "Intentos",
    size: 96,
    meta: { num: true },
    cell: ({ row: { original: t } }) => (
      <span className="block text-right tabular-nums">{t.attemptCount}</span>
    ),
  },
]

// Segundos de entrar en la cola a salir hacia la impresora, o null si aún no salió.
const segundosHastaSalir = (t: TrabajoCola) =>
  t.sentAtUtc
    ? Math.max(0, (Date.parse(t.sentAtUtc) - Date.parse(t.createdAtUtc)) / 1000)
    : null

const duracion = (s: number | null) =>
  s === null
    ? "—"
    : s < 60
      ? `${s.toFixed(s < 10 ? 1 : 0)} s`
      : `${Math.floor(s / 60)} min ${Math.round(s % 60)} s`

function mediaHastaSalir(trabajos: TrabajoCola[]) {
  const s = trabajos.map(segundosHastaSalir).filter((x) => x !== null)
  return s.length ? duracion(s.reduce((a, b) => a + b, 0) / s.length) : null
}

// Número con − y +, que no pasa de sus límites; se puede escribir a mano.
function Contador({
  etiqueta,
  value,
  onChange,
  min,
  max,
  unidad,
}: {
  etiqueta: string
  value: string
  onChange: (v: string) => void
  min: number
  max: number
  unidad?: string
}) {
  const n = Number(value)
  const poner = (v: number) => onChange(String(Math.min(max, Math.max(min, v))))
  return (
    <ButtonGroup>
      <Button
        variant="outline"
        size="icon"
        aria-label={`${etiqueta}: menos`}
        disabled={!(n > min)}
        onClick={() => poner(n - 1)}
      >
        <MinusIcon />
      </Button>
      <Input
        className="w-12 text-center tabular-nums"
        inputMode="numeric"
        aria-label={etiqueta}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
      {unidad && (
        <span className="flex items-center border border-l-0 bg-muted px-2 text-xs text-muted-foreground">
          {unidad}
        </span>
      )}
      <Button
        variant="outline"
        size="icon"
        aria-label={`${etiqueta}: más`}
        disabled={n >= max}
        onClick={() => poner(Number.isFinite(n) ? n + 1 : min)}
      >
        <PlusIcon />
      </Button>
    </ButtonGroup>
  )
}

// La impresora que le toca al lote con las reglas de ahora (el simulador de
// Reglas) y si responde; sin regla, los trabajos quedarían en Error.
function SaldraPor({ destino: d }: { destino: Destino }) {
  if (d.tipo === "incompleto")
    return <span className="text-muted-foreground">—</span>
  if (d.tipo === "buscando")
    return <span className="text-xs text-muted-foreground">Buscando…</span>
  if (d.tipo === "fallo")
    return (
      <span className="text-xs text-muted-foreground">
        No se ha podido consultar
      </span>
    )
  if (d.tipo === "sinRegla")
    return (
      <span className="flex items-center gap-1.5 text-destructive">
        <TriangleAlertIcon className="size-4 shrink-0" />
        <span className="leading-tight">
          Sin regla
          <span className="block text-xs">quedarán en Error</span>
        </span>
      </span>
    )
  const i = d.impresora
  return (
    <span className="flex items-center gap-2">
      <span
        aria-hidden
        className={cn(
          "size-2 shrink-0 rounded-full",
          COLOR_PUNTO[i ? TONO_SEVERIDAD[i.connectivitySeverity] : "neutro"]
        )}
      />
      <span className="min-w-0 leading-tight">
        <span className="block truncate">{d.nombre}</span>
        <span className="block truncate text-xs text-muted-foreground">
          regla {d.ruleId}
          {i && ` · ${i.connectivityLabel.toLowerCase()}`}
        </span>
      </span>
    </span>
  )
}
