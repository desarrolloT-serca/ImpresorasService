# Prompt — Sistema visual ImpresorasService

Rediseña visualmente ImpresorasService, una aplicación web interna que supervisa y administra la
infraestructura de impresión de 34 tiendas de retail: cola de trabajos, impresoras por tienda,
reglas de enrutado, incidencias y salud por tienda.

Céntrate exclusivamente en UI/UX visual. No propongas cambios de backend, arquitectura, lógica de
negocio, APIs ni tecnologías. La aplicación es Laravel con Blade y CSS propio, sin framework de
JavaScript: cualquier propuesta tiene que poder expresarse en HTML y CSS.

## Dirección

Consola de operaciones empresarial. Debe transmitir precisión, estabilidad, control e
infraestructura. Referencias de equilibrio: el refinamiento de Linear, la claridad del dashboard de
Stripe, la densidad operativa de Datadog y la sobriedad administrativa de GitHub.

Evita: glassmorphism, neumorfismo, gradientes decorativos, sombras marcadas, radios grandes,
tarjetas innecesarias, huecos enormes, tono simpático o de juguete, y dashboards llenos de gráficas
decorativas. Nada de emoji como iconos.

Cuando alguien la vea debe pensar «esta es una consola profesional sobre la que una empresa opera a
diario», no «esta es una plantilla SaaS bonita».

## Punto de partida verificado

No son impresiones: son datos medidos sobre la hoja de estilos actual.

- **No hay escala tipográfica.** 117 declaraciones con peso 700 o superior (800 × 40, 850 × 35,
  700 × 31, 900 × 11) frente a **una sola de 500 y una de 400**. Cuando todo es negrita, la negrita
  deja de crear jerarquía.
- **Hay texto ilegible.** Unas cuarenta declaraciones por debajo de 12px, incluidas seis a 8 y 9px.
- **La fuente no es la que parece.** El CSS pide `Futura, "Futura PT", "Trebuchet MS", Arial`, pero
  Futura no viene instalada en Windows y Futura PT es de Adobe: en un puesto normal se ve
  **Trebuchet MS**. Existe además un token `--font-sans: 'Instrument Sans'` en `app.css` que **no se
  usa en ningún sitio y cuya fuente no se carga**: es residuo del starter de Laravel, no una
  decisión de diseño. Sea cual sea la elegida, hay que cargarla como fuente web.
- **El azul pinta superficies.** Cabeceras de tabla en `#1b2f82` con texto blanco y filete rojo,
  item de navegación activo relleno de azul sólido, títulos en azul. El azul ha dejado de significar
  «esto se pulsa».
- **El rojo de marca y el rojo de error son el mismo valor.** `#ed1b2f` se usa como adorno
  (triángulos de titular, filetes de cabecera) y como alarma. Por eso un error real no destaca.
- **Radios de 6px y 4px**, densidad de fila alta. Ambas cosas están bien: consérvalas.
- **El modo oscuro es GitHub Dark literal** (`#0d1117`, `#161b22`, `#58a6ff`). Funciona, pero no
  tiene identidad propia.

## Identidad que se conserva

Paleta corporativa AD Serca, obligatoria:

- Primario `#1b2f82`
- Primario oscuro `#102368`
- Rojo de marca `#ed1b2f`

El azul debe reservarse para navegación activa, selección, acción principal, enlaces y foco. El rojo
de marca se limita a identidad (logo, login, detalles puntuales) y **se separa del rojo de estado**,
que debe ser un valor distinto para que una alarma se distinga de un adorno. El resto de la interfaz
se construye sobre blancos y grises fríos.

## Sistema que quiero definido

**Tipografía.** Una sola familia de interfaz, cargada como fuente web. Propón dos o tres candidatas
y muéstralas sobre el mismo fragmento real (un KPI y tres filas de tabla con nombres de tienda y
cifras), cada una con su contrapartida honesta. Valora que tenga monoespaciada hermana: hay muchos
identificadores que se comparan carácter a carácter (colas de spool tipo `\\SRV-PRINT\TICKET-02`,
IPs, códigos de tienda). No propongas Inter, Roboto ni Arial.

Escala: título de página 22px/600, título de sección 16px/600, interfaz 14px/400, tabla 13px,
metadato 12px, chip 11px, KPI 26–32px/650. **Suelo duro de 11px.** La jerarquía se construye con
tamaño, color y espacio, no con peso.

**Superficies.** Menos caja. Agrupar con espacio, divisores y títulos, no con contenedor dentro de
contenedor. Sombras casi imperceptibles: borde de 1px y como mucho `0 1px 2px`. Sombra visible solo
en modal, dropdown y popover.

**Tablas.** Rejilla de datos: cabecera discreta, separadores finos, filas blancas, hover
extremadamente suave, selección en azul tenue, texto a la izquierda, cifras a la derecha con cifras
tabulares. Altura de fila entre 36 y 44px: la densidad actual es buena, no la aflojes. Mejora el
aire horizontal, no el vertical.

**Estados.** `operativo / atención / crítico / sin comprobar`. Indicador de punto de color más texto,
no superficies coloreadas. Con 34 tiendas en pantalla, el patrón actual de fondo más borde más texto
de color convierte la tabla en un semáforo.

**Navegación.** Barra lateral de 240–248px, fondo neutro, grupos Operación / Configuración / Sistema
/ Desarrollo con etiquetas discretas. El item activo se tiñe (azul muy suave, texto e icono azules,
barra lateral de 2–3px), no se rellena de azul sólido.

**Cabecera.** El título de página no debe parecer que está dentro de otra tarjeta. Título oscuro y
neutro, no azul.

**Botones.** Cuatro familias y ninguna más: primario azul sólido, secundario blanco con borde,
fantasma sin borde, y peligro rojo reservado para la confirmación final.

**Formularios.** Etiqueta encima, campo de 38–40px, pista debajo cuando haga falta. Poco espacio
entre etiqueta, campo y pista; mucho entre grupos. Los grupos se separan con títulos y divisores, no
metiendo cada uno en su tarjeta.

**Iconos.** Un solo lenguaje, lineal, 16–18px, grosor homogéneo, referencia Lucide. Dibujados como
SVG. Hoy hay SVG en línea mezclados con un componente de icono.

**Modo oscuro.** Extensión real de la identidad, no una copia de GitHub Dark. Debe conservar el azul
Serca adaptado.

## Dos decisiones que quiero ver, no que decidas

Móntalas como comparación lado a lado con los mismos datos, y escribe la contrapartida de cada
opción, no solo el argumento de tu favorita:

1. **Cabecera de tabla**: la azul actual frente a una neutra. La azul es un diseño aprobado y ya
   desplegado, así que quitarla es revertir una decisión tomada, no corregir un descuido. Incluye
   también un punto intermedio: cabecera neutra con una regla azul fina debajo.

2. **KPI del bloque de estado**: cuatro tarjetas independientes frente a una sola superficie con
   divisores verticales. Ojo: una versión previa que fundía los KPI en una franja fina se rechazó
   porque la cifra perdía tamaño. Si propones la superficie única, la cifra tiene que seguir siendo
   lo primero que se escanea.

## Alcance

Primero el sistema, no las pantallas: escala tipográfica, roles de color, botones, chips de estado,
fila de tabla, campo de formulario e item de navegación. Después **solo dos pantallas** que lo
ejerciten:

- **Dashboard** — estado actual de la operación (cola, sin reenviar, impresoras con problema, sin
  impresora), incidencias priorizadas con acción por fila, rendimiento del periodo y salud por
  tienda.
- **Impresoras** — patrón maestro-detalle: rail izquierdo de tiendas y panel derecho con la tabla de
  impresoras de la seleccionada. El rail debe leerse como navegación contextual, no como una
  colección de tarjetas.

El resto de pantallas (Cola, Tiendas, Usuarios, Reglas, Ajustes, Login) se derivan de ahí y no hacen
falta en esta pasada.

## Datos reales para las maquetas

Nada de contenido de relleno. Tiendas: `0412 Getafe Norte` (crítica, 34 en cola, 7 sin reenviar),
`2044 Leganés Este` (crítica, sin impresoras activas), `0731 Alcalá Centro` (aviso, 18 en cola, sin
host), `1180 Torrejón Sur` (aviso, 3 sin asignar), `0208 Vallecas` (operativa). Impresoras:
`TICKET-01`, `TICKET-02`, `ALBARAN-01`. Periodo de hoy: 1 284 recibidos, 1 191 impresos, 63
fallidos, 93 % de éxito, 47 en cola con umbral crítico en 30.
