# Despliegue Frontend Laravel

## Variables de entorno

| Variable | Descripción | Ejemplo |
|----------|-------------|---------|
| `API_URL` | URL **directa** de la API .NET (Kestrel) | `http://127.0.0.1:5105` |
| `APP_URL` | URL pública del frontend Laravel | `https://app.empresa.com` |

### Producción recomendada (Laravel)

```env
APP_ENV=production
APP_DEBUG=false
APP_URL=https://app.empresa.com

API_URL=http://127.0.0.1:5105

SESSION_ENCRYPT=true
SESSION_SECURE_COOKIE=true
```

> **Importante:** `API_URL` debe apuntar al proceso Kestrel en el mismo servidor (o red interna), **no** al prefijo `/api` de Nginx. Laravel llama a rutas como `api/printjobs` sobre esa base.

## Desarrollo local

1. API .NET: `dotnet run --project src/ImpresorasService.Api` (puerto 5105)
2. Laravel: `cd src/ImpresorasService.Web.PHP && php artisan serve` (puerto 8000)
3. Acceder a http://localhost:8000

## Producción (Nginx)

Ejemplo de configuración para mismo dominio (UI pública + proxy opcional de API):

```nginx
server {
    listen 80;
    server_name app.empresa.com;

    # Frontend Laravel (PHP)
    location / {
        root /var/www/impresoras-service/src/ImpresorasService.Web.PHP/public;
        try_files $uri $uri/ /index.php?$query_string;
        location ~ \.php$ {
            fastcgi_pass unix:/var/run/php/php8.2-fpm.sock;
            fastcgi_param SCRIPT_FILENAME $document_root$fastcgi_script_name;
            include fastcgi_params;
        }
    }

    # API .NET (proxy opcional para clientes externos; sin barra final en proxy_pass)
    location /api/ {
        proxy_pass http://127.0.0.1:5105;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
    }
}
```

Archivo de referencia en repo:
- `docs/nginx/impresoras-service.conf`

### Nginx vs `API_URL`

| Componente | Cómo llega a la API |
|------------|---------------------|
| **Laravel (BFF)** | `API_URL` → `http://127.0.0.1:5105` (directo a Kestrel) |
| **Clientes vía Nginx** | `https://app.empresa.com/api/...` → proxy a Kestrel |
| **Health check** | `GET http://127.0.0.1:5105/health` (no está bajo `/api` en Kestrel) |

## Validación operativa del reverse proxy (QA/PROD)

Checklist recomendado para cerrar validación real en entorno con Nginx:

1. Validar sintaxis:
   - `sudo nginx -t`
2. Aplicar config:
   - `sudo systemctl reload nginx`
3. Comprobar frontend por `/`:
   - `curl -I http://app.empresa.com/`
   - Esperado: `200` o `302` a `/login`.
4. Comprobar API (directo en el servidor):
   - `curl -s http://127.0.0.1:5105/health`
   - Esperado: JSON con `status` y comprobación de base de datos.
5. Comprobar proxy Nginx (si se expone `/api/`):
   - `curl -I http://app.empresa.com/api/printjobs`
   - Esperado: `401` sin token (la ruta existe).
6. Prueba funcional mínima:
   - Login en `/login`.
   - Navegar a `Dashboard`, `Cola`, `Impresoras`, `Alertas`.
   - Crear trabajo de prueba y validar que aparece en cola.

Evidencias sugeridas para auditoría:
- Salida de `nginx -t`.
- Respuesta de `curl http://127.0.0.1:5105/health`.
- Capturas de flujo login + navegación + acción de prueba.

## Requisitos

- PHP 8.2+
- Composer
- Extensión PHP: curl, json, mbstring, openssl, pdo, tokenizer, xml
