import { useContext, useEffect, useRef } from "react"
import { create } from "zustand"
import type { z } from "zod"
import { CriteriosAvanzadosCola } from "@serca/contratos"
import type { Opcion } from "@/components/filtro-lista"
import { nombres } from "@/lib/format"
import { PanelIdCtx } from "@/workspace/modulo"

// Campos de cada búsqueda avanzada: clave, etiqueta del formulario, etiqueta
// corta (chips y listado impreso) y tipo («sn»: desplegable Todos/S/N;
// «lista»: varios códigos numéricos; «textos»: varios códigos de texto;
// «fecha»: un día). Copia de la del ERP (apps/web/src/core/avanzados.ts) con
// solo lo que usa el módulo de impresión.
export type Campo = readonly [
  clave: string,
  etiqueta: string,
  corta: string,
  tipo?: "sn" | "lista" | "textos" | "opcion" | "fecha",
]
export const AVANZADOS = {
  cola: {
    esquema: CriteriosAvanzadosCola,
    // Tienda y estado, en la barra.
    campos: [
      ["impresoras", "Impresoras:", "Impresoras", "lista"],
      ["tipos", "Tipo de documento:", "Tipo", "textos"],
      ["desde", "Fecha desde:", "Desde", "fecha"],
      ["hasta", "Fecha hasta:", "Hasta", "fecha"],
    ],
    ayuda: [
      [
        "Impresoras",
        "Una o varias. Con una tienda elegida en la barra, solo las suyas.",
      ],
      ["Tipo de documento", "Uno o varios tipos de documento del ERP."],
      [
        "Fechas",
        "Días enteros, los dos incluidos, sobre la última vez que se mandó imprimir (llegada, reintento o reimpresión). Vacías, sin límite.",
      ],
    ],
  },
} as const satisfies Record<
  string,
  {
    esquema: z.ZodType
    campos: readonly Campo[]
    ayuda: string | readonly (readonly [string, string])[]
  }
>
export type TipoAvanzado = keyof typeof AVANZADOS
export type CriteriosDe<T extends TipoAvanzado> = z.output<
  (typeof AVANZADOS)[T]["esquema"]
>
type Criterios = Record<string, unknown>

export type OpcionesAvanzadas = Record<string, Opcion[]>
export const resumenAvanzados = (
  tipo: TipoAvanzado,
  c: Criterios,
  opciones?: OpcionesAvanzadas
) =>
  AVANZADOS[tipo].campos
    .filter(([k]) => c[k] !== undefined)
    .map(([k, , corta]) => {
      const valor = String(c[k])
      const opcion = opciones?.[k]?.find((o) => o.cod === valor)
      return `${corta}: ${Array.isArray(c[k]) ? nombres(c[k].map(String), opciones?.[k]) : opcion && opcion.nombre !== valor ? `${valor} · ${opcion.nombre}` : valor}`
    })

// Búsqueda avanzada por pestaña: cada pantalla que la admite se registra con
// el id de su panel, con sus criterios y cómo aplicarlos. El botón de la barra
// solo se activa en esas pestañas y aplica a la activa; cerrar la pestaña
// retira sus criterios. Para otra pantalla: una entrada más en AVANZADOS.
export type BusquedaAvanzadaPanel = {
  tipo: TipoAvanzado
  criterios: Criterios
  aplicar: (c: Criterios) => void
  // Una consulta puede ofrecer distintos campos según su tipo/subtipo.
  campos?: readonly string[]
  opciones?: OpcionesAvanzadas
}
export const useBusquedasAvanzadas = create<{
  paneles: Record<string, BusquedaAvanzadaPanel>
  registrar: (id: string, b: BusquedaAvanzadaPanel) => () => void
}>((set) => ({
  paneles: {},
  registrar: (id, b) => {
    set((s) => ({ paneles: { ...s.paneles, [id]: b } }))
    return () =>
      set((s) => {
        if (s.paneles[id] !== b) return s
        const paneles = { ...s.paneles }
        delete paneles[id]
        return { paneles }
      })
  },
}))

export function useBusquedaAvanzadaPanel<T extends TipoAvanzado>(
  tipo: T,
  criterios: CriteriosDe<T>,
  aplicar: (c: CriteriosDe<T>) => void,
  campos?: readonly string[],
  opciones?: OpcionesAvanzadas
) {
  const id = useContext(PanelIdCtx)
  // aplicar cambia en cada render: con una referencia, el registro solo se
  // rehace cuando cambian los criterios.
  const aplicarRef = useRef(aplicar)
  useEffect(() => {
    aplicarRef.current = aplicar
  })
  useEffect(() => {
    if (!id || campos?.length === 0) return
    return useBusquedasAvanzadas.getState().registrar(id, {
      tipo,
      criterios,
      campos,
      opciones,
      // El formulario valida con el esquema de este tipo antes de aplicar.
      aplicar: (c) => aplicarRef.current(c as CriteriosDe<T>),
    })
  }, [id, tipo, criterios, campos, opciones])
}
