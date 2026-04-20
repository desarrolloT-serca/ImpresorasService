# Runbook dual-run HANA

## Objetivo
Ejecutar una transicion por fases con control operativo del flujo de ingesta e impresion.

## Configuracion recomendada por etapa
1. **Shadow**: `Source:Mode=SapHana` en entorno de preproduccion con telemetria reforzada.
2. **Canary**: activar un subconjunto de workers con HANA.
3. **Full**: todos los workers y API con `Database:Provider=Hana`.

## Señales a monitorizar
- `fetched`, `inserted`, `duplicates`, `ackCandidates` por lote de ingesta.
- latencia de lote de ingesta.
- porcentaje de `ErrorFinal` y `RetryScheduled`.
- numero de reclaims (lease expirado).

## Regla de rollback
- Si sube `ErrorFinal` o baja `inserted/fetched` de forma sostenida, volver a configuracion previa.
- Mantener backup de base y configuracion versionada por entorno.

## Checklist post-cutover
- Migraciones aplicadas sin errores.
- Dashboard operativo con lectura/escritura de umbrales por ORM.
- Ingestion y ack en origen HANA funcionando.
- Smoke E2E de enrutado e impresion superado.
