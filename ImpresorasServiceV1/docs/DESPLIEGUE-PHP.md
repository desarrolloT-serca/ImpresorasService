# Despliegue Frontend Laravel

## Variables de entorno

| Variable | Descripción | Ejemplo |
|----------|-------------|---------|
| `API_URL` | URL de la API .NET | `http://localhost:5105` |
| `APP_URL` | URL del frontend Laravel | `http://localhost:8000` |

## Desarrollo local

1. API .NET: `dotnet run --project src/ImpresorasService.Api` (puerto 5105)
2. Laravel: `cd src/ImpresorasService.Web.PHP && php artisan serve` (puerto 8000)
3. Acceder a http://localhost:8000

## Producción (Nginx)

Ejemplo de configuración para mismo dominio:

```nginx
server {
    listen 80;
    server_name app.empresa.com;

    # Frontend Laravel (PHP)
    location / {
        root /var/www/impresoras-service/public;
        try_files $uri $uri/ /index.php?$query_string;
        location ~ \.php$ {
            fastcgi_pass unix:/var/run/php/php8.2-fpm.sock;
            fastcgi_param SCRIPT_FILENAME $document_root$fastcgi_script_name;
            include fastcgi_params;
        }
    }

    # API .NET (proxy)
    location /api {
        proxy_pass http://127.0.0.1:5105;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
    }
}
```

Archivo de referencia en repo:
- `docs/nginx/impresoras-service.conf`

## Validación operativa del reverse proxy (QA/PROD)

Checklist recomendado para cerrar validación real en entorno con Nginx:

1. Validar sintaxis:
   - `sudo nginx -t`
2. Aplicar config:
   - `sudo systemctl reload nginx`
3. Comprobar frontend por `/`:
   - `curl -I http://app.empresa.com/`
   - Esperado: `200` o `302` a `/login`.
4. Comprobar API por `/api`:
   - `curl -I http://app.empresa.com/api/health`
   - Esperado: `200`.
5. Prueba funcional mínima:
   - Login en `/login`.
   - Navegar a `Dashboard`, `Cola`, `Impresoras`, `Alertas`.
   - Crear trabajo de prueba y validar que aparece en cola.

Evidencias sugeridas para auditoría:
- Salida de `nginx -t`.
- Cabeceras HTTP de `/` y `/api/health`.
- Capturas de flujo login + navegación + acción de prueba.

## Requisitos

- PHP 8.2+
- Composer
- Extensión PHP: curl, json, mbstring, openssl, pdo, tokenizer, xml
