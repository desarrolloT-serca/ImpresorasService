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
  // El módulo pide /api/impresion/…, como lo pedirá a la pasarela del ERP. En
  // desarrollo va a la API .NET quitando «/impresion» (en producción, nginx).
  server: {
    proxy: {
      "/api/impresion": {
        target: process.env.API_URL ?? "http://localhost:5105",
        rewrite: (p) => p.replace(/^\/api\/impresion/, "/api"),
      },
    },
  },
  test: {
    environment: "jsdom",
    // Como en el ERP: las pruebas viven en test/, con la estructura de src/.
    include: ["test/**/*.test.{ts,tsx}"],
  },
})
