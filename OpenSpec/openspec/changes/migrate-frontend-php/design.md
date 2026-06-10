## Context

El proyecto ImpresorasService tiene una API .NET (puerto 5105) y un frontend Blazor Server (puerto 5106). La API expone endpoints para auth, print jobs, impresoras, reglas, etc. La migración sustituye Blazor por Laravel, manteniendo la API como fuente de verdad. El usuario tiene licencia Metronic y templates en `ImpresorasServiceV1/templates/`. Logo corporativo: ad SERCA (azul oscuro, blanco, rojo).

## Goals / Non-Goals

**Goals:**
- Frontend Laravel con sesión PHP + token para llamadas a la API.
- Layout Metronic con paleta SERCA, logo + texto "Impresoras Service" en sidebar.
- Tema claro/oscuro con toggle persistente en `localStorage`.
- Mismo dominio, mismo repo, mismo servidor.
- Uso selectivo de templates (sin clonar todo).
- Implementar pantallas: Login, Dashboard, Cola, Impresoras, Reglas, Alertas, Prueba.

**Non-Goals:**
- No cambiar la API más allá de token y Bearer auth.
- No soportar AD Negotiate en esta fase (queda para futura iteración).
- No mantener Blazor en paralelo tras validación.

## Decisions

### 1. Framework y base: Laravel + Metronic
- **Laravel:** framework PHP, sesiones, Blade, routing.
- **Metronic:** templates en `templates/dist/`; se copian solo layout, CSS themes y assets necesarios.
- **Alternativa considerada:** Sneat (Bootstrap 5). Descartado: Metronic ya disponible y con licencia.

### 2. Autenticación: sesión PHP + token en sesión
- Login: formulario POST → Laravel valida contra `POST /api/auth/login` (o `POST /api/auth/token`).
- API devuelve `{ token, expiresAt, user }`.
- Laravel guarda `token` y `user` en `$_SESSION`.
- Cada llamada a la API: `Authorization: Bearer {token}`.
- **Alternativa:** JWT en cookie HttpOnly. Descartado: sesión PHP más simple para intranet.

### 3. Token en API: JWT
- API genera JWT con claims: `UserId`, `Login`, `Role`, `StoreId`, `exp`.
- Expiración: 8 horas (alineado con sesión actual).
- **Alternativa:** token opaco en DB. Descartado: JWT estándar, sin estado en API.

### 4. Ubicación del proyecto: `ImpresorasServiceV1/src/ImpresorasService.Web.PHP/`
- Mismo repo, carpeta hermana de API y Web.
- **Alternativa:** repo separado. Descartado: mayor complejidad de despliegue.

### 5. Uso de templates: selectivo
- Copiar de `templates/dist/`: layout HTML, `assets/css/themes/layout/` (light/dark), `assets/css/style.bundle.css`, `assets/js/plugins.bundle.js`, `assets/media/`.
- Sobrescribir variables CSS con paleta SERCA.
- Crear vistas Blade que extiendan el layout.
- **No clonar:** solo lo necesario.

### 6. Paleta SERCA
- Primary: `#1a237e` (azul oscuro).
- Accent: `#c62828` (rojo).
- Neutral: `#78909c` (gris metálico).
- Tema claro: fondo `#f5f5f5`, texto `#263238`.
- Tema oscuro: fondo `#0d1b4d`, contenido `#1a237e`, texto `#ffffff`.

### 7. Toggle tema: `localStorage`
- Clave: `theme` (valores: `light`, `dark`).
- Persiste entre sesiones.
- **Alternativa:** solo sesión. Descartado: `localStorage` mejora UX.

### 8. Despliegue: reverse proxy
- Nginx: `/` → PHP-FPM (Laravel), `/api` → Kestrel (API .NET).
- Mismo dominio: `app.empresa.com` o `localhost:port`.

### 9. Fuente: Poppins
- Mantiene Metronic y legibilidad.

## Risks / Trade-offs

| Riesgo | Mitigación |
|--------|------------|
| JWT mal configurado | Probar en DEV; validar exp y claims. |
| CORS bloqueando requests | Definir orígenes permitidos explícitamente. |
| Sesión PHP vs JWT expirado | Redirigir a login si API devuelve 401; limpiar sesión. |
| Rollback | Mantener Blazor desplegado hasta validar PHP, o tener plan de vuelta atrás. |
| Metronic y paths | Verificar que los assets copiados tengan las rutas correctas relativas al public. |

## Migration Plan

1. **Fase 1 – Preparación API:** Añadir endpoint token, Bearer auth, CORS.
2. **Fase 2 – Proyecto Laravel:** Crear proyecto, configurar auth, middleware, cliente HTTP.
3. **Fase 3 – Layout y assets:** Copiar layout Metronic, aplicar paleta SERCA, logo, toggle tema.
4. **Fase 4 – Pantallas:** Login, Dashboard, Cola, Impresoras, Reglas, Alertas, Prueba.
5. **Fase 5 – Validación:** Pruebas funcionales, comparación con Blazor.
6. **Fase 6 – Retirada:** Eliminar Blazor, actualizar configuración de despliegue.

**Rollback:** Si falla, mantener Blazor y revertir cambios en Nginx/routing.

## Open Questions

- Ninguna: decisiones cerradas en sesión de exploración.
