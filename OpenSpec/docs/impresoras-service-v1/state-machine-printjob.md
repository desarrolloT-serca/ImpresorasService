# Maquina de estados PrintJob V1

## Estados
- `Pending`: job recibido y validado, pendiente de enrutado.
- `Routed`: impresora resuelta y lista para ejecutar.
- `Printing`: intento en curso contra spooler.
- `SpoolAccepted`: spooler acepta el trabajo.
- `PrintedConfirmed`: confirmacion positiva de impresion.
- `PrintedUnknown`: sin confirmacion concluyente, pero spooler acepto.
- `RetryScheduled`: fallo transitorio, pendiente del siguiente intento.
- `Cancelled`: cancelacion logica solicitada por usuario autorizado.
- `ErrorFinal`: fallo no recuperable o agotamiento de reintentos.

## Transiciones validas
- `Pending -> Routed`
- `Routed -> Printing`
- `Printing -> SpoolAccepted`
- `SpoolAccepted -> PrintedConfirmed`
- `SpoolAccepted -> PrintedUnknown`
- `Printing -> RetryScheduled`
- `RetryScheduled -> Printing`
- `Pending -> Cancelled`
- `Routed -> Cancelled`
- `RetryScheduled -> Cancelled`
- `Pending -> ErrorFinal` (validacion/enrutado)
- `Routed -> ErrorFinal` (impresora invalida o no operativa)
- `Printing -> ErrorFinal` (error no transitorio)
- `RetryScheduled -> ErrorFinal` (intentos agotados)

## Reglas operativas
- Cancelacion logica solo permitida en `Pending`, `Routed`, `RetryScheduled`.
- Cualquier accion de reintento manual cambia a `RetryScheduled` con prioridad inmediata.
- El paso a `ErrorFinal` dispara alerta visible para Admin y Supervisor de la tienda.

## Concurrencia y consistencia
- Toda transicion debe hacerse de forma atomica en BD:
  - `UPDATE ... WHERE JobId = @id AND Status IN (...) AND RowVersion = @rv`.
- Si no hay filas afectadas:
  - Rechazar accion con mensaje "estado ya cambiado por otro usuario/proceso".
- Reintento manual y automatico compiten por lock logico:
  - Solo una ejecucion puede mover el job a `Printing`.

## Reglas de negocio para reimpresion manual
- Solo permitido si estado actual en `ErrorFinal`, `PrintedUnknown` o `Cancelled` (segun permiso).
- Crear evento de auditoria con actor, motivo y timestamp UTC.
- Nunca duplicar job:
  - Se reutiliza `JobId` original incrementando `AttemptCount`.
  - No se crea nuevo registro de `PrintJobs` para reimpresion V1.
