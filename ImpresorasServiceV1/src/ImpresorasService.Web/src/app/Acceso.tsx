import { useState, type ReactNode } from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { api, ErrorApi } from "@/core/api"
import { consultaSesion, nuevaSesion, Usuario } from "@/core/sesion"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

// Puerta de la aplicación, copiada de app/Acceso.tsx del ERP: sin sesión
// válida (401) pide usuario y contraseña. Se consulta cada minuto para volver
// aquí si la sesión caduca o se revoca. Dentro del ERP manda su propia puerta.
export function Acceso({ children }: { children: ReactNode }) {
  const sesion = useQuery({
    ...consultaSesion,
    retry: false,
    throwOnError: false,
    refetchInterval: 60_000,
  })
  if (sesion.isPending)
    return (
      <div className="flex h-full items-center justify-center" role="status">
        Comprobando sesión…
      </div>
    )
  if (sesion.error instanceof ErrorApi && sesion.error.problema.status === 401)
    return <Login />
  // Un fallo del sondeo con la sesión ya cargada (API reiniciándose, 502)
  // no desmonta el escritorio: se reintenta en el siguiente minuto. Sin sesión
  // cargada se dice el motivo (la base caída da 503 con él) y también se
  // reintenta solo cada minuto.
  if (sesion.isError && !sesion.data)
    return (
      <div className="flex h-full flex-col items-center justify-center gap-4 p-6 text-center">
        <p role="alert">
          {sesion.error instanceof ErrorApi && sesion.error.problema.status === 503
            ? sesion.error.problema.title
            : "No se pudo comprobar la sesión."}
        </p>
        <p className="text-sm text-muted-foreground">Se vuelve a intentar cada minuto.</p>
        <Button onClick={() => void sesion.refetch()}>Reintentar ahora</Button>
      </div>
    )
  return children
}

function Login() {
  const cache = useQueryClient()
  const [login, setLogin] = useState("")
  const [password, setPassword] = useState("")
  const [error, setError] = useState("")
  const entrar = useMutation({
    mutationFn: () =>
      api("/impresion/auth/login", Usuario, { body: { login, password } }),
    onSuccess: (u) => nuevaSesion(cache, u),
    onError: (e) => {
      setPassword("")
      setError(
        e instanceof ErrorApi
          ? (e.problema.detail ?? e.problema.title)
          : "No se pudo iniciar sesión"
      )
    },
  })
  return (
    <main className="flex h-full items-center justify-center p-6">
      <form
        noValidate
        className="w-full max-w-sm space-y-5 rounded-xl border bg-card p-6"
        onSubmit={(e) => {
          e.preventDefault()
          setError("")
          if (!login || !password) {
            setError("Introduce tu usuario y tu contraseña")
            return
          }
          entrar.mutate()
        }}
      >
        <h1 className="text-2xl font-semibold">Impresión</h1>
        <p className="text-sm text-muted-foreground">
          Entra con tu usuario de la cola de impresión.
        </p>
        <div className="space-y-2">
          <Label htmlFor="login-usuario">Usuario</Label>
          <Input
            id="login-usuario"
            name="username"
            autoComplete="username"
            autoFocus
            value={login}
            onChange={(e) => setLogin(e.target.value.trim())}
            disabled={entrar.isPending}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="login-contrasena">Contraseña</Label>
          <Input
            id="login-contrasena"
            name="password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            disabled={entrar.isPending}
          />
        </div>
        {error && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}
        <Button className="w-full" type="submit" disabled={entrar.isPending}>
          {entrar.isPending ? "Comprobando…" : "Entrar"}
        </Button>
      </form>
    </main>
  )
}
