# Backlog V1 por fases (orden optimo)

## Fase 0 - Base tecnica y decisiones cerradas
- Definir contratos de dominio (`PrintJob`, `Printer`, `RoutingRule`, `AuditEvent`).
- Crear esquema SQL inicial con constraints de no duplicado.
- Preparar configuracion por entorno (DEV/QA/PROD).
- Definir catalogo de estados y codigos de error.

## Fase 1 - Ingesta y cola interna
- Implementar interfaz `IJobSourceAdapter`.
- Implementar `SqlTestAdapter` para pruebas.
- Implementar `SapHanaAdapter` (stub inicial + contrato finalizable).
- Polling service cada 5s.
- Normalizacion e insercion en `PrintJobs` con idempotencia.

## Fase 2 - Enrutado y configuracion de impresoras
- CRUD de impresoras (alta manual V1).
- CRUD de reglas de enrutado por prioridad.
- Servicio de resolucion de ruta con fallback.
- Validaciones de integridad de reglas activas.

## Fase 3 - Motor de impresion y reintentos
- Integracion con Windows Print Spooler.
- Ejecucion de intento con timeout 30s.
- Politica de reintentos 15/30/60/90s.
- Clasificacion de errores transitorio/no transitorio.
- Transiciones atomicas con `RowVersion`.

## Fase 4 - Panel Blazor y operacion
- Login Negotiate y autorizacion por rol.
- Vistas de cola por tienda y global admin.
- Filtros por estado, tienda, impresora y rango.
- Acciones: reintentar, cancelar, test print.
- Alertas en panel para `ErrorFinal`.

## Fase 5 - Auditoria, export y endurecimiento
- Registro completo en `PrintJobEvents`.
- Export CSV de historico.
- Dashboards operativos basicos.
- Pruebas E2E de caminos felices y fallos.
- Hardening de logs y manejo de excepciones.

## Definicion de terminado (DoD) V1
- Todos los CA del Open Spec validados en QA.
- No duplicados verificados con pruebas de concurrencia.
- Reintentos y timeout validados con fallos simulados.
- Aislamiento por tienda validado con cuentas reales AD de prueba.
- Runbook de operacion y recuperacion publicado.

## Riesgos de ejecucion a vigilar
- Dependencias de acceso SAP HANA y credenciales.
- Variabilidad de respuesta real de impresoras.
- Alineacion de grupos AD antes de UAT.
- Politica final de retencion y capacidad de almacenamiento.
