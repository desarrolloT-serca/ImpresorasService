import { cleanup, render } from "@testing-library/react"
import { afterEach, describe, expect, it } from "vitest"
import { MensajeTelegram } from "@/modules/impresion/Telegram"

afterEach(cleanup)

// El mensaje de Telegram llega como HTML (b, i, code). Se pinta sin innerHTML:
// cualquier otra etiqueta, aunque venga del nombre de una tienda, es texto (WEB-9).
describe("vista previa del mensaje de Telegram", () => {
  it("pinta negrita, cursiva y código, y deshace las entidades", () => {
    const { container } = render(
      <MensajeTelegram
        html={"<b>ATALAYAS</b> en <i>crítico</i>: <code>2 &amp; 3</code>"}
      />
    )
    expect(container.querySelector("b")?.textContent).toBe("ATALAYAS")
    expect(container.querySelector("i")?.textContent).toBe("crítico")
    expect(container.querySelector("code")?.textContent).toBe("2 & 3")
  })

  it("lo que no es b, i o code queda como texto", () => {
    const { container } = render(
      <MensajeTelegram
        html={"Tienda &lt;script&gt;x&lt;/script&gt; <a href='y'>z</a>"}
      />
    )
    expect(container.querySelector("script, a")).toBeNull()
    expect(container.textContent).toBe(
      "Tienda <script>x</script> <a href='y'>z</a>"
    )
  })

  it("anida etiquetas", () => {
    const { container } = render(
      <MensajeTelegram html={"<b>uno <i>dos</i></b>"} />
    )
    expect(container.querySelector("b i")?.textContent).toBe("dos")
  })
})
