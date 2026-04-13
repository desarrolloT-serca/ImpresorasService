# Smoke tests de regresion (PHP + API)

Objetivo: validar de forma rapida que el frontend Laravel y la API .NET siguen operativos tras cambios.

## Prerrequisitos

- API levantada en `http://localhost:5105`
- Frontend PHP levantado en `http://127.0.0.1:8000`
- Credenciales de prueba disponibles (`admin` / `supervisor`)

## 1) Smoke tecnico automatizado

Desde la raiz del repo:

```powershell
dotnet test "ImpresorasServiceV1/tests/ImpresorasService.Api.IntegrationTests/ImpresorasService.Api.IntegrationTests.csproj"
cd "ImpresorasServiceV1/src/ImpresorasService.Web.PHP"
php artisan test
```

Resultado esperado:
- API integration tests en verde.
- Test suite Laravel en verde.

## 2) Smoke funcional manual (5-10 min)

### 2.1 Login y sesion

1. Ir a `http://127.0.0.1:8000/login`
2. Login con `admin`
3. Confirmar redireccion al dashboard
4. Cerrar sesion y comprobar vuelta a `/login`

### 2.2 Navegacion base

Validar acceso sin errores a:
- `/`
- `/cola`
- `/impresoras`
- `/alertas`
- `/prueba` (solo Admin)

### 2.3 Acciones clave

- En `prueba`, crear un trabajo de test.
- En `cola`, validar que el trabajo aparece.
- En `impresoras`, ejecutar ping manual de una impresora.

### 2.4 Roles

- Con usuario `supervisor`, validar que no ve `reglas` ni `prueba`.
- Confirmar que filtros y datos quedan acotados por tienda.

## 3) Criterio de salida

- No hay errores 500 en navegacion principal.
- Login/logout y sesion expirada se comportan como esperado.
- Acciones clave (crear prueba, ver cola, ping) funcionan.
