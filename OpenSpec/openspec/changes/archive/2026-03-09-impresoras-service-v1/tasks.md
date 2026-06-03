## 1. Formalizacion de especificacion

- [x] 1.1 Completar y revisar `proposal.md`
- [x] 1.2 Completar y revisar `design.md`
- [x] 1.3 Validar deltas en `specs/sap-printing-service/spec.md`
- [x] 1.4 Ejecutar `openspec validate impresoras-service-v1 --type change`

## 2. Fase 1 - Ingesta e idempotencia

- [x] 2.1 Definir contratos de origen (`IJobSourceAdapter`)
- [x] 2.2 Implementar adaptador SQL de pruebas
- [x] 2.3 Implementar adaptador SAP HANA (minimo viable)
- [x] 2.4 Implementar polling cada 5s con checkpoint seguro
- [x] 2.5 Aplicar constraint de no duplicados por `SourceSystem + ExternalJobId`
- [x] 2.6 Registrar eventos de ingesta en auditoria

## 3. Fase 2 - Enrutado y configuracion de impresoras

- [x] 3.1 Implementar CRUD de impresoras (alta manual V1)
- [x] 3.2 Implementar CRUD de reglas de enrutado
- [x] 3.3 Implementar resolucion por prioridad de reglas
- [x] 3.4 Implementar error funcional cuando no hay ruta valida

## 4. Fase 3 - Ejecucion de impresion y reintentos

- [x] 4.1 Integrar Windows Print Spooler
- [x] 4.2 Aplicar timeout de 30s por intento
- [x] 4.3 Implementar reintentos 15/30/60/90
- [x] 4.4 Clasificar errores transitorios y no transitorios
- [x] 4.5 Garantizar transiciones atomicas y control de concurrencia

## 5. Fase 4 - Operacion web y seguridad

### 5.0 Preparacion UI (ver docs/PLANNING-UI-Y-LOGIN.md)

- [x] 5.0.0 Crear docs/PLANNING-UI-Y-LOGIN.md
- [x] 5.0.1 Anadir MudBlazor a ImpresorasService.Web
- [x] 5.0.2 Configurar Web para llamar a API (HttpClient, BaseAddress)
- [x] 5.0.3 Definir estructura de carpetas (Pages, Components, Services)

### 5.1 Login basico (Cookie Auth MVP)

- [x] 5.1.1 Crear endpoint POST /api/auth/login (usuario/contraseña)
- [x] 5.1.2 Crear tabla Users (UserId, Login, PasswordHash, Role, StoreId)
- [x] 5.1.3 Configurar Cookie Authentication en Web
- [x] 5.1.4 Pagina Login (/login) con formulario
- [x] 5.1.5 Layout: mostrar usuario, boton Logout
- [x] 5.1.6 Proteger rutas [Authorize] en paginas
- [x] 5.1.7 Redirect a /login si no autenticado

### 5.2 Layout y navegacion

- [x] 5.2.1 Layout principal con MudBlazor (AppBar, Drawer)
- [x] 5.2.2 Menu: Dashboard, Cola, Impresoras, Reglas, Prueba, Alertas
- [x] 5.2.3 Filtro de tienda en header (para Supervisor)
- [x] 5.2.4 Pagina 404 / Error

### 5.3 Pantallas core

- [ ] 5.3.1 Cola: tabla con filtros, acciones Enrutar/Cancelar/Reintentar
- [ ] 5.3.2 Detalle job: historial de eventos, acciones
- [ ] 5.3.3 Impresoras: CRUD con MudDataGrid o tabla
- [ ] 5.3.4 Reglas: CRUD + simulador de resolucion
- [ ] 5.3.5 Crear prueba: formulario que llama a /api/sourceprintjobs/test

### 5.4 Dashboard y alertas

- [ ] 5.4.1 Dashboard: KPIs por estado (cards)
- [ ] 5.4.2 Lista de alertas (jobs ErrorFinal)
- [ ] 5.4.3 Auto-refresh opcional (Timer, cada 30s)

### 5.5 Autorizacion por rol (Admin/Supervisor)

- [ ] 5.5.1 Policy Admin / Supervisor
- [ ] 5.5.2 Ocultar menu segun rol
- [ ] 5.5.3 Filtro automatico por StoreId para Supervisor
- [ ] 5.5.4 Validar en API: rechazar acciones fuera de ambito

### 5.6 Autenticacion AD Negotiate (produccion)

- [ ] 5.6.1 Configurar AddAuthentication(Negotiate) en API y/o Web
- [ ] 5.6.2 Mapeo AD a Role/StoreId (grupos o tabla)
- [ ] 5.6.3 Modo hibrido: Negotiate si esta, si no Cookie para dev

### 5.7 Acciones manuales y filtros

- [ ] 5.7.1 Implementar filtros operativos por tienda/estado/impresora
- [ ] 5.7.2 Implementar acciones manuales reintentar/cancelar
- [ ] 5.7.3 Evitar doble reimpresion en acciones simultaneas

## 6. Fase 5 - Auditoria y salida operativa

- [ ] 6.1 Completar auditoria end-to-end en `PrintJobEvents`
- [ ] 6.2 Implementar alertas de `ErrorFinal` en panel
- [ ] 6.3 Implementar export CSV del historial
- [ ] 6.4 Verificar retencion de 365 dias

## 7. Validacion y despliegue

- [ ] 7.1 Ejecutar pruebas funcionales en DEV y QA
- [ ] 7.2 Ejecutar pruebas de concurrencia sobre reintentos manuales
- [ ] 7.3 Validar aislamiento de tienda con cuentas AD reales
- [ ] 7.4 Aprobar checklist de paso a PROD

## 8. Priorizacion por necesidad y dificultad (Plan de ejecucion)

Escala usada:
- Necesidad: Critica / Alta / Media
- Dificultad: Baja / Media / Alta

### OLA P0 (maxima prioridad)

- [ ] 8.1 [Necesidad: Critica][Dificultad: Media] Proteger API con autorizacion real por rol/ambito:
  - [ ] 8.1.1 Aplicar [Authorize] y policies en endpoints operativos.
  - [ ] 8.1.2 Forzar aislamiento por StoreId para Supervisor en API.
  - [ ] 8.1.3 Agregar tests de integracion para casos 401/403.
- [ ] 8.2 [Necesidad: Critica][Dificultad: Baja] Eliminar fallback de privilegio Admin en frontend PHP cuando role no existe.
- [ ] 8.3 [Necesidad: Alta][Dificultad: Baja] Alinear acciones de UI con transiciones validas del backend (reintentar/cancelar).

### OLA P1 (cierre funcional core)

- [ ] 8.4 [Necesidad: Alta][Dificultad: Media] Implementar cancelacion manual end-to-end:
  - [ ] 8.4.1 Endpoint/API con validacion por estado permitido.
  - [ ] 8.4.2 Accion de UI y feedback operativo.
  - [ ] 8.4.3 Auditoria de actor/motivo/timestamp.
- [ ] 8.5 [Necesidad: Alta][Dificultad: Media] Hardening de seguridad operativa:
  - [ ] 8.5.1 Eliminar secretos por defecto en configuracion.
  - [ ] 8.5.2 Restringir CORS por entorno.
  - [ ] 8.5.3 Sustituir credenciales de seed inseguras y documentar bootstrap seguro.
- [ ] 8.6 [Necesidad: Alta][Dificultad: Alta] Completar adaptador SAP HANA con fetch operativo real (si entorno objetivo lo requiere).

### OLA P2 (operacion y salida V1)

- [ ] 8.7 [Necesidad: Media][Dificultad: Media] Pantalla detalle de job con historial de eventos.
- [ ] 8.8 [Necesidad: Media][Dificultad: Media] Dashboard y alertas operativas con auto-refresh configurable.
- [ ] 8.9 [Necesidad: Media][Dificultad: Media] Exportacion CSV + verificacion de retencion 365 dias.
- [ ] 8.10 [Necesidad: Media-Alta][Dificultad: Alta] Endurecer concurrencia para acciones manuales simultaneas (evitar doble reimpresion).

## 9. Plan por sprints (4 semanas)

### Sprint 1 (Semana 1) - Seguridad y control de acceso (P0)

Responsables sugeridos:
- Backend/API: autorizacion por rol/ambito en endpoints.
- PHP/UI: correccion de fallback de rol y visibilidad de acciones.
- QA: casos negativos 401/403 y bypass por rol.

Objetivos:
- [ ] 9.1 Completar 8.1.1, 8.1.2 y 8.1.3.
- [ ] 9.2 Completar 8.2.
- [ ] 9.3 Completar 8.3.

Criterio de salida:
- [ ] 9.4 Ningun endpoint operativo sensible accesible sin token/permisos correctos.

### Sprint 2 (Semana 2) - Acciones manuales y hardening (P1 parcial)

Responsables sugeridos:
- Backend/API: cancelacion valida por estados + auditoria.
- PHP/UI: accion cancelar/reintentar coherente con backend.
- DevOps/Security: configuracion segura por entorno.

Objetivos:
- [ ] 9.5 Completar 8.4.1, 8.4.2 y 8.4.3.
- [ ] 9.6 Completar 8.5.1, 8.5.2 y 8.5.3.

Criterio de salida:
- [ ] 9.7 Cancelacion/reintento funcionando con validacion de transicion y trazabilidad.

### Sprint 3 (Semana 3) - Integracion origen real y operacion UI (P1/P2)

Responsables sugeridos:
- Integraciones: adaptador SAP HANA.
- Backend/API + PHP/UI: detalle de job e historial.
- QA: pruebas funcionales con datos representativos.

Objetivos:
- [ ] 9.8 Completar 8.6 (si aplica al entorno objetivo inmediato).
- [ ] 9.9 Completar 8.7.
- [ ] 9.10 Completar 8.8.

Criterio de salida:
- [ ] 9.11 Flujo operativo principal usable por Admin y Supervisor en entorno QA.

### Sprint 4 (Semana 4) - Cierre V1 y preparacion PROD (P2)

Responsables sugeridos:
- Backend/API: export y retencion.
- QA: concurrencia y aislamiento por tienda.
- Operaciones: checklist final de paso a produccion.

Objetivos:
- [ ] 9.12 Completar 8.9 y 8.10.
- [ ] 9.13 Completar 7.1, 7.2, 7.3 y 7.4.

Criterio de salida:
- [ ] 9.14 Validacion V1 cerrada con evidencias de QA y checklist de despliegue aprobado.
