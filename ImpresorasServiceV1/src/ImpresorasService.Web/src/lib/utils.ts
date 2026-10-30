export { cn } from "cn"

// Clic en la cabecera de una tabla en servidor: alterna los dos sentidos (las
// numéricas empiezan por la mayor). Para volver al orden inicial está
// «Quitar orden».
export const siguienteOrden = (
  id: string,
  num: boolean,
  actual: string | undefined
) => {
  const [primero, segundo] = num ? [`-${id}`, id] : [id, `-${id}`]
  return actual === primero ? segundo : primero
}
