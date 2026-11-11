import { describe, expect, it } from "vitest"
import { ESTADOS_TRABAJO as E, type Impresora } from "@serca/contratos"
import {
  apiSinEstado,
  autor,
  avisoWeb,
  conNivel,
  conProblema,
  pasaFiltroEstado,
  documentoErp,
  enOrden,
  nivelRegla,
  faltaParaReimprimir,
  fechaHora,
  fechaHoraCorta,
  franjasDe,
  horaTrabajo,
  puedeCancelar,
  puedeReimprimir,
  puedeReintentar,
  puerto,
  resumen,
  textoBandeja,
  textoPrevision,
  tonoToner,
  webImpresora,
} from "@/modules/impresion/formato"

const impresora = (connectivitySeverity: Impresora["connectivitySeverity"]) =>
  ({ connectivitySeverity }) as Impresora

describe("orden de las reglas (RoutingResolver)", () => {
  const regla = (
    ruleId: number,
    storeId: number | null,
    documentType: string | null,
    channel: string | null,
    priority = 10
  ) =>
    ({ ruleId, storeId, documentType, channel, priority }) as Parameters<
      typeof enOrden
    >[0][number]

  it("nivel por especificidad; la global con tipo no se aplica nunca", () => {
    expect(nivelRegla(regla(1, 6, "TICKET", "WEB"))).toBe(5)
    expect(nivelRegla(regla(1, 6, "TICKET", null))).toBe(4)
    expect(nivelRegla(regla(1, 6, null, "WEB"))).toBe(3)
    expect(nivelRegla(regla(1, 6, " ", null))).toBe(2)
    expect(nivelRegla(regla(1, null, null, null))).toBe(1)
    expect(nivelRegla(regla(1, null, "TICKET", null))).toBe(0)
  })

  it("gana la más específica y, entre iguales, la de menor prioridad y menor número", () => {
    const orden = enOrden([
      regla(1, 6, null, null, 1),
      regla(2, 6, "TICKET", null, 50),
      regla(3, 6, "TICKET", null, 5),
      regla(4, null, null, null, 0),
      regla(5, 6, "TICKET", null, 5),
    ])
    expect(orden.map((r) => r.ruleId)).toEqual([3, 5, 2, 1, 4])
  })
})

describe("resumen de una tienda", () => {
  it("manda el peor caso", () => {
    expect(
      resumen([
        impresora("healthy"),
        impresora("warning"),
        impresora("critical"),
      ])
    ).toEqual({ tono: "error", texto: "1 con error" })
    expect(resumen([impresora("healthy"), impresora("warning")])).toEqual({
      tono: "aviso",
      texto: "1 pendiente",
    })
    expect(resumen([impresora("healthy"), impresora("neutral")]).tono).toBe(
      "ok"
    )
    expect(resumen([]).texto).toBe("Sin impresoras")
  })

  // docs/72, R2: lo que dice la propia impresora cuenta igual.
  it("cuenta el estado IPP: parada es error y tóner bajo, aviso", () => {
    const ipp = (ippSeverity: Impresora["ippSeverity"]) => ({
      ...impresora("healthy"),
      ippSeverity,
    })
    expect(resumen([ipp("healthy"), ipp("critical")])).toEqual({
      tono: "error",
      texto: "1 con error",
    })
    expect(resumen([ipp("healthy"), ipp("warning")])).toEqual({
      tono: "aviso",
      texto: "1 con aviso",
    })
    // Lo de conexión manda en el texto: sin host o sin comprobar.
    expect(resumen([impresora("warning"), ipp("warning")]).texto).toBe(
      "1 pendiente"
    )
    expect(resumen([ipp("neutral"), ipp(undefined)]).tono).toBe("ok")
    expect(conProblema(ipp("warning"))).toBe(true)
    expect(conProblema(ipp("neutral"))).toBe(false)
  })
})

describe("color del tóner (umbrales de Umbrales)", () => {
  const con = (extra: Partial<Impresora>) => extra as Impresora
  it("verde, amarillo y rojo con los umbrales de la API", () => {
    const i = con({ tonerWarningPct: 30, tonerCriticalPct: 15 })
    expect(tonoToner(31, i)).toBe("ok")
    expect(tonoToner(30, i)).toBe("aviso")
    expect(tonoToner(16, i)).toBe("aviso")
    expect(tonoToner(15, i)).toBe("error")
  })
  it("sin umbrales (API anterior), los de por defecto: 20 y 10 %", () => {
    expect(tonoToner(21, con({}))).toBe("ok")
    expect(tonoToner(14, con({}))).toBe("aviso")
    expect(tonoToner(10, con({}))).toBe("error")
  })
})

describe("filtro, previsión y bandejas (docs/72, mejoras 2, 6 y 7)", () => {
  const con = (extra: Partial<Impresora>) => extra as Impresora
  const marcador = (nivel: number) => ({
    nombre: "BK",
    color: null,
    tipo: "toner",
    nivel,
    bajo: 7,
  })
  it("filtra por lo que dice la propia impresora", () => {
    const parada = con({ ippSeverity: "critical", ippMarkers: [marcador(80)] })
    const tambor = con({ ippSeverity: "warning", ippMarkers: [marcador(60)] })
    const toner = con({ ippSeverity: "warning", ippMarkers: [marcador(14)] })
    const bien = con({ ippSeverity: "healthy", ippMarkers: [marcador(90)] })
    const todas = [parada, tambor, toner, bien]
    expect(todas.filter((i) => pasaFiltroEstado(i, "avisos"))).toEqual([
      parada,
      tambor,
      toner,
    ])
    expect(todas.filter((i) => pasaFiltroEstado(i, "criticas"))).toEqual([
      parada,
    ])
    expect(todas.filter((i) => pasaFiltroEstado(i, "toner"))).toEqual([toner])
    expect(todas.filter((i) => pasaFiltroEstado(i, "todas"))).toHaveLength(4)
  })
  it("dice cuándo se agota el tóner según la Canon, si lo da", () => {
    expect(textoPrevision(null)).toBe(null)
    expect(textoPrevision(1)).toBe("Se agota en 1 día (según la impresora)")
    expect(textoPrevision(0)).toBe("Agotado o a punto (según la impresora)")
    expect(textoPrevision(15)).toBe(
      "Se agota en unos 15 días (según la impresora)"
    )
    expect(textoPrevision(366)).toBe(
      "Se agota en más de un año (según la impresora)"
    )
  })
  it("cuenta las hojas de cada bandeja o lo que se sabe", () => {
    expect(
      textoBandeja({ nombre: "Drawer 1", capacidad: 550, nivel: 1100 })
    ).toBe("1100 de 550 hojas")
    expect(textoBandeja({ nombre: "TRAY1", capacidad: 250, nivel: -3 })).toBe(
      "Con papel"
    )
    expect(textoBandeja({ nombre: "Bypass", capacidad: 100, nivel: 0 })).toBe(
      "Vacía"
    )
    expect(textoBandeja({ nombre: "X", capacidad: null, nivel: -2 })).toBe(
      "Sin dato"
    )
  })
})

describe("tóner con los umbrales de Reglas y avisos", () => {
  const con = (extra: Partial<Impresora>) => extra as Impresora
  it("mandan los de Reglas y avisos sobre los que trae la impresora", () => {
    const i = con({ tonerWarningPct: 20, tonerCriticalPct: 10 })
    const reglas = { aviso: 40, critico: 25 }
    expect(tonoToner(35, i, reglas)).toBe("aviso")
    expect(tonoToner(25, i, reglas)).toBe("error")
    expect(tonoToner(35, i)).toBe("ok") // sin reglas, los de la impresora
  })
  it("sabe cuándo la API del servicio no manda el estado", () => {
    expect(apiSinEstado([con({}), con({})])).toBe(true)
    expect(apiSinEstado([con({}), con({ ippSeverity: "neutral" })])).toBe(false)
    expect(apiSinEstado([])).toBe(false)
  })
})

describe("aviso al abrir la web de la impresora", () => {
  it("nombra la tienda a cuya red hay que tener acceso", () => {
    const a = avisoWeb(
      { printerName: "MOSTRADOR01", storeId: 15 } as Impresora,
      [{ storeId: 15, name: "Hellín", isActive: true } as never]
    )
    expect(a.titulo).toBe("Abriendo la web de MOSTRADOR01")
    expect(a.detalle).toContain("red de la tienda 15 · Hellín")
  })
})

describe("web y consumibles de la impresora", () => {
  const con = (extra: Partial<Impresora>) =>
    ({ ...impresora("healthy"), ...extra }) as Impresora
  it("enlaza la raíz de su web, solo si es http(s)", () => {
    // Canon: portal (Remote UI) en el mismo puerto.
    expect(
      webImpresora(
        con({ ippMoreInfoUrl: "http://192.42.170.6:8000/rps/airprint" })
      )
    ).toBe("http://192.42.170.6:8000/")
    // Brother: la raíz lleva a la página de estado, sin login.
    expect(
      webImpresora(
        con({ ippMoreInfoUrl: "http://192.42.170.28/net/net/airprint.html" })
      )
    ).toBe("http://192.42.170.28/")
    expect(webImpresora(con({ ippMoreInfoUrl: "no es una url" }))).toBe(null)
    expect(webImpresora(con({ ippMoreInfoUrl: "javascript:alert(1)" }))).toBe(
      null
    )
    expect(webImpresora(con({ ippMoreInfoUrl: null }))).toBe(null)
  })
  it("deja fuera los niveles que no son un porcentaje", () => {
    const marcador = (nivel: number | null) => ({
      nombre: "BK",
      color: null,
      tipo: "toner",
      nivel,
      bajo: 7,
    })
    expect(
      conNivel(
        con({ ippMarkers: [marcador(83), marcador(-3), marcador(null)] })
      ).map((c) => c.nivel)
    ).toEqual([83])
  })
})

describe("acciones por estado (como el PHP)", () => {
  it("reintenta solo lo que no ha salido", () => {
    expect([E.pendiente, E.error].every(puedeReintentar)).toBe(true)
    expect(
      [E.sinConfirmar, E.bloqueado, E.impreso, E.cancelado].some(
        puedeReintentar
      )
    ).toBe(false)
  })
  it("reimprime solo lo que está sin confirmar", () => {
    expect(puedeReimprimir(E.sinConfirmar)).toBe(true)
    expect(puedeReimprimir(E.error)).toBe(false)
  })
  it("cancela pendientes, errores, sin confirmar y bloqueados", () => {
    expect(
      [E.pendiente, E.error, E.sinConfirmar, E.bloqueado].every(puedeCancelar)
    ).toBe(true)
    expect([E.impreso, E.cancelado, E.aceptado].some(puedeCancelar)).toBe(false)
  })
})

describe("formato", () => {
  it("da la fecha UTC en hora de Madrid, con y sin horario de verano", () => {
    expect(fechaHora("2026-07-01T10:05:00+00:00")).toBe("01/07/2026 12:05")
    expect(fechaHora("2026-12-01T23:30:00Z")).toBe("02/12/2026 00:30")
    expect(fechaHora(null)).toBe("-")
  })
  it("cada periodo ofrece las franjas con 2 a 500 tramos, y siempre la automática", () => {
    const minutos = (v: Parameters<typeof franjasDe>[0]) =>
      franjasDe(v).map((f) => f.minutos)
    expect(minutos("today")).toEqual([0, 15, 30, 60, 120, 240, 720])
    expect(minutos("7d")).toEqual([0, 30, 60, 120, 240, 720, 1440])
    expect(minutos("30d")).toEqual([0, 120, 240, 720, 1440])
  })
  it("el documento del ERP sale del tipo y del número; las pruebas no tienen", () => {
    expect(documentoErp("A", "123456")).toEqual({
      tipo: "A",
      nombre: "Albarán",
      numero: 123456,
    })
    expect(documentoErp("C", "98765-2")).toMatchObject({
      nombre: "Contado",
      numero: 98765,
    }) // reimpresión
    expect(documentoErp("PRUEBA", "PRUEBA-IP-115904")).toBeNull()
    expect(documentoErp("A", "PRUEBA-IP-115904")).toBeNull()
    expect(documentoErp("A", "0")).toBeNull()
    expect(documentoErp("A", "99999999999")).toBeNull() // fuera de integer
  })
  it("lo de hoy en Madrid, solo con la hora", () => {
    const ahora = Date.parse("2026-10-26T15:00:00Z") // 16:00 en Madrid
    expect(fechaHoraCorta("2026-10-26T08:05:00Z", ahora)).toBe("09:05")
    expect(fechaHoraCorta("2026-10-25T23:30:00Z", ahora)).toBe("00:30") // ya es 26 en Madrid
    expect(fechaHoraCorta("2026-10-25T22:30:00Z", ahora)).toBe(
      "25/10/2026 23:30"
    )
    expect(fechaHoraCorta(null, ahora)).toBe("-")
  })
  it("saca el puerto del transporte", () => {
    expect(puerto("tcp/9100")).toBe("9100")
    expect(puerto(null)).toBe("-")
  })
})

describe("hora de un trabajo reintentado o reimpreso", () => {
  const ahora = Date.parse("2026-10-26T15:00:00Z")
  const llego = "2026-10-26T09:15:00Z"
  const salio = "2026-10-26T11:55:00Z"

  it("sin reintentos, la de llegada y sin marca", () => {
    expect(horaTrabajo({ createdAtUtc: llego }, ahora)).toEqual({
      marca: null,
      hora: "10:15",
      detalle: "Llegó 26/10/2026 10:15",
    })
  })

  it("reintentado tras un error, la de la nueva salida", () => {
    const h = horaTrabajo(
      { createdAtUtc: llego, sentAtUtc: salio, retries: 1, reprints: 0 },
      ahora
    )
    expect(h.marca).toBe("reintento")
    expect(h.hora).toBe("12:55")
    expect(h.detalle).toBe(
      "Llegó 26/10/2026 10:15\nReintentado tras un error 1 vez\nSalió de nuevo 26/10/2026 12:55"
    )
  })

  it("la reimpresión, que puede duplicar, manda sobre el reintento", () => {
    expect(
      horaTrabajo(
        { createdAtUtc: llego, sentAtUtc: salio, retries: 1, reprints: 2 },
        ahora
      ).marca
    ).toBe("reimpresion")
  })

  it("si aún no ha vuelto a salir, la de llegada y lo dice", () => {
    const h = horaTrabajo(
      { createdAtUtc: llego, sentAtUtc: null, reprints: 1 },
      ahora
    )
    expect(h.hora).toBe("10:15")
    expect(h.detalle).toContain("Aún no ha vuelto a salir")
  })
})

describe("reimprimir lo que ya salió", () => {
  it("solo lo impreso y lo sin confirmar; lo que espera confirmación, no", () => {
    expect(puedeReimprimir(E.impreso)).toBe(true)
    expect(puedeReimprimir(E.sinConfirmar)).toBe(true)
    expect(puedeReimprimir(E.aceptado)).toBe(false)
    expect(puedeReimprimir(E.error)).toBe(false)
  })

  it("pide motivo, explicación con Otro y aceptar el riesgo", () => {
    const ok = { motivo: "PERDIDO" as const, nota: "", entiendo: true }
    expect(faltaParaReimprimir(ok)).toBeNull()
    expect(faltaParaReimprimir({ ...ok, motivo: "" })).toBe("Elige el motivo")
    expect(faltaParaReimprimir({ ...ok, motivo: "OTRO", nota: "  " })).toBe(
      "Explica el motivo"
    )
    expect(
      faltaParaReimprimir({
        ...ok,
        motivo: "OTRO",
        nota: "lo pidió el cliente",
      })
    ).toBeNull()
    expect(faltaParaReimprimir({ ...ok, entiendo: false })).toMatch(/duplicado/)
    expect(faltaParaReimprimir({ ...ok, nota: "x".repeat(501) })).toMatch(/500/)
  })
})

describe("quién hizo una acción", () => {
  it("del ERP, código y nombre por separado; del PHP, su login de nombre", () => {
    expect(autor("ERP 123 Ana López")).toEqual({
      codigo: "123",
      nombre: "Ana López",
    })
    expect(autor("ERP 123")).toEqual({ codigo: "123", nombre: null })
    expect(autor("jefe.alicante")).toEqual({
      codigo: null,
      nombre: "jefe.alicante",
    })
    expect(autor(null)).toEqual({ codigo: null, nombre: null })
  })
})
