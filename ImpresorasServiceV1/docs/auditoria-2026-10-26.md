# Auditoría del 26/10/2026 y hoja de ruta para resolverla

**Alcance:** todo lo hecho el 26/10/2026 en los dos repositorios.
- ImpresorasService, rama `feat/web-erp`: commits `2ed91d6..601f865` y el árbol sin commit (B9.4 y B10).
- ERP, rama `cayetanoImpresion`: commits `064b912..HEAD` (sin `erp_antiguo/`).

**Qué se revisó:** circuito de impresión (IPP, PWG raster, watchdog, `pdf_url`, `EstadoBd`), API y datos
(reimpresiones, franjas), seguridad (pase del ERP, pasarela `/api/impresion`) y pantallas (web, ERP y PHP).
Lo revisaron cuatro revisores por separado y después se comprobó en el código cada hallazgo alto y medio.

**Estado:** solo informe; **no hay nada aplicado**. Cuando se decida qué se hace, las filas pasan a
`PLAN.md`, que sigue siendo la fuente de verdad del trabajo pendiente. Este documento queda como registro
de la auditoría.

**Origen:** dice de qué trabajo viene cada fallo.
- B9: imprimir por IP.
- B9.4: errores para la gente.
- B10: reimpresiones. Está sin commit. Se atribuyó a la sesión paralela, que el 27/10 dijo que no es suyo: queda sin dueño.
- `pdf_url`: también de la otra sesión.
- Pase: pase del ERP y pasarela.
- Franjas: franjas del dashboard.
- Anterior: el fallo ya existía antes del 26/10.

**Tamaño:** S, menos de 1 hora. M, unas horas. L, más de un día.

No se ha encontrado ningún camino en el que el sistema imprima dos veces por sí solo. Los duplicados
posibles vienen de situaciones que empujan a una persona a reimprimir algo que sí salió, o que acabará saliendo.

---

## 1. Hallazgos

### Alta

| ID | Qué pasa | Dónde | Origen | Arreglo propuesto | Tamaño |
|---|---|---|---|---|---|
| A1 | El botón «Reimprimir» del PHP falla siempre. Sale en «Sin confirmar» y llama a `/route`, que ahora rechaza ese estado con un 400. **Ya está desplegado (14:10).** | `cola.blade.php:219-226` y `:366-372`, `ColaController.php:242`, `RoutingService.cs:63-67` | B10 | Ver **D1** | S |
| A2 | Con `UseRealSpooler=false` sigue saliendo papel real. La simulación solo cambia el spooler, pero `IIppConfirmationService` se registra siempre y `EnviarAsync` imprime antes por IP. Un Worker de pruebas que apunte a esta BD imprime en las tiendas. | `DependencyInjection.cs:83-89`, `PrintExecutionService.cs:333` | B9 | En simulación, crear `PrintExecutionService` sin `ipp` | S |
| A3 | Una impresora con solo cola local (sin IP ni ruta UNC) no imprime nunca. El monitor anota `HOST_NOT_CONFIGURED` con `LastConnectionOk=false` y la protección de `PrintExecutionService` lo toma por «no alcanzable». Tras 4 intentos acaba en `PRINTER_UNREACHABLE`. | `PrinterConnectivityMonitorService.cs:150-160`, `PrintExecutionService.cs:157` | Anterior, pero B9 lleva a esa configuración | Añadir `&& !IsHostNotConfiguredError(printer.LastConnectionError)` a la protección | S |

### Media

| ID | Qué pasa | Dónde | Origen | Arreglo propuesto | Tamaño |
|---|---|---|---|---|---|
| M1 | Parar el servicio a mitad de un envío corta el envío IPP. Si IPP ya había dicho «aceptado», la transición falla porque el token está cancelado. Al arrancar queda `PRINTING_INTERRUPTED` («puede haberse impreso») y eso invita a reimprimir. Pasa **en cada despliegue**. | `IppConfirmationService.cs:158`, `PrintExecutionService.cs:250-292` | B9 | `CancellationToken.None` en las transiciones y en el UPDATE posteriores al envío. Para el envío, un token propio con el timeout | S |
| M2 | Un trabajo que la impresora acepta y luego aborta por el documento queda en `ErrorFinal IPP_DOCUMENT_ERROR`. «Reintentar» lo manda otra vez por IP y se aborta igual. La pista «dale una cola de Windows» no sirve: la cola solo se usa cuando IP no llega a enviar. | watchdog `:306-313`, `ErroresTrabajo.cs:38` | B9.4 | Ver **D2** | S-M |
| M3 | El watchdog abandona a los 30 min un trabajo que la impresora tiene pendiente o detenido (job-state 3-6). Queda en `PrintedUnknown`, que se puede reimprimir; si luego se carga papel, salen el original y la copia. | watchdog `:278-295`, `:315-324` | B9 | Ver **D3** | M |
| M4 | «Se reintenta solo» aparece en trabajos que ya están en `ErrorFinal`. Al agotar los intentos se conserva el código transitorio (`IPP_NO_RESPONSE`, `IPP_PRINTER_BUSY`, `PRINTER_UNREACHABLE`…) y el operador espera un reintento que no llega. | `ErroresTrabajo.cs:11` | B9.4 | Que el texto dependa del estado, o cambiarlo por «Se reintenta solo unas veces; si queda en error, reintenta» | S |
| M5 | Algunos errores de IPP se tratan como «no salió nada» y pueden llegar después de sacar páginas: 0x0410/0x0411 (además provocan el reenvío como imagen) y 0x0500/0x0504/0x0508. Posible duplicado. *Confianza media.* | `IppConfirmationService.cs:211-215` | B9.4 | Ver **D4** | S |
| M6 | El límite de 30 s del Print-Job incluye la subida del documento. Una impresora que lee al ritmo de impresión corta los documentos largos o mandados como imagen: `NET_TIMEOUT`, `PrintedUnknown` y salida parcial. *Confianza media.* | `IppConfirmationService.cs:159` | B9 | Ver **D5** | S |
| M7 | Un motivo IPP de fabricante de más de 60 caracteres va crudo a `last_error_code varchar(60)`. PostgreSQL da error 22001 y **tumba el lote del watchdog en cada ciclo**. | `IppConfirmationService.cs:62` | Anterior | Truncar a 60 | S |
| M8 | La descarga de `pdf_url` no tiene tope de tiempo para el cuerpo: con `ResponseHeadersRead`, el timeout solo cubre las cabeceras. Un cuerpo que se queda parado cuelga la ingesta entera, que es secuencial. | `ErpJobSourceAdapter.cs:163,173` | `pdf_url` | Un CTS con `CancelAfter(30 s)` que cubra también la lectura | S |
| M9 | El KPI «Reimpresiones» recorre toda `printer_print_job_event` en cada carga del dashboard. Solo hay índice `(job_id, occurred_at_utc)`. | `DashboardController.cs:94-97`, `crear-esquema.sql:132` | B10 | Índice parcial `(occurred_at_utc) WHERE event_type = 'REPRINT'` (migración: la aplica el usuario) | S |
| M10 | El ERP lee hasta 28 MB de JSON en `/api/impresion/sourceprintjobs/test` antes de comprobar la sesión. La comprobación de origen se salta poniendo la cabecera a mano, así que un cliente sin sesión puede agotar la memoria del ERP. | `erp/apps/api/src/core/configurar.ts:66` | Pase | Comprobar la sesión antes del parser de esa ruta | S |
| M11 | El pase viaja en claro por HTTP al 5105 y vale 120 s. Con el rol Admin entra en `/api/users` (`AdminOnly`) y puede crear un usuario Admin permanente del PHP. La pasarela no expone `/api/users`, pero .NET acepta el pase si se le llama directamente. | `PaseErp.cs:34-59`, `UsersController.cs:12` | Pase | Ver **D6** | S-M |
| M12 | `Erp__Secret` está en el registro del servicio, que cualquier usuario local del servidor puede leer; con él se pueden firmar pases. Ya pasaba antes con `Jwt__Secret` y la BD. | `install-windows-services.ps1` | Pase y anterior | Ver **D7** | S |
| M13 | Con «Hoy» a primera hora y franja de 4 h o 12 h queda un solo punto. La gráfica no se pinta y **su selector de franja desaparece con ella**; no se recupera sin recargar. | `Dashboard.tsx:418`, `formato.ts:119-125` | Franjas | Pintar el selector fuera de la gráfica, o volver a «Automática» si hay menos de 2 puntos | S |
| M14 | La columna «Documento» de la cola puede quedarse a 0 px. Las columnas fijas suman unos 1282 px, la tabla tiene `min-w` de 62rem (992 px) y es `table-fixed`: si no hay espacio, la columna sin ancho fijo desaparece. | `Cola.tsx:152`, `:271-417` | B9.4 (Error a 220 px) y anterior | Subir `min-w` a la suma real, o dar ancho mínimo a «Documento» | S |

### Baja

L viene de «leve», para no confundirla con los bloques B del plan.

| ID | Qué pasa | Dónde | Origen |
|---|---|---|---|
| L1 | El watchdog escribe con `Attach` sin condicionar el estado y puede pisar una cancelación. Ahora la ventana es mayor, porque hace una consulta IPP por trabajo y en serie. | watchdog `:134-150` | Anterior |
| L2 | `SentAtUtc` toma el último `SpoolAccepted`, también el de «impresora recuperada». | `PrintJobsController.cs:107-113` | B10 |
| L3 | `ResetForResendAsync` no usa transacción: si algo falla entre el UPDATE y el evento, queda una copia sin registrar. | `RoutingService.cs:127-160` | B10 |
| L4 | Se puede reintentar un `ErrorFinal` sin PDF y acaba en `PDF_MISSING`. `ReprintAsync` sí lo comprueba. | `RoutingService.cs:54` | B10 |
| L5 | Reimprimir pone `AttemptCount=0`, lo que afecta al KPI de fallos, y las reimpresiones anteriores al 26/10 constan como `RETRY_ROUTE`. | `RoutingService.cs` | B10 |
| L6 | Carrera entre la retención de PDF y una reimpresión. | `PdfRetention…:52-54` | Anterior y B10 |
| L7 | Reimprimir carga `PdfBlob` dos veces. Una `InvalidOperationException` sale como 400 con el mensaje interno. | `RoutingService.cs`, `PrintJobsController.cs` | B10 |
| L8 | El registro de reimpresiones no cuadra con el KPI: incluye tiendas inactivas, corta en 500 y calcula «desde» en la hora del navegador. | `Reimpresiones.tsx`, `GetReprints` | B10 |
| L9 | Cambio de hora en las franjas: el comentario de la hora repetida es falso (`ConvertTimeToUtc` usa la hora estándar) y el primer tramo sale mal con la hora que se salta. | `DashboardController` | Franjas |
| L10 | `EstadoBd` trata un timeout de comando como «BD caída». | `EstadoBd.cs:36` | Anterior |
| L11 | El raster no dibuja anotaciones ni campos de formulario. Arreglo: `GetImage(RenderFlags.RenderAnnotations \| RenderFlags.RenderForPrinting)`. | `PwgRaster.cs:29` | B9 |
| L12 | El raster no rota las páginas apaisadas ni ajusta las que no son A4, guarda unas 4 copias del documento en memoria y un OutOfMemory se informa como `PDF_INVALID`. | `PwgRaster.cs`, `IppConfirmationService.cs:146-153` | B9 |
| L13 | Get-Job-Attributes no manda `requesting-user-name`: una impresora con privacidad de trabajos puede ocultar el estado. | `IppConfirmationService.cs:325-347` | B9 |
| L14 | Cuando falla IPP, el paso a la cola de Windows es silencioso: el evento no guarda el motivo. No se usa `IppSupported==false` para saltarse IPP, así que con el 631 filtrado se pierden 3 s por trabajo. | `PrintExecutionService.cs:340-341` | B9 |
| L15 | Un trabajo abortado o cancelado en la impresora se reintenta como `RETRY_ROUTE` y no como reimpresión, aunque pudo salir en parte. | `RoutingService.cs` | B9 y B10 |
| L16 | Un fallo pasajero de `pdf_url` se reintenta sin tope y no aparece en la cola. Solo se ve en `sourcePrintJobsPending`. | `ErpJobSourceAdapter.cs:117-122` | `pdf_url` |
| L17 | El Worker se cae al arrancar si la BD no responde y depende del reinicio de Windows, al contrario de lo que dice B8. | `Worker/Program.cs:28-36` | Anterior |
| L18 | Al catálogo de errores le falta `UNKNOWN`, y el test no lo detecta. | `PrintExecutionService.cs:314` | B9.4 |
| L19 | Seguridad menor: `ClockSkew` cero (si los relojes no cuadran, el log culpa al secreto); el `x-request-id` del ERP no lo lee .NET, que usa `X-Correlation-Id`; en la pasarela, el `fetch` sigue redirecciones y el `arrayBuffer` está fuera del try; `VeTodasLasTiendas` también habilita acciones; el instalador acepta cualquier firma válida y no rota `Erp__Secret`. | Pase y pasarela | Pase |
| L20 | Pantallas: se ofrece reimprimir sin PDF (más de 90 días); `useReimprimir` invalida todo `["impresion"]` y las acciones masivas, una vez por trabajo; la cola no tiene filtro «Impreso»; hay un `<button>` con un `<div>` dentro en `Cifra`; el comentario de `core/api.ts:54-56` es falso y un 403 vacío se muestra como «Sin respuesta»; el error del formulario de impresora aparece pegado a `host`; el PHP admite 255 caracteres en el nombre y la API 120; faltan `aria-label` en los botones de fila. | Web, ERP y PHP | Varios |

### Encontrado después de la auditoría

| ID | Qué pasa | Dónde | Estado |
|---|---|---|---|
| N1 | En el dashboard, Ondara sale «0 / 0 OK» y «Impresoras con problema 0», pero en Impresoras tiene 2 impresoras y una en Error; Atalayas, 5 activas y el dashboard dice 0 (visto por la sesión `impresorasservicev1-d2` el 27/10/2026) | `DashboardController.BuildStoreRowsAsync`: el desglose por impresora solo salía de las que tenían trabajos | ✅ 27/10/2026, sesión `impresorasservicev1-d2`: parte de las impresoras activas de la tienda y las que no tienen trabajos salen a 0 (`GetOverview_PerPrinterBreakdown_IncludesActivePrintersWithoutJobs`). Sin desplegar |

### Código muerto o de más

| ID | Qué sobra | Origen |
|---|---|---|
| C1 | `PrintPdfAsync` solo devuelve `null` con el Host vacío, y el único llamador ya lo excluye. Sobran el `?`, la rama `porIp ??` y el test `Sin_IP_valida_ni_cola_no_se_puede_imprimir`, que prueba un camino inalcanzable. | B9 |
| C2 | Se pide `printer-state-message` y no se usa (`IppConfirmationService.cs:52`). | B9 |
| C3 | `EstadoDelTrabajo` une los motivos con «, » para que `TranslateReasons` los vuelva a partir. | B9.4 |
| C4 | Las entradas `job-completed-with-errors` y `job-completed-with-warnings` de `MotivosIpp` no se leen nunca. | B9.4 |
| C5 | En la pasarela del ERP, `dep` no se usa, `pase` está exportado sin necesidad y `ahora` no se usa. | Pase |
| C6 | No se usan `previousReprints` ni el parámetro `hasta` de `useReimpresiones`. | B10 |
| C7 | El límite de 500 está repetido en el cliente, `VISTAS` duplica `VistaCola.options` y hay un literal `8` suelto en `Cola.tsx`. | B10 y B9.4 |
| C8 | `TryRetryRouteAsync` tiene un parámetro opcional detrás del `CancellationToken`; `bucket` vale `"hour"` aunque la franja no sea horaria; se usa `DateTimeOffset.UtcNow` en vez de `TimeProvider`. | B10 y franjas |
| C9 | Comentarios desfasados: `PrintExecutionService.cs:252` («venció el plazo») y «dos manifiestos» en el README. | Varios |
| C10 | Los extractores de host están duplicados en `PrintersController` y en el monitor de conectividad. | Anterior |

### Huecos de pruebas

| ID | Qué falta |
|---|---|
| T1 | El watchdog no tiene ninguna prueba: ni `ApplyOutcome`, ni `QueryJobAsync` con su vuelta al estado de la impresora, ni los casos CompletedWithErrors, JobAborted y abandono. |
| T2 | `PrintPdfAsync` no se prueba de principio a fin porque el `HttpClient` no se puede inyectar. Faltan la negociación de formato, el paso de PDF a imagen y `accepting-jobs=false`. |
| T3 | En PwgRaster faltan varias páginas, color y apaisado. |
| T4 | Faltan pruebas de la simulación con una impresora con Host (A2), de una impresora solo con cola local (A3) y de una descarga con el cuerpo lento (M8). |

---

## 2. Decisiones

| ID | Pregunta | Decisión | Estado |
|---|---|---|---|
| D1 | ¿Qué hace el «Reimprimir» del PHP? | **Quitarlo**: lo ya salido se reimprime con motivo desde la web o el ERP (usuario, 27/10/2026) | ✅ Hecho (R0) |
| D2 | Un trabajo abortado por el documento después de aceptarlo, ¿cómo se vuelve a mandar? | **Al reintentar, como imagen directamente**, y la pista lo dice. Sin ajuste nuevo por impresora (usuario, 27/10/2026) | Decidido, va en R1 |
| D3 | ¿Qué hacemos con un trabajo que la impresora tiene parado (sin papel, atasco)? | **El tope no puede ser 24 h**: son pedidos de clientes y urgen (usuario, 27/10/2026). Se aprueba la propuesta de 2.1: **aviso al momento y tope de 10 min**. **Sin Cancel-Job automático** (cambio del usuario, 27/10/2026, tras desplegar el Worker a las 10:48: «prefiero que se haga a mano y que quede constancia de todo, así evitamos errores ocultos»). A los 10 min el trabajo queda «Sin confirmar» con «puede salir aún». **Cancelar a mano en la aplicación cancela también en la impresora** (usuario, 27/10/2026: si no, duplicados silenciosos), y si se reimprime es con motivo. Además de Telegram, llegarán **notificaciones del ERP por usuario u operador** (todavía sin empezar): ver D8. Telegram puede ir **solo en horario** (`TelegramSoloEnHorario`, usuario, 27/10/2026): un bloqueo fuera de horario se avisa al abrir; cada aviso queda como evento `TELEGRAM_ALERT` (`GET /api/telegram/history`) | ✅ Hecho (R1) |
| D8 | Notificaciones del ERP por usuario u operador (futuro) | Lo diseña el ERP. Para que lleguen a quien mandó el documento, ImpresorasService necesita saber **quién lo pidió**: hoy `printer_source_print_job` no lo recoge, así que hará falta una columna nueva en el contrato de entrada (por ejemplo, el código de operador del ERP). Los avisos que vale la pena repartir ya existen como estados y eventos: parado, sin confirmar, error, cancelado por tardar | Cuando el ERP empiece |
| D4 | ¿Qué hacemos con los errores de IPP que pueden llegar después de haber impreso (M5)? | **La tabla de 2.2**: si pudo salir papel, «Sin confirmar» y sin reenvío; si lo canceló alguien en la impresora, «Error», sin reenvío (usuario, 27/10/2026) | Decidido, va en R1 |
| D5 | ¿Qué plazo damos al Print-Job? | **120 s** (usuario, 27/10/2026) | Decidido, va en R1 |
| D6 | Pase del ERP interceptado (M11) | La API deja de aceptar el pase para gestionar usuarios, porque sirve sea cual sea el alojamiento. **Sin decidir si el ERP y la impresión comparten servidor**: seguramente irán separados, pero se valora juntarlos teniendo en cuenta qué pasa si cae el servidor (usuario, 27/10/2026). Si van separados: cortafuegos en el 5105 (ver 2.3) | Lo del código, en R2; lo demás, cuando se decida el alojamiento |
| D7 | Secretos legibles en el registro (M12) | **En el servidor solo entra informática: se acepta el riesgo** y no se cambia nada (usuario, 27/10/2026). Revisarlo si entra más gente | ✅ Cerrado |

### 2.1 D3, nueva propuesta: trabajos urgentes en una impresora parada

**Qué hay hoy:**
- El trabajo pasa a «Bloqueado», con el motivo («Sin papel en la bandeja», «Atasco de papel»), a los 2-3 minutos de mandarlo.
- Eso solo lo ve quien mira la cola. Telegram está desactivado y, aunque se activara, un trabajo bloqueado en su primer intento no cuenta para la salud de la tienda.
- A los 30 min se da por perdido («Sin confirmar»). Si después alguien carga papel, sale el original; y si ya lo habían reimpreso, sale dos veces.

**Propuesta:**
1. **Aviso al momento.** Un trabajo bloqueado cuenta como incidencia de su tienda desde que se bloquea: sale en el dashboard y, con Telegram activo, avisa.
2. **Tope corto, de N minutos (propuesta: 10).** Pasado ese tiempo, el servicio cancela el trabajo en la propia impresora (Cancel-Job). El trabajo queda en «Error» con «Cancelado en la impresora tras N min parada: no saldrá; reintenta».
   - «Reintentar» es seguro: el original ya no puede salir cuando se cargue el papel.
3. **Si la impresora no deja cancelar,** queda «Sin confirmar» con «Puede salir aún cuando se recupere la impresora: compruébalo antes de reimprimir».

**Lo que cuesta:** si alguien carga el papel después del tope, el documento no sale solo; hay que pulsar «Reintentar». Es el precio de no duplicar.

**Falta decidir:** N, y si el bloqueo debe contar ya para la salud de la tienda y Telegram. Hay que comprobar en la Brother que deja cancelar (**V10**).

**Decidido (27/10/2026):** puntos 1 y 3 con N = 10; el punto 2 se descartó después: no hay Cancel-Job automático y se cancela a mano (D3).

### 2.2 D4 en detalle: qué responde la impresora y qué hacemos

Al mandar un trabajo por IP, la impresora contesta con un código. La pregunta es siempre la misma:
**¿puede haber salido papel?**
- Si es seguro que no salió nada, se puede insistir sin riesgo: como imagen, por la cola de Windows o reintentándolo más tarde.
- Si pudo salir algo, no se reenvía solo: se marca «Sin confirmar» y decide una persona, que si reimprime lo hace con motivo y queda registrado.

| Código | Qué significa | Ejemplo | ¿Pudo salir papel? | Hoy | Propuesta |
|---|---|---|---|---|---|
| No conecta (conexión rechazada) | La impresora no contesta por IP | Apagada, otra red, IPP desactivado | No | Prueba la cola o reintenta | Igual |
| HTTP 401/403, 0x0401-0x0403 | Pide usuario o no deja imprimir por IP | HP con «IPP seguro» activado | No: rechaza antes de leer el documento | `IPP_NOT_AUTHORIZED`, prueba la cola | Igual |
| HTTP 413, 0x0408 | Documento demasiado grande | PDF de 40 MB en una impresora con poca memoria | No | `IPP_TOO_LARGE`, prueba la cola | Igual |
| 0x040A, 0x040F | «No sé leer este formato» | La Brother MFC-L2800DW recibe un PDF | No | Se reenvía como imagen | Igual |
| 0x0400, 0x0404-0x0407, 0x0409, 0x040B-0x040E | La petición no le gusta (un valor que no admite) | Una impresora antigua que no entiende un atributo | No | `IPP_REJECTED`, prueba la cola o reintenta | Igual |
| HTTP 503, 0x0502, 0x0506, 0x0507 | «Ahora no puedo» | Está imprimiendo un trabajo grande de otro ordenador, o arrancando desde el reposo | No | `IPP_PRINTER_BUSY`, prueba la cola o reintenta | Igual |
| 0x0501, 0x0503 | No admite imprimir por IPP | Impresora que solo sirve para consultar el estado | No | `IPP_REJECTED`, prueba la cola | Igual, pero sin reintentar por IP |
| **0x0410, 0x0411, 0x0412** | Empezó a leer el documento y encontró algo que no entiende | Albarán de 5 hojas con una fuente dañada en la 4.ª: la HP saca 3 hojas y contesta 0x0411 | **Sí, una parte** | Lo trata como «no salió nada» y lo reenvía como imagen o por la cola: salen otras 5 hojas, 3 duplicadas | **«Sin confirmar»**: «La impresora falló leyendo el documento y puede haber sacado parte. Comprueba qué salió; si falta, reimprime». Sin reenvío automático |
| **0x0500, 0x0504, 0x0505** | Fallo de la impresora mientras trabajaba | Atasco o error del fusor en la hoja 2 de 4; memoria llena a mitad | **Sí, una parte** | Lo manda al momento por la cola o reintenta a los 15 s: duplica lo que ya salió | **«Sin confirmar»**, con el mismo aviso |
| **0x0508** | Alguien canceló el trabajo en el panel mientras salía | El dependiente ve que sale mal y pulsa «Cancelar» | Sí, una parte, y **quería pararlo** | Lo vuelve a mandar entero por la cola, contra la decisión de quien lo canceló | **«Error: cancelado en la impresora»** (`IPP_JOB_CANCELED`), sin reenvío; se reintenta a mano si hace falta |
| Corte a mitad (timeout, conexión cerrada) | No se sabe qué llegó | Se cae la red en plena subida | Puede | «Sin confirmar» (`NET_TIMEOUT`) | Igual (ya es así) |
| **Cualquier otro código o HTTP 500** | No lo conocemos | Firmware raro | No se sabe | Lo trata como «no salió nada» y lo manda por la cola | **«Sin confirmar»**: ante la duda, no duplicar |

**Qué ve y hace el operador:** el trabajo aparece en la cola como «Sin confirmar», con el texto del caso. Comprueba
qué salió y, si falta algo, pulsa «Reimprimir» en la web o el ERP con el motivo (por ejemplo «Mal impreso» o
«Atasco o sin papel»). La copia queda en el registro de reimpresiones.

**El coste:** con un código que de verdad no sacó nada, alguien tiene que mirar la impresora y reimprimir
a mano. La prueba **V2** dirá si la Brother da alguno de estos códigos en la práctica.

### 2.3 D6 explicado: el pase del ERP

**Qué es el pase.**
- Cuando alguien usa la impresión desde el ERP, el servidor del ERP llama a la API de impresión (puerto 5105) en su nombre.
- Para que la API sepa quién es y qué puede hacer, el ERP adjunta un **pase**: un papelito firmado que dice «este es el operador X, de la tienda Y, con estos permisos» y que caduca a los 2 minutos.
- El navegador nunca lo ve; va de servidor a servidor.

**El riesgo.**
- Ese viaje va **sin cifrar** (HTTP, no HTTPS). Quien pudiera escuchar la red entre los dos servidores podría copiar un pase y usarlo esos 2 minutos llamando directamente a la API, sin pasar por el ERP.
- Si el pase es de un administrador, podría incluso **crear un usuario administrador en el PHP**, que no caduca. El ERP nunca pide eso, pero la API lo acepta igual.

**Qué propongo:**
1. **Siempre, en el código:** que la API no acepte un pase para gestionar usuarios; eso queda solo para quien entra con su usuario y contraseña. Es pequeño y quita lo peor.
2. **Según dónde esté cada cosa:**
   - Si el ERP y la API de impresión están **en el mismo servidor**, el pase no sale de esa máquina y no hace falta nada más.
   - Si están **en servidores distintos**, conviene que el puerto 5105 solo acepte conexiones del servidor del ERP y de Nginx (cortafuegos de Windows), o cifrar el tramo con HTTPS.

**La pregunta (V4):** ¿el ERP y el servicio de impresión están en el mismo servidor?

### 2.4 D7 explicado: los secretos en el registro de Windows

**Qué pasa.**
- Los servicios de Windows (la API y el Worker) necesitan tres secretos: la contraseña de la base de datos, la clave con la que firman las sesiones y la clave que comparten con el ERP para los pases.
- El instalador los guarda en el **registro de Windows**, en la ficha de cada servicio.
- En una instalación normal, **cualquier usuario que pueda iniciar sesión en ese servidor**, aunque no sea administrador, puede leer esa parte del registro.
- Con esas claves podría entrar en la base de datos o fabricarse pases de administrador de impresión.

**Qué propongo:**
- Si en ese servidor **solo entra informática**, el riesgo es bajo y se puede aceptar tal cual.
- Si **entra más gente** (otros departamentos, cuentas de tienda, escritorio remoto compartido), el instalador debería quitar a los usuarios normales el permiso de leer esa parte del registro. Es un cambio pequeño en `install-windows-services.ps1`.

**La pregunta (V6):** ¿quién puede iniciar sesión en el servidor donde corren los servicios de impresión?

---

## 3. Hoja de ruta

Ordenada por riesgo. Cada fase puede ir en su propio commit y su propio despliegue. Las filas de B10 y
`pdf_url` caen sobre código sin commit de la otra sesión: hay que coordinarlas antes de tocarlas.

### R0 · Antes del siguiente despliegue ✅ hecho el 27/10/2026, sin commit ni despliegue

| Orden | Hallazgos | Cómo quedó |
|---|---|---|
| 1 | A1 «Reimprimir» del PHP | Quitado de `cola.blade.php` (la fila y la que pinta el JS). En «Sin confirmar» y «Bloqueado» solo queda Cancelar |
| 2 | A2 simulación sin IP | `PrintExecutionService.EnviarAsync`: con `NoOpPrintSpooler` no se prueba IPP (`En_simulacion_tampoco_sale_por_IP`) |
| 3 | A3 impresora con solo cola local | La protección de conectividad ignora `HOST_NOT_CONFIGURED` (`Una_impresora_con_solo_cola_local_imprime`, con el contraste de un fallo real) |
| 4 | M7 motivo IPP largo | Recortado a 60 caracteres en `QueryPrinterStateAsync` |
| 5 | M4 «Se reintenta solo» | «Se reintenta solo unas veces; si acaba en error, reintenta.» |

`dotnet test` 306/306 y `php artisan test` 29/29.

### R1 · Duplicados en el circuito de impresión
La regla del negocio es «parar antes que duplicar», y aquí es donde se rompe en la práctica.

| Orden | Hallazgos | Decisión | Nota |
|---|---|---|---|
| 1 | M1 parada a mitad de envío | — | ✅ 27/10/2026: tras reclamar el trabajo ya no se usa el token de parada, y `HostOptions.ShutdownTimeout` = plazo + 30 s. Parar espera al trabajo en curso; el lote no empieza otro (`Parar_el_servicio_a_mitad_no_pierde_lo_que_la_impresora_acepto`, que falla sin el cambio) |
| 2 | M3 trabajo parado en la impresora | D3 ✅ | ✅ 27/10/2026. **Aviso:** `PrinterBlocked` cuenta para la salud de la tienda (y Telegram) desde el primer intento (`DashboardPrintJobPredicates`, `contrato-kpi-dashboard.md`). **Tope:** 10 min parada (30 min si solo está ocupada); entonces `PrintedUnknown IPP_PRINTER_STOPPED_TIMEOUT` con «puede salir aún». El Cancel-Job automático que se desplegó a las 10:48 se quitó ese mismo día (D3). `IPP_CANCELED_BY_SERVICE` sigue en `ErroresTrabajo` por los trabajos que pudo cancelar entretanto. **Cancelar a mano** (`POST /api/printjobs/{id}/cancel`) cancela también en la impresora lo que salió por IP, después del UPDATE y sin transacción. Antes pide el trabajo (Get-Job-Attributes) y compara `job-name` con el que se mandó (`IIppConfirmationService.JobNameFor(tipo, id externo)`, recortado a 100, el mismo del Print-Job). Resultados: `Cancelado`, `YaSalio` (job-state 9), `YaNoSaldra` (7-8), `NoLoTiene`, `NoEsNuestro`, `SinNombre` (no devuelve `job-name`: no se toca), `SinRespuesta` y `NoDeja`; sin `ipp_job_id` (cola de Windows o IPP sin número), `SinNumero` con el aviso de cancelarlo allí. El texto queda en el evento `CANCELLED_BY_USER`, en la respuesta (`printerCancel`, `printerMessage`) y en la cola como aviso que no se cierra solo. **Reimprimir un `PrintedUnknown`** cancela igual la copia anterior si sigue en la cola de la impresora, solo cuando la nueva ya va a una impresora (si el reenvío acaba en `ErrorFinal`, no se toca: el documento no se queda sin salir); si no se puede cancelar, aviso en la respuesta y evento `PREVIOUS_COPY_IN_PRINTER` con quién reimprimió (`Reprint_SinConfirmar_CancelaLaCopiaAnteriorSoloSiLaNuevaTieneImpresora`). Get-Job-Attributes y Cancel-Job van con `requesting-user-name` (resuelve L13). El watchdog sigue sin pruebas propias (T1) |
| 3 | M2 abortado por documento | D2 ✅ | ✅ 27/10/2026: si el trabajo tiene un evento `IPP_DOCUMENT_ERROR`, el siguiente envío va como imagen; la pista lo dice (`Lo_que_la_impresora_aborto_por_el_documento_se_reintenta_como_imagen`) |
| 4 | M5 errores tardíos | D4 ✅ | ✅ 27/10/2026: `Rechazo` según la tabla de 2.2. Código nuevo `IPP_ERROR_WHILE_PRINTING` → `PrintedUnknown`; 0x0508 → `IPP_JOB_CANCELED` en `ErrorFinal`; ninguno de los dos pasa por la cola (`Un_rechazo_dice_por_que_y_si_pudo_salir_papel`, `Si_pudo_salir_papel_no_se_reenvia_por_la_cola`) |
| 5 | M6 plazo del Print-Job | D5 ✅ | ✅ 27/10/2026: `TimeoutSeconds` 120 (opción y `appsettings`). En el servidor, revisar el `appsettings.json` desplegado al desplegar |
| 6 | L15 abortado reintentado como `RETRY_ROUTE`; L14 paso a la cola silencioso | — | |

### R2 · Seguridad

✅ Hecho el 27/10/2026, sin commit ni despliegue.

| Orden | Hallazgos | Cómo quedó |
|---|---|---|
| 1 | M10 parser antes de la sesión (ERP) | `configurar.ts`: en `/api/impresion/sourceprintjobs/test` se resuelve la sesión antes del parser de 28 MB; sin ella, 401 sin leer el cuerpo (`impresion-sin-bd.e2e-spec.ts`: con JSON roto da 401, antes 400) |
| 2 | M11 pase con acceso a `/api/users` | Política `AdminAccountOnly` (Admin con `NameIdentifier`, es decir, usuario propio) en `UsersController`: con el pase, 403 (`ConPaseDeAdministradorNoSeGestionanUsuarios`). El cortafuegos del 5105 espera a decidir el alojamiento (D6) |
| 3 | M12 secretos en el registro | Riesgo aceptado (D7): no se cambia |
| 4 | L19 | **Reloj:** `ClockSkew` de 30 s, y el log del ERP ante un 401 avisa también de la hora. **Traza:** la pasarela manda `x-correlation-id`, que es lo que lee la API .NET. **Pasarela:** `redirect: "error"` (el pase no se reenvía a otra dirección) y el cuerpo se lee dentro del try (cortado a medias: 502, no 500). **Instalador:** SumatraPDF solo con la firma de su autor (`CN=Krzysztof Kowalczyk`), y `-RotateErpSecret` para cambiar `Erp__Secret`. **`VeTodasLasTiendas` habilita acciones:** es así por diseño (`ForbidOutsideStoreAsync`: «quien ve todas, sí»); además hace falta la clave de la acción. No se cambia |

Pruebas: `dotnet test` 319/319; pasarela del ERP 15/15. De la batería completa del ERP fallan 4 pruebas que necesitan la BD real, inalcanzable desde este equipo el 27/10 (EHOSTUNREACH); no tienen que ver con estos cambios.

### R3 · Datos y rendimiento

| Orden | Hallazgos | Cómo quedó (27/10/2026, sin commit ni despliegue) |
|---|---|---|
| 1 | M8 tope de la descarga de `pdf_url` | ✅ Un plazo (el `Timeout` del cliente) cubre cabeceras y cuerpo (`Un_cuerpo_que_se_queda_parado_se_corta_y_se_reintenta`) |
| 2 | M9 índice de reimpresiones | ✅ `migraciones/2026-10-27-indice-reimpresiones.sql` (índice parcial `WHERE event_type = 'REPRINT'`), también en `crear-esquema.sql` y en el modelo EF. **La aplica el usuario** |
| 3 | L2-L8 (reimpresiones, B10) | ✅ 27/10/2026 (el usuario pidió hacerlos aquí; nadie más los llevaba). **L2:** `SentAtUtc` solo cuenta el paso de `Printing` a `SpoolAccepted`; el «impresora recuperada» (desde `PrinterBlocked`) ya no mueve la hora. **L3:** el UPDATE a Pending y su evento van en una transacción. **L4:** reintentar un `ErrorFinal` sin PDF se rechaza con «el ERP tiene que volver a mandar el documento». **L5:** `AttemptCount=0` al reimprimir **no se cambia** (sin él, un trabajo que salió al 4.º intento daría `RETRIES_EXHAUSTED` nada más reimprimirse; es la limitación conocida del KPI, A-KPI-06); las reimpresiones de antes del 26/10, con la migración **opcional** `2026-10-27-reimpresiones-antiguas.sql`. **L6:** la retención repite sus condiciones en el UPDATE y el reenvío exige PDF en el suyo: ninguno de los dos órdenes de la carrera deja un trabajo sin PDF. **L7:** reintentar y reimprimir leen el trabajo sin el PDF; un trabajo que no existe da 404, y los mensajes ya no llevan nombres de estado internos. **L8:** `GET reprints?window=` calcula el periodo como el dashboard (`BusinessTimeZoneClock.WindowStartUtc`, compartido) y, como su KPI, solo cuenta tiendas activas; la web lo usa (sin `desdeDe` del navegador) y avisa «(las 500 más recientes)» si llega al tope. Pruebas: `Un_error_sin_PDF_no_se_reintenta_y_lo_dice`, `Con_el_periodo_del_dashboard_el_registro_cuadra_con_su_KPI`, `GetQueue_SeparaReintento…` (con un «recuperada»), `Route_WhenJobNotExists_Returns404`. Sin prueba propia: L3 y L6 (son carreras) |
| 4 | L1 watchdog con estado condicionado | ✅ Cada cambio va con `ExecuteUpdate … WHERE status = <el leído>` y su evento, en una transacción abierta después de las consultas a las impresoras: una cancelación hecha mientras tanto ya no se pisa. Sin prueba propia (el Worker no tiene proyecto de pruebas, T1) |
| 5 | L16 `pdf_url` sin tope | ✅ Un fallo pasajero se reintenta durante 10 min desde que llegó la fila; después entra en `ErrorFinal PDF_DOWNLOAD_FAILED`, a la vista en la cola, con «Se dejó de reintentar tras 10 min» (`Un_PDF_que_no_se_puede_descargar_se_reintenta_solo_un_rato`) |
| 6 | L17 Worker sin BD al arrancar | ✅ Ya no se cae: avisa y sigue; los servicios se reconectan solos. Probado arrancando el Worker contra una base inalcanzable |
| 7 | L10 `EstadoBd` con timeouts | **No se cambia.** Un tiempo agotado no distingue «lenta» de «caída» (también es lo que da una red cortada). El efecto de equivocarse es un 503 y una línea de log, y se recupera en la siguiente consulta que responda |

`dotnet test` 322/322.

### R4 · Pantallas

| Orden | Hallazgos | Nota |
|---|---|---|
| 1 | M14 columna «Documento» | **Abierto.** La sesión `impresorasservicev1-d2` lo arregló el 27/10/2026 (ancho mínimo calculado con `anchoTabla()`), pero el usuario le pidió deshacer todos sus cambios de interfaz |
| 2 | M13 gráfica y selector de franja | **Abierto.** Igual que M14: arreglado y deshecho el 27/10/2026 a petición del usuario |
| 3 | L8 registro frente al KPI | Mismo filtro de tiendas que el KPI |
| 4 | L9 cambio de hora, L20 | Las copias de la web y del ERP, idénticas |

### R5 · Limpieza y pruebas
Se pueden repartir entre las fases anteriores cuando se toque cada fichero.

| Hallazgos | Nota |
|---|---|
| C1-C10 | Sin cambio de comportamiento |
| L11, L12, L13, L18 | L11 conviene antes si V3 dice que hay anotaciones |
| T1-T4 | T1 (watchdog) primero: R1 cambia sus reglas |

---

## 3b. Revisión de lo hecho el 27/10/2026

Revisión independiente (sin ver estas conclusiones) más la propia, de R0-R3 y de las reimpresiones.
No apareció nada grave. Lo que sí:

| # | Qué | Estado |
|---|---|---|
| 1 | Un trabajo en cola tras una impresora parada (job-state 3-5 con `printer-stopped`) se tomaba por «procesando»: sin aviso al momento y con tope de 30 min, no de 10 | ✅ `EstadoDelTrabajo` lo da como parada (prueba en `Los_motivos_del_trabajo_afinan_el_resultado`) |
| 2 | El cálculo de respaldo del dashboard PHP seguía contando PrinterBlocked solo con reintentos | ✅ Igual que la API (`DashboardController.php`) |
| 3 | Con `comoImagen`, si la imagen fallaba se volvía a mandar la imagen y nunca el PDF | ✅ El segundo intento como imagen solo si el primero fue PDF |
| 4 | Los plazos suponen un solo envío; encadenando PDF, imagen y cola puede pasar de `ShutdownTimeout` | Comentarios corregidos: acaba en `PRINTING_INTERRUPTED`, sin duplicar. Un plazo único para todo el envío si molesta |
| 5 | El registro de reimpresiones ocultaba siempre las tiendas dadas de baja | ✅ Solo con `window` (lo que abre el KPI); con fechas sueltas, todas |
| 6 | Pistas que no siempre eran ciertas (`IPP_DOCUMENT_ERROR`, `IPP_REJECTED`) | ✅ Textos ajustados |
| 7 | HTTP 4xx sin respuesta IPP (405, 415…) acababa en «Sin confirmar» | ✅ Rechazo previo: no salió nada (`IPP_REJECTED`) |
| 8 | Cancel-Job (y la confirmación por trabajo de B9.2) se fían solo del número de trabajo: si la impresora se reinicia y reutiliza números, podría cancelar o confirmar otro documento | Cancelar, cubierto: no hay Cancel-Job automático y el manual comprueba antes `job-name` (D3). **Pendiente** la confirmación (`QueryJobStateAsync`): la misma comprobación con `JobNameFor`. Añadido a V10 |
| 9 | ERP: si falla al resolver la sesión antes del parser, la respuesta no salía en el formato de Nest | ✅ Se deja al guard. La sesión se resuelve dos veces en esa ruta: aceptado (solo la prueba con PDF) |
| 10 | Comentarios desfasados (`api.ts`) | ✅. `CLAUDE.md` (flujo, paso 4: «Stopped: ErrorFinal») también está desfasado: **pendiente**, a decisión del usuario |
| 11 | `RoutingService.TryRouteJobAsync` sin llamadores | ✅ Quitado (interfaz, implementación y doble de pruebas) |
| 12 | La lógica nueva del watchdog (`WHERE status`, tope de 10 min) sigue sin pruebas | **Pendiente** (T1: el Worker no tiene proyecto de pruebas) |

`dotnet test` 326/326, `php artisan test` 29/29, pasarela del ERP 15/15, web 39/39; tipos de la web en los dos repos, bien.

---

## 4. Verificación manual

| ID | Qué comprobar | Para qué | Quién |
|---|---|---|---|
| V1 | PDF directo en la HP M401dne o en la Canon de la tienda 25 (B9.1) | Cerrar B9.1 | Usuario, en tienda |
| V2 | Un documento de 15-20 páginas en la Brother, por PDF y como imagen: tiempo de subida y códigos de error | D4 y D5 | Usuario |
| V3 | Si los PDF del ERP llevan anotaciones o campos de formulario, y si hay albaranes apaisados o que no son A4 | L11 y L12 | Usuario o equipo del ERP |
| V4 | Si en producción `IMPRESION_API_URL` es localhost o pasa por la red | D6 | Infraestructura |
| V5 | Si el ERP es accesible desde internet | Prioridad de M10 | Infraestructura |
| V6 | Permisos del registro de los servicios en el servidor | D7 | Infraestructura |
| V7 | `GET /api/printjobs` contra PostgreSQL real (tres subconsultas por fila) | Rendimiento de la cola | Desarrollo |
| V8 | Longitud de los motivos IPP reales de cada modelo del parque | Dimensionar M7 | Desarrollo |
| V9 | Que `cod_alm` del ERP sea siempre `store_id` | Pase y filtro por tienda | Usuario |
| V10 | En la Brother (y las demás): que Get-Job-Attributes devuelva `job-name` (nameWithoutLanguage; si no, la cancelación manual sale `SinNombre` y no cancela), que deje hacer Cancel-Job a `requesting-user-name` «ImpresorasService» y si reutilizan los números de trabajo al reiniciarse (3b.8) | D3 | Desarrollo, con la Brother de Informática |
