const eurFmt = new Intl.NumberFormat("es-ES", {
  style: "currency",
  currency: "EUR",
})
const numFmt = new Intl.NumberFormat("es-ES")
const dosDecimales = new Intl.NumberFormat("es-ES", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

// Los importes llegan de la API como texto decimal; Intl los formatea sin
// pasarlos por coma flotante.
type Numero = number | `${number}` | string
const n = (v: Numero) => v as Intl.StringNumericLiteral

// Para buscar sin distinguir mayúsculas ni acentos.
export const normalizar = (s: string) =>
  s.toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "")

export const eur = (v: Numero) => eurFmt.format(n(v))
export const num = (v: Numero) => numFmt.format(n(v))
export const pct = (v: Numero) => `${numFmt.format(n(v))} %`
// Importe de las consultas de ventas: dos decimales, sin símbolo (la moneda
// va en el título) y sin agrupar los de cuatro cifras, a diferencia de la ficha.
export const dinero = (v: Numero) => dosDecimales.format(n(v))
// Fecha de la base ("AAAA-MM-DD") a "DD/MM/AAAA" sin pasar por Date, que la
// interpretaría en UTC y podría cambiar el día según la zona horaria.
export const fechaIso = (iso: string) => {
  const [a, m, d] = iso.split("-")
  return `${d}/${m}/${a}`
}
// Fecha y hora de fdobser ("AAAA-MM-DDTHH:MM:SS") como "DD/MM/AAAA HH:MM".
export const fechaHoraFicha = (v: string) =>
  `${fechaIso(v.slice(0, 10))} ${v.slice(11, 16)}`
export const fechaLarga = (d: Date) =>
  d.toLocaleDateString("es-ES", {
    weekday: "long",
    day: "numeric",
    month: "long",
  })

// Códigos con su nombre, para los criterios de los listados impresos.
export const nombres = (
  cods: string[],
  opciones: { cod: string; nombre: string }[] | undefined
) =>
  cods
    .map((c) => `${c} ${opciones?.find((o) => o.cod === c)?.nombre ?? ""}`)
    .join(", ")

// Formato de la ficha heredada: agrupación también con cuatro dígitos.
const decimal = new Intl.NumberFormat("es-ES", {
  useGrouping: "always",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})
const entero = new Intl.NumberFormat("es-ES", {
  useGrouping: "always",
  maximumFractionDigits: 0,
})
export const decimalFicha = (v: string | number | null | undefined) =>
  v == null ? "" : decimal.format(v as Intl.StringNumericLiteral)
export const enteroFicha = (v: string | number | null | undefined) =>
  v == null ? "" : entero.format(v as Intl.StringNumericLiteral)
// Suma exacta de valores numeric de la base (texto), sin coma flotante: se
// pasan a enteros con 4 decimales. null si no hay ningún valor.
const ESCALA = 4
export const sumaFicha = (valores: (string | null | undefined)[]) => {
  const presentes = valores.filter((v): v is string => v != null)
  if (!presentes.length) return null
  const total = presentes.reduce((t, v) => {
    const [entera, decimales = ""] = v.replace("-", "").split(".")
    const n = BigInt(entera + decimales.padEnd(ESCALA, "0").slice(0, ESCALA))
    return t + (v.startsWith("-") ? -n : n)
  }, 0n)
  const digitos = (total < 0n ? -total : total)
    .toString()
    .padStart(ESCALA + 1, "0")
  return `${total < 0n ? "-" : ""}${digitos.slice(0, -ESCALA)}.${digitos.slice(-ESCALA)}`
}
export const porcentajeFicha = (v: string | null | undefined) =>
  v == null ? "" : `${decimalFicha(v)} %`
// Importe con su moneda: «€» para el euro (EU) y el código para las demás.
export const importeMoneda = (importe: string, moneda: string) =>
  `${decimalFicha(importe)} ${moneda === "EU" ? "€" : moneda}`
// Teléfono en pantalla: solo las cifras, sin guiones ni otros símbolos en
// medio, para copiarlo sin retocar (decisión del 15/10/2026). Es solo
// presentación: el dato de la base no cambia.
export const telefono = (v: string | null | undefined) =>
  (v ?? "").replace(/\D/g, "")

// Meses de las tablas por mes (ventas, consumo, departamentos).
export const MESES = [
  "Enero",
  "Febrero",
  "Marzo",
  "Abril",
  "Mayo",
  "Junio",
  "Julio",
  "Agosto",
  "Septiembre",
  "Octubre",
  "Noviembre",
  "Diciembre",
]
