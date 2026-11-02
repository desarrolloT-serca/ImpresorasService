import * as React from "react"
import { CalendarIcon } from "lucide-react"
import { es } from "react-day-picker/locale"

import { Calendar } from "@/components/ui/calendar"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupInput,
} from "@/components/ui/input-group"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { fechaIso } from "@/lib/format"
import { cn } from "@/lib/utils"

// Fecha local (sin pasar por UTC) <-> "AAAA-MM-DD".
const deIso = (iso: string) => {
  const [a, m, d] = iso.split("-").map(Number)
  return new Date(a, m - 1, d)
}
const aIso = (f: Date) =>
  `${f.getFullYear()}-${String(f.getMonth() + 1).padStart(2, "0")}-${String(f.getDate()).padStart(2, "0")}`
// Admite tanto 25092026 como 25/09/2026 y fechas cortas (5/3/2026).
// Una barra escrita tras la automática no abre un segmento adicional.
const conBarras = (texto: string, borrando = false) => {
  const partes = ["", "", ""]
  let parte = 0
  for (const caracter of texto) {
    if (/\d/.test(caracter)) {
      const limite = parte === 2 ? 4 : 2
      if (partes[parte].length === limite) {
        if (parte === 2) continue
        parte++
      }
      partes[parte] += caracter
    } else if (caracter === "/" && parte < 2 && partes[parte]) {
      parte++
    }
  }
  let resultado = partes.slice(0, parte + 1).join("/")
  if (!borrando && parte < 2 && partes[parte].length === 2) resultado += "/"
  return resultado
}
// "D/M/AAAA" escrito a mano; null si no es una fecha que exista.
const leer = (texto: string) => {
  const m = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(texto.trim())
  if (!m) return null
  const f = new Date(Number(m[3]), Number(m[2]) - 1, Number(m[1]))
  return f.getDate() === Number(m[1]) &&
    f.getMonth() === Number(m[2]) - 1 &&
    f.getFullYear() === Number(m[3])
    ? aIso(f)
    : null
}

// Fecha que se escribe (dd/mm/aaaa) o se elige en el calendario. El texto a
// medio escribir se formatea; una fecha completa y válida se aplica al instante.
export function CampoFecha({
  value,
  onChange,
  etiqueta,
  className,
  permitirVacio = false,
  name,
  form,
  max,
}: {
  value: string
  onChange: (iso: string) => void
  etiqueta: string
  className?: string
  permitirVacio?: boolean
  name?: string
  form?: string
  max?: string
}) {
  const [editando, setEditando] = React.useState<string | null>(null)
  const [abierto, setAbierto] = React.useState(false)
  const input = React.useRef<HTMLInputElement>(null)
  const cursor = React.useRef<number | null>(null)
  React.useLayoutEffect(() => {
    if (cursor.current !== null) {
      input.current?.setSelectionRange(cursor.current, cursor.current)
      cursor.current = null
    }
  })
  const confirmar = () => {
    if (editando === null) return
    if (permitirVacio && editando === "") {
      if (value) onChange("")
      setEditando(null)
      return
    }
    const iso = leer(editando)
    if (iso && iso !== value) onChange(iso)
    setEditando(null)
  }
  const fecha = value ? deIso(value) : undefined
  const visible = value ? fechaIso(value) : ""
  // El calendario va fuera del InputGroupAddon: su onClick lleva el foco al
  // input y, como los eventos de React suben por el portal, cerraba los
  // desplegables de mes y año nada más abrirlos.
  return (
    <Popover open={abierto} onOpenChange={setAbierto}>
      <InputGroup className={cn("w-36", className)}>
        <InputGroupInput
          ref={input}
          name={name}
          form={form}
          aria-label={etiqueta}
          placeholder="dd/mm/aaaa"
          autoComplete="off"
          inputMode="numeric"
          className="tabular-nums"
          value={editando ?? visible}
          onChange={(e) => {
            const texto = e.target.value
            const anterior = editando ?? visible
            const tipo = (e.nativeEvent as InputEvent).inputType
            const borrando = tipo
              ? tipo.startsWith("delete")
              : texto.length < anterior.length && anterior.startsWith(texto)
            const posicion = e.target.selectionStart ?? texto.length
            const nuevo = conBarras(texto, borrando)
            cursor.current = Math.min(
              conBarras(texto.slice(0, posicion), borrando).length,
              nuevo.length
            )
            setEditando(nuevo)
            const iso = leer(nuevo)
            if (iso && iso !== value) onChange(iso)
          }}
          onBlur={confirmar}
          onKeyDown={(e) => {
            if (e.key === "/") {
              const campo = e.currentTarget
              const posicion = campo.selectionStart ?? 0
              if (posicion === campo.selectionEnd) {
                if (campo.value[posicion] === "/") {
                  e.preventDefault()
                  campo.setSelectionRange(posicion + 1, posicion + 1)
                } else if (campo.value[posicion - 1] === "/") {
                  e.preventDefault()
                }
              }
            }
            if (e.key === "Enter") confirmar()
            // Esc deshace lo tecleado; solo eso (no reinicia la pantalla).
            if (e.key === "Escape" && editando !== null) {
              e.preventDefault()
              setEditando(null)
            }
          }}
        />
        <InputGroupAddon align="inline-end">
          <PopoverTrigger
            render={
              <InputGroupButton
                size="icon-xs"
                aria-label={`Elegir ${etiqueta.toLowerCase()} en el calendario`}
              />
            }
          >
            <CalendarIcon />
          </PopoverTrigger>
        </InputGroupAddon>
      </InputGroup>
      <PopoverContent align="end" className="w-auto p-0">
        <Calendar
          mode="single"
          locale={es}
          captionLayout="dropdown"
          startMonth={new Date(2000, 0)}
          endMonth={
            max ? deIso(max) : new Date(new Date().getFullYear() + 1, 11)
          }
          disabled={max ? { after: deIso(max) } : undefined}
          defaultMonth={fecha}
          selected={fecha}
          onSelect={(f) => {
            if (f) onChange(aIso(f))
            setEditando(null)
            setAbierto(false)
          }}
        />
      </PopoverContent>
    </Popover>
  )
}
