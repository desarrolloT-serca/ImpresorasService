import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"

import "./index.css"
import { Acceso } from "@/app/Acceso"
import { App } from "@/app/App"
import { ThemeProvider } from "@/components/theme-provider"
import { ErrorApi } from "@/core/api"
import { PanelSeguro } from "@/workspace/PanelSeguro"

// Igual que en el ERP (main.tsx).
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Los errores de consulta los pinta la barrera de la pestaña.
      throwOnError: true,
      staleTime: 30_000,
      // Un 4xx no se arregla reintentando.
      retry: (n, e) => !(e instanceof ErrorApi && e.problema.status < 500) && n < 2,
    },
  },
})

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <div className="h-svh">
          <PanelSeguro>
            <Acceso>
              <App />
            </Acceso>
          </PanelSeguro>
        </div>
      </QueryClientProvider>
    </ThemeProvider>
  </StrictMode>
)
