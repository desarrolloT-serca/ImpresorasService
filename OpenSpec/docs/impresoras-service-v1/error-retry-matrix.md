# Matriz de errores y reintentos V1

## Politica general
- Maximo 4 intentos por job.
- Backoff por intento: 15s, 30s, 60s, 90s.
- Timeout por intento de spooler: 30s.
- Si se agotan intentos, estado `ErrorFinal` y alerta.

## Matriz

| Categoria | Codigo sugerido | Ejemplo | Reintentar | Estado siguiente |
|---|---|---|---|---|
| Conectividad temporal | `NET_TIMEOUT` | timeout de red, socket temporal | Si | `RetryScheduled` |
| Spooler no disponible | `SPOOLER_DOWN` | servicio detenido o no responde | Si | `RetryScheduled` |
| Cola bloqueada temporal | `QUEUE_BUSY` | cola ocupada, error temporal | Si | `RetryScheduled` |
| Impresora offline temporal | `PRINTER_OFFLINE_TEMP` | desconexion transitoria | Si | `RetryScheduled` |
| Regla no encontrada | `ROUTE_NOT_FOUND` | no hay regla activa aplicable | No | `ErrorFinal` |
| Impresora inactiva/no valida | `PRINTER_INVALID` | impresora deshabilitada o inexistente | No | `ErrorFinal` |
| PDF corrupto/invalido | `PDF_INVALID` | BLOB no parseable | No | `ErrorFinal` |
| Duplicado detectado | `DUPLICATE_JOB` | mismo `ExternalJobId` | No | `Cancelled` logico/descartado |
| Permiso insuficiente | `AUTH_FORBIDDEN` | usuario sin rol para accion | No | Sin cambio |
| Conflicto de concurrencia | `CONCURRENCY_CONFLICT` | otro actor ya cambio estado | No | Sin cambio |

## Reglas de clasificacion
- Transitorio:
  - Fallos de red, spooler, infraestructura temporal.
- No transitorio:
  - Configuracion invalida, datos corruptos, ausencia de regla, seguridad.

## Acciones al llegar a ErrorFinal
- Crear evento en `PrintJobEvents`.
- Marcar alerta activa para:
  - Admin global.
  - Supervisor de `StoreId`.
- Exponer causa tecnica y mensaje amigable en panel.
- Permitir reintento manual controlado por roles.

## Prevencion de doble reintento manual
- Endpoint de reintento manual debe validar `RowVersion`.
- Si dos usuarios disparan accion simultanea:
  - Solo uno gana la transicion.
  - El otro recibe `409 Conflict` con mensaje funcional.

## Observabilidad minima por error
- `CorrelationId`
- `JobId`
- `StoreId`
- `PrinterId` (si aplica)
- `AttemptNumber`
- `ErrorCode`
- `LatencyMs`
- `OccurredAtUtc`
