# Sprint 1 - Guia de entorno y smoke test (Windows/PowerShell)

Este documento permite validar rapidamente los cambios de hardening del Sprint 1:

- secretos fuera de `appsettings`,
- seed por defecto desactivado,
- API/Worker arrancando con configuracion segura,
- login/token operativo.

## 1) Variables de entorno requeridas

> Ejecutar en PowerShell en la misma terminal donde se arrancara API/Worker.

```powershell
$env:Database__Provider = "Sqlite"
$env:ConnectionStrings__PrintQueue = "Data Source=impresoras-smoke-s1.db"
$env:Source__Mode = "SqlTest"
$env:Jwt__Secret = "Impresoras_2026_S3guro_MuyLargo_123456"
$env:Bootstrap__SeedDefaultUsers = "false"
```

Notas:
- `Jwt__Secret` debe tener al menos 32 caracteres.
- No usar valores triviales (`ChangeMe123`, `changeme`, etc.).
- Para entorno persistente, mover estas variables a configuracion de sistema/CI-CD (no terminal temporal).

## 2) Arranque de servicios

Desde la raiz `ImpresorasServiceV1`:

```powershell
dotnet run --project "src/ImpresorasService.Api"
```

En otra terminal (repitiendo variables de entorno):

```powershell
dotnet run --project "src/ImpresorasService.Worker"
```

## 3) Smoke test rapido

## 3.1 Health de API

```powershell
Invoke-RestMethod "https://localhost:5001/health"
```

Debe devolver estado `ok` (el puerto puede variar segun `launchSettings`).

## 3.2 Login / token (sin seed automatico)

Si no hay usuarios creados manualmente en BD, el login debe fallar con 401.
Esto confirma que el seed por defecto no esta activo.

```powershell
$body = @{
  login = "admin"
  password = "admin123"
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
  -Uri "https://localhost:5001/api/auth/token" `
  -ContentType "application/json" `
  -Body $body
```

Resultado esperado:
- **401** si no existe usuario (comportamiento correcto con hardening).
- **200** solo si el usuario fue provisionado por proceso explicito.

## 3.3 Verificacion de secreto obligatorio

Para comprobar el guard de seguridad:

```powershell
Remove-Item Env:\Jwt__Secret
dotnet run --project "src/ImpresorasService.Api"
```

Resultado esperado:
- La API no arranca y muestra error de configuracion de `Jwt:Secret`.

## 4) Checklist de validacion Sprint 1

- [ ] API no arranca sin `Jwt__Secret` valido.
- [ ] No hay secretos en `appsettings*.json` (valores vacios).
- [ ] `Bootstrap__SeedDefaultUsers=false`.
- [ ] Login con credenciales por defecto no funciona salvo provision explicita.
- [ ] Worker arranca con variables externas.
- [ ] No se exponen rutas de ficheros temporales en logs de spooler.

## 5) Siguiente paso recomendado

Tras pasar este smoke test, continuar con Sprint 2:
- coherencia de KPIs entre API y PHP,
- transaccionalidad en hard delete de tiendas.
