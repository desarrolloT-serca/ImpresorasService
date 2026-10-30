import {
  useMutation,
  useQueryClient,
  useSuspenseQuery,
  type QueryClient,
} from "@tanstack/react-query"
import { z } from "zod"
import { PERMISOS_IMPRESION as P, tienePermiso } from "@serca/contratos"

import { api } from "@/core/api"

// Sesión de esta aplicación: el usuario de la API .NET (/api/auth/me), que la
// guarda en una cookie que el navegador no deja leer. Mismos nombres que
// core/sesion.ts del ERP; dentro del ERP se usa el suyo y este se tira.
export const Usuario = z.object({
  userId: z.number().int(),
  login: z.string(),
  displayName: z.string(),
  role: z.enum(["Admin", "StoreManager", "Employee"]),
  storeId: z.number().int().nullable(),
})
export type Usuario = z.infer<typeof Usuario>

export const ROLES: Record<Usuario["role"], string> = {
  Admin: "Administrador",
  StoreManager: "Encargado",
  Employee: "Empleado",
}

// Claves que da cada rol. El módulo solo mira claves, como en el ERP.
export const PERMISOS_DE_ROL: Record<Usuario["role"], string[]> = {
  Employee: [P.leer],
  StoreManager: [P.leer, P.reimprimir],
  Admin: [P.leer, P.reimprimir, P.administrar, P.todasLasTiendas],
}

export const consultaSesion = {
  queryKey: ["sesion"],
  queryFn: ({ signal }: { signal: AbortSignal }) =>
    api("/impresion/auth/me", Usuario, { signal }),
}

export const useSesion = () => {
  const usuario = useSuspenseQuery({ ...consultaSesion, staleTime: Infinity })
    .data
  return { ...usuario, permisos: PERMISOS_DE_ROL[usuario.role] }
}

// Solo oculta lo que el usuario no puede usar; quien decide es la API.
export const usePermiso = (permiso?: string) => {
  const { permisos } = useSesion()
  return !permiso || tienePermiso(permisos, permiso)
}

// Al cambiar de identidad no puede quedar nada del anterior en caché.
export async function nuevaSesion(cache: QueryClient, usuario: Usuario | null) {
  await cache.cancelQueries()
  cache.removeQueries({ predicate: (q) => q.queryKey[0] !== "sesion" })
  if (usuario) cache.setQueryData(["sesion"], usuario)
  else await cache.resetQueries({ queryKey: ["sesion"], exact: true })
}

export function useCerrarSesion() {
  const cache = useQueryClient()
  return useMutation({
    mutationFn: () => api("/impresion/auth/logout", z.null(), { body: {} }),
    onSuccess: () => nuevaSesion(cache, null),
  })
}
