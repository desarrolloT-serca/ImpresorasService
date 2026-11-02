import { expect, it } from "vitest"

// El módulo se copia tal cual dentro del ERP: no puede depender de lo que
// allí no existe (el contenedor, la transición ni el registro de esta app).
const ficheros = import.meta.glob("/src/modules/**/*.{ts,tsx}", {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>

it("el módulo de impresión solo usa lo que el ERP da a sus módulos", () => {
  expect(Object.keys(ficheros).length).toBeGreaterThan(0)
  const prohibidos = Object.entries(ficheros).filter(([, codigo]) =>
    /from ["'](@\/app\/|@\/workspace\/registro)/.test(codigo)
  )
  expect(prohibidos.map(([ruta]) => ruta)).toEqual([])
})
