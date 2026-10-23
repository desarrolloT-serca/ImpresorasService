import { describe, expect, it } from "vitest"
import { datosUsuario } from "@/app/transicion/contratos"

const usuario = { login: "tienda12", displayName: "", role: "Employee", storeId: 12, password: "", isActive: true }

describe("datosUsuario (reglas de UsersController)", () => {
  it("al crear exige contraseña de 6", () => {
    expect(datosUsuario(true).safeParse(usuario).success).toBe(false)
    expect(datosUsuario(true).safeParse({ ...usuario, password: "123456" }).success).toBe(true)
  })
  it("al editar, contraseña vacía = no se cambia (null)", () => {
    expect(datosUsuario(false).parse(usuario).password).toBeNull()
    expect(datosUsuario(false).safeParse({ ...usuario, password: "123" }).success).toBe(false)
  })
  it("tienda obligatoria salvo para el administrador, que no la lleva", () => {
    const sinTienda = datosUsuario(false).safeParse({ ...usuario, storeId: null })
    expect(sinTienda.error?.issues[0].path).toEqual(["storeId"])
    expect(datosUsuario(false).parse({ ...usuario, role: "Admin" }).storeId).toBeNull()
  })
})
