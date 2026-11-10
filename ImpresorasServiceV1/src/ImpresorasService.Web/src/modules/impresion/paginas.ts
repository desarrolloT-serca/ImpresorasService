import type { PaginasImpresora, PeriodoPaginas, Tienda } from "@serca/contratos"

import type { Tabla } from "@/core/exportar"
import { fechaIso } from "@/lib/format"
import { fechaHora, nombreTienda } from "./formato"

// Páginas impresas por impresora y rango de fechas (docs/73 del ERP, opción
// E). El Worker guarda cada noche a las 00:00 el contador acumulado de cada
// impresora (en las Canon, el 113 «Total»: impresión, copias e informes); las
// páginas de un día son la lectura del día siguiente menos la suya. Solo
// administración: lo abre el botón de la pestaña Impresoras.

export const isoLocal = (f: Date) =>
  `${f.getFullYear()}-${String(f.getMonth() + 1).padStart(2, "0")}-${String(f.getDate()).padStart(2, "0")}`
export const sumarDias = (iso: string, n: number) => {
  const [a, m, d] = iso.split("-").map(Number)
  return isoLocal(new Date(a, m - 1, d + n))
}
const diasEntre = (desde: string, hasta: string) => {
  const utc = (iso: string) => {
    const [a, m, d] = iso.split("-").map(Number)
    return Date.UTC(a, m - 1, d)
  }
  return Math.round((utc(hasta) - utc(desde)) / 86_400_000)
}
// «HH:MM» de una lectura, en hora de Madrid, si no fue en torno a las 00:00
// (el Worker reintenta cada pocos minutos: hasta la 01:00 es puntual).
const horaTardia = (iso: string) => {
  const hora = fechaHora(iso).slice(11)
  return hora > "01:00" ? hora : null
}

// Día o días que cubre un periodo: «10/11/2026» o «08/11/2026 – 09/11/2026».
export const diasPeriodo = (p: PeriodoPaginas) => {
  const ultimo = sumarDias(p.to, -1)
  return ultimo === p.from
    ? fechaIso(p.from)
    : `${fechaIso(p.from)} – ${fechaIso(ultimo)}`
}

// Lo que hay que saber de un periodo para fiarse de su número.
export function avisosPeriodo(p: PeriodoPaginas) {
  const avisos: string[] = []
  if (p.pages === null)
    avisos.push("El contador bajó: ¿impresora cambiada o reiniciada?")
  const dias = diasEntre(p.from, p.to)
  if (dias > 1) avisos.push(`Falta la lectura de algún día: suma ${dias} días`)
  const empieza = horaTardia(p.fromReadAtUtc)
  if (empieza) avisos.push(`Empieza a las ${empieza}`)
  const acaba = horaTardia(p.toReadAtUtc)
  if (acaba) avisos.push(`Acaba a las ${acaba} del día siguiente`)
  return avisos
}

// Si lo leído no cubre justo las fechas pedidas, por qué. `hoy`, para decir
// que el día de hoy se cuenta esta noche.
export function avisosRango(
  i: PaginasImpresora,
  desde: string,
  hasta: string,
  hoy: string
) {
  const p = i.periods
  // Con una sola lectura (el primer día que se guarda) aún no hay páginas: la
  // API da su origen, pero ningún periodo.
  if (!p.length)
    return [
      i.source
        ? "Solo hay una lectura: las páginas salen con la de las 00:00 siguientes"
        : "Sin lecturas en estas fechas",
    ]
  const avisos: string[] = []
  const inicio = p[0].from
  const fin = sumarDias(p[p.length - 1].to, -1)
  if (inicio > desde)
    avisos.push(`Desde el ${fechaIso(inicio)}: no hay lecturas anteriores`)
  if (inicio < desde)
    avisos.push(
      `Desde el ${fechaIso(inicio)}: falta la lectura del ${fechaIso(desde)}`
    )
  if (fin < hasta)
    avisos.push(
      hasta >= hoy
        ? `Hasta el ${fechaIso(fin)}: cada día se cuenta a las 00:00 del siguiente`
        : `Hasta el ${fechaIso(fin)}: falta la lectura del ${fechaIso(sumarDias(hasta, 1))}`
    )
  if (fin > hasta)
    avisos.push(
      `Hasta el ${fechaIso(fin)}: falta la lectura del ${fechaIso(sumarDias(hasta, 1))}`
    )
  const conAvisos = p.filter((x) => avisosPeriodo(x).length).length
  if (conAvisos)
    avisos.push(
      conAvisos === 1
        ? "1 periodo con avisos"
        : `${conAvisos} periodos con avisos`
    )
  return avisos
}

// Lo que se ve, para Excel: con «Todas», una fila por impresora; con una,
// una fila por día y el total. Números sin separador de miles y fechas
// dd/mm/aaaa, que Excel en español lee como tales.
export function tablaExportar(
  lista: readonly PaginasImpresora[],
  tiendas: readonly Tienda[],
  desde: string,
  hasta: string,
  hoy: string,
  detalle: boolean
): Tabla {
  const n = (v: number | null) => (v === null ? "" : String(v))
  if (!detalle)
    return [
      ["Tienda", "Impresora", "Desde", "Hasta", "Páginas", "Avisos"],
      lista.map((i) => [
        nombreTienda(i.storeId, tiendas),
        i.printerName,
        i.periods.length ? fechaIso(i.periods[0].from) : "",
        i.periods.length
          ? fechaIso(sumarDias(i.periods[i.periods.length - 1].to, -1))
          : "",
        n(i.pages),
        avisosRango(i, desde, hasta, hoy).join(". "),
      ]),
    ]
  const i = lista[0]
  return [
    ["Tienda", "Impresora", "Día", "Páginas", "Avisos"],
    [
      ...(i?.periods ?? []).map((p) => [
        nombreTienda(i.storeId, tiendas),
        i.printerName,
        diasPeriodo(p),
        n(p.pages),
        avisosPeriodo(p).join(". "),
      ]),
      ...(i ? [["", "", "Total", n(i.pages), ""]] : []),
    ],
  ]
}
