## 1. Preparación API .NET

- [x] 1.1 Añadir endpoint POST /api/auth/token que devuelva JWT (UserId, Login, Role, StoreId, exp 8h)
- [x] 1.2 Configurar autenticación Bearer (JWT) en la API además de cookies
- [x] 1.3 Configurar CORS para permitir origen del frontend PHP
- [x] 1.4 Mantener POST /api/auth/login compatible (o redirigir internamente a token)

## 2. Proyecto Laravel base

- [x] 2.1 Crear proyecto Laravel en ImpresorasServiceV1/src/ImpresorasService.Web.PHP/
- [x] 2.2 Configurar .env con API_URL apuntando a la API .NET
- [x] 2.3 Crear servicio ApiClient (Guzzle) que envíe Bearer token desde sesión
- [x] 2.4 Crear middleware de autenticación que redirija a /login si no hay sesión
- [x] 2.5 Crear controlador AuthController (login, logout)

## 3. Layout y assets Metronic

- [x] 3.1 Copiar layout base de templates/dist/ (header, aside, estructura HTML)
- [x] 3.2 Copiar assets/css/themes/layout/ (light.css, dark.css) a public/
- [x] 3.3 Copiar style.bundle.css y plugins necesarios (Metronic completo: fase posterior)
- [x] 3.4 Crear archivo CSS de overrides con paleta SERCA (#1a237e, #c62828, #78909c)
- [x] 3.5 Integrar logo assets/logo.png en public y sidebar (logo + texto "Impresoras Service")
- [x] 3.6 Implementar toggle tema claro/oscuro con localStorage
- [x] 3.7 Crear layout Blade principal que extienda la estructura Metronic

## 4. Login y sesión

- [x] 4.1 Crear vista login.blade.php con formulario POST
- [x] 4.2 Ruta POST /login que valide contra API, guarde token y user en sesión
- [x] 4.3 Redirección a / tras login exitoso
- [x] 4.4 Ruta GET /logout que limpie sesión y redirija a /login
- [x] 4.5 Manejo de 401 en ApiClient: limpiar sesión y redirigir a login

## 5. Pantallas core

- [x] 5.1 Dashboard: vista con KPIs por estado (llamada a API)
- [x] 5.2 Cola: tabla con filtros (tienda, estado, impresora), acciones reintentar/cancelar
- [x] 5.3 Impresoras: CRUD (listar, crear, editar, eliminar)
- [x] 5.4 Reglas: CRUD de reglas de enrutado
- [x] 5.5 Alertas: lista de trabajos en ErrorFinal
- [x] 5.6 Prueba: formulario que llama a /api/sourceprintjobs/test

## 6. Menú y autorización

- [x] 6.1 Menú lateral: Dashboard, Cola, Impresoras, Reglas, Prueba, Alertas
- [x] 6.2 Ocultar o restringir opciones según rol (Admin vs Supervisor)
- [x] 6.3 Filtro de tienda en header para Supervisor (o fijar StoreId automáticamente)
- [x] 6.4 Mostrar usuario y botón Logout en header

## 7. Despliegue y validación

- [x] 7.1 Configurar Nginx (o equivalente) para reverse proxy: / → PHP, /api → .NET
- [x] 7.2 Documentar variables de entorno (API_URL, etc.)
- [x] 7.3 Validar flujo completo: login, navegación, acciones
- [x] 7.4 Retirar proyecto Blazor (ImpresorasService.Web) tras validación

## 8. Ping a impresoras

- [x] 8.1 API .NET: Añadir endpoint POST /api/printers/{id}/ping que extraiga host de SpoolQueue (UNC \\host\share) y haga ping, devolviendo { reachable: bool, latencyMs?: int, error?: string }
- [x] 8.2 API .NET: Opcionalmente añadir campo Host en Printers si SpoolQueue no permite extraer host de forma fiable
- [x] 8.3 Laravel: Botón "Ping" manual en cada fila de la tabla Impresoras que llame al endpoint y muestre resultado (alerta o badge)
- [x] 8.4 Laravel: Ping automático cada X segundos (configurable, ej. 30s) para las impresoras visibles; mostrar indicador de estado (online/offline) en la tabla
- [x] 8.5 Configurar intervalo de ping automático en .env (PING_INTERVAL_SECONDS=30)

## 9. Corrección de estados y etiquetas

- [x] 9.1 Crear helper PHP o pasar labels desde controlador para evitar que Blade muestre código crudo (@php/@endphp) en Cola y Alertas
- [x] 9.2 Reemplazar @php inline en cola.blade.php por variable $statusLabels pasada desde ColaController
- [x] 9.3 Revisar todas las vistas que usen mapeo estado→texto y unificar en un único helper (ej. StatusLabels::get($status))
- [x] 9.4 Asegurar que la API devuelve status como int; si devuelve string, normalizar en el controlador antes de pasar a la vista

## 10. Restricción de pantallas por rol Supervisor

- [x] 10.1 Ocultar enlaces "Reglas" y "Prueba" del menú lateral cuando el usuario es Supervisor (usar @if($isAdmin ?? false))
- [x] 10.2 Añadir middleware o verificación en ReglasController y PruebaController para devolver 403 si el usuario es Supervisor
- [x] 10.3 Documentar en tasks que Supervisor solo ve: Dashboard, Cola, Impresoras, Alertas

## 11. Modo oscuro: contraste de texto

- [x] 11.1 Añadir clases dark: para tablas, cards y contenido: dark:bg-gray-800, dark:text-gray-200, dark:divide-gray-700
- [x] 11.2 Ajustar encabezados de tabla en modo oscuro: dark:bg-gray-700, dark:text-gray-100
- [x] 11.3 Ajustar inputs, selects y botones en modo oscuro para que el texto sea visible
- [x] 11.4 Revisar mensajes flash (success/error) para que tengan contraste en modo oscuro
- [x] 11.5 Asegurar que el área main (contenido) use color de texto explícito en dark: dark:text-gray-200

## 12. Plan de cierre de migracion (priorizado)

Escala usada:
- Necesidad: Critica / Alta / Media
- Dificultad: Baja / Media / Alta

### OLA M0 (cierre minimo para declarar migracion completada)

- [x] 12.1 [Necesidad: Alta][Dificultad: Media] Completar validacion E2E formal:
  - [x] 12.1.1 Login/logout, navegacion y sesion expirada (401).
  - [x] 12.1.2 Cola, impresoras, reglas, alertas, prueba.
  - [x] 12.1.3 Restricciones de rol Admin/Supervisor verificadas.
- [x] 12.2 [Necesidad: Alta][Dificultad: Media] Documentar y probar despliegue reverse proxy real (/ -> PHP, /api -> .NET).
- [x] 12.3 [Necesidad: Alta][Dificultad: Baja] Confirmar en tareas la retirada definitiva de Blazor cuando el entorno quede validado.

### OLA M1 (mejora de calidad visual/plantilla)

- [x] 12.4 [Necesidad: Media][Dificultad: Media] Completar assets pendientes de Metronic (o cerrar alcance explicitamente si no se usaran).
- [x] 12.5 [Necesidad: Media][Dificultad: Baja] Consolidar checklist de contraste/accesibilidad en modo oscuro.

### OLA M2 (estabilizacion post-migracion)

- [x] 12.6 [Necesidad: Media][Dificultad: Media] Definir smoke tests de regresion para futuras releases de UI.
- [x] 12.7 [Necesidad: Media][Dificultad: Baja] Revisar y normalizar documentacion final de operacion PHP.

## 13. Plan por sprints (3 semanas)

### Sprint M1 (Semana 1) - Cierre funcional de migracion

Responsables sugeridos:
- PHP/UI: flujo de pantallas y validaciones de sesion.
- Backend/API: soporte de errores/autorizacion para frontend.
- QA: ejecucion E2E formal.

Objetivos:
- [x] 13.1 Completar 12.1.1, 12.1.2 y 12.1.3.
- [x] 13.2 Completar 7.3 (validar flujo completo: login, navegacion, acciones).

Criterio de salida:
- [x] 13.3 Evidencia de pruebas E2E en DEV/QA sin bloqueantes criticos.

### Sprint M2 (Semana 2) - Despliegue y consolidacion tecnica

Responsables sugeridos:
- DevOps: reverse proxy y hardening de despliegue.
- PHP/UI: ajuste de assets y consistencia visual.
- QA: pruebas post-despliegue.

Objetivos:
- [x] 13.4 Completar 12.2 y 7.1 (configuracion y prueba de reverse proxy).
- [x] 13.5 Completar 12.4 y reevaluar 3.3 (assets Metronic pendientes).
- [x] 13.6 Completar 12.5.

Criterio de salida:
- [x] 13.7 Frontend PHP funcionando en esquema de despliegue objetivo (/ y /api) con estilo estable.

### Sprint M3 (Semana 3) - Estabilizacion y cierre definitivo

Responsables sugeridos:
- QA: smoke de regresion y checklist final.
- Producto/Arquitectura: cierre de alcance y retirada de legado.
- Documentacion: runbook y docs operativas.

Objetivos:
- [x] 13.8 Completar 12.6 y 12.7.
- [x] 13.9 Completar 12.3 y 7.4 (retirada definitiva de Blazor tras validacion).

Criterio de salida:
- [x] 13.10 Migracion declarada completada con documentacion final y backlog residual explicitado.
