import { useMemo, useState } from "react"
import type { ColumnDef } from "@tanstack/react-table"
import { PlusIcon } from "lucide-react"
import { toast } from "sonner"
import type { Tienda } from "@serca/contratos"

import { DataTable } from "@/components/data-table"
import { Panel } from "@/components/ficha"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { ErrorApi } from "@/core/api"
import { cn } from "@/lib/utils"
import { useEsc } from "@/workspace/modulo"
import { useTiendas } from "@/modules/impresion/api"
import { CLASE_TONO, nombreTienda } from "@/modules/impresion/formato"
import { Campo, DialogoFormulario } from "@/modules/impresion/formulario"
import { useBorrarUsuario, useGuardarUsuario, useUsuarios } from "./api"
import {
  datosUsuario,
  RolUsuario,
  type CuentaUsuario,
} from "./contratos"

const error = (e: unknown, defecto: string) =>
  toast.error(e instanceof ErrorApi ? e.problema.title : defecto)

const ROL_ETIQUETA: Record<RolUsuario, string> = {
  Admin: "Administrador",
  StoreManager: "Encargado",
  Employee: "Empleado",
}

// Usuarios de la aplicación (usuarios/index.blade.php del PHP). Solo durante
// la transición: en el ERP la identidad es su operador (docs/PLAN.md, B7.4).
// La API impide quedarse sin administradores y tocarse a uno mismo.
export default function Usuarios() {
  const usuarios = useUsuarios()
  const tiendas = useTiendas().data ?? []
  const guardar = useGuardarUsuario()
  const borrar = useBorrarUsuario()
  const [editando, setEditando] = useState<CuentaUsuario | "nuevo">()
  const [reinicio, setReinicio] = useState(0)
  useEsc(() => setReinicio((n) => n + 1))

  const columnas = useMemo<ColumnDef<CuentaUsuario, unknown>[]>(
    () => [
      {
        id: "login",
        header: "Usuario",
        size: 160,
        accessorKey: "login",
        cell: ({ row: { original: u } }) => (
          <span className="block truncate font-mono">{u.login}</span>
        ),
      },
      {
        id: "nombre",
        header: "Nombre",
        size: 0,
        accessorFn: (u) => u.displayName ?? "",
        cell: ({ getValue }) => (
          <span className="block truncate">{getValue<string>()}</span>
        ),
      },
      {
        id: "rol",
        header: "Rol",
        size: 130,
        accessorFn: (u) => ROL_ETIQUETA[u.role],
      },
      {
        id: "tienda",
        header: "Tienda",
        size: 180,
        accessorFn: (u) =>
          u.storeId === null ? "Todas" : nombreTienda(u.storeId, tiendas),
        cell: ({ getValue }) => (
          <span className="block truncate">{getValue<string>()}</span>
        ),
      },
      {
        id: "estado",
        header: "Estado",
        size: 100,
        cell: ({ row: { original: u } }) => (
          <Badge
            variant="outline"
            className={cn("border-transparent", CLASE_TONO[u.isActive ? "ok" : "neutro"])}
          >
            {u.isActive ? "Activo" : "Inactivo"}
          </Badge>
        ),
      },
      {
        id: "acciones",
        header: "",
        size: 250,
        enableSorting: false,
        cell: ({ row: { original: u } }) => (
          <div className="flex justify-end gap-1">
            <Button size="xs" variant="outline" onClick={() => setEditando(u)}>
              Editar
            </Button>
            <Button
              size="xs"
              variant="outline"
              onClick={() =>
                (u.isActive
                  ? window.confirm(`${u.login} no podrá entrar y se cerrarán sus sesiones abiertas.`)
                  : true) &&
                guardar.mutate(
                  { id: u.userId, datos: { ...u, password: null, isActive: !u.isActive } },
                  {
                    onSuccess: () =>
                      toast.success(u.isActive ? "Usuario desactivado" : "Usuario activado"),
                    onError: (e) => error(e, "No se pudo cambiar"),
                  }
                )
              }
            >
              {u.isActive ? "Desactivar" : "Activar"}
            </Button>
            <Button
              size="xs"
              variant="destructive"
              onClick={() =>
                window.confirm(`Se eliminará el usuario ${u.login}.`) &&
                borrar.mutate(u.userId, {
                  onSuccess: () => toast.success("Usuario eliminado"),
                  onError: (e) => error(e, "No se pudo eliminar"),
                })
              }
            >
              Eliminar
            </Button>
          </div>
        ),
      },
    ],
    // guardar.mutate y borrar.mutate son estables.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [tiendas]
  )

  return (
    <Panel className="[&_table]:min-w-[56rem] [&_table]:table-fixed">
      <DataTable
        key={reinicio}
        columns={columnas}
        data={usuarios.data ?? []}
        paginar={false}
        buscar="Usuario, nombre o tienda"
        buscarEtiqueta="Buscar usuario"
        emptyContent={usuarios.isPending ? undefined : "No hay usuarios."}
        toolbar={
          <Button size="sm" className="ml-auto" onClick={() => setEditando("nuevo")}>
            <PlusIcon data-icon="inline-start" />
            Nuevo usuario
          </Button>
        }
      />
      {editando && (
        <FormularioUsuario
          usuario={editando === "nuevo" ? undefined : editando}
          tiendas={tiendas}
          onCerrar={() => setEditando(undefined)}
        />
      )}
    </Panel>
  )
}

const ROL_ITEMS = RolUsuario.options.map((r) => ({ value: r, label: ROL_ETIQUETA[r] }))

function FormularioUsuario({
  usuario,
  tiendas,
  onCerrar,
}: {
  usuario?: CuentaUsuario
  tiendas: readonly Tienda[]
  onCerrar: () => void
}) {
  const guardar = useGuardarUsuario()
  const tiendaItems = tiendas
    .filter((t) => t.isActive || t.storeId === usuario?.storeId)
    .map((t) => ({ value: String(t.storeId), label: nombreTienda(t.storeId, tiendas) }))
  return (
    <DialogoFormulario
      titulo={usuario ? `Editar ${usuario.login}` : "Nuevo usuario"}
      aviso={usuario ? "Usuario guardado" : "Usuario creado"}
      esquema={datosUsuario(!usuario)}
      inicial={{
        login: usuario?.login ?? "",
        displayName: usuario?.displayName ?? "",
        role: usuario?.role ?? ("Employee" as RolUsuario),
        storeId: usuario?.storeId ?? null,
        password: "",
        isActive: usuario?.isActive ?? true,
      }}
      guardar={(datos) => guardar.mutateAsync({ id: usuario?.userId, datos })}
      onCerrar={onCerrar}
    >
      {({ valores, errores, enviando, cambiar, id }) => (
        <>
          <Campo id={id("login")} etiqueta="Usuario" error={errores.login}>
            <Input
              id={id("login")}
              autoFocus
              autoComplete="off"
              className="font-mono"
              value={valores.login}
              disabled={enviando}
              aria-invalid={!!errores.login}
              onChange={(e) => cambiar("login", e.target.value)}
            />
          </Campo>
          <Campo id={id("displayName")} etiqueta="Nombre" error={errores.displayName}>
            <Input
              id={id("displayName")}
              value={valores.displayName}
              disabled={enviando}
              aria-invalid={!!errores.displayName}
              onChange={(e) => cambiar("displayName", e.target.value)}
            />
          </Campo>
          <Campo id={id("role")} etiqueta="Rol" error={errores.role}>
            <Select
              items={ROL_ITEMS}
              value={valores.role}
              disabled={enviando}
              onValueChange={(v) => cambiar("role", v as RolUsuario)}
            >
              <SelectTrigger id={id("role")} className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {ROL_ITEMS.map((i) => (
                  <SelectItem key={i.value} value={i.value}>
                    {i.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Campo>
          {valores.role !== "Admin" && (
            <Campo id={id("storeId")} etiqueta="Tienda" error={errores.storeId}>
              <Select
                items={tiendaItems}
                value={valores.storeId === null ? null : String(valores.storeId)}
                disabled={enviando}
                onValueChange={(v) => cambiar("storeId", Number(v))}
              >
                <SelectTrigger
                  id={id("storeId")}
                  className="w-full"
                  aria-invalid={!!errores.storeId}
                >
                  <SelectValue placeholder="Elige la tienda" />
                </SelectTrigger>
                <SelectContent>
                  {tiendaItems.map((i) => (
                    <SelectItem key={i.value} value={i.value}>
                      {i.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Campo>
          )}
          <Campo
            id={id("password")}
            etiqueta="Contraseña"
            error={errores.password}
            ayuda={
              usuario
                ? "Vacía, no se cambia. Cambiarla cierra sus sesiones abiertas."
                : "Mínimo 6 caracteres."
            }
          >
            <Input
              id={id("password")}
              type="password"
              autoComplete="new-password"
              value={valores.password}
              disabled={enviando}
              aria-invalid={!!errores.password}
              onChange={(e) => cambiar("password", e.target.value)}
            />
          </Campo>
          {usuario && (
            <label className="flex items-center gap-2 text-sm">
              <Checkbox
                checked={valores.isActive}
                disabled={enviando}
                onCheckedChange={(v) => cambiar("isActive", !!v)}
              />
              Activo
            </label>
          )}
        </>
      )}
    </DialogoFormulario>
  )
}
