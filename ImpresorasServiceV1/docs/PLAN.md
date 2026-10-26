# Plan de trabajo — ImpresorasService

**Fuente de verdad única del trabajo pendiente.**

**Estado a 2026-10-26**, rama `feat/web-erp` (desplegada en los servicios de Windows). `dotnet test` 189/189.

El 23/10/2026 la cola pasó de SAP HANA a PostgreSQL y el origen de los trabajos pasó de SAP al ERP
propio. La documentación y los scripts de la etapa HANA se retiraron del repositorio; siguen en el
historial de git (hasta el commit `602018f`).

---

## 1. Documentos vivos

| Documento | Para qué |
|---|---|
| **`PLAN.md`** (este) | Qué hacer y en qué orden |
| `contrato-kpi-dashboard.md` | Contrato de los KPI del dashboard |
| `gate-i1-ipp-por-trabajo.md` | Medición del parque: por qué no se puede confirmar la impresión por trabajo |
| `ejecutable.md`, `DESPLIEGUE-PHP.md`, `DESPLIEGUE-WEB.md`, `SMOKE-TESTS-PHP.md`, `GUIA-ESTILO-DBX-UI.md`, `FRONTEND-WEB-PHP-GIT.md` | Guías operativas |
| `RESUMEN-PROYECTO.md` | Orientación rápida en el código |

---

## 2. El trabajo, por bloques

### B0 · Puesta en marcha sobre PostgreSQL

Hecho el 23/10/2026: esquema creado (`scripts/crear-esquema.ps1`), 29 tiendas (almacenes activos de
`gesserca.fdalmacen`, `store_id` = `cod_alm`), 59 usuarios y servicios de Windows reinstalados.

| # | Acción | Estado |
|---|---|---|
| B0.1 | **El ERP escribe en `printer_source_print_job`.** Mínimo: `external_job_id`, `store_id`, `document_type` y `pdf_url` (o `pdf_blob`); contrato en el comentario de la tabla en `crear-esquema.sql`. Un `external_job_id` repetido se descarta como duplicado: cada reimpresión necesita uno nuevo. **Documento (26/10/2026):** `document_type` con el código del ERP antiguo (A albarán y C contado, los de `fdalba_c.tipo`; P presupuesto) y `external_job_id` = número del documento, con `-n` en cada reimpresión (123456, 123456-2); la cola lo usa en «Documento» y el enlace amarillo se activa cuando el ERP tenga pantalla de documento. **PDF por URL (usuario, 26/10/2026), hecho:** el ERP deja la URL de `facturas.repuestos-serca.es/generate.php?id=…` (`inline=0` descarga, `inline=1` muestra: al servicio le da igual) y la ingesta (`ErpJobSourceAdapter`) la descarga una vez: solo https, hosts de `Source:PdfUrlHosts`, sin redirecciones, 30 s, 50 MB, tiene que empezar por `%PDF`. Red, timeout, 408/429 y 5xx: sin ack, se reintenta al vencer el lease. El resto: `ErrorFinal` con `PDF_DOWNLOAD_FAILED`. El `id` de la URL basta para bajarse la factura: no sale en logs ni en errores. Probado contra el servidor real. Falta aplicar `migraciones/2026-10-26-pdf-url.sql` en la BD | ⏳ Sin esto no entra ningún trabajo |
| B0.2 | Impresoras, reglas de enrutado y Telegram. Las impresoras se dan de alta como impresoras de Windows **en el servidor del Worker** (su nombre es `spool_queue`); los PC de las tiendas no instalan nada | ⏳ Los configura el usuario. Telegram: `install-windows-services.ps1 -SkipPublish -ConfigureTelegram`. SumatraPDF 3.6.1 portable (firma comprobada) en `C:\ImpresorasService\SumatraPDF` desde el 26/10/2026 (`-SumatraExe`); sin él cada trabajo acaba en `SPOOLER_DOWN` |
| B0.3 | Esquema definitivo. Hoy **`admon`, provisional**: `pb8_xp57` no puede crear esquemas. Para mover: el DBA crea el destino (`CREATE SCHEMA impresoras AUTHORIZATION pb8_xp57`), `ALTER TABLE admon.<tabla> SET SCHEMA impresoras;` en las 12 `printer_*` (arrastra índices y secuencias) y cambiar `Search Path` en la cadena (`.env` y servicios) | ⏳ Decisión |
| B0.4 | Usuarios con contraseña = login (decisión temporal). Varias no cumplen el mínimo de 6 de la Api: se insertaron por SQL con el mismo BCrypt. Al embeber la app en el ERP, el login automático debería usar un secreto del ERP, no estas contraseñas | ⏳ Temporal |
| B0.5 | Permisos: las `printer_*` solo las lee `pb8_xp57` (dueño) y los superusuarios; `admon` no tiene permisos por defecto. Si el ERP conecta con otro usuario: `GRANT SELECT, INSERT ON admon.printer_source_print_job TO <usuario>` | ⏳ Según el usuario del ERP |
| B0.6 | **Servidor del frontend con PHP 8.5.12 o superior** (corrige PHP2Shell, CVE-2026-109618/109619, explotado desde el 21/10/2026). El proyecto exige `^8.5` desde el 23/10/2026; la 8.2 deja de recibir parches el 31/12/2026 | ⏳ Al montar el servidor. En este equipo, PHP 8.5.12 y Laravel 12.70.1 (el último de la 12), sin avisos de `composer audit` (26/10/2026). Laravel 12 solo recibe parches de seguridad hasta febrero de 2027: si el PHP sigue después, subir a la 13 |

### ~~B6 · Simplificar lo heredado de HANA~~ ✅ COMPLETO 2026-10-23

Rodeos que solo existían por limitaciones del proveedor de HANA. Ya no queda ninguno: la marca
`herencia-hana:` ha desaparecido del código.

| # | Qué | Dónde | Nota |
|---|---|---|---|
| ~~B6.1~~ | ~~Fechas como texto → `timestamptz`~~ | `ImpresorasDbContext`, `StoresController`, `crear-esquema.sql` | ✅ 23/10/2026. Migración `scripts/sql/migraciones/2026-10-23-fechas-timestamptz.sql` aplicada en la BD real y desplegada: 21 columnas, 0 diferencias en los datos, servicios y frontend verificados. Solo tocó las `printer_*` |
| ~~B6.2~~ | ~~`AnyAsync` en vez de `Take(1).CountAsync() > 0`~~ | 12 comprobaciones de existencia | ✅ 23/10/2026. Verificado contra PostgreSQL |
| ~~B6.3~~ | ~~Dashboard: `Any()` correlacionado y `NOT IN` directo~~ | `DashboardController` | ✅ 23/10/2026. Prueba A/B contra PostgreSQL: 8 de 8 respuestas del dashboard idénticas a la versión anterior. El agrupado por tramos se queda en memoria a propósito (marca `ponytail:` con su techo): en SQL necesitaría código solo para PostgreSQL y sin tests |
| ~~B6.4~~ | ~~`UPDATE` de impresora por EF en vez de SQL crudo~~ | `PrintersController.Update` | ✅ 23/10/2026. Test nuevo: el cambio de host anula `ipp_supported` y la telemetría del Worker no se pisa. Verificado contra PostgreSQL |
| ~~B6.5~~ | ~~Booleanos: variables `activeOnly`/`pendingOnly`~~ | 10 ficheros, 13 condiciones | ✅ 23/10/2026. Condiciones directas (`x.IsActive`, `!x.IsProcessed`) |
| ~~B6.6~~ | ~~`row_version`: columna que no participaba en la concurrencia~~ | `PrintJob`, `ImpresorasDbContext`, `crear-esquema.sql` | ✅ 23/10/2026. Columna borrada (`scripts/sql/migraciones/2026-10-23-quitar-row-version.sql`) |

### B3 · Confirmación de impresión

| # | Trabajo | Estado |
|---|---|---|
| B3.1 | Confirmación IPP por trabajo | ❌ Descartado: el `job-id` lo asigna la impresora y nunca llega a nuestro proceso (enviamos por SumatraPDF → spooler de Windows). Ver `gate-i1-ipp-por-trabajo.md` |
| B3.2 | Renombrar `PrintedConfirmed` y sus etiquetas para que no afirmen lo que no comprueban | ⏳ Decisión de producto: toca UI, KPI e histórico |

Hoy una lectura de `printer-state` marca `PrintedConfirmed` a todo el lote de esa impresora, PHP lo
etiqueta «Impreso» y el KPI `printed` incluye además `SpoolAccepted` y `PrintedUnknown`.

### B5 · Higiene y deuda reconocida

| Qué | Estado |
|---|---|
| Alertas de Telegram sin outbox | Una alerta que no acepta ningún chat se reintenta en el siguiente ciclo, pero no hay estado por destinatario ni backoff |
| `StoreHealthAlertBackgroundService` sin tests | `RunOnceAsync` es privado; hacerlo testeable es un refactor |
| Lanzamiento real del proceso de impresión e IPP sin tests | Necesitan spooler o impresora reales. `WindowsPrintSpooler.ClassifyFailedExit` sí está cubierto |
| Reclamar entre la lectura y el UPDATE (409 de `cancel`) sin test | Exige interponerse en el comando SQL |
| Ruido en el registro de eventos | Cada `dotnet test` escribe avisos y errores en el Event Log de Windows: el entorno `Testing` no debería registrar ese proveedor |
| `GetHealth_ReturnsDatabaseCheck` intermitente | A veces da `degraded`: los checks `database` y `worker` corren en paralelo sobre la única conexión SQLite en memoria de las pruebas. Visto el 26/10/2026 (1 de 4 ejecuciones) |

### B7 · Frontend React con las piezas del ERP

Decidido el 23/10/2026. Sustituye al PHP por fases. El módulo se escribe como uno del ERP nuevo,
para poder abrirlo en un iframe o copiarlo dentro del ERP el día que se decida
(`src/ImpresorasService.Web/README.md`).

| # | Fase | Estado |
|---|---|---|
| B7.0 | Base: app, piezas del ERP copiadas (commit `064b912`), contenedor con pestañas, `api()` | ✅ 23/10/2026, rama `feat/web-erp` |
| B7.1 | Sesión: la API .NET deja el token en una cookie `httpOnly` (como el ERP); se entra con los usuarios actuales | ✅ 23/10/2026: `login` abre la sesión, `me` y `logout` nuevos, sin límite de intentos en esos dos |
| B7.2 | Cola e impresoras; las tiendas pasan a la app nueva y el PHP queda para administración | 🟡 Pantallas hechas el 23/10/2026 y despliegue preparado (`DESPLIEGUE-WEB.md`, `nginx/impresion-web.conf`, sin probar contra un nginx). La API que necesita está en los servicios de Windows desde el 26/10/2026 (compatible con el PHP). Falta desplegar la web en el servidor |
| B7.3 | Dashboard y administración; se retira el PHP (y con él B0.6) | 🟡 Pantallas hechas el 23/10/2026: dashboard, alta/edición/baja de impresoras, reglas, umbrales, Telegram y pruebas, todas en el grupo «Impresión» del menú (las cuatro últimas, juntas en «Reglas y avisos» desde el 26/10/2026); Tiendas y Usuarios, aparte y solo para la transición (`src/app/transicion`). Falta retirar el PHP (B7.3a), después de desplegar B7.2 |
| B7.4 | Paso al ERP: integración, con un pase de entrada firmado por el ERP | 🟡 En curso desde el 26/10/2026, rama `cayetanoImpresion` de ERP_SERCA (`4a22fc9`; aquí `d0ab406`): el módulo web copiado tal cual en su grupo «Impresión»; permisos por departamento (tabla de abajo); pasarela `/api/impresion` en su API, que firma un pase (JWT HS256, 2 min) con operador, departamento, almacén y claves. Lado .NET: `Security/PaseErp.cs` lo acepta si hay `Erp:Secret` (la clave va por emisor; los tokens propios siguen igual) y lo traduce a rol y tienda; `VeTodasLasTiendas` separa ver todas de ser administrador. Probado de punta a punta contra esta API. El módulo se cambia aquí y se vuelve a copiar al ERP. Desplegado el 26/10/2026: la API acepta pases (`Erp__Secret` lo genera `install-windows-services.ps1`) y el ERP de desarrollo de este equipo apunta a ella (`IMPRESION_API_URL`/`IMPRESION_SECRETO` en su `.env`). Falta: las mismas dos variables en el servidor del ERP; confirmación al imprimir fuera del almacén propio; y, al apagar el PHP y la web independiente, quitar `printer_user`, `POST /api/auth/login` y las pantallas Tiendas y Usuarios |

**B7.3a · Retirar el PHP.** Los dos sistemas conviven (decisión del usuario): el PHP y el ERP usan
la misma API, la misma base y el mismo Worker, a la vez o indistintamente; lo que se hace en uno se ve
en el otro. El PHP se retira solo cuando el usuario lo decida, con el ERP comprobado en producción y
las tiendas avisadas: borrarlo de `main` impediría volver a desplegarlo.

1. Borrar `src/ImpresorasService.Web.PHP/` (134 ficheros versionados).
2. Borrar `DESPLIEGUE-PHP.md`, `SMOKE-TESTS-PHP.md`, `FRONTEND-WEB-PHP-GIT.md`, `GUIA-ESTILO-DBX-UI.md` y
   `nginx/impresoras-service.conf`; quitar las menciones de `README.md`, `RESUMEN-PROYECTO.md`,
   `ejecutable.md` (`-FrontendOrigin`) y `CLAUDE.md`; quitar B0.6 y las entradas de Laravel del `.gitignore`.
3. API: quitar `POST /api/auth/token` (solo lo usa el PHP) y su test, y los orígenes `:8000` de
   `Cors:AllowedOrigins`. La web nueva va en el mismo origen y no necesita CORS, pero la API exige la
   lista fuera de Development: decidir si se deja vacía con esa comprobación quitada.
4. Servidor: quitar php-fpm y el `server` de nginx del PHP.

Diferencias con el PHP que quedan a propósito: Pruebas guarda los escenarios en el navegador y pide
el PDF en cada prueba (el PHP los guardaba en su disco); la cola ya no deja ocultar columnas.

Acceso en el ERP: sin login propio (todo sale de la sesión del ERP) y por departamento
(`fdopera.cod_dep`), no por el catálogo `PANTALLAS` ni por programas del ERP antiguo. Acordado
con el usuario el 26/10/2026; entre paréntesis, operadores activos ese día. Se hace en la rama
`cayetanoImpresion` de ERP_SERCA.

| Acceso | Departamentos | Tiendas |
|---|---|---|
| Administrador | 200 INFORMATICA (9) | Todas |
| Jefe de tienda | 10 RESPONSABLE TIENDA (25) | La suya |
| Usuario (solo ver) | 1 MOSTRADOR (54), 2 ALMACEN (86), 3 REPARTO (142), 5 NOTAS (9), 11 SUSTITUTO REPARTO (33), 116 COMODIN (6) | La suya |
| Usuario, todas las tiendas | 113 CALL-CENTER (47), 108 COORDINADOR CALL CENTER (0), 114 COMPRAS-URGENCIAS (6), 17 CARROCERIAS (15), 100 GERENCIA (4) | Todas |

- Cerrado por defecto: cualquier otro departamento, también los que se creen, no ve nada (el
  grupo «Impresión» no sale en su menú y la API responde 403).
- Rol (qué hace) y tiendas (qué ve) van por separado. «La suya» es `fdopera.cod_alm`; sin tienda
  válida no se ve ninguna, nunca todas.
- Varias tiendas por operador: se diseña junto con la decisión D1 del ERP. Imprimir fuera del
  almacén propio pide confirmación.
- **Para más adelante (no hacer aún):** reparto, sustituto de reparto y comodín trabajan a veces
  en otra tienda que la de su `cod_alm`. Para ellos, la tienda debería salir de la IP del
  dispositivo con el que entran al ERP. Va con el acceso por IP y tienda que ya prevé el ERP
  (`ERP_SERCA/docs/12_acceso_akamai_dispositivos.md`, y `tipo_acceso` en docs/15): detrás de
  Akamai el servidor solo ve la IP real si llega en una cabecera.

### B9 · Imprimir primero por IP

Decisión del usuario (26/10/2026): conviven los dos caminos y siempre se intenta primero por IP. Así
dar de alta una impresora es poner su IP, sin cola, driver ni Sumatra en el servidor, y la impresora
devuelve un número de trabajo con el que se puede confirmar de verdad. Consulta IPP del 26/10/2026:
la HP LaserJet 400 M401dne y la Canon imageFORCE 1643 aceptan PDF; la Brother MFC-L2800DW, solo
imagen (IPP Everywhere y AirPrint).

| # | Acción | Estado |
|---|---|---|
| B9.1 | Por IPP (Print-Job) si la impresora acepta PDF o imagen PWG; si no hay IP, no responde o rechaza el trabajo, por la cola de Windows. Un envío por IP cortado a medias no se repite por la cola (`PrintedUnknown`). La cola pasa a ser opcional si hay IP (API, web, ERP y PHP). Las consultas IPP prueban `/ipp/print` y `/ipp/printer` (la Canon solo responde en la primera) | ✅ Desplegado el 26/10/2026, con las migraciones `cola-opcional` e `ipp-job-id` aplicadas. Falta probar en real el camino del PDF directo (HP M401dne o Canon de la tienda 25) |
| B9.2 | Confirmación por trabajo: se guarda el número que da la impresora (`ipp_job_id`) y el watchdog pregunta por ese trabajo (Get-Job-Attributes): terminado → `PrintedConfirmed`, cancelado o abortado → `ErrorFinal` con el motivo, detenido → `PrinterBlocked`. Si la impresora ya no lo recuerda, por su estado, como antes | ✅ Desplegado y probado el 26/10/2026: un trabajo cancelado en la Brother quedó en `ErrorFinal` con el motivo y uno terminado, en `PrintedConfirmed` (a los 2 min). Resuelve B3.1 y B3.2 para lo que sale por IP |
| B9.3 | Impresoras sin PDF: el servicio dibuja el PDF con PDFium (Docnet.Core, única dependencia nueva) y lo manda como imagen PWG (IPP Everywhere): escala de grises si la admite, 300 ppp | ✅ Probado el 26/10/2026 en la Brother de Informática: 1 hoja, confirmada. La primera prueba sacó 18 hojas en blanco hasta cancelarla: la cabecera llevaba `NumCopies` a 0 y la Brother lo tomó como copias sin fin; ahora va a 1, como en CUPS (`PwgRasterTests` lo comprueba) |

### ~~B8 · Caídas de la base de datos~~ ✅ 26/10/2026

El 26/10/2026 la base dejó de responder desde las 07:53: la Api no arrancaba (leía los usuarios al
arrancar), respondía 500 sin motivo y el Worker registraba una traza en cada ciclo. Ahora:

- `EstadoBd` + `EstadoBdInterceptor` (Core): anotan el corte una vez con su motivo, lo recuerdan cada
  5 min y anotan la vuelta con lo que duró (registro de eventos). `FallosBd` distingue «la base no
  responde» de los errores de consulta. Los avisos de EF de cada fallo bajan a Debug.
- Api: arranca sin base; durante el corte responde 503 con el motivo y `Retry-After` (también con
  sesión: antes daba 401 y mandaba al login); vuelve sola en la siguiente petición.
- Worker: ya se reconectaba al renovar el lock; ahora sin traza por ciclo y sin culpar a los permisos.
- Servicios de Windows: reinicio automático si se caen (`install-windows-services.ps1`).
- Probado contra una base inalcanzable real y en `BdCaidaTests`/`EstadoBdTests`.

---

## 3. Decisiones abiertas

Se van viendo sobre la marcha (usuario, 26/10/2026): B3.2, B0.3, el servidor beta con MD5, la tienda
por IP y varias tiendas por operador (B7) y la deuda de B5.

| Decisión | Quién |
|---|---|
| Esquema definitivo (B0.3) | Usuario / DBA |
| El servidor es PostgreSQL **19beta4** (beta) y autentica con MD5 (obsoleto) | Infraestructura |
| Contraseñas = login (B0.4) | Usuario, cuando se embeba en el ERP |
| ¿Se revocó con BotFather el token de Telegram que quedó escrito en un documento? El documento ya no está, pero el token sigue en el historial de git | Operaciones |

---

## 4. Definición de hecho

- Un test que falla antes del cambio y pasa después.
- `dotnet test`, `php artisan test` y `npm run build` en verde según el componente tocado.
- Si toca SQL, concurrencia o tipos: verificado contra PostgreSQL, no solo contra SQLite.
- La fila de este plan tachada o actualizada.
