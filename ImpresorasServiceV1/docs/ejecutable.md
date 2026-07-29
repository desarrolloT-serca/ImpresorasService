# Ejecutable e instalación como servicio Windows (Api + Worker)

Guía de uso de `scripts/install-windows-services.ps1`: publica `ImpresorasService.Api` y
`ImpresorasService.Worker` como ejecutables self-contained y los instala/actualiza como
servicios Windows (`ImpresorasServiceApi`, `ImpresorasServiceWorker`).

No cubre el frontend Laravel (`ImpresorasService.Web.PHP`) ni SAP HANA/impresoras en sí — ver
`docs/DESPLIEGUE-PHP.md` para el frontend.

---

## 1. Software que debe estar instalado en el servidor destino

| Requisito | Por qué | Cómo verificar |
|---|---|---|
| Windows 10/Server 2016+ | Necesario para `sc`/SCM, `New-Service`, `Print Operators` | — |
| **Driver SAP HANA ODBC (HDBODBC)** | `SapHana:ConnectionString` usa `Driver=HDBODBC`; sin él, la Api/Worker no arrancan | `Get-OdbcDriver \| Where-Object Name -like "*HDBODBC*"` |
| Resolución de `hanab1` (o el host HANA real) | Api/Worker conectan a HANA en el arranque | `Resolve-DnsName hanab1` (o usa `-HanaIp` al lanzar el script) |
| **SumatraPDF** en `C:\Program Files\SumatraPDF\SumatraPDF.exe` | El Worker lo invoca para imprimir (`PrintExecution:PdfPrinterExecutablePath`) | Comprobar que el .exe existe en esa ruta, o cambiar la ruta en `C:\ImpresorasService\Worker\appsettings.json` |
| `C:\ImpresorasService\dashboard-threshold-rules.json` (G5.3) | Reglas de severidad de dashboard (1-3 niveles), compartidas por Api y Worker (`Dashboard:ThresholdRulesFilePath` en ambos `appsettings.json`) — si falta, se usan valores por defecto embebidos, no hace falta crearlo a mano | Se crea solo al guardar umbrales desde `/ajustes`; para verificar que Api y Worker comparten el mismo fichero, confirma que ambos `appsettings.json` apuntan a la misma ruta |
| Impresoras instaladas en Windows con el mismo nombre que `spool_queue` (tabla `printer_printer`) | El Worker imprime contra la cola de Windows por nombre exacto | `Get-Printer \| Select Name` |
| Cuenta de servicio en el grupo local **Print Operators** (si usas `-ServiceAccount`) | Necesario para que el proceso pueda hablar con el spooler de Windows | El script lo intenta automáticamente vía `Add-LocalGroupMember` |
| .NET Runtime | **No hace falta** — el publish es self-contained (incluye su propio runtime) | — |

> No uses `-p:PublishSingleFile` ni `-p:PublishTrimmed` si algún día tocas el publish a mano.
> El driver `Sap.Data.Hana` localiza su DLL nativa (`libadonetHDB.dll`) vía `Assembly.Location`,
> que con single-file queda vacío y revienta con `ArgumentNullException` al primer intento de
> conexión. El trimming rompe la carga por reflection de `ImpresorasService.Core/Infrastructure/DependencyInjection.cs`
> (`ConfigureHanaProvider`). El script ya publica sin ninguna de las dos.

---

## 2. Ejecutar el script

Siempre en **PowerShell como Administrador**, desde la raíz del repo (`ImpresorasServiceV1`).

### Primera vez en una máquina nueva

```powershell
.\scripts\install-windows-services.ps1 -HanaUser IMPRESION
```

Te pedirá el **password de HANA** de forma interactiva (input oculto, no queda en el historial
de comandos ni en ningún archivo de texto). Con eso:

1. Publica Api y Worker en `C:\ImpresorasService\Api` y `\Worker`.
2. Crea los dos servicios Windows (`Automatic`, arrancan solos en cada reinicio).
3. Guarda las credenciales en el registro **de cada servicio** (no en variables de entorno de
   máquina — ver la sección 4 para el porqué).
4. Arranca ambos servicios.

### Pulls posteriores (actualizar binarios)

```powershell
.\scripts\install-windows-services.ps1
```

No pide nada — reutiliza las credenciales ya guardadas, para los servicios, republica y los
vuelve a arrancar.

### Si `hanab1` no resuelve por DNS en esa red

```powershell
.\scripts\install-windows-services.ps1 -HanaUser IMPRESION -HanaIp 192.168.1.20
```

### Si el frontend PHP corre en otra máquina (necesita CORS)

```powershell
.\scripts\install-windows-services.ps1 -FrontendOrigin "http://192.168.1.50:8000"
```

### Cambiar credenciales ya guardadas

```powershell
.\scripts\install-windows-services.ps1 -Reconfigure -HanaUser IMPRESION
```

### Activar alertas de Telegram (opcional)

```powershell
.\scripts\install-windows-services.ps1 -ConfigureTelegram
```

Sin esto, el sistema funciona igual — `TelegramNotifierService` no hace nada si el token está
vacío, no rompe nada.

### Cuenta de servicio dedicada (en vez de LocalSystem)

```powershell
.\scripts\install-windows-services.ps1 -ServiceAccount ".\svc_impresoras"
```

Pide la password de esa cuenta con `Get-Credential` y la mete en el grupo local *Print Operators*.

---

## 3. Referencia de flags

| Flag | Tipo | Default | Para qué |
|---|---|---|---|
| `-InstallDir` | string | `C:\ImpresorasService` | Carpeta de publish/instalación |
| `-HanaHost` | string | `hanab1` | Host del servidor HANA |
| `-HanaPort` | int | `30015` | Puerto HANA |
| `-HanaSchema` | string | `ZTEST_VICENTE_2` | Schema HANA |
| `-HanaUser` | string | (vacío, pide) | Usuario HANA (UID) |
| `-HanaIp` | string | (vacío) | Si se indica, añade `IP hanab1` a `hosts` (solo si no existe ya) |
| `-FrontendOrigin` | string | (vacío) | Origen a añadir a `Cors:AllowedOrigins` de la Api publicada |
| `-ServiceAccount` | string | (vacío = LocalSystem) | Cuenta bajo la que corren los servicios, ej. `.\svc_impresoras` |
| `-Reconfigure` | switch | off | Fuerza volver a pedir/regenerar HANA + Jwt secret aunque ya existan |
| `-SkipPublish` | switch | off | No recompila/republica, solo (re)instala/arranca servicios con los binarios ya presentes |
| `-ConfigureTelegram` | switch | off | Pide/actualiza el Bot Token de Telegram |

---

## 4. Dónde y cómo se guardan las credenciales

En **`HKLM:\SYSTEM\CurrentControlSet\Services\<NombreServicio>\Environment`** (un valor
`REG_MULTI_SZ` por servicio), no como variables de entorno de máquina.

**Por qué no variables de máquina:** `[Environment]::SetEnvironmentVariable($x, $y, "Machine")`
escribe en el registro pero el *Service Control Manager* de Windows cachea su propio entorno al
arrancar el sistema — un servicio creado y arrancado en la misma sesión **no ve** una variable de
máquina nueva hasta reiniciar el equipo. Se confirmó este fallo en pruebas reales: el servicio
fallaba con `Jwt:Secret es obligatorio...` aunque la variable de máquina ya estaba puesta.

**Por qué registro por servicio:** el SCM relee esa clave cada vez que arranca *ese* servicio
concreto, sin reinicios, y además aísla el secreto — no queda visible en el entorno global de la
máquina para cualquier proceso que lo consulte.

Variables que gestiona el script:

| Variable | Servicios | Contenido |
|---|---|---|
| `ConnectionStrings__PrintQueue` | Api + Worker | Cadena EF hacia HANA |
| `SapHana__ConnectionString` | Api + Worker | Cadena ODBC cruda (usada también por `/diagnostics/hana`) |
| `Jwt__Secret` | Api | Generado aleatoriamente (48 bytes, base64) si no existe |
| `ASPNETCORE_URLS` | Api | Fijo a `http://+:5105` — sin esto, Kestrel cae al puerto 5000 por defecto, que en muchos equipos ya está ocupado (en las pruebas, por un servicio de VPN preexistente) |
| `Telegram__BotToken` | Api + Worker | Vacío si no se usó `-ConfigureTelegram` |

Para verlas manualmente (necesita admin):

```powershell
(Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\ImpresorasServiceApi" -Name Environment).Environment
```

> `src/ImpresorasService.Api/Properties/launchSettings.json` (y el del Worker) llevan
> credenciales HANA reales en texto plano — son **solo para `dotnet run` en local** y están
> commiteadas en el repo; no las usa el servicio publicado ni este script, pero son un secreto
> expuesto en git que conviene rotar en algún momento (fuera del alcance de este documento).

---

## 5. Comprobar que funciona

```powershell
# Estado de los dos servicios (no requiere admin)
Get-Service ImpresorasServiceApi, ImpresorasServiceWorker

# La Api responde y la BD está sana
Invoke-RestMethod http://localhost:5105/health
# -> {"status":"ok","checks":{"database":"Healthy"}}

# Logs / errores recientes (Api y Worker escriben al Visor de eventos gracias a AddWindowsService())
Get-WinEvent -LogName Application -MaxEvents 20 | Format-Table TimeCreated, ProviderName, Id, Message -Wrap
```

Si un servicio queda en `Stopped` justo después de arrancar, casi siempre es una excepción no
capturada en el arranque (falta de conectividad a HANA, credenciales mal escritas, puerto
ocupado...) — revisa el Visor de eventos, ahí aparece el stack trace completo.

Reiniciar uno solo:

```powershell
Restart-Service ImpresorasServiceApi
```

---

## 6. Desinstalar

```powershell
Stop-Service ImpresorasServiceApi, ImpresorasServiceWorker -Force
sc.exe delete ImpresorasServiceApi
sc.exe delete ImpresorasServiceWorker

# Solo si además configuraste variables de entorno de MAQUINA en pruebas antiguas del script
# (versiones nuevas ya no las usan; las credenciales por servicio desaparecen solas con el servicio)
[Environment]::SetEnvironmentVariable("ConnectionStrings__PrintQueue", $null, "Machine")
[Environment]::SetEnvironmentVariable("SapHana__ConnectionString", $null, "Machine")
[Environment]::SetEnvironmentVariable("Jwt__Secret", $null, "Machine")

Remove-Item -Recurse -Force "C:\ImpresorasService"
```

> Windows PowerShell 5.1 (el que trae Windows por defecto) no tiene el cmdlet `Remove-Service`
> (llegó en PowerShell 6+); por eso se usa `sc.exe delete`.
