## Purpose

Definir los requisitos funcionales y operativos de la V1 del servicio centralizado de impresion para SAP en entorno Windows corporativo multi-tienda.

## ADDED Requirements

### Requirement: Ingesta flexible y persistente de trabajos
El sistema SHALL ingerir trabajos de impresion desde una base de datos origen mediante polling configurable y persistirlos en una cola interna transaccional.

#### Scenario: Polling base cada 5 segundos
- **WHEN** el worker de ingesta esta operativo
- **THEN** el sistema SHALL consultar la fuente de trabajos cada 5 segundos por defecto
- **AND** el intervalo SHALL ser configurable por entorno

#### Scenario: Origen desacoplado por adaptadores
- **WHEN** se configura un adaptador SQL de pruebas o SAP HANA
- **THEN** el sistema SHALL usar el mismo flujo de normalizacion y encolado
- **AND** cambiar de origen SHALL NOT requerir cambios en reglas de negocio

#### Scenario: Persistencia de PDF en BLOB
- **WHEN** se recibe un trabajo valido
- **THEN** el PDF SHALL almacenarse en BLOB dentro de la base interna
- **AND** el registro SHALL incluir metadatos minimos de trazabilidad

#### Scenario: Proveedor de base de datos por entorno
- **WHEN** el sistema corre en entorno local de desarrollo
- **THEN** la implementacion MAY usar SQLite para facilitar pruebas sin infraestructura externa
- **AND** en QA/PROD la persistencia SHALL usar SQL Server segun el objetivo V1
- **AND** la semantica funcional de idempotencia, estados y auditoria SHALL mantenerse equivalente

### Requirement: Idempotencia estricta y control de duplicados
El sistema SHALL impedir la creacion de trabajos duplicados en cola interna y SHALL controlar carreras de concurrencia en acciones manuales.

#### Scenario: Duplicado por id externo
- **WHEN** llegan dos registros con el mismo `SourceSystem + ExternalJobId`
- **THEN** el sistema SHALL aceptar solo uno
- **AND** el intento duplicado SHALL registrarse en auditoria

#### Scenario: Reintento manual simultaneo
- **WHEN** dos usuarios intentan reimprimir el mismo trabajo al mismo tiempo
- **THEN** solo una transicion de estado SHALL completarse
- **AND** la segunda accion SHALL devolver conflicto de concurrencia

### Requirement: Enrutado determinista por prioridad de reglas
El sistema SHALL resolver la impresora objetivo aplicando reglas activas por prioridad determinista.

#### Scenario: Coincidencia total
- **WHEN** existe regla para `StoreId + DocumentType + Channel`
- **THEN** esa regla SHALL tener precedencia sobre cualquier otra

#### Scenario: Fallback por niveles
- **WHEN** no existe coincidencia total
- **THEN** el sistema SHALL evaluar en orden: `StoreId + DocumentType`, luego `StoreId`, luego `Global`
- **AND** la primera coincidencia valida SHALL definir la impresora destino

#### Scenario: Sin regla aplicable
- **WHEN** no existe ninguna regla activa aplicable
- **THEN** el trabajo SHALL pasar a `ErrorFinal`
- **AND** el sistema SHALL registrar causa `ROUTE_NOT_FOUND`

### Requirement: Motor de impresion con timeout y reintentos
El sistema SHALL ejecutar impresion por Windows Print Spooler con timeout por intento, clasificacion de errores y politica de reintentos.

#### Scenario: Intento con timeout
- **WHEN** un intento de impresion supera 30 segundos sin resultado
- **THEN** el intento SHALL marcarse como fallo transitorio
- **AND** el trabajo SHALL pasar a `RetryScheduled` si quedan intentos

#### Scenario: Secuencia de reintentos
- **WHEN** el trabajo falla por causa transitoria
- **THEN** el sistema SHALL aplicar backoff 15s, 30s, 60s y 90s
- **AND** al agotarse intentos SHALL mover el trabajo a `ErrorFinal`

#### Scenario: Error no transitorio
- **WHEN** la causa es no recuperable (ej. PDF invalido o impresora invalida)
- **THEN** el sistema SHALL evitar reintentos automaticos
- **AND** SHALL finalizar en `ErrorFinal`

### Requirement: Estados operativos y cancelacion logica
El sistema SHALL mantener una maquina de estados explicita para cada trabajo y SHALL restringir la cancelacion logica a estados permitidos.

#### Scenario: Estados permitidos en V1
- **WHEN** el job recorre su ciclo de vida
- **THEN** el estado SHALL pertenecer al conjunto:
  `Pending`, `Routed`, `Printing`, `SpoolAccepted`, `PrintedConfirmed`, `PrintedUnknown`, `RetryScheduled`, `Cancelled`, `ErrorFinal`

#### Scenario: Cancelacion valida
- **WHEN** un usuario autorizado solicita cancelacion en `Pending`, `Routed` o `RetryScheduled`
- **THEN** el sistema SHALL cambiar a `Cancelled`
- **AND** SHALL registrar evento de auditoria con actor y timestamp

#### Scenario: Cancelacion invalida
- **WHEN** se solicita cancelacion en `Printing`, `SpoolAccepted`, `PrintedConfirmed`, `PrintedUnknown` o `ErrorFinal`
- **THEN** el sistema SHALL rechazar la accion sin cambiar estado

### Requirement: Seguridad y aislamiento por tienda
El sistema SHALL autenticar usuarios por AD y aplicar autorizacion por rol y ambito de tienda.

#### Scenario: Supervisor con alcance acotado
- **WHEN** un supervisor consulta cola y errores
- **THEN** solo SHALL visualizar registros de su `StoreId`
- **AND** solo SHALL poder ejecutar acciones en su tienda

#### Scenario: Admin global
- **WHEN** un admin accede al panel
- **THEN** SHALL visualizar todas las tiendas
- **AND** SHALL gestionar reglas e impresoras globalmente

### Requirement: Auditoria y alertado operativo
El sistema SHALL registrar trazabilidad completa de cada job y SHALL generar alertas operativas ante fallo final.

#### Scenario: Registro end-to-end
- **WHEN** un trabajo cambia de estado
- **THEN** el sistema SHALL registrar evento en historial con `JobId`, estado anterior/nuevo, actor y timestamp UTC

#### Scenario: Alerta por ErrorFinal
- **WHEN** un trabajo entra en `ErrorFinal`
- **THEN** el sistema SHALL crear una alerta activa visible en panel
- **AND** la alerta SHALL ser visible al Admin y al Supervisor de la tienda

#### Scenario: Retencion y exportacion
- **WHEN** se consulta historico operativo
- **THEN** los eventos SHALL conservarse al menos 365 dias
- **AND** el sistema SHALL permitir exportacion CSV manual en V1
