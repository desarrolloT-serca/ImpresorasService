# ImpresorasService Web PHP (UI oficial)

Este proyecto es el frontend oficial de `ImpresorasServiceV1` en esta fase.
Consume la API .NET (`ImpresorasService.Api`) para operar cola, impresoras, reglas y autenticacion.

## Rol en arquitectura

- UI oficial: `ImpresorasServiceV1/src/ImpresorasService.Web.PHP`
- Backend requerido:
  - `ImpresorasService.Api`
  - `ImpresorasService.Worker`

## Requisitos

- PHP 8.2+
- Composer
- Node.js + npm

## Configuracion

1) Copia variables de entorno:

```bash
cp .env.example .env
```

2) Ajusta la URL de API en `.env`:

```env
API_URL=http://localhost:5105
```

## Arranque local

```bash
composer install
npm install
php artisan key:generate
php artisan serve
```

Opcional para assets en caliente:

```bash
npm run dev
```

## Despliegue de todos los componentes de la app

Este frontend depende del backend completo (`Api` + `Worker`). Para un despliegue/arranque correcto del sistema, levanta los componentes en este orden.

### 1) Backend compartido (.NET)

Desde la raiz de `ImpresorasServiceV1`:

```powershell
dotnet restore
dotnet build -c Debug
```

Terminal 1 (API):

```powershell
dotnet run --project "src/ImpresorasService.Api"
```

Terminal 2 (Worker):

```powershell
dotnet run --project "src/ImpresorasService.Worker"
```

### 2) Frontend oficial (Laravel)

Desde `src/ImpresorasService.Web.PHP`:

```powershell
composer install
npm install
copy .env.example .env
php artisan key:generate
php artisan serve
```

Si quieres assets en caliente:

```powershell
npm run dev
```

### 3) Configuracion de enlace UI -> API

Asegura en `.env`:

```env
API_URL=http://localhost:5105
```

## Producción (checklist mínimo)

```env
APP_ENV=production
APP_DEBUG=false
APP_URL=https://tu-dominio

API_URL=http://127.0.0.1:5105

SESSION_ENCRYPT=true
SESSION_SECURE_COOKIE=true
```

- `API_URL` apunta a Kestrel en la misma máquina (no al proxy Nginx `/api`).
- Health de la API: `curl http://127.0.0.1:5105/health`
- Guía completa: `ImpresorasServiceV1/docs/DESPLIEGUE-PHP.md`

### 4) Verificacion rapida post-despliegue

1. Abre la UI y verifica login.
2. Navega por `dashboard`, `cola`, `impresoras` y `reglas`.
3. Comprueba filtros/acciones basicas sin errores de sesion o API.
4. Si aplica, valida que API y Worker comparten la misma BD local.

## Flujos principales

- Login contra `POST /api/auth/token` (JWT en sesión)
- Dashboard/cola/impresoras/reglas consumiendo endpoints de la API con token Bearer en sesion
- Middleware propio para:
  - `auth.impresoras`
  - `admin.only`

## Nota de cohesion

Esta carpeta es la unica UI web del proyecto.
