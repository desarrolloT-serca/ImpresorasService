import {
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
} from "@tanstack/react-query"
import { z } from "zod"
import { Tienda } from "@serca/contratos"

import { api } from "@/core/api"
import {
  CuentaUsuario,
  Mensaje,
  TiendaResumen,
  type DatosTienda,
  type DatosUsuario,
} from "./contratos"

// Tiendas y usuarios propios de la aplicación, solo durante la transición
// (docs/PLAN.md, B7.4). Mismas rutas /impresion/… que el módulo.

export const useTiendasResumen = () =>
  useQuery({
    queryKey: ["impresion", "tiendas", "resumen"],
    queryFn: ({ signal }) =>
      api("/impresion/stores", z.array(TiendaResumen), { signal }),
  })

// Una tienda cambia también sus impresoras (desactivarla las desactiva).
const tiendasEImpresoras = (cache: QueryClient) =>
  Promise.all([
    cache.invalidateQueries({ queryKey: ["impresion", "tiendas"] }),
    cache.invalidateQueries({ queryKey: ["impresion", "impresoras"] }),
  ])

export function useGuardarTienda() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: ({ nueva, datos }: { nueva: boolean; datos: DatosTienda }) =>
      nueva
        ? api("/impresion/stores", Tienda, { body: datos })
        : api(`/impresion/stores/${datos.storeId}`, Tienda, {
            body: { name: datos.name, isActive: datos.isActive },
            metodo: "PUT",
          }),
    onSettled: () => tiendasEImpresoras(cache),
  })
}

// Sin definitivo, la desactiva con sus impresoras y conserva el histórico.
// Definitivo borra también su histórico de impresión.
export function useBorrarTienda() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: ({ id, definitivo }: { id: number; definitivo: boolean }) =>
      api(
        `/impresion/stores/${id}${definitivo ? "?hardDelete=true&purgeHistory=true" : ""}`,
        Mensaje,
        { metodo: "DELETE" }
      ),
    onSettled: () => tiendasEImpresoras(cache),
  })
}

export const useUsuarios = () =>
  useQuery({
    queryKey: ["impresion", "usuarios"],
    queryFn: ({ signal }) =>
      api("/impresion/users", z.array(CuentaUsuario), { signal }),
  })

// Las tiendas cuentan sus usuarios: también se refrescan.
const usuariosYTiendas = (cache: QueryClient) =>
  Promise.all([
    cache.invalidateQueries({ queryKey: ["impresion", "usuarios"] }),
    cache.invalidateQueries({ queryKey: ["impresion", "tiendas"] }),
  ])

export function useGuardarUsuario() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: ({ id, datos }: { id?: number; datos: DatosUsuario }) =>
      id
        ? api(`/impresion/users/${id}`, z.unknown(), { body: datos, metodo: "PUT" })
        : api("/impresion/users", z.unknown(), { body: datos }),
    onSettled: () => usuariosYTiendas(cache),
  })
}

export function useBorrarUsuario() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: (id: number) =>
      api(`/impresion/users/${id}`, z.null(), { metodo: "DELETE" }),
    onSettled: () => usuariosYTiendas(cache),
  })
}

