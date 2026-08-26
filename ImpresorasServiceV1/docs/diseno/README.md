# Sistema visual — fuentes del lienzo

Propuesta de dirección visual para ImpresorasService: pasar de «herramienta interna cuidada» a
consola de operaciones. Aquí viven las **fuentes** de los tableros; el lienzo navegable está
publicado como artifact:

<https://claude.ai/code/artifact/dad8f4bb-66d8-4397-9b45-7681b7bf5827>

## Qué hay

| Fichero | Tablero |
|---|---|
| `Main.dc.html` | Sistema: escala tipográfica, color por función, botones, estados, navegación, campo |
| `Tipografia.dc.html` | Tres candidatas de fuente sobre el mismo fragmento real |
| `Tablas.dc.html` | Cabecera de tabla azul (actual) frente a neutra, más un punto intermedio |
| `Dashboard.dc.html` | El sistema aplicado al dashboard |
| `Impresoras.dc.html` | El sistema aplicado al maestro-detalle de impresoras |
| `canvas.json` | Posición de los tableros en el lienzo y notas |
| `prompt.md` | El brief que los produjo, autocontenido |

## Cómo se abren

**No se abren en el navegador directamente.** Un `.dc.html` referencia `./support.js`, que solo
existe dentro del lienzo publicado; suelto en disco no pinta nada. Para verlos, usa el enlace de
arriba. Para volver a montar el lienzo desde estas fuentes hace falta el helper de la skill
`design`, que no vive en este repositorio.

El fichero ensamblado (2,2 MB, casi todo código del editor) queda deliberadamente fuera de git: es
regenerable a partir de estos siete ficheros.

## Estado

Dos decisiones seguían abiertas cuando se publicó, y los tableros de Dashboard e Impresoras asumen
una respuesta provisional para poder enseñarse coherentes:

1. **Fuente** — los tableros asumen IBM Plex Sans. Sin decidir.
2. **Cabecera de tabla** — los tableros asumen la neutra. La azul es la que está desplegada hoy.

Nada de esto se ha llevado todavía al código de la aplicación.
