# Publica ImpresorasService.Api e ImpresorasService.Worker como ejecutables
# self-contained e instala/actualiza ambos como servicios Windows.
#
# Las credenciales (HANA, Jwt secret) se guardan en el registro propio de cada
# servicio (HKLM:\SYSTEM\CurrentControlSet\Services\<Nombre>\Environment), NO como
# variables de entorno de maquina: las de maquina solo las heredan procesos nuevos
# tras un reinicio (el SCM cachea su entorno al arrancar Windows), las de servicio
# las relee el SCM cada vez que arranca ESE servicio, sin reiniciar nada.
#
# Uso (elevado, como Administrador):
#   .\scripts\install-windows-services.ps1 -HanaUser IMPRESION
#     -> primera vez: pide password de HANA (no se guarda en ningun archivo de texto)
#
#   .\scripts\install-windows-services.ps1
#     -> en un pull posterior: reutiliza las credenciales ya guardadas en el servicio,
#        solo republica los binarios y reinicia los servicios.
#
#   .\scripts\install-windows-services.ps1 -Reconfigure -HanaUser IMPRESION -FrontendOrigin "http://192.168.1.50:8000"
#     -> fuerza volver a pedir credenciales y añade un origen CORS para un frontend en otra IP.
#
#   .\scripts\install-windows-services.ps1 -ConfigureTelegram
#     -> activa/actualiza las alertas de Telegram (opcional; sin token, TelegramNotifierService
#        no-opea solo, no rompe nada).
#
# NOTA: no añadir -p:PublishTrimmed. SapHanaJobSourceAdapter/DependencyInjection.cs cargan el
# provider EF de HANA vía Assembly.Load + reflection; el trimming lo rompe.
#
# NOTA: no añadir -p:PublishSingleFile. Sap.Data.Hana.HanaUnmanagedDll.SearchNativeDlls()
# localiza libadonetHDB.dll vía Assembly.Location, que con single-file queda vacío y
# revienta con "ArgumentNullException: Value cannot be null. (Parameter 'path1')" al
# primer intento de conexión. Confirmado en pruebas reales (07/2026).

param(
    [string]$InstallDir        = "C:\ImpresorasService",
    [string]$HanaHost          = "hanab1",
    [int]   $HanaPort          = 30015,
    [string]$HanaSchema        = "ZTEST_VICENTE_2",
    [string]$HanaUser          = "",
    [string]$HanaIp            = "",            # si hanab1 no resuelve por DNS, añade la entrada a hosts
    [string]$FrontendOrigin    = "",             # ej. http://192.168.1.50:8000 (se añade a Cors:AllowedOrigins de la Api)
    [string]$ServiceAccount    = "",             # ej. ".\svc_impresoras"; vacio = LocalSystem
    [switch]$Reconfigure,                        # fuerza repedir/regenerar credenciales aunque ya existan
    [switch]$SkipPublish,                        # reutiliza binarios ya publicados, solo (re)instala servicios
    [switch]$ConfigureTelegram                   # pide/actualiza el Telegram Bot Token (opcional)
)

$ErrorActionPreference = "Stop"

function Assert-Admin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
        Write-Host "Este script requiere PowerShell elevado (Ejecutar como Administrador)." -ForegroundColor Red
        exit 1
    }
}

function ConvertFrom-SecureStringPlain([Security.SecureString]$Secure) {
    return [System.Net.NetworkCredential]::new("", $Secure).Password
}

function Get-ServiceEnvVar([string]$ServiceName, [string]$VarName) {
    $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    $prop = Get-ItemProperty -Path $regPath -Name Environment -ErrorAction SilentlyContinue
    if (-not $prop) { return $null }
    foreach ($line in $prop.Environment) {
        if ($line -like "$VarName=*") { return $line.Substring($VarName.Length + 1) }
    }
    return $null
}

function Set-ServiceEnv([string]$ServiceName, [hashtable]$Vars) {
    $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"

    # Merge, no reemplazo. Antes se reescribia el valor entero desde $Vars, asi que cualquier
    # variable puesta a mano fuera de este script desaparecia en el siguiente despliegue y el
    # servicio volvia en silencio al estado anterior (17/08/2026: se perdieron WorkerLock__Enabled
    # y Telegram__Enabled al republicar, y el Worker quedo otra vez inerte).
    $existing = @()
    $prop = Get-ItemProperty -Path $regPath -Name Environment -ErrorAction SilentlyContinue
    if ($prop) { $existing = @($prop.Environment) }

    $kept = @($existing | Where-Object { -not $Vars.ContainsKey((($_ -split '=', 2)[0])) })
    $lines = $kept + @($Vars.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" })

    New-ItemProperty -Path $regPath -Name Environment -PropertyType MultiString -Value $lines -Force | Out-Null
}

# ── 0. Elevacion y rutas ────────────────────────────────────────────────────
Assert-Admin
$RepoRoot  = Split-Path -Parent $PSScriptRoot
$ApiDir    = Join-Path $InstallDir "Api"
$WorkerDir = Join-Path $InstallDir "Worker"
$ApiSvc    = "ImpresorasServiceApi"
$WorkerSvc = "ImpresorasServiceWorker"

Write-Host ""
Write-Host "=== Instalacion ImpresorasService (Api + Worker) ===" -ForegroundColor Cyan
Write-Host "Repo: $RepoRoot"
Write-Host "Destino: $InstallDir"
Write-Host ""

# ── 1. Entrada hosts para HANA (opcional) ───────────────────────────────────
if ($HanaIp) {
    $hostsFile = "$env:WinDir\System32\drivers\etc\hosts"
    $already = Select-String -Path $hostsFile -Pattern "\b$HanaHost\b" -Quiet -ErrorAction SilentlyContinue
    if (-not $already) {
        Add-Content -Path $hostsFile -Value "`n$HanaIp`t$HanaHost"
        Write-Host "Añadida entrada hosts: $HanaIp $HanaHost" -ForegroundColor Yellow
    }
}

# ── 2. Detener servicios existentes (liberar los .exe para republicar) ─────
foreach ($svc in @($ApiSvc, $WorkerSvc)) {
    $existing = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($existing -and $existing.Status -ne "Stopped") {
        Write-Host "Deteniendo $svc..." -ForegroundColor Yellow
        Stop-Service -Name $svc -Force
        $existing.WaitForStatus("Stopped", "00:00:30")
    }
}

# ── 3. Publicar ──────────────────────────────────────────────────────────────
if (-not $SkipPublish) {
    Write-Host "-- Publicando Api --" -ForegroundColor Cyan
    dotnet publish "$RepoRoot\src\ImpresorasService.Api" -c Release -r win-x64 `
        --self-contained true -o $ApiDir
    if ($LASTEXITCODE -ne 0) { throw "Fallo publicando Api." }

    Write-Host "-- Publicando Worker --" -ForegroundColor Cyan
    dotnet publish "$RepoRoot\src\ImpresorasService.Worker" -c Release -r win-x64 `
        --self-contained true -o $WorkerDir
    if ($LASTEXITCODE -ne 0) { throw "Fallo publicando Worker." }
}

# ── 4. CORS del frontend (opcional) ─────────────────────────────────────────
if ($FrontendOrigin) {
    $apiSettingsPath = Join-Path $ApiDir "appsettings.json"
    $json = Get-Content $apiSettingsPath -Raw | ConvertFrom-Json
    $origins = [System.Collections.Generic.List[string]]::new([string[]]$json.Cors.AllowedOrigins)
    if (-not $origins.Contains($FrontendOrigin)) {
        $origins.Add($FrontendOrigin)
        $json.Cors.AllowedOrigins = $origins.ToArray()
        $json | ConvertTo-Json -Depth 10 | Set-Content $apiSettingsPath
        Write-Host "Añadido origen CORS: $FrontendOrigin" -ForegroundColor Yellow
    }
}

# ── 5. Credencial de la cuenta de servicio (opcional) ───────────────────────
$serviceCred = $null
if ($ServiceAccount) {
    $serviceCred = Get-Credential -UserName $ServiceAccount -Message "Password de la cuenta de servicio $ServiceAccount"
    Add-LocalGroupMember -Group "Print Operators" -Member $ServiceAccount -ErrorAction SilentlyContinue
}

# ── 6. Crear servicios (sin arrancar todavia; hace falta la clave de registro) ─
function Ensure-Service([string]$Name, [string]$DisplayName, [string]$ExePath) {
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $svc) {
        Write-Host "Creando servicio $Name..." -ForegroundColor Cyan
        $params = @{
            Name           = $Name
            BinaryPathName = $ExePath
            DisplayName    = $DisplayName
            StartupType    = "Automatic"
        }
        if ($serviceCred) { $params["Credential"] = $serviceCred }
        New-Service @params | Out-Null
    }

    # Reinicio automatico ante caida del proceso.
    #
    # El Worker puede morir con AccessViolationException dentro de
    # Sap.Data.Hana.PInvokeMethods64.HanaCommand_Cancel. Viene del driver nativo de SAP y NO se
    # puede capturar con try/catch: se lleva el proceso entero. Sin acciones de recuperacion, un
    # proceso caido se queda caido hasta que alguien lo arranque a mano.
    #
    # Se aplica siempre, no solo al crear: los servicios ya instalados no las tenian.
    # reset=86400 -> el contador de fallos vuelve a cero pasado un dia sin caidas.
    & sc.exe failure $Name reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

    # SIN failureflag a proposito. Las dos veces observadas (09:15 y 13:38 del 19/08/2026) la
    # AccessViolation salto dentro de BackgroundService.StopAsync, es decir PARANDO el servicio:
    # el stoppingToken cancela el comando en vuelo y el driver revienta. Con failureflag=1, esa
    # muerte sucia durante una parada solicitada se contaria como fallo y el SCM rearrancaria el
    # servicio a los 5 s — justo despues de que alguien lo pare a proposito. Las acciones por
    # defecto ya cubren el caso que importa: el proceso que muere sin que nadie se lo pidiera.
}

Ensure-Service $ApiSvc    "ImpresorasService API"    (Join-Path $ApiDir "ImpresorasService.Api.exe")
Ensure-Service $WorkerSvc "ImpresorasService Worker" (Join-Path $WorkerDir "ImpresorasService.Worker.exe")

# ── 7. Credenciales HANA / JWT, guardadas en el registro de cada servicio ──
Write-Host ""
Write-Host "-- Credenciales --" -ForegroundColor Cyan
$needsHanaCreds = $Reconfigure -or -not (Get-ServiceEnvVar $ApiSvc "ConnectionStrings__PrintQueue")
if ($needsHanaCreds) {
    if (-not $HanaUser) { $HanaUser = Read-Host "Usuario HANA (UID)" }
    $hanaPasswordSecure = Read-Host "Password HANA (no se muestra)" -AsSecureString
    $hanaPasswordPlain  = ConvertFrom-SecureStringPlain $hanaPasswordSecure

    $printQueueConn = "ServerNode=$HanaHost`:$HanaPort;UID=$HanaUser;PWD=$hanaPasswordPlain;Current Schema=$HanaSchema"
    $sapHanaConn    = "Driver=HDBODBC;ServerNode=$HanaHost`:$HanaPort;Database=$HanaSchema;UID=$HanaUser;PWD=$hanaPasswordPlain"
    Write-Host "  Conexion HANA capturada." -ForegroundColor Green
} else {
    $printQueueConn = Get-ServiceEnvVar $ApiSvc "ConnectionStrings__PrintQueue"
    $sapHanaConn    = Get-ServiceEnvVar $ApiSvc "SapHana__ConnectionString"
    Write-Host "  Conexion HANA ya configurada (usa -Reconfigure para cambiarla)." -ForegroundColor DarkGray
}

$jwtSecret = Get-ServiceEnvVar $ApiSvc "Jwt__Secret"
if ($Reconfigure -or -not $jwtSecret) {
    $bytes = New-Object byte[] 48
    (New-Object System.Security.Cryptography.RNGCryptoServiceProvider).GetBytes($bytes)
    $jwtSecret = [Convert]::ToBase64String($bytes)
    Write-Host "  Jwt__Secret generado." -ForegroundColor Green
} else {
    Write-Host "  Jwt__Secret ya configurado (usa -Reconfigure para regenerarlo)." -ForegroundColor DarkGray
}

if ($ConfigureTelegram) {
    $telegramSecure = Read-Host "Telegram Bot Token (no se muestra)" -AsSecureString
    $telegramToken  = ConvertFrom-SecureStringPlain $telegramSecure
    Write-Host "  Telegram__BotToken actualizado." -ForegroundColor Green
} else {
    $telegramToken = Get-ServiceEnvVar $ApiSvc "Telegram__BotToken"
    if ($telegramToken) {
        Write-Host "  Telegram__BotToken ya configurado (usa -ConfigureTelegram para cambiarlo)." -ForegroundColor DarkGray
    } else {
        $telegramToken = ""
        Write-Host "  Telegram__BotToken no configurado (opcional; usa -ConfigureTelegram para activar alertas)." -ForegroundColor DarkGray
    }
}

Set-ServiceEnv $ApiSvc @{
    "ConnectionStrings__PrintQueue" = $printQueueConn
    "SapHana__ConnectionString"     = $sapHanaConn
    "Jwt__Secret"                   = $jwtSecret
    "ASPNETCORE_URLS"               = "http://+:5105"
    "Telegram__BotToken"            = $telegramToken
}
Set-ServiceEnv $WorkerSvc @{
    "ConnectionStrings__PrintQueue" = $printQueueConn
    "SapHana__ConnectionString"     = $sapHanaConn
    "Telegram__BotToken"            = $telegramToken
}

# ── 8. Arrancar ──────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "-- Arrancando servicios --" -ForegroundColor Cyan
foreach ($svc in @($ApiSvc, $WorkerSvc)) {
    Start-Service -Name $svc
    Write-Host "$svc -> $((Get-Service -Name $svc).Status)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Listo. Comprueba: Invoke-RestMethod http://localhost:5105/health" -ForegroundColor Cyan
Write-Host "Pendiente manual (no automatizable desde aqui): driver SAP HANA ODBC, SumatraPDF, impresoras instaladas con el nombre de spool_queue." -ForegroundColor DarkYellow
