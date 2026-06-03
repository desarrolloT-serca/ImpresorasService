## Why

El frontend Blazor Server actual presenta fricciones operativas (formularios, flujos de autenticación) y el equipo prefiere PHP con un framework maduro para el panel de operación. Migrar a Laravel con Metronic permite un diseño más sólido, formularios nativos y mejor experiencia de desarrollo.

## What Changes

### 1) Sustitución del frontend Blazor por Laravel
- Nuevo proyecto Laravel dentro del mismo repo (`ImpresorasServiceV1/src/ImpresorasService.Web.PHP/`).
- Uso selectivo de templates Metronic existentes en `ImpresorasServiceV1/templates/`.
- Paleta de colores SERCA (azul `#1a237e`, rojo `#c62828`) y logo corporativo.
- Tema claro/oscuro con toggle persistente en `localStorage`.

### 2) Autenticación sesión PHP + token API
- Login vía formulario POST que valida contra la API .NET.
- API devuelve token (JWT u opaco) que Laravel guarda en sesión.
- Laravel llama a la API con `Authorization: Bearer <token>` en cada petición.
- El usuario no gestiona tokens; la sesión PHP es transparente.

### 3) Cambios en la API .NET
- Nuevo endpoint o extensión de `/api/auth/login` para devolver token.
- Endpoints existentes aceptan `Authorization: Bearer` además de cookies.
- CORS configurado para el origen del frontend PHP (mismo dominio).

### 4) Despliegue unificado
- Mismo servidor: reverse proxy (Nginx) enruta `/` a PHP y `/api` a .NET.
- Mismo repo, mismo entorno (DEV, QA, PROD).

### 5) Retirada de Blazor
- Blazor se elimina una vez validada la migración.
- Sin coexistencia prolongada de ambos frontends.

## Capabilities

### New Capabilities
- `php-frontend`: panel web Laravel que consume la API .NET, con login por sesión, layout Metronic, paleta SERCA y tema claro/oscuro.

### Modified Capabilities
- `sap-printing-service`: la operación web pasa de Blazor a Laravel; los requisitos funcionales (cola, impresoras, reglas, alertas, auditoría) se mantienen, cambia solo la implementación del frontend.

## Impact

### New Files
- `openspec/changes/migrate-frontend-php/proposal.md`
- `openspec/changes/migrate-frontend-php/design.md`
- `openspec/changes/migrate-frontend-php/specs/php-frontend/spec.md`
- `openspec/changes/migrate-frontend-php/tasks.md`
- `ImpresorasServiceV1/src/ImpresorasService.Web.PHP/` (proyecto Laravel completo)

### Modified Files
- `ImpresorasServiceV1/src/ImpresorasService.Api/` (endpoint token, Bearer auth, CORS)
- Configuración de despliegue (Nginx, variables de entorno)

### Removed
- `ImpresorasServiceV1/src/ImpresorasService.Web/` (Blazor) tras validación
