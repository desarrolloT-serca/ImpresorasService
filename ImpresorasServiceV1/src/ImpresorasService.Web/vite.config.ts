/// <reference types="vitest/config" />
import path from "path"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      // En el ERP es packages/contratos; aquí, su sustituto local. El módulo
      // importa igual en los dos sitios.
      "@serca/contratos": path.resolve(__dirname, "./src/contratos/index.ts"),
    },
  },
  // Solo para las pruebas: la pantalla se ve en el ERP, no aquí (11/11/2026).
  test: {
    environment: "jsdom",
    // Como en el ERP: las pruebas viven en test/, con la estructura de src/.
    include: ["test/**/*.test.{ts,tsx}"],
  },
})
