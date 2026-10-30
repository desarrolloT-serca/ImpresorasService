export const ATAJOS = {
  busqueda: (e: KeyboardEvent) =>
    (e.ctrlKey || e.metaKey) &&
    !e.shiftKey &&
    !e.altKey &&
    e.key.toLowerCase() === "k",
  busquedaAlternativa: (e: KeyboardEvent) => e.key === "F2",
  busquedaAvanzada: (e: KeyboardEvent) =>
    (e.ctrlKey || e.metaKey) &&
    e.shiftKey &&
    !e.altKey &&
    e.key.toLowerCase() === "f",
}
