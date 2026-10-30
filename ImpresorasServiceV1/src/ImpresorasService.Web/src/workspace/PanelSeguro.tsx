import * as React from "react"
import { QueryErrorResetBoundary } from "@tanstack/react-query"
import { RefreshCwIcon, RotateCcwIcon, TriangleAlertIcon } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/components/ui/empty"
import { Spinner } from "@/components/ui/spinner"
import { ErrorApi } from "@/core/api"

// Cada pestaña se aísla: si falla o está cargando, solo afecta a esa pestaña.
export function PanelSeguro({ children }: { children: React.ReactNode }) {
  return (
    <QueryErrorResetBoundary>
      {({ reset }) => (
        <Barrera onReset={reset}>
          <React.Suspense fallback={<Cargando />}>{children}</React.Suspense>
        </Barrera>
      )}
    </QueryErrorResetBoundary>
  )
}

export function Cargando() {
  return (
    <div className="flex h-full items-center justify-center text-muted-foreground">
      <Spinner />
    </div>
  )
}

// El código de la pestaña no se pudo descargar (servidor reiniciado, versión
// nueva desplegada o red). lazy() recuerda el fallo y «Reintentar» no lo
// arregla: hay que recargar, y el escritorio se conserva (se guarda en el
// navegador). Mensajes de Chrome, Firefox y Safari.
const sinDescargar = (e: unknown) =>
  e instanceof TypeError &&
  /dynamically imported module|module script failed/i.test(e.message)

class Barrera extends React.Component<
  { onReset: () => void; children: React.ReactNode },
  { error: unknown; fallo: boolean }
> {
  state = { error: null as unknown, fallo: false }

  static getDerivedStateFromError(error: unknown) {
    return { error, fallo: true }
  }

  componentDidCatch(error: unknown) {
    // ponytail: consola hasta tener Sentry (plan, observabilidad).
    console.error(error)
  }

  render() {
    if (!this.state.fallo) return this.props.children
    const e = this.state.error
    if (sinDescargar(e))
      return (
        <Empty className="h-full">
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <TriangleAlertIcon />
            </EmptyMedia>
            <EmptyTitle>No se ha podido descargar esta pantalla</EmptyTitle>
            <EmptyDescription>
              Puede haber una versión nueva del ERP o se ha perdido la conexión.
              Al recargar se conservan las pestañas abiertas.
            </EmptyDescription>
          </EmptyHeader>
          <EmptyContent>
            <Button
              variant="outline"
              size="sm"
              onClick={() => location.reload()}
            >
              <RefreshCwIcon data-icon="inline-start" />
              Recargar
            </Button>
          </EmptyContent>
        </Empty>
      )
    const p = e instanceof ErrorApi ? e.problema : null
    return (
      <Empty className="h-full">
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <TriangleAlertIcon />
          </EmptyMedia>
          <EmptyTitle>{p?.title ?? "No se ha podido mostrar"}</EmptyTitle>
          <EmptyDescription>
            {p?.detail ??
              (p
                ? null
                : "Error inesperado en esta pestaña. El resto del ERP sigue funcionando.")}
            {p?.traza && (
              <span className="mt-1 block font-mono text-xs">
                Referencia: {p.traza}
              </span>
            )}
          </EmptyDescription>
        </EmptyHeader>
        <EmptyContent>
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              this.props.onReset()
              this.setState({ error: null, fallo: false })
            }}
          >
            <RotateCcwIcon data-icon="inline-start" />
            Reintentar
          </Button>
        </EmptyContent>
      </Empty>
    )
  }
}
