// «Guardar como» del ERP antiguo: SaveAs() sin argumentos de la DataWindow
// abre el diálogo de Windows para elegir carpeta, nombre y tipo de fichero, y
// guarda las filas con sus cabeceras.
export type Tabla = [columnas: string[], filas: string[][]]

// CSV que abre Excel en español (punto y coma, UTF-8 con BOM).
export function csv(columnas: string[], filas: string[][]) {
  const celda = (v: string) =>
    /[";\r\n]/.test(v) ? `"${v.replaceAll('"', '""')}"` : v
  return [columnas, ...filas].map((f) => f.map(celda).join(";")).join("\r\n")
}

// Texto separado por tabuladores: un tabulador o salto dentro de una celda
// rompería la fila, se cambia por un espacio.
export const tabuladores = (columnas: string[], filas: string[][]) =>
  [columnas, ...filas]
    .map((f) => f.map((v) => v.replace(/[\t\r\n]+/g, " ")).join("\t"))
    .join("\r\n")

const escapar = (v: string) =>
  v.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;")
export const html = (titulo: string, columnas: string[], filas: string[][]) =>
  `<!doctype html>\r\n<html lang="es"><head><meta charset="utf-8"><title>${escapar(titulo)}</title></head><body><table border="1">\r\n<tr>${columnas.map((c) => `<th>${escapar(c)}</th>`).join("")}</tr>\r\n${filas.map((f) => `<tr>${f.map((v) => `<td>${escapar(v)}</td>`).join("")}</tr>`).join("\r\n")}\r\n</table></body></html>`

// Marca BOM: Excel lee el fichero como UTF-8.
const BOM = String.fromCharCode(0xfeff)

// Los tipos del desplegable «Tipo» del diálogo, el primero por defecto.
const FORMATOS = [
  {
    extension: ".csv",
    description: "CSV para Excel (separado por punto y coma)",
    mime: "text/csv",
    texto: (_: string, c: string[], f: string[][]) => `${BOM}${csv(c, f)}`,
  },
  {
    extension: ".txt",
    description: "Texto separado por tabuladores",
    mime: "text/plain",
    texto: (_: string, c: string[], f: string[][]) =>
      `${BOM}${tabuladores(c, f)}`,
  },
  {
    extension: ".html",
    description: "Tabla HTML",
    mime: "text/html",
    texto: html,
  },
]

// Aún no está en lib.dom de TypeScript (Chrome y Edge lo tienen).
type Selector = (o: {
  suggestedName: string
  id: string
  startIn: string
  types: { description: string; accept: Record<string, string[]> }[]
}) => Promise<{
  name: string
  createWritable: () => Promise<{
    write: (b: Blob) => Promise<void>
    close: () => Promise<void>
  }>
}>

// El diálogo se abre antes de leer las filas: el navegador solo lo deja abrir
// justo tras el clic, y leerlas puede tardar. Cancelar el diálogo no lee nada.
// `filas` devuelve null si al final no hay que guardar (p. ej. el usuario no
// acepta el recorte).
export async function guardarFichero(
  nombre: string,
  filas: () => Tabla | null | undefined | Promise<Tabla | null | undefined>
) {
  const selector = (window as { showSaveFilePicker?: Selector })
    .showSaveFilePicker
  if (!selector) {
    // shortcut: sin el diálogo (Firefox, Safari) se descarga el CSV con el
    // nombre propuesto; si se usan, haría falta un diálogo propio de tipo.
    const t = await filas()
    if (!t) return
    const url = URL.createObjectURL(
      new Blob([FORMATOS[0].texto(nombre, ...t)], {
        type: "text/csv;charset=utf-8",
      })
    )
    const a = document.createElement("a")
    a.href = url
    a.download = `${nombre}.csv`
    a.click()
    URL.revokeObjectURL(url)
    return
  }
  let fichero
  try {
    fichero = await selector({
      suggestedName: `${nombre}.csv`,
      // Recuerda la última carpeta, como el diálogo de Windows del original.
      id: "guardar-como",
      startIn: "documents",
      types: FORMATOS.map((f) => ({
        description: f.description,
        accept: { [f.mime]: [f.extension] },
      })),
    })
  } catch (e) {
    if ((e as Error).name === "AbortError") return
    throw e
  }
  const t = await filas()
  if (!t) return
  // El tipo, por la extensión que quedó en el nombre elegido.
  const formato =
    FORMATOS.find((f) => fichero.name.toLowerCase().endsWith(f.extension)) ??
    FORMATOS[0]
  const w = await fichero.createWritable()
  await w.write(
    new Blob([formato.texto(nombre, ...t)], {
      type: `${formato.mime};charset=utf-8`,
    })
  )
  await w.close()
}
