import { afterEach, describe, expect, it, vi } from "vitest"
import { z } from "zod"

import { api, ErrorApi, mantenerSiMismaEmpresa, params } from "@/core/api"

const respuesta = (status: number, cuerpo: unknown) =>
  new Response(JSON.stringify(cuerpo), { status })

describe("api()", () => {
  afterEach(() => vi.unstubAllGlobals())

  it("envía la empresa recibida y la señal de cancelación", async () => {
    const fetch = vi.fn(async () => respuesta(200, { ok: true }))
    vi.stubGlobal("fetch", fetch)
    const signal = new AbortController().signal
    await api("/x", z.object({ ok: z.boolean() }), { empresa: 11, signal })
    expect(fetch).toHaveBeenCalledWith("/api/x", {
      signal,
      headers: { "x-empresa": "11" },
    })
  })

  it("convierte el problem+json en ErrorApi", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        respuesta(404, { status: 404, title: "No encontrado", traza: "abc" })
      )
    )
    const e = (await api("/x", z.unknown()).catch((x: unknown) => x)) as ErrorApi
    expect(e).toBeInstanceOf(ErrorApi)
    expect(e.problema.traza).toBe("abc")
  })

  it("da los fallos de red y del proxy en español", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => {
      throw new TypeError("Failed to fetch")
    }))
    await expect(api("/x", z.unknown())).rejects.toThrow("Sin conexión con el servidor")
    vi.stubGlobal("fetch", vi.fn(async () => new Response("", { status: 502, statusText: "Bad Gateway" })))
    await expect(api("/x", z.unknown())).rejects.toThrow("Sin respuesta del servidor")
  })

  it("usa el { error } de la API .NET como título", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => respuesta(400, { error: "Trabajo no encontrado" }))
    )
    const e = (await api("/x", z.unknown()).catch((x: unknown) => x)) as ErrorApi
    expect(e.problema).toEqual({ status: 400, title: "Trabajo no encontrado" })
    vi.stubGlobal("fetch", vi.fn(async () => respuesta(400, { errors: ["Uno.", "Dos."] })))
    await expect(api("/x", z.unknown())).rejects.toThrow("Uno. Dos.")
  })

  it("envía PUT y DELETE de la API .NET", async () => {
    const fetch = vi.fn(async () => new Response(null, { status: 204 }))
    vi.stubGlobal("fetch", fetch)
    await api("/x", z.null(), { metodo: "DELETE" })
    expect(fetch).toHaveBeenLastCalledWith("/api/x", expect.objectContaining({ method: "DELETE" }))
    await api("/x", z.null(), { body: { a: 1 }, metodo: "PUT" })
    expect(fetch).toHaveBeenLastCalledWith("/api/x", expect.objectContaining({ method: "PUT", body: '{"a":1}' }))
  })

  it("falla si la respuesta no cumple el contrato", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respuesta(200, { ok: "sí" })))
    await expect(api("/x", z.object({ ok: z.boolean() }))).rejects.toThrow()
  })
})

describe("mantenerSiMismaEmpresa", () => {
  it("no enseña datos de otra sociedad mientras carga", () => {
    const mantener = mantenerSiMismaEmpresa(11)
    expect(mantener("previo", { queryKey: ["clientes", 11, "lista"] })).toBe("previo")
    expect(mantener("previo", { queryKey: ["clientes", 1, "lista"] })).toBeUndefined()
  })
})

describe("params", () => {
  it("omite vacíos", () => {
    expect(params({ q: "", pagina: 2, orden: undefined })).toBe("pagina=2")
  })
})
