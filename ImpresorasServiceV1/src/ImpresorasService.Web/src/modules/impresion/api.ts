import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
} from "@tanstack/react-query"
import { z } from "zod"
import {
  ChatTelegram,
  ConfigTelegram,
  Conexion,
  DatosChat,
  Impresora,
  PaginaCola,
  PruebaCreada,
  PruebasCanceladas,
  PruebaTelegram,
  Regla,
  ReglasUmbral,
  ResultadoAccion,
  ResumenDashboard,
  SerieDashboard,
  Tienda,
  type ConsultaCola,
  type VentanaDashboard,
  type DatosImpresora,
  type DatosRegla,
  type TrabajoPrueba,
} from "@serca/contratos"
import { api, params } from "@/core/api"

// Rutas /impresion/… con las de la API .NET: el proxy (o la pasarela del ERP)
// quita «/impresion».

// La cola y las impresoras cambian solas: se releen cada 30 s mientras la
// pestaña está abierta (como la ficha de artículo del ERP con el stock).
const REFRESCO_MS = 30_000

export const useCola = (c: ConsultaCola) =>
  useQuery({
    queryKey: ["impresion", "cola", c],
    queryFn: ({ signal }) =>
      api(
        `/impresion/printjobs?${params({
          includeTotal: "true",
          page: c.pagina,
          limit: c.tamano,
          externalJobId: c.q,
          storeId: c.tienda,
          status: c.vista ? undefined : c.estado,
          failedWithoutRetry: c.vista === "sinReenviar" ? "true" : undefined,
          unassigned: c.vista === "sinImpresora" ? "true" : undefined,
        })}`,
        PaginaCola,
        { signal }
      ),
    // Mientras llega la página siguiente se sigue viendo la anterior.
    placeholderData: keepPreviousData,
    refetchInterval: REFRESCO_MS,
  })

// La API las da todas a cualquier usuario (para los nombres).
export const useTiendas = () =>
  useQuery({
    queryKey: ["impresion", "tiendas"],
    queryFn: ({ signal }) =>
      api("/impresion/stores", z.array(Tienda), { signal }),
    staleTime: 5 * 60_000,
  })

// Sin tienda, la API da las de la tienda del usuario o, al que ve todas, todas.
export const useImpresoras = () =>
  useQuery({
    queryKey: ["impresion", "impresoras"],
    queryFn: ({ signal }) =>
      api("/impresion/printers", z.array(Impresora), { signal }),
    refetchInterval: REFRESCO_MS,
  })

export type AccionTrabajo = "reintentar" | "cancelar"

// Reintentar es POST …/route: vuelve a enrutar y el Worker lo imprime.
export function useAccionTrabajo() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: ({ id, accion }: { id: string; accion: AccionTrabajo }) =>
      api(
        `/impresion/printjobs/${id}/${accion === "reintentar" ? "route" : "cancel"}`,
        ResultadoAccion,
        { body: {} }
      ),
    onSettled: () =>
      cache.invalidateQueries({ queryKey: ["impresion", "cola"] }),
  })
}

export function useGuardarImpresora() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: ({ id, datos }: { id?: number; datos: DatosImpresora }) =>
      api(id ? `/impresion/printers/${id}` : "/impresion/printers", Impresora, {
        body: datos,
        ...(id && { metodo: "PUT" as const }),
      }),
    onSettled: () =>
      cache.invalidateQueries({ queryKey: ["impresion", "impresoras"] }),
  })
}

export function useBorrarImpresora() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: (id: number) =>
      api(`/impresion/printers/${id}`, z.null(), { metodo: "DELETE" }),
    onSettled: () =>
      cache.invalidateQueries({ queryKey: ["impresion", "impresoras"] }),
  })
}

export const useReglas = () =>
  useQuery({
    queryKey: ["impresion", "reglas"],
    queryFn: ({ signal }) =>
      api("/impresion/routingrules", z.array(Regla), { signal }),
  })

// Editar exige las fechas de vigencia: se conservan las que tenía.
export function useGuardarRegla() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: ({ regla, datos }: { regla?: Regla; datos: DatosRegla }) =>
      regla
        ? api(`/impresion/routingrules/${regla.ruleId}`, z.unknown(), {
            body: {
              ...datos,
              validFromUtc: regla.validFromUtc,
              validToUtc: regla.validToUtc,
            },
            metodo: "PUT",
          })
        : api("/impresion/routingrules", z.unknown(), { body: datos }),
    onSettled: () =>
      cache.invalidateQueries({ queryKey: ["impresion", "reglas"] }),
  })
}

export function useBorrarRegla() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: (id: number) =>
      api(`/impresion/routingrules/${id}`, z.null(), { metodo: "DELETE" }),
    onSettled: () =>
      cache.invalidateQueries({ queryKey: ["impresion", "reglas"] }),
  })
}

export const useResumen = (ventana: VentanaDashboard, tienda?: number) =>
  useQuery({
    queryKey: ["impresion", "dashboard", "resumen", ventana, tienda],
    queryFn: ({ signal }) =>
      api(
        `/impresion/dashboard/overview?${params({ window: ventana, storeId: tienda })}`,
        ResumenDashboard,
        { signal }
      ),
    placeholderData: keepPreviousData,
    refetchInterval: REFRESCO_MS,
  })

export const useSerie = (ventana: VentanaDashboard, tienda?: number) =>
  useQuery({
    queryKey: ["impresion", "dashboard", "serie", ventana, tienda],
    queryFn: ({ signal }) =>
      api(
        `/impresion/dashboard/timeseries?${params({ window: ventana, storeId: tienda })}`,
        SerieDashboard,
        { signal }
      ),
    placeholderData: keepPreviousData,
    refetchInterval: REFRESCO_MS,
  })

export const useUmbrales = () =>
  useQuery({
    queryKey: ["impresion", "umbrales"],
    queryFn: ({ signal }) =>
      api("/impresion/dashboard/threshold-rules", ReglasUmbral, { signal }),
  })

export function useGuardarUmbrales() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: (reglas: ReglasUmbral) =>
      api("/impresion/dashboard/threshold-rules", ReglasUmbral, {
        body: reglas,
        metodo: "PUT",
      }),
    // El dashboard calcula la salud con ellos.
    onSuccess: (reglas) => {
      cache.setQueryData(["impresion", "umbrales"], reglas)
      return cache.invalidateQueries({ queryKey: ["impresion", "dashboard"] })
    },
  })
}

export const useConfigTelegram = () =>
  useQuery({
    queryKey: ["impresion", "telegram", "config"],
    queryFn: ({ signal }) =>
      api("/impresion/telegram/config", ConfigTelegram, { signal }),
  })

export const useChatsTelegram = () =>
  useQuery({
    queryKey: ["impresion", "telegram", "chats"],
    queryFn: ({ signal }) =>
      api("/impresion/telegram/chats", z.array(ChatTelegram), { signal }),
  })

const telegram = (cache: QueryClient) =>
  cache.invalidateQueries({ queryKey: ["impresion", "telegram"] })

export function useGuardarConfigTelegram() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: (c: Omit<ConfigTelegram, "enabled">) =>
      api("/impresion/telegram/config", z.null(), { body: c, metodo: "PUT" }),
    onSettled: () => telegram(cache),
  })
}

export function useAnadirChat() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: (c: z.output<typeof DatosChat>) =>
      api("/impresion/telegram/chats", z.unknown(), { body: c }),
    onSettled: () => telegram(cache),
  })
}

export function useBorrarChat() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: (chatId: number) =>
      api(`/impresion/telegram/chats/${chatId}`, z.null(), { metodo: "DELETE" }),
    onSettled: () => telegram(cache),
  })
}

// Manda de verdad un mensaje a todos los chats activos.
export const usePruebaTelegram = () =>
  useMutation({
    mutationFn: () =>
      api("/impresion/telegram/test", PruebaTelegram, { body: {} }),
  })

// Trabajos de una prueba: su id externo empieza por el de la prueba. Se
// releen cada 5 s mientras la pantalla los enseña.
export const useTrabajosPrueba = (prueba?: string) =>
  useQuery({
    queryKey: ["impresion", "cola", "prueba", prueba],
    queryFn: ({ signal }) =>
      api(
        `/impresion/printjobs?${params({ includeTotal: "true", limit: 100, externalJobId: prueba })}`,
        PaginaCola,
        { signal }
      ),
    enabled: !!prueba,
    refetchInterval: 5_000,
  })

export const crearTrabajoPrueba = (t: TrabajoPrueba) =>
  api("/impresion/sourceprintjobs/test", PruebaCreada, { body: t })

export function useCancelarPruebas() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: (prueba?: string) =>
      api(
        `/impresion/sourceprintjobs/test/cancel-all${prueba ? `?${params({ runId: prueba })}` : ""}`,
        PruebasCanceladas,
        { body: {} }
      ),
    onSettled: () => cache.invalidateQueries({ queryKey: ["impresion", "cola"] }),
  })
}

// Comprueba la conexión ahora y la guarda (persist): la lista la refleja.
export function useComprobarImpresora() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: (id: number) =>
      api(`/impresion/printers/${id}/netconnection?persist=true`, Conexion, {
        body: {},
      }),
    onSettled: () =>
      cache.invalidateQueries({ queryKey: ["impresion", "impresoras"] }),
  })
}
