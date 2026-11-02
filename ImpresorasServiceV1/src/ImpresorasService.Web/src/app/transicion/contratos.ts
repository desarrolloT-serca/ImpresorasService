import { z } from "zod"
import { Tienda } from "@serca/contratos"

// Contratos de la transición: tiendas y usuarios propios de la aplicación.
// No van al ERP: allí los usuarios son sus operadores (fdopera) y las tiendas,
// sus almacenes (fdalmacen). Se tiran al integrar (docs/PLAN.md, B7.4).

// GET /api/stores: la lista lleva además sus usuarios e impresoras.
export const TiendaResumen = Tienda.extend({
  usersCount: z.number().int(),
  printersCount: z.number().int(),
})
export type TiendaResumen = z.infer<typeof TiendaResumen>

// CreateStoreRequest (el número es el cod_alm del ERP) y UpdateStoreRequest.
export const DatosTienda = z.object({
  storeId: z.number({ error: "Obligatorio" }).int().min(0, "No puede ser negativo"),
  name: z.string().trim().min(1, "Obligatorio").max(120, "Máximo 120 caracteres"),
  isActive: z.boolean(),
})
export type DatosTienda = z.output<typeof DatosTienda>

// Usuarios propios de la aplicación (GET /api/users). Desaparecen cuando la
// identidad sea el operador del ERP (docs/PLAN.md, B7).
export const RolUsuario = z.enum(["Admin", "StoreManager", "Employee"])
export type RolUsuario = z.infer<typeof RolUsuario>

export const CuentaUsuario = z.object({
  userId: z.number().int(),
  login: z.string(),
  displayName: z.string().nullable(),
  role: RolUsuario,
  storeId: z.number().int().nullable(),
  isActive: z.boolean(),
})
export type CuentaUsuario = z.infer<typeof CuentaUsuario>

// CreateUserRequest y UpdateUserRequest, con las reglas de la API: tienda
// obligatoria salvo para el administrador, que no debe tenerla; contraseña
// obligatoria al crear y, al editar, vacía = no se cambia.
export const datosUsuario = (nuevo: boolean) =>
  z
    .object({
      login: z.string().trim().min(3, "Mínimo 3 caracteres").max(80, "Máximo 80 caracteres"),
      displayName: z
        .string()
        .trim()
        .max(120, "Máximo 120 caracteres")
        .transform((v) => v || null),
      role: RolUsuario,
      storeId: z.number().int().nullable(),
      password: z
        .string()
        .trim()
        .refine((p) => (nuevo ? p.length >= 6 : !p || p.length >= 6), "Mínimo 6 caracteres"),
      isActive: z.boolean(),
    })
    .refine((u) => u.role === "Admin" || u.storeId !== null, {
      path: ["storeId"],
      message: "Elige la tienda",
    })
    .transform((u) => ({
      ...u,
      storeId: u.role === "Admin" ? null : u.storeId,
      password: u.password || null,
    }))
export type DatosUsuario = z.output<ReturnType<typeof datosUsuario>>

// Respuesta de la baja de una tienda.
export const Mensaje = z.object({ message: z.string() })

