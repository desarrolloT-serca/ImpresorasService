import * as React from "react"

const MOBILE_BREAKPOINT = 768
const consulta = `(max-width: ${MOBILE_BREAKPOINT - 1}px)`

const suscribir = (cb: () => void) => {
  const mql = window.matchMedia(consulta)
  mql.addEventListener("change", cb)
  return () => mql.removeEventListener("change", cb)
}

export function useIsMobile() {
  return React.useSyncExternalStore(
    suscribir,
    () => window.matchMedia(consulta).matches
  )
}
