# Open Spec V1 - Servicio Centralizado de Impresion SAP

## 1. Objetivo
Construir una plataforma centralizada de impresion para SAP que desacople la impresion de los PCs y de SAP, enrute trabajos PDF por reglas, ejecute la impresion en servidores Windows del dominio y mantenga trazabilidad completa con control de errores y reintentos.

## 2. Alcance V1
- Ingesta de trabajos desde base de datos origen con polling cada 5 segundos.
- Soporte de origen flexible mediante adaptadores de acceso a datos:
  - Adaptador SQL para pruebas.
  - Adaptador SAP HANA para entorno objetivo.
- Cola interna y auditoria en SQL Server.
- Enrutado por reglas con prioridad:
  1) Tienda + TipoDocumento + Canal
  2) Tienda + TipoDocumento
  3) Tienda
  4) Global
- Motor de impresion con Windows Print Spooler.
- Reintentos automaticos con backoff 15/30/60/90 segundos.
- Panel web Blazor Server para administracion y operacion.
- Autenticacion integrada de AD (Negotiate) y autorizacion por roles.
- Alertado en panel para errores finales.

## 3. Fuera de alcance V1
- Descubrimiento automatico de impresoras en Active Directory.
- Integraciones de notificacion externa (correo, Teams, SMS).
- Alta disponibilidad activa-activa (se prepara diseno, no se activa).
- Confirmacion fisica garantizada de impresion para todos los modelos de impresora.

## 4. Usuarios y permisos V1
- Admin:
  - Acceso global a todas las tiendas.
  - Gestion de reglas e impresoras.
  - Reintento, cancelacion logica y consulta total de auditoria.
- Supervisor de tienda:
  - Acceso solo a su tienda.
  - Ver estados de cola e impresoras.
  - Reintentar y cancelar logicamente.
- Call Center:
  - Fuera de V1.

## 5. Requisitos funcionales

### RF-01 Ingesta de trabajos
- El sistema consulta origen cada 5 segundos.
- Cada trabajo nuevo se normaliza y se inserta en `PrintJobs`.
- El PDF se almacena como BLOB en SQL Server interno.

### RF-02 Control estricto de duplicados
- No se permiten duplicados nunca.
- Unicidad por `SourceSystem + ExternalJobId`.
- Control adicional por hash de contenido (`PdfSha256`) para reducir riesgo de duplicado sin ID fiable.

### RF-03 Enrutado
- El motor resuelve impresora objetivo en base a reglas activas por prioridad.
- Si no existe regla valida, el trabajo pasa a `ErrorFinal` con causa de enrutado.

### RF-04 Impresion y seguimiento
- El motor envia a spooler y registra:
  - Tiempo de inicio.
  - Tiempo de aceptacion spooler.
  - Resultado best effort de confirmacion de impresora.
- Timeout por intento: 30 segundos.

### RF-05 Reintentos
- Reintentos en errores transitorios.
- Secuencia de backoff: 15s, 30s, 60s, 90s.
- Al agotar intentos, estado final `ErrorFinal`.

### RF-06 Cancelacion logica
- Permitida solo en estados `Pending`, `Routed`, `RetryScheduled`.
- Si el job ya esta en `Printing` o posterior, no se puede cancelar logicamente.

### RF-07 Operacion en panel
- Filtros por tienda, estado, rango temporal e impresora.
- Acciones:
  - Reintentar job.
  - Cancelar logicamente job.
  - Test print de impresora.

### RF-08 Alertas de error final
- Al entrar en `ErrorFinal`, generar alerta visible para Supervisor (tienda) y Admin (global).
- Alertas mostradas en panel con prioridad alta hasta atencion.

### RF-09 Auditoria completa
- Registro de eventos por cada transicion de estado.
- Historial consultable por `JobId`, `ExternalJobId`, tienda y documento.
- Exportacion CSV manual en V1.
- Retencion minima: 365 dias.

## 6. Requisitos no funcionales
- Disponibilidad operativa orientada a intranet corporativa.
- Tolerancia a fallos temporales de red o direccionamiento sin perdida de jobs.
- Observabilidad con logs estructurados y correlacion (`CorrelationId`).
- Seguridad integrada con AD y control de acceso por rol.
- Entornos obligatorios: DEV, QA, PROD.

## 7. Modelo de datos minimo V1

### Tabla `PrintJobs` (resumen)
- `JobId` (GUID, PK)
- `SourceSystem` (nvarchar)
- `ExternalJobId` (nvarchar)
- `StoreId` (int)
- `DocumentType` (nvarchar)
- `Channel` (nvarchar, default `DEFAULT`)
- `PdfBlob` (varbinary(max))
- `PdfSha256` (char(64))
- `Status` (nvarchar)
- `AttemptCount` (int)
- `NextRetryAtUtc` (datetime2, nullable)
- `LastErrorCode` (nvarchar, nullable)
- `LastErrorMessage` (nvarchar, nullable)
- `CorrelationId` (uniqueidentifier)
- `CreatedAtUtc` / `UpdatedAtUtc` (datetime2)
- `RowVersion` (rowversion)

### Tabla `PrintJobEvents`
- Historial inmutable de eventos y cambios de estado.

### Tabla `Printers`
- Alta manual de impresoras:
  - `PrinterId`, `PrinterName`, `SpoolQueue`, `StoreId`, `IsActive`, `CapabilitiesJson`.

### Tabla `RoutingRules`
- Reglas por prioridad con vigencia y activacion.

## 8. Criterios de aceptacion V1
- CA-01: Un job insertado en origen se refleja en cola interna en <= 10 segundos en condiciones normales.
- CA-02: No se crean duplicados cuando llega 2 veces el mismo `ExternalJobId`.
- CA-03: Ante error transitorio, se ejecutan 4 intentos con los backoff definidos.
- CA-04: Ante error persistente, el job finaliza en `ErrorFinal` y aparece alerta en panel.
- CA-05: Supervisor no puede ver tiendas ajenas.
- CA-06: Dos usuarios que intentan reimprimir el mismo job a la vez no generan doble reimpresion.

## 9. Riesgos y mitigaciones
- Confirmacion de impresora no uniforme por modelo:
  - Mitigacion: V1 best effort y estados `PrintedConfirmed`/`PrintedUnknown`.
- Cambios en estructura de origen SAP:
  - Mitigacion: arquitectura por adaptadores y mapeo configurable.
- Concurrencia en acciones manuales:
  - Mitigacion: transiciones atomicas + `RowVersion`.
