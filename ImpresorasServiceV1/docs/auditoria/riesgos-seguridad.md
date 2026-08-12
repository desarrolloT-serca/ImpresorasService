# Riesgos de seguridad

**Proyecto/commit:** ImpresorasServiceV1 · `49a0b9691e484472fb1da23417de172f1e60473f`  
**Fecha:** 2026-07-29  
**Método:** revisión estática, tests locales y auditoría de lockfiles. No se efectuó pentest ni contacto con sistemas externos.

## Evaluación

Riesgo de seguridad global: **alto** hasta revocar el secreto versionado y actualizar dependencias. No se confirmó ejecución remota, SQL injection ni acceso anónimo administrativo. El aislamiento por tienda es un control funcional importante, pero su contrato no es uniforme.

| ID | Riesgo | Estado | Gravedad | OWASP/CWE aproximado | Acción |
| --- | --- | --- | --- | --- | --- |
| SEC-01 | Token real de Telegram en árbol e historial Git | Confirmado | Alta | A02/A07; CWE-798 | Revocar, sustituir, retirar y activar secret scanning |
| SEC-02 | Dependencias Composer con 16 avisos/4 paquetes | Confirmado en lockfile; explotación no demostrada | Alta | A06 | Actualizar Laravel ≥12.61.1, Guzzle ≥7.15.1, PSR-7 ≥2.12.3, CommonMark ≥2.8.2 |
| SEC-03 | Democión del último Admin | Confirmado | Alta | A01; CWE-284 | Invariante transaccional compartida entre update/delete |
| SEC-04 | Ámbito de tienda falla en abierto sin claim | Probable exposición bajo identidad corrupta/antigua | Media | A01; CWE-862/639 | Requerir `StoreId` para roles de tienda y filtrar siempre |
| SEC-05 | JWT de 8 h no se revoca al desactivar/cambiar rol | Confirmado | Media | A07; CWE-613 | Token version/estado activo o vida corta+refresh revocable |
| SEC-06 | Sesión Laravel no regenera al login ni invalida al logout | Confirmado | Media | A07; CWE-384/613 | `regenerate`, `invalidate`, regenerar CSRF y limpiar ámbito |
| SEC-07 | Rate limit de login global, no por origen | Confirmado | Media | A07; CWE-770 | Limiter particionado IP+login con límites y telemetría |
| SEC-08 | API instalada en HTTP sobre todas las interfaces | Potencial; depende de firewall/proxy | Media | A02; CWE-319 | Loopback o TLS/mTLS; regla de firewall explícita |
| SEC-09 | API y Worker usan LocalSystem por defecto | Confirmado en instalador | Media | Mínimo privilegio; CWE-250 | Cuentas separadas de bajo privilegio |
| SEC-10 | Sin CSP/HSTS/Permissions-Policy en aplicación de referencia | Confirmado en código/config; proxy real no visto | Baja/Media | A05; CWE-693 | Cabeceras en proxy HTTPS y CSP por fases |
| SEC-11 | Detalle de excepción HANA se devuelve al Admin | Confirmado | Baja | A05; CWE-209 | Correlation ID al cliente; detalle solo en log protegido |
| SEC-12 | Posible HTML almacenado en tabla de pruebas por `innerHTML` | Potencial, requiere ID controlado vía API por Admin | Baja | A03; CWE-79 | Crear nodos/textContent o escapar |
| SEC-13 | SQLite nativo vulnerable solo en tests | Confirmado en dependencia transitiva | Baja para producción | A06; CVE-2025-6965 | Aislar CI y actualizar cuando exista cadena corregida |

## Evidencias clave

### Secreto versionado

`docs/auditoriaimpresoras.md:153` contiene una credencial con formato de bot de Telegram completa. Se verificó sin reproducirla:

- longitud: 46;
- fingerprint SHA-256 (12 caracteres): `D751A9E3FFB7`;
- 13 ocurrencias históricas de un único token;
- primera aparición: 2026-06-29;
- sigue presente en el commit auditado.

El documento afirma que el token ya no está en el árbol, pero lo vuelve a publicar como evidencia. No se probó si sigue activo.

### Dependencias

`composer audit --locked` devolvió exit code 1 y 16 avisos:

| Paquete bloqueado | Máxima severidad | Versión mínima que cierra los avisos observados | Nota de exposición |
| --- | --- | --- | --- |
| `laravel/framework` 12.53.0 | Alta | 12.61.1 | La regla de email vulnerable puede no usarse hoy; el framework sí está en el camino principal |
| `guzzlehttp/guzzle` 7.10.0 | Media | 7.15.1 | Cliente API interno; varios avisos requieren cookies, redirects o proxy |
| `guzzlehttp/psr7` 2.8.0 | Media | 2.12.3 | Construcción/validación de URI y CRLF |
| `league/commonmark` 2.8.0 | Media | 2.8.2 | Extensiones vulnerables no localizadas en el flujo actual |

El advisory de SQLite afecta a `SQLitePCLRaw.lib.e_sqlite3` 2.1.6, transitivo de `Microsoft.Data.Sqlite` 8.0.15 y usado por pruebas; no se despliega desde los proyectos API/Worker auditados. `npm audit --omit=dev` informó cero vulnerabilidades de producción.

### Autorización y sesión

- `UsersController.cs:108-145` actualiza rol sin reutilizar la protección de último Admin presente en `Delete` (`:149-178`).
- `PrintJobsController.cs:40-43`, `PrintersController.cs:38-40` y `DashboardController.cs:52-67` solo filtran cuando `effectiveStoreId.HasValue`.
- Las mutaciones tienden a cerrar el acceso cuando falta la tienda, por lo que no se generaliza el hallazgo a todas las rutas.
- `AuthController.php:59-69` guarda identidad/ámbito sin regenerar sesión; `:75-78` olvida dos claves sin invalidar la sesión.
- El JWT solo incluye claims y expiración fija de ocho horas; no hay `jti`, refresh, denylist ni comparación con usuario activo/versión.

### Red y privilegios

`scripts/install-windows-services.ps1:41` declara `LocalSystem` como valor por defecto; `:145-161` crea ambos servicios con esa identidad y `:205-215` configura secretos. La API se enlaza a `http://+:5105` (`:209`). `UseHttpsRedirection` no crea por sí mismo un listener TLS.

## Superficie de ataque

- Login Laravel/API y cookies de sesión.
- 59 marcadores de endpoints API y 53 rutas Laravel.
- Operaciones administrativas sobre usuarios, reglas, impresoras, tiendas, Telegram y pruebas.
- Carga/envío de PDF y proceso externo SumatraPDF.
- HANA por credenciales de servicio.
- IPP/TCP hacia hosts configurables de impresora.
- Telegram como tercero.
- Endpoint público `/health` y diagnóstico Admin.
- CI que ejecuta acciones y scripts de dependencias.

## Controles existentes verificados

- JWT con validación de issuer, audience, firma y secreto mínimo; arranque falla con secreto inseguro.
- Roles/policies en controladores; Swagger y bootstrap limitados a Development.
- BCrypt para contraseñas y mensajes de login no enumerativos.
- CSRF y escaping Blade por defecto.
- CORS allowlist con validación de configuración.
- Límite de login existente, aunque global.
- SQL mayoritariamente construido por EF o parámetros.
- Límites de paginación y lotes en varias entradas.
- `X-Frame-Options`, `X-Content-Type-Options` y `Referrer-Policy`.
- Lockfiles y auditorías ejecutables.

## Controles ausentes o incompletos

- Rotación/escaneo de secretos en CI y protección de pushes.
- Revocación de sesión/JWT y autorización central fail-closed por tienda.
- TLS verificable de extremo a extremo.
- Cuenta de servicio mínima y separación API/Worker.
- CSP, HSTS y Permissions-Policy en el despliegue real.
- Límite y validación de PDF uniforme en API.
- Audit trail explícito de acciones administrativas (`CreatedBy` se acepta del cliente en reglas).
- SAST/DAST/SCA como gate, SBOM y firma/pinning SHA de GitHub Actions.
- Runbook de incidente, backup/restore y rotación.

## Secuencia segura de remediación

1. Revocar el bot token desde BotFather, generar otro, actualizar solo el almacén operativo y comprobar envío.
2. Retirar la credencial de documentos; coordinar reescritura histórica solo después de rotar y comunicar el impacto en clones/PR.
3. Actualizar los cuatro paquetes Composer de forma dirigida; ejecutar tests, Vite y smoke de login/API.
4. Corregir invariantes de Admin y tienda en API, con tests negativos.
5. Regenerar/inutilizar sesiones y definir revocación JWT.
6. Cambiar instalación a loopback/TLS y cuentas separadas; verificar desde otro host que 5105 no es accesible.
7. Añadir headers, límites de PDF, secret scanning y matriz automatizada de autorización.

## Referencias

- [Laravel CRLF en regla de email, GHSA-5vg9-5847-vvmq](https://github.com/advisories/GHSA-5vg9-5847-vvmq)
- [Guzzle disclosure en Referer, GHSA-h95v-h523-3mw8](https://github.com/advisories/GHSA-h95v-h523-3mw8)
- [PSR-7 host confusion, GHSA-c2w2-prh8-qm98](https://github.com/advisories/GHSA-c2w2-prh8-qm98)
- [CommonMark allowed-domains bypass, GHSA-hh8v-hgvp-g3f5](https://github.com/advisories/GHSA-hh8v-hgvp-g3f5)
- [SQLite memory corruption, GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)

