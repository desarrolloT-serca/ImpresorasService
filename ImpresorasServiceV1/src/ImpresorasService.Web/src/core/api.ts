import type { z } from "zod"
import { CABECERA_EMPRESA, Problema } from "@serca/contratos"

// Error de la API con el formato común (problem+json).
export class ErrorApi extends Error {
  readonly problema: Problema
  constructor(problema: Problema) {
    super(problema.detail ?? problema.title)
    this.problema = problema
  }
}

export type OpcionesApi = {
  // Sociedad de trabajo. Sale de la clave de la consulta, nunca del estado
  // global: así la respuesta siempre se guarda con la empresa que la pidió.
  empresa?: number | null
  // Para cancelar la petición si la consulta deja de interesar.
  signal?: AbortSignal
  body?: unknown
  // Impresión: la API .NET edita con PUT y borra con DELETE.
  metodo?: "PUT" | "DELETE"
}

// Único punto de acceso HTTP. Valida la respuesta con el contrato compartido:
// si la API cambia, falla aquí y no en una pantalla.
export async function api<T extends z.ZodType>(
  ruta: string,
  esquema: T,
  { empresa, signal, body, metodo }: OpcionesApi = {}
): Promise<z.output<T>> {
  const res = await fetch(`/api${ruta}`, {
    signal,
    ...(body === undefined
      ? {}
      : { method: "POST", body: JSON.stringify(body) }),
    ...(metodo && { method: metodo }),
    headers: {
      ...(empresa ? { [CABECERA_EMPRESA]: String(empresa) } : {}),
      ...(body === undefined ? {} : { "Content-Type": "application/json" }),
    },
  }).catch((e: unknown) => {
    // Sin red el navegador da «Failed to fetch» (en inglés). La cancelación
    // (AbortError) se deja pasar tal cual.
    if (e instanceof TypeError)
      throw new ErrorApi({ status: 0, title: "Sin conexión con el servidor" })
    throw e
  })
  const cuerpo = await res.json().catch(() => null)
  if (!res.ok)
    throw new ErrorApi(
      // Sin problem+json (API parada tras el proxy): statusText va en inglés.
      Problema.safeParse(cuerpo).data ?? {
        status: res.status,
        // Impresión: la API .NET responde { error }. Dentro del ERP no hace
        // falta: su pasarela ya devuelve problem+json.
        title: errorNet(cuerpo) ?? "Sin respuesta del servidor",
      }
    )
  return esquema.parse(cuerpo)
}

// { error: "…" } o, en los umbrales, { errors: ["…", …] }.
const errorNet = (cuerpo: unknown) => {
  if (typeof cuerpo !== "object" || cuerpo === null) return undefined
  if ("error" in cuerpo && typeof cuerpo.error === "string") return cuerpo.error
  if ("errors" in cuerpo && Array.isArray(cuerpo.errors))
    return cuerpo.errors.filter((e) => typeof e === "string").join(" ") || undefined
  return undefined
}

// Parámetros de consulta sin los vacíos.
// Las listas van separadas por comas.
export const params = (
  o: Record<string, string | number | string[] | undefined>
) =>
  new URLSearchParams(
    Object.entries(o)
      .filter(([, v]) => v !== undefined && v !== "")
      .map(([k, v]) => [k, String(v)])
  ).toString()

// Mientras carga la página siguiente se sigue viendo la anterior, salvo que
// sea de otra sociedad: nunca se muestran datos de una empresa con otra activa.
export const mantenerSiMismaEmpresa =
  (empresa: number | null) =>
  <D>(anterior: D | undefined, consulta?: { queryKey: readonly unknown[] }) =>
    consulta?.queryKey[1] === empresa ? anterior : undefined
