# Casos de prueba recomendados

**Corte:** 2026-07-29. Ordenados por capacidad de evitar pérdida, fuga o duplicado antes que por facilidad.

## P0/P1: gates de corrección

| ID | Tipo | Escenario mínimo | Aserción indispensable | Hallazgo |
| --- | --- | --- | --- | --- |
| T-001 | Secret scanning | Escanear árbol, commits y PR con patrón/entropía | Ningún token o secreto real; el fingerprint expuesto desaparece y el token anterior está revocado | AUD-01 |
| T-002 | API integración | Único Admin se edita a Employee/StoreManager | 409/422; permanece Admin | AUD-03 |
| T-003 | Concurrencia DB | Dos conexiones intentan demover/borrar dos admins simultáneamente | Al menos un Admin permanece tras commit | AUD-03 |
| T-004 | Autorización | JWT Employee sin `StoreId` consulta jobs/printers/dashboard/stores | 403, nunca datos globales | AUD-04 |
| T-005 | Autorización | Employee tienda A intenta leer/actuar sobre recurso tienda B | 403/404 homogéneo en todos los endpoints | AUD-04 |
| T-006 | Rate limit | 10 intentos desde IP A y uno válido desde IP B | B no queda bloqueada; A recibe 429 | AUD-05 |
| T-007 | Contrato Store 0 | Crear tienda 0, usuario, impresora, regla, escenario de prueba y abrir dashboard | Todos aceptan y muestran 0, o todos rechazan la creación inicial según decisión | AUD-07 |
| T-008 | Regla | Crear regla de tienda A con impresora de B | API/constraint rechaza; nunca se persiste | AUD-08 |
| T-009 | Ingesta | Repositorio lanza `DbUpdateException` no correspondiente al índice único | No se llama a `MarkJobsProcessedAsync`; lote reintentable | AUD-09 |
| T-010 | Ingesta HANA | Dos workers/conexiones reclaman el mismo origen | Solo uno obtiene/ACK el `ClaimToken`; sin doble `PrintJob` | AUD-09 |
| T-011 | Crash injection | Terminar Worker justo después de que el spooler acepte y antes del segundo commit | No hay reenvío automático ciego; queda estado ambiguo/revisión | AUD-10 |
| T-012 | Cancelación | Cancelar token durante `SendToPrinterAsync` tras aceptación simulada | Se persiste resultado ambiguo con token de compensación, no se deja `Printing` reenviable | AUD-10 |
| T-013 | Concurrencia HANA | API cancela mientras watchdog/worker actualiza el mismo job | UPDATE condicional afecta 0 filas; `Cancelled` no se sobrescribe | AUD-12 |
| T-014 | IPP | La impresora está `idle`, pero el job consultado no existe/fue descartado | No se marca `PrintedConfirmed` | AUD-11 |
| T-015 | Retención | Transición exitosa a estado definido por política de retención | `PrintJob.PdfBlob` y copia origen se limpian/expiran; hash/eventos permanecen | AUD-13 |
| T-016 | Alertas | Telegram devuelve 500/timeout para todos los chats | Estado no queda confirmado; existe reintento/outbox; no se registra “enviada” | AUD-14 |
| T-017 | Upgrade | Actualizar Composer a versiones corregidas | `composer audit --locked` sin advisories aplicables y 12/12 tests | AUD-02 |

## P2: funcionalidad, resiliencia y datos

| ID | Tipo | Escenario | Aserción |
| --- | --- | --- | --- |
| T-018 | Sesión | Fijar ID antes de login y cerrar sesión | Login regenera ID; logout invalida sesión, regenera CSRF y elimina tienda seleccionada |
| T-019 | JWT | Desactivar/cambiar rol de un usuario con token emitido | Token queda rechazado según política de revocación/versionado |
| T-020 | Borrado | Purgar tienda con 100k jobs/eventos | Operación por lotes/SQL set-based, memoria acotada y sin límite de parámetros |
| T-021 | Integridad | Borrar y recrear mismo StoreId | No hereda chats, estado de alertas ni historial sin decisión explícita |
| T-022 | Dashboard degradado | Overview falla y listados también fallan | UI muestra “datos no disponibles”, no ceros saludables |
| T-023 | Dashboard truncado | Más de 500 jobs en ventana | Marca resultado parcial de forma visible o usa agregación no truncada |
| T-024 | Config concurrente | 100 lecturas mientras se guardan umbrales | Nunca JSON parcial; todas observan versión vieja o nueva válida |
| T-025 | Lease | `HeartbeatSeconds >= LeaseSeconds`, pérdida a mitad de lote | Startup rechaza config; un servicio deja de producir efectos al perder titularidad |
| T-026 | Temp files | Matar proceso durante impresión | Limpieza de arranque retira solo temporales propios caducados, con ACL restrictiva |
| T-027 | Bulk API | Body enorme, GUID inválido y duplicados | 400/413/422 temprano, límite y deduplicación documentados |
| T-028 | Paginación | `page=int.MaxValue`, búsqueda con caracteres especiales | Respuesta válida y acotada, sin overflow/500 |
| T-029 | Fechas HANA | UTC, DST, límites de ventana y precisión de timestamp | Misma semántica que contrato KPI y serialización documentada |
| T-030 | DDL | Aplicar scripts en clon de staging y repetir | Idempotencia definida o fallo seguro; constraints/índices esperados presentes |

## Rendimiento

| ID | Carga | Medición | Objetivo inicial propuesto |
| --- | --- | --- | --- |
| T-P01 | 100 trabajos, 10k reglas, 2k impresoras | Consultas de `RoutingResolver` y tiempo/lote | O(1) consultas por lote o consulta indexada por job; p95 < 1 s/lote sin spool |
| T-P02 | 1M jobs/10M eventos, ventana 30 días | Dashboard p50/p95, filas transferidas, memoria | p95 < 2 s; memoria < 150 MB; agregación en DB |
| T-P03 | 500 tiendas, 5k impresoras | Ciclo de alertas y número de consultas | Consultas O(1) por categoría, ciclo < intervalo/2 |
| T-P04 | 100k jobs de una tienda | Purga, locks, log, memoria | Por lotes; memoria estable; progreso/reanudación |
| T-P05 | Búsqueda `externalJobId` | Plan HANA con `%texto%` | Presupuesto explícito; evitar full scan o usar estrategia de búsqueda |
| T-P06 | Vite + navegación móvil | bundle, LCP, INP, CSS usado | Baseline real y regresión; no fijar objetivo sin dispositivo/red acordados |

## Seguridad

| ID | Prueba no destructiva | Resultado esperado |
| --- | --- | --- |
| T-S01 | Matriz rol × endpoint × tienda | Solo acciones documentadas; 401/403 coherentes |
| T-S02 | Cabeceras detrás del proxy real | HSTS solo HTTPS; CSP sin `unsafe-inline` tras migración; frame/referrer/permissions correctas |
| T-S03 | Inputs en `externalJobId`, nombres de tienda y mensajes Telegram | Codificación contextual; sin HTML/CRLF ejecutable |
| T-S04 | PDF vacío, no PDF, 20 MB, > límite y PDF malicioso | Tipo/tamaño validados; no ejecución; timeout y memoria acotados |
| T-S05 | CORS con origen permitido/no permitido y sin lista en producción | Solo allowlist; producción falla al arrancar sin configuración |
| T-S06 | Endpoint bootstrap en Production/Development con usuarios/no usuarios | Solo Development + flag + cero usuarios; auditado y desactivable |
| T-S07 | `/diagnostics/hana` provoca fallo controlado | No devuelve cadena de conexión, host, usuario ni detalle interno |
| T-S08 | DAST pasivo sobre staging | Sin rutas administrativas anónimas, cookies inseguras ni debug |

## UI, accesibilidad y compatibilidad

1. Corregir o actualizar `TiendasControllerTest::test_index_shows_actions_for_store_id_zero` según el contrato aprobado; mantener aserciones de nombre accesible y confirmación.
2. E2E Playwright: login, cambio de tienda, cola, acciones masivas, CRUD y logout; Chrome/Firefox, 360 px y zoom 200 %.
3. `axe-core` o equivalente sobre login, dashboard, cola, tiendas, impresoras, reglas, usuarios, pruebas y alertas.
4. Teclado: orden de foco, diálogo de confirmación, Escape, retorno de foco y feedback `aria-live`.
5. Simular API lenta, 401, 403, 409, 422, 429, 500 y caída total; cada pantalla debe distinguir vacío de error.
6. Doble clic y reenvío tras back/refresh en operaciones mutantes; debe existir idempotencia o bloqueo visible.

## Calidad de las pruebas existentes

- Conservar los 142 casos .NET que pasaron, pero ejecutar el gate HANA de staging para SQL, BLOB, `row_version`, claim y fechas.
- Los 12 tests PHP son pocos para 53 rutas; el fallo actual impide que el CI raíz esté verde.
- No usar solo SQLite para afirmar concurrencia HANA: su proveedor, tipos y semántica de filas afectadas difieren.
- Los dobles de spooler deben registrar llamadas, aceptar puntos de fallo deterministas y modelar “aceptado pero respuesta perdida”.
- Toda corrección alta necesita una prueba que falle antes del cambio y una regresión end-to-end del flujo afectado.

