# Despliegue del frontend React

Frontend de `src/ImpresorasService.Web` (B7 de `docs/PLAN.md`). Son ficheros estáticos que sirve
nginx; no hay proceso propio. Convive con el PHP hasta que este se retire: cada uno en su nombre de
servidor, contra el mismo Kestrel.

## Construir

```bash
cd src/ImpresorasService.Web
npm ci
npm test && npm run build      # deja la web en dist/
```

Copiar el contenido de `dist/` a la ruta `root` de nginx (en el ejemplo, `/var/www/impresion-web`).
No lleva configuración ni secretos: la misma `dist/` vale para cualquier entorno.

## nginx

Referencia: `docs/nginx/impresion-web.conf`. Lo que importa:

| Qué | Por qué |
| --- | --- |
| HTTPS obligatorio | La cookie de sesión (`impresoras_sesion`) es `Secure` fuera de Development |
| Mismo origen para web y API | La cookie es `SameSite=Strict` y `Path=/api`; sin CORS |
| `location /api/impresion/` → `proxy_pass http://127.0.0.1:5105/api/;` | El módulo pide `/api/impresion/…` (como a la pasarela del ERP) y Kestrel atiende `/api/…`. La barra final de `proxy_pass` quita `/impresion` |
| `try_files $uri /index.html` | Aplicación de una sola página |
| `assets/` con caché inmutable e `index.html` con `no-cache` | Los ficheros de `assets/` llevan huella en el nombre; una versión nueva se ve al recargar |

Kestrel no cambia: la misma API sirve al PHP (token en cabecera) y a esta web (token en la cookie).

**Límite de intentos de entrada**: la API cuenta por IP y detrás de nginx todas las peticiones
llegan desde la suya. Los 10 intentos por minuto de `/api/auth/login` son, en la práctica, para
todas las tiendas a la vez, como ya pasa con el PHP. `me` y `logout` no cuentan.

## Comprobar

1. `sudo nginx -t` y `sudo systemctl reload nginx`.
2. `curl -I https://impresion.empresa.com/` → `200`.
3. `curl -s -o /dev/null -w "%{http_code}" https://impresion.empresa.com/api/impresion/auth/me` →
   `401` (la ruta llega a la API y no hay sesión).
4. Entrar con un usuario: la respuesta de `POST /api/impresion/auth/login` trae
   `Set-Cookie: impresoras_sesion=…; path=/api; secure; samesite=strict; httponly`.
5. Abrir Cola de impresión e Impresoras.
