import { useState } from "react"
import { PlusIcon, XIcon } from "lucide-react"
import { toast } from "sonner"
import type { ReglasUmbral, SeveridadUmbral } from "@serca/contratos"

import { Panel } from "@/components/ficha"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Spinner } from "@/components/ui/spinner"
import { ErrorApi } from "@/core/api"
import { useGuardarUmbrales, useUmbrales } from "./api"

type Metrica = keyof ReglasUmbral
// Como ajustes.blade.php del PHP.
const METRICAS: { clave: Metrica; titulo: string; ayuda: string; valor: string }[] = [
  { clave: "queue", titulo: "Cola de impresión", ayuda: "Trabajos en cola ahora mismo", valor: "Cola ≥" },
  { clave: "failed", titulo: "Fallos sin reenviar", ayuda: "Errores finales sin reintentar", valor: "Fallos ≥" },
  { clave: "missingHost", titulo: "Impresoras sin host", ayuda: "Impresoras sin host ni ruta UNC", valor: "Sin host ≥" },
  { clave: "conn", titulo: "Fallos de conexión", ayuda: "Fallos seguidos al comprobar una impresora", valor: "Seguidos ≥" },
]
const SEVERIDADES: { value: SeveridadUmbral; label: string }[] = [
  { value: "info", label: "Info" },
  { value: "warning", label: "Aviso" },
  { value: "critical", label: "Crítica" },
]

// Los valores se editan como texto (solo dígitos) y se convierten al guardar.
type Fila = { min: string; severity: SeveridadUmbral }
type Edicion = Record<Metrica, Fila[]>

const aEdicion = (r: ReglasUmbral): Edicion =>
  Object.fromEntries(
    METRICAS.map(({ clave }) => [
      clave,
      r[clave].map((u) => ({ min: String(u.min), severity: u.severity })),
    ])
  ) as Edicion

// Umbrales de salud (ajustes.blade.php del PHP): de qué cifra en adelante una
// tienda pasa a info, aviso o crítica. Los usan el dashboard y las alertas de
// Telegram. La API comprueba que crezcan con la severidad.
export default function Umbrales() {
  const umbrales = useUmbrales()
  if (!umbrales.data)
    return (
      <Panel className="items-center justify-center">
        <Spinner />
      </Panel>
    )
  return <Editor inicial={umbrales.data} />
}

function Editor({ inicial }: { inicial: ReglasUmbral }) {
  const guardar = useGuardarUmbrales()
  const [edicion, setEdicion] = useState(() => aEdicion(inicial))
  const [mensaje, setMensaje] = useState("")
  const cambiado = JSON.stringify(edicion) !== JSON.stringify(aEdicion(inicial))
  const cambiar = (m: Metrica, filas: Fila[]) => {
    setEdicion((e) => ({ ...e, [m]: filas }))
    setMensaje("")
  }

  function enviar() {
    const malos = METRICAS.filter(({ clave }) =>
      edicion[clave].some((f) => !/^\d{1,9}$/.test(f.min.trim()))
    )
    if (malos.length) {
      setMensaje(`Indica un número entero en ${malos.map((m) => m.titulo).join(", ")}.`)
      return
    }
    guardar.mutate(
      Object.fromEntries(
        METRICAS.map(({ clave }) => [
          clave,
          edicion[clave].map((f) => ({ min: Number(f.min), severity: f.severity })),
        ])
      ) as ReglasUmbral,
      {
        onSuccess: (r) => {
          setEdicion(aEdicion(r))
          toast.success("Umbrales guardados")
        },
        onError: (e) =>
          setMensaje(e instanceof ErrorApi ? e.problema.title : "No se pudieron guardar"),
      }
    )
  }

  return (
    <Panel>
      <div className="grid gap-4 lg:grid-cols-2">
        {METRICAS.map((m) => (
          <section key={m.clave} className="space-y-3 rounded-lg border bg-card p-4">
            <div>
              <h2 className="font-semibold">{m.titulo}</h2>
              <p className="text-xs text-muted-foreground">{m.ayuda}</p>
            </div>
            {edicion[m.clave].map((f, i) => (
              <div key={i} className="flex items-center gap-2">
                <label htmlFor={`umbral-${m.clave}-${i}`} className="w-24 text-sm">
                  {m.valor}
                </label>
                <Input
                  id={`umbral-${m.clave}-${i}`}
                  inputMode="numeric"
                  className="w-24"
                  value={f.min}
                  disabled={guardar.isPending}
                  onChange={(e) =>
                    cambiar(
                      m.clave,
                      edicion[m.clave].map((x, j) => (j === i ? { ...x, min: e.target.value } : x))
                    )
                  }
                />
                <Select
                  items={SEVERIDADES}
                  value={f.severity}
                  disabled={guardar.isPending}
                  onValueChange={(v) =>
                    cambiar(
                      m.clave,
                      edicion[m.clave].map((x, j) =>
                        j === i ? { ...x, severity: v as SeveridadUmbral } : x
                      )
                    )
                  }
                >
                  <SelectTrigger className="w-32" aria-label="Severidad">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {SEVERIDADES.map((s) => (
                      <SelectItem key={s.value} value={s.value}>
                        {s.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label="Quitar umbral"
                  disabled={guardar.isPending || edicion[m.clave].length === 1}
                  onClick={() =>
                    cambiar(m.clave, edicion[m.clave].filter((_, j) => j !== i))
                  }
                >
                  <XIcon />
                </Button>
              </div>
            ))}
            {edicion[m.clave].length < 3 && (
              <Button
                variant="outline"
                size="xs"
                disabled={guardar.isPending}
                onClick={() =>
                  cambiar(m.clave, [
                    ...edicion[m.clave],
                    {
                      min: "",
                      // La primera severidad que falte.
                      severity:
                        SEVERIDADES.find(
                          (s) => !edicion[m.clave].some((f) => f.severity === s.value)
                        )?.value ?? "critical",
                    },
                  ])
                }
              >
                <PlusIcon data-icon="inline-start" />
                Añadir umbral
              </Button>
            )}
          </section>
        ))}
      </div>
      <div className="flex items-center justify-end gap-3">
        <p role="status" aria-live="polite" className="text-sm text-destructive">
          {mensaje}
        </p>
        <Button
          variant="outline"
          size="sm"
          disabled={!cambiado || guardar.isPending}
          onClick={() => {
            setEdicion(aEdicion(inicial))
            setMensaje("")
          }}
        >
          Deshacer
        </Button>
        <Button size="sm" disabled={!cambiado || guardar.isPending} onClick={enviar}>
          {guardar.isPending ? "Guardando…" : "Guardar umbrales"}
        </Button>
      </div>
    </Panel>
  )
}
