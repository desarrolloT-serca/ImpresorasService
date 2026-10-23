import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, cleanup, renderHook } from "@testing-library/react"
import type { ReactNode } from "react"
import { afterEach, beforeEach, expect, it, vi } from "vitest"
import { PERMISOS_IMPRESION as P, tienePermiso } from "@serca/contratos"
import { api } from "@/core/api"
import { PERMISOS_DE_ROL, useCerrarSesion, type Usuario } from "@/core/sesion"

// Adaptada de test/core/sesion.test.tsx del ERP.
vi.mock("@/core/api", () => ({ api: vi.fn() }))
const usuario: Usuario = { userId: 1, login: "tienda12", displayName: "Tienda 12", role: "Employee", storeId: 12 }
let cache: QueryClient
beforeEach(() => {
  cache = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } } })
  cache.setQueryData(["sesion"], usuario)
  cache.setQueryData(["impresion", "cola"], { datos: "de la sesión anterior" })
})
afterEach(() => { cleanup(); cache.clear(); vi.resetAllMocks() })
const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={cache}>{children}</QueryClientProvider>

it("cada rol da sus claves y ninguna de más", () => {
  expect(PERMISOS_DE_ROL.Employee).toEqual([P.leer])
  expect(tienePermiso(PERMISOS_DE_ROL.StoreManager, P.reimprimir)).toBe(true)
  expect(tienePermiso(PERMISOS_DE_ROL.StoreManager, P.administrar)).toBe(false)
  expect(tienePermiso(PERMISOS_DE_ROL.Admin, P.administrar)).toBe(true)
})
it("logout elimina la sesión y los datos anteriores", async () => {
  vi.mocked(api).mockResolvedValue(null)
  const { result } = renderHook(() => useCerrarSesion(), { wrapper })
  await act(async () => { await result.current.mutateAsync() })
  expect(api).toHaveBeenCalledWith("/impresion/auth/logout", expect.anything(), { body: {} })
  expect(cache.getQueryData(["sesion"])).toBeUndefined()
  expect(cache.getQueryData(["impresion", "cola"])).toBeUndefined()
})
it("un logout fallido no finge haber cerrado la sesión en el servidor", async () => {
  vi.mocked(api).mockRejectedValue(new Error("Sin red"))
  const { result } = renderHook(() => useCerrarSesion(), { wrapper })
  await act(async () => { await expect(result.current.mutateAsync()).rejects.toThrow("Sin red") })
  expect(cache.getQueryData<Usuario>(["sesion"])?.login).toBe("tienda12")
})
