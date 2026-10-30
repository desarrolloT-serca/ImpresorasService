import {
  keepPreviousData,
  queryOptions,
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
} from "@tanstack/react-query"
import { z } from "zod"
import {
  AvisoTelegram,
  ChatTelegram,
  ConfigTelegram,
  Conexion,
  DatosChat,
  EdicionChat,
  Impresora,
  PaginaCola,
  PruebaCreada,
  PruebasCanceladas,
  PruebaTelegram,
  ReglaEnrutado,
  ReglasUmbral,
  ResolucionRegla,
  PaginaReimpresiones,
  ResultadoAccion,
  ResumenDashboard,
  SerieDashboard,
  Tienda,
  VistaPreviaTelegram,
  type ConsultaCola,
  type VentanaDashboard,
  type DatosImpresora,
  type DatosRegla,
  type PeticionReimpresion,
  type TrabajoPrueba,
} from "@serca/contratos"
import { api, ErrorApi, params } from "@/core/api"

// Lo que devuelven al guardar una regla o un chat: la pantalla no lo usa (relee
// la lista), pero se comprueba que es lo guardado y no otra cosa (WEB-6).
const ReglaGuardada = z.object({ ruleId: z.number().int() })
const ChatGuardado = z.object({ chatId: z.number().int() })

// Rutas /impresion/… con las de la API .NET: el proxy (o la pasarela del ERP)
// quita «/impresion».

// La cola y las impresoras cambian solas: se releen cada 30 s mientras la
// pestaña está abierta (como la ficha de artículo del ERP con el stock).
const REFRESCO_MS = 30_000
// Con datos en pantalla, un refresco que falla (un corte de la base) no tumba
// la pestaña: se sigue viendo lo último y SinActualizar lo avisa (WEB-10). Sin
// datos, o si es un 4xx (sesión caducada, sin permiso), como el resto del ERP:
// a la barrera de errores de la pestaña.
const soloSinDatos = (e: Error, q: { state: { data: unknown } }) =>
  q.state.data === undefined ||
  (e instanceof ErrorApi && e.problema.status < 500)

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
          fromDate: c.desde,
          toDate: c.hasta,
          printerIds: c.impresoras?.join(","),
          documentTypes: c.tipos?.join(","),
        })}`,
        PaginaCola,
        { signal }
      ),
    // Mientras llega la página siguiente se sigue viendo la anterior.
    placeholderData: keepPreviousData,
    refetchInterval: REFRESCO_MS,
    throwOnError: soloSinDatos,
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
    throwOnError: soloSinDatos,
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

// Reimprimir lo que ya salió: POST …/reprint, con motivo. La API lo registra
// (quién, cuándo, por qué) en el registro de reimpresiones.
export function useReimprimir() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: ({
      id,
      peticion,
    }: {
      id: string
      peticion: PeticionReimpresion
    }) =>
      api(`/impresion/printjobs/${id}/reprint`, ResultadoAccion, {
        body: peticion,
      }),
    onSettled: () => cache.invalidateQueries({ queryKey: ["impresion"] }),
  })
}

// Registro de reimpresiones: de la tienda del usuario o, al que ve todas, de
// todas o de la del filtro. El periodo lo calcula la API como el del
// dashboard: abierto desde su KPI, da las mismas (auditoría L8).
// Por páginas en la API (WEB-2), con el motivo filtrado allí.
export const useReimpresiones = (f: {
  tienda?: number
  ventana: VentanaDashboard
  motivo?: string
  pagina: number
  tamano: number
}) =>
  useQuery({
    queryKey: ["impresion", "reimpresiones", f],
    queryFn: ({ signal }) =>
      api(
        `/impresion/printjobs/reprints?${params({ storeId: f.tienda, window: f.ventana, reason: f.motivo, page: f.pagina, limit: f.tamano })}`,
        PaginaReimpresiones,
        { signal }
      ),
    // Mientras llega la página siguiente se sigue viendo la anterior.
    placeholderData: keepPreviousData,
    refetchInterval: REFRESCO_MS,
    throwOnError: soloSinDatos,
  })

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
      api("/impresion/routingrules", z.array(ReglaEnrutado), { signal }),
  })

// Editar exige las fechas de vigencia: se conservan las que tenía.
export function useGuardarRegla() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: ({
      regla,
      datos,
    }: {
      regla?: ReglaEnrutado
      datos: DatosRegla
    }) =>
      regla
        ? api(`/impresion/routingrules/${regla.ruleId}`, ReglaGuardada, {
            body: {
              ...datos,
              validFromUtc: regla.validFromUtc,
              validToUtc: regla.validToUtc,
            },
            metodo: "PUT",
          })
        : api("/impresion/routingrules", ReglaGuardada, { body: datos }),
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

// Qué regla ganaría para un trabajo, con el RoutingResolver de la API (no se
// imita aquí, así el simulador no se separa nunca de lo que hace el servidor).
// 404 = ninguna regla.
export const useSimularRegla = () =>
  useMutation({
    mutationFn: (trabajo: {
      storeId: number
      documentType: string
      channel: string
    }) => api("/impresion/routing/resolve", ResolucionRegla, { body: trabajo }),
  })

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
    throwOnError: soloSinDatos,
  })

export const useSerie = (
  ventana: VentanaDashboard,
  tienda?: number,
  franja = 0
) =>
  useQuery({
    queryKey: ["impresion", "dashboard", "serie", ventana, tienda, franja],
    queryFn: ({ signal }) =>
      api(
        `/impresion/dashboard/timeseries?${params({ window: ventana, storeId: tienda, bucketMinutes: franja })}`,
        SerieDashboard,
        { signal }
      ),
    placeholderData: keepPreviousData,
    refetchInterval: REFRESCO_MS,
    throwOnError: soloSinDatos,
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
    mutationFn: (c: Omit<ConfigTelegram, "enabled" | "horario">) =>
      api("/impresion/telegram/config", z.null(), { body: c, metodo: "PUT" }),
    onSettled: () => telegram(cache),
  })
}

// Token del bot; "" lo quita y vuelve el del servidor. La API lo comprueba con
// Telegram y devuelve el usuario del bot.
export function useGuardarTokenTelegram() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: (botToken: string) =>
      api(
        "/impresion/telegram/token",
        z.object({ bot: z.string().nullable() }),
        { body: { botToken }, metodo: "PUT" }
      ),
    onSettled: () => telegram(cache),
  })
}

export function useAnadirChat() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: (c: z.output<typeof DatosChat>) =>
      api("/impresion/telegram/chats", ChatGuardado, { body: c }),
    onSettled: () => telegram(cache),
  })
}

export function useBorrarChat() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: (chatId: number) =>
      api(`/impresion/telegram/chats/${chatId}`, z.null(), {
        metodo: "DELETE",
      }),
    onSettled: () => telegram(cache),
  })
}

// Editar un chat o pausarlo sin borrarlo.
export function useEditarChat() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: ({ chatId, ...datos }: EdicionChat & { chatId: number }) =>
      api(`/impresion/telegram/chats/${chatId}`, ChatGuardado, {
        body: datos,
        metodo: "PUT",
      }),
    onSettled: () => telegram(cache),
  })
}

// Manda de verdad un mensaje a todos los chats activos o, con chatId, solo a
// ese.
export const usePruebaTelegram = () =>
  useMutation({
    mutationFn: (chatId?: number) =>
      api(
        `/impresion/telegram/test${chatId === undefined ? "" : `?chatId=${chatId}`}`,
        PruebaTelegram,
        { body: {} }
      ),
  })

// Los últimos avisos mandados. Si falla (una API anterior no lo tiene), lo dice
// su tarjeta y no tumba la pestaña.
export const useHistorialTelegram = () =>
  useQuery({
    queryKey: ["impresion", "telegram", "historial"],
    queryFn: ({ signal }) =>
      api("/impresion/telegram/history?limit=50", z.array(AvisoTelegram), {
        signal,
      }),
    refetchInterval: 60_000,
    throwOnError: false,
    retry: false,
  })

// El aviso tal como llega: el mismo texto que manda el Worker.
export const useVistaPreviaTelegram = () =>
  useQuery({
    queryKey: ["impresion", "telegram", "vistaPrevia"],
    queryFn: ({ signal }) =>
      api("/impresion/telegram/preview", VistaPreviaTelegram, { signal }),
    staleTime: 60_000,
    // Es un adorno: si falla (una API anterior no la tiene), lo dice su
    // tarjeta y no tumba la pestaña entera.
    throwOnError: false,
    retry: false,
  })

// A qué regla e impresora iría un trabajo así, con el RoutingResolver de la
// API (el mismo que el simulador de Reglas). Null = ninguna regla: quedaría en
// Error. Para enseñarlo en Pruebas antes de lanzar, uno por lote
// (useQueries); si falla, no tumba la pestaña.
export const consultaDestino = (trabajo?: {
  storeId: number
  documentType: string
  channel: string
}) =>
  queryOptions({
    queryKey: ["impresion", "reglas", "destino", trabajo],
    queryFn: async ({ signal }) => {
      try {
        return await api("/impresion/routing/resolve", ResolucionRegla, {
          body: trabajo,
          signal,
        })
      } catch (e) {
        if (e instanceof ErrorApi && e.problema.status === 404) return null
        throw e
      }
    },
    enabled: !!trabajo,
    staleTime: 30_000,
    throwOnError: false,
    retry: false,
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
    throwOnError: soloSinDatos,
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
    onSettled: () =>
      cache.invalidateQueries({ queryKey: ["impresion", "cola"] }),
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
