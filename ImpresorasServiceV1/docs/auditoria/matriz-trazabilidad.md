# Matriz de trazabilidad

**Corte:** 2026-07-29 · **Commit:** `49a0b9691e484472fb1da23417de172f1e60473f`

No se proporcionó una especificación funcional aprobada. Los requisitos marcados como **inferidos** se han reconstruido desde controladores, vistas, servicios, tests y documentación del repositorio.

| ID | Requisito o funcionalidad | Código relacionado | Pruebas existentes | Hallazgos | Estado |
| --- | --- | --- | --- | --- | --- |
| R-01 | Autenticar por login/contraseña y emitir JWT (inferido) | `Api/Controllers/AuthController.cs`; `Web.PHP/app/Http/Controllers/AuthController.php` | `AuthControllerTests`, `ApiClientTest` | AUD-03, AUD-05, AUD-06 | Parcial: login probado; revocación y sesión incompletas |
| R-02 | Impedir que el sistema quede sin administradores (inferido por protección al borrar) | `UsersController.cs:108-178` | Cobertura de borrado de último admin | AUD-03 | Defectuoso al editar/demover |
| R-03 | Aislar StoreManager/Employee a su tienda (inferido) | `PrintJobsController`, `PrintersController`, `DashboardController`, claims JWT | Pruebas parciales de roles/tienda | AUD-04 | Parcial; lecturas fallan en abierto sin `StoreId` |
| R-04 | Administrar tiendas, incluido ID 0 “Almacén Central” (inferido por tests/API) | `StoresController.cs`; controladores PHP de tiendas/dashboard | `StoresControllerTests`; `TiendasControllerTest` | AUD-07 | Confirmadamente inconsistente |
| R-05 | Administrar usuarios vinculados a una tienda | `UsersController.cs`; `UsuariosController.php` | CRUD y validación parciales | AUD-03, AUD-07 | Parcial |
| R-06 | Administrar impresoras y asociarlas a una tienda | `PrintersController.cs`; `ImpresorasController.php` | Integración API y vistas parciales | AUD-07 | Parcial; Store 0 no válido |
| R-07 | Crear reglas de enrutado coherentes con la tienda | `RoutingRulesController.cs`; `RoutingResolver.cs` | Tests de prioridad/resolución | AUD-08 | Defectuoso: permite impresora de otra tienda |
| R-08 | Ingerir cada trabajo del origen una sola vez sin perderlo | `IngestionService.cs`; `SapHanaJobSourceAdapter.cs` | Duplicados y adaptador parcial | AUD-09 | Probablemente defectuoso ante error DB no único |
| R-09 | Reclamar el origen de forma segura entre workers | `SapHanaJobSourceAdapter.cs`; `WorkerLockCoordinator.cs` | Pruebas SQLite/dobles | AUD-10 | Parcial; HANA real no verificado |
| R-10 | Enviar un PDF a la impresora sin duplicados silenciosos | `PrintExecutionService.cs`; `WindowsPrintSpooler.cs` | Spooler falso y estados | AUD-10 | No garantizado ante crash/cancelación |
| R-11 | Confirmar el resultado físico del trabajo | `SpoolAcceptedWatchdogBackgroundService.cs` | Pruebas de estados IPP parciales | AUD-11 | Semántica defectuosa: `idle` no identifica el job |
| R-12 | Cancelar/reintentar solo estados seguros | `PrintJobsController.cs`; `SourcePrintJobsController.cs`; `ColaController.php` | Pruebas de estados | AUD-10 | Parcial; cancelación de pruebas incluye `Printing` |
| R-13 | Conservar trazabilidad de estados sin sobrescrituras | `PrintJobEvent`; `RowVersion`; `ImpresorasDbContext.cs` | Concurrencia en memoria/SQLite | AUD-12 | Defectuoso en concurrencia real |
| R-14 | Minimizar/retirar el PDF tras entregarlo al spooler (requisito explícito en SQL) | `migrate_pdf_blob_nullable.sql`; `PrintExecutionService.cs` | No localizada | AUD-13 | Defectuoso: el blob no se limpia |
| R-15 | Mostrar KPIs correctos por ventana y tienda | `Api/DashboardController.cs`; `Web.PHP/DashboardController.php`; contrato KPI | Tests API/PHP de KPI | AUD-19, AUD-21 | Parcial; fallback puede mostrar ceros engañosos |
| R-16 | Alertar por Telegram sin perder alertas | `StoreHealthAlertBackgroundService.cs`; `TelegramNotifierService.cs` | Evaluador probado; envío parcial | AUD-14 | Defectuoso ante fallo externo |
| R-17 | Configurar umbrales compartidos por API y Worker | `DashboardThresholdRuleStore.cs`; dashboard PHP | Tests de reglas parciales | AUD-20 | Riesgo de lectura parcial/divergencia multi-host |
| R-18 | Borrar/purgar una tienda y sus datos de forma coherente | `StoresController.cs` | Pruebas CRUD parciales | AUD-15 | Parcial; quedan asociaciones/historial ambiguo |
| R-19 | Exponer la API solo por canal y cuenta de servicio seguros | `install-windows-services.ps1`; Nginx de referencia | No | AUD-16 | Potencialmente inseguro por defecto |
| R-20 | Limitar abuso del login por origen sin afectar a todos | `Program.cs:164-180` | No localizada | AUD-05 | Defectuoso: bucket global |
| R-21 | Mantener dependencias sin vulnerabilidades conocidas relevantes | `.csproj`, `composer.lock`, `package-lock.json` | Auditorías manuales ejecutadas | AUD-02 | Defectuoso en lockfiles actuales |
| R-22 | Registrar y detectar fallos operativos | logs .NET/PHP, `/health`, Telegram | No hay test de alertado end-to-end | AUD-14, AUD-22 | Insuficiente |
| R-23 | Recuperar servicio y datos tras desastre | scripts/docs de despliegue | No | AUD-22 | No verificable; runbook ausente |
| R-24 | Interfaz accesible y contratos UI estables | Blade/CSS/JS; componentes de acción | 12 pruebas PHP; sin E2E/a11y | AUD-21, AUD-23 | Parcial; una regresión de test y riesgos no ejercitados |
| R-25 | Mantener una única definición de reglas de negocio | evaluador de salud, predicados KPI, controladores dashboard | Tests de contrato parciales | AUD-20 | Parcial; queda duplicación PHP/API |

## Flujos críticos reconstruidos

### Ingesta e impresión

`SAP origen → claim/lease → PrintJob+evento → ACK origen → regla → Routed → Printing → Sumatra/spooler → SpoolAccepted → IPP → PrintedConfirmed/Unknown/Error`.

Puntos no demostrados en el entorno auditado: atomicidad del claim HANA, SQL generado, spooler real, identidad de job IPP, comportamiento ante reinicio exacto y recuperación de leases.

### Administración

`Navegador → validación Laravel → ApiClient/JWT → política ASP.NET → EF/HANA → mensaje/redirect`. La validación de seguridad debe residir en API/BD; se encontraron contratos que dependen o divergen entre Laravel y API (`StoreId = 0`).

### Dashboard y alertas

`PrintJobs/Eventos/Impresoras → agregación API → Laravel`. Si falla el overview, Laravel reconstruye una aproximación con listados limitados. El Worker calcula salud por separado y persiste estado antes de llamar a Telegram.

## Vacíos de trazabilidad

- No hay definición aprobada de si `StoreId = 0` es válido en todo el dominio o solo una representación histórica.
- No hay política formal “duplicado frente a pérdida” cuando el resultado físico es ambiguo.
- No hay SLO/volúmenes previstos, RPO/RTO, periodo de retención del PDF ni clasificación de datos.
- No hay contrato formal de aislamiento por tienda ni matriz de permisos por endpoint.
- No hay evidencia de aceptación sobre qué significa “impreso”: aceptado por spooler, terminado por impresora o verificado por trabajo IPP.

