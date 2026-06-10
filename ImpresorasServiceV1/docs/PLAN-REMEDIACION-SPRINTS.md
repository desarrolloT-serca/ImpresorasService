# Plan de Remediacion por Sprints

Documento operativo para implementar la remediacion tecnica del proyecto sin perder contexto.

## Objetivo del plan

- Reducir primero el riesgo de seguridad en produccion.
- Corregir despues incoherencias funcionales entre modulos.
- Mejorar finalmente rendimiento y deuda tecnica con bajo impacto colateral.
- Ejecutar cambios en lotes pequenos, con validacion y despliegue controlado.

## Contexto consolidado (fuente de hallazgos)

- Arquitectura activa: `API (.NET) + Worker (.NET) + Core + Frontend PHP (Laravel)`.
- Riesgos criticos detectados:
  - secretos/credenciales en configuracion versionada,
  - bootstrap de usuarios por defecto con credenciales debiles,
  - incoherencia de KPIs entre API y PHP,
  - operaciones de borrado duro sin transaccion explicita,
  - patrones de rendimiento mejorables en cola y conectividad.
- Principio de ejecucion: priorizar cambios con alta reduccion de riesgo y bajo coste de integracion.

---

## Sprint 1 - Cierre de exposicion de seguridad (P0)

### Objetivo

Cerrar exposiciones inmediatas de seguridad sin romper contratos API y con el menor acoplamiento posible.

### Alcance

1. **Secretos y credenciales**
   - Eliminar secretos hardcodeados de `appsettings*.json` (API/Worker).
   - Exigir `Jwt:Secret` obligatorio sin fallback inseguro.
2. **Bootstrap de usuarios**
   - Desactivar `Bootstrap:SeedDefaultUsers` por defecto fuera de local.
   - Mantener seed solo como operacion explicita y controlada.
3. **Proteccion de datos temporales de impresion**
   - Configurar `KeepTempFileOnFailure=false` por defecto.
   - Evitar logs con rutas sensibles de temporales.

### Archivos candidatos

- `src/ImpresorasService.Api/appsettings.json`
- `src/ImpresorasService.Api/appsettings.Development.json`
- `src/ImpresorasService.Worker/appsettings.json`
- `src/ImpresorasService.Worker/appsettings.Development.json`
- `src/ImpresorasService.Api/Program.cs`
- `src/ImpresorasService.Api/Controllers/AuthController.cs`
- `src/ImpresorasService.Core/Infrastructure/Services/WindowsPrintSpooler.cs`

### Dependencias previas

- Secret store/variables de entorno disponibles en entornos objetivo.
- Inventario de variables requeridas para API y Worker.

### Riesgos

- Fallo de arranque por configuracion incompleta (riesgo controlado y deseado).
- Necesidad de adaptar pipelines que dependian de defaults inseguros.

### Validaciones manuales

- Arranque de API/Worker solo con secretos externos.
- Login y emision JWT funcional.
- Simular fallo de impresion y verificar que no persisten PDF temporales por defecto.

### Pruebas automaticas

- Integracion de arranque: falla sin secretos obligatorios.
- Integracion auth: token valido con secreto inyectado.
- Unit/integracion spooler: politica de limpieza de temporales.

### Checklist de despliegue

- [ ] Secretos cargados en entorno (sin valores por defecto inseguros).
- [ ] `Bootstrap:SeedDefaultUsers` desactivado fuera de desarrollo.
- [ ] Rotacion de credenciales historicas conocidas.
- [ ] Smoke test de login + flujo basico de impresion.
- [ ] Verificacion de logs sin exposicion sensible.

### Criterio de cierre del sprint

- No hay secretos sensibles en repositorio.
- El arranque falla si faltan secretos obligatorios.
- No se crean usuarios por defecto salvo accion explicita.

---

## Sprint 2 - Coherencia funcional entre modulos (P1)

### Objetivo

Unificar semantica funcional entre API, Worker y PHP para eliminar decisiones operativas contradictorias.

### Alcance

1. **KPI "fallo sin reintento"**
   - Corregir clasificacion para excluir `RetryScheduled`.
2. **Unificacion Dashboard API vs Dashboard PHP**
   - Alinear reglas de calculo de KPIs/alertas.
   - Reducir duplicacion de reglas de negocio cuando sea posible.
3. **Integridad de borrado duro de tiendas**
   - Envolver hard delete y purgas en transaccion explicita.
4. **Documentacion funcional**
   - Actualizar definiciones de estados, KPIs y runbook operativo.

### Archivos candidatos

- `src/ImpresorasService.Api/Controllers/DashboardController.cs`
- `src/ImpresorasService.Web.PHP/app/Http/Controllers/DashboardController.php`
- `src/ImpresorasService.Api/Controllers/StoresController.cs`
- `README.md`
- `docs/RESUMEN-PROYECTO.md`
- `docs/HANA-DUALRUN-RUNBOOK.md` (solo definiciones que impacten operacion)

### Dependencias previas

- Sprint 1 desplegado y estable.
- Validacion funcional con negocio/operacion de definiciones KPI.

### Riesgos

- Cambio visible en indicadores y alertas respecto al historico.
- Posible confusion temporal de operacion si no hay comunicacion.

### Validaciones manuales

- Comparar dashboard API y dashboard PHP con mismo dataset.
- Probar casos de estado: `RetryScheduled`, `ErrorFinal`, `PrintedUnknown`.
- Simular fallo intermedio en hard delete y confirmar rollback total.

### Pruebas automaticas

- Unit de reglas KPI/salud.
- Integracion `GET /api/dashboard/overview` con datos controlados.
- Integracion de borrado duro con pruebas de atomicidad.
- E2E PHP contra API validando coherencia de cifras.

### Checklist de despliegue

- [ ] Nota operativa con cambio semantico de KPI.
- [ ] Despliegue coordinado API + PHP en la misma ventana.
- [ ] Smoke test dashboard global y por tienda.
- [ ] Prueba de hard delete en staging.
- [ ] Documentacion operativa actualizada.

### Criterio de cierre del sprint

- API y PHP devuelven KPIs consistentes para la misma entrada.
- `RetryScheduled` no se cuenta como "sin reintento".
- Hard delete no deja estados parciales ante fallo.

---

## Sprint 3 - Rendimiento y deuda tecnica (P2)

### Objetivo

Mejorar eficiencia operativa y cerrar deuda tecnica priorizada sin cambios disruptivos.

### Alcance

1. **Rendimiento de consulta de cola**
   - Aplicar `OrderBy/Take` en SQL antes de materializar resultados.
2. **Monitor de conectividad**
   - Evitar `SaveChanges` por impresora y pasar a guardado por lote.
3. **Alineacion HANA (codigo vs documentacion)**
   - Cerrar decision tecnica: staging local explicito vs adapter remoto real.
   - Actualizar docs para reflejar comportamiento real.
4. **(Opcional si hay capacidad) Endurecimiento auth**
   - Rate limiting o lockout basico para endpoints de login/token.

### Archivos candidatos

- `src/ImpresorasService.Api/Controllers/PrintJobsController.cs`
- `src/ImpresorasService.Worker/PrinterConnectivityMonitorService.cs`
- `src/ImpresorasService.Core/Infrastructure/Adapters/SapHanaJobSourceAdapter.cs` (si se decide cambio funcional)
- `docs/HANA-MIGRATION-SPIKE.md`
- `docs/HANA-DUALRUN-RUNBOOK.md`
- `README.md`
- `src/ImpresorasService.Api/Program.cs` (si se incorpora rate limiting)
- `src/ImpresorasService.Api/Controllers/AuthController.cs` (si aplica)

### Dependencias previas

- Sprint 2 validado.
- Decision tecnica formal sobre estrategia HANA.

### Riesgos

- Cambios de rendimiento pueden modificar timings y percepcion operativa.
- Si HANA pasa a remoto real, el alcance puede crecer (infra/licencias/permisos).

### Validaciones manuales

- Prueba de carga media en `GET /api/printjobs`.
- Verificar ciclo de conectividad con volumen alto de impresoras.
- Ensayo de runbook HANA actualizado en preproduccion.

### Pruebas automaticas

- Integracion de orden/limite/filtros en cola.
- Integracion del monitor para medir reduccion de escrituras a BD.
- E2E de ingesta claim/lease/ack segun modo HANA definitivo.

### Checklist de despliegue

- [ ] Baseline de metricas pre/post (latencia, CPU, I/O DB).
- [ ] Despliegue controlado API/Worker con observabilidad reforzada.
- [ ] Validacion operativa 24-48h tras despliegue.
- [ ] Documentacion HANA actualizada y aprobada.
- [ ] Plan de rollback por componente definido.

### Criterio de cierre del sprint

- Mejora medible de latencia/consumo en endpoints y workers afectados.
- Menor presion de escritura en monitor de conectividad.
- Documentacion HANA coherente con el comportamiento real implementado.

### Estado de ejecucion (avance actual)

- [x] `GET /api/printjobs` optimizado (orden y limite en SQL).
- [x] Monitor de conectividad refactorizado a guardado por lote por ciclo.
- [x] Documentacion HANA/README alineada con el comportamiento real actual.
- [ ] Decidir estrategia final de HANA remoto dedicado vs mantener enfoque ORM para dual-run.

---

## Matriz de impacto (para planificar sin romper contratos)

### Cambios sin ruptura de contrato API

- Endurecimiento de secretos y bootstrap.
- Limpieza de temporales spooler.
- Optimizacion SQL en `GET /api/printjobs`.
- Transaccionalidad interna en hard delete.
- Batch de escrituras en monitor de conectividad.

### Cambios con posible impacto semantico o coordinacion

- Semantica de KPIs/alertas en dashboard.
- Cualquier decision que cambie flujo HANA real.
- Posible rate-limit en login (nuevos 429 bajo abuso).

### Cambios que requieren coordinacion API + Worker + PHP

- Unificacion de KPIs entre API y PHP.
- Alineacion de estrategia HANA y su documentacion operativa.

---

## Definicion de "Done" global del plan

- Seguridad basal cerrada (sin secretos expuestos ni bootstrap inseguro).
- Coherencia funcional entre paneles y backend validada.
- Mejoras de rendimiento desplegadas con evidencia de impacto.
- Documentacion de arquitectura/operacion alineada con el sistema real.
- Checklists de despliegue y rollback probados en staging.
