# Verifica configuración HANA-first y conectividad ODBC (sin imprimir credenciales).
# Ejecutar desde: ImpresorasServiceV1

param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot ".." "src" "ImpresorasService.Api" "appsettings.json"),
    [string]$DevelopmentConfigPath = (Join-Path $PSScriptRoot ".." "src" "ImpresorasService.Api" "appsettings.Development.json"),
    [switch]$SkipConnectionTest
)

function Get-ConfigValue {
    param([hashtable]$Root, [string]$Key)
    $envKey = ($Key -replace ':', '__')
    $envValue = [Environment]::GetEnvironmentVariable($envKey)
    if (-not [string]::IsNullOrWhiteSpace($envValue)) { return $envValue }

    $parts = $Key.Split(':')
    $node = $Root
    foreach ($part in $parts) {
        if (-not $node.ContainsKey($part)) { return $null }
        $node = $node[$part]
        if ($null -eq $node) { return $null }
    }
    if ($node -is [System.Collections.IDictionary] -or $node -is [pscustomobject]) { return $null }
    return [string]$node
}

function Merge-Hashtable {
    param([hashtable]$Base, [hashtable]$Overlay)
    foreach ($key in $Overlay.Keys) {
        if ($Base.ContainsKey($key) -and $Base[$key] -is [hashtable] -and $Overlay[$key] -is [hashtable]) {
            Merge-Hashtable -Base $Base[$key] -Overlay $Overlay[$key]
        } else {
            $Base[$key] = $Overlay[$key]
        }
    }
}

function ConvertTo-HashtableDeep {
    param($InputObject)
    if ($null -eq $InputObject) { return @{} }
    if ($InputObject -is [hashtable]) { return $InputObject }
    $table = @{}
    foreach ($prop in $InputObject.PSObject.Properties) {
        if ($prop.Value -is [System.Management.Automation.PSCustomObject]) {
            $table[$prop.Name] = ConvertTo-HashtableDeep $prop.Value
        } else {
            $table[$prop.Name] = $prop.Value
        }
    }
    return $table
}

function Mask-ConnectionString {
    param([string]$ConnectionString)
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) { return "(vacío)" }
    $masked = $ConnectionString -replace '(?i)(PWD|PASSWORD|UID|USER\s*ID)\s*=\s*[^;]+', '$1=***'
    return $masked
}

Write-Host "=== Verificación HANA (ImpresorasService) ===" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $ConfigPath)) {
    Write-Error "No se encontró $ConfigPath"
    exit 1
}

$config = ConvertTo-HashtableDeep (Get-Content $ConfigPath -Raw | ConvertFrom-Json)
if (Test-Path $DevelopmentConfigPath) {
    $dev = ConvertTo-HashtableDeep (Get-Content $DevelopmentConfigPath -Raw | ConvertFrom-Json)
    Merge-Hashtable -Base $config -Overlay $dev
}

$provider = Get-ConfigValue -Root $config -Key "Database:Provider"
$applyMigrations = Get-ConfigValue -Root $config -Key "Database:ApplyMigrations"
$sourceMode = Get-ConfigValue -Root $config -Key "Source:Mode"
$printQueue = Get-ConfigValue -Root $config -Key "ConnectionStrings:PrintQueue"
$sapHanaCs = Get-ConfigValue -Root $config -Key "SapHana:ConnectionString"
$schema = Get-ConfigValue -Root $config -Key "SapHana:Schema"
$table = Get-ConfigValue -Root $config -Key "SapHana:Table"

$ok = $true

function Assert-Config {
    param([bool]$Condition, [string]$Message)
    script:ok = script:ok -and $Condition
    if ($Condition) {
        Write-Host "  [OK] $Message" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] $Message" -ForegroundColor Red
    }
}

Write-Host "Configuración efectiva:" -ForegroundColor Yellow
Assert-Config ($provider -eq "Hana") "Database:Provider = Hana (actual: $provider)"
Assert-Config ($sourceMode -eq "SapHana") "Source:Mode = SapHana (actual: $sourceMode)"
Assert-Config (-not [string]::IsNullOrWhiteSpace($printQueue)) "ConnectionStrings:PrintQueue definido"
Assert-Config ($applyMigrations -eq "false" -or $applyMigrations -eq $false) "Database:ApplyMigrations = false (recomendado en HANA)"
Assert-Config (-not [string]::IsNullOrWhiteSpace($schema)) "SapHana:Schema = $schema"
Assert-Config (-not [string]::IsNullOrWhiteSpace($table)) "SapHana:Table = $table"

Write-Host ""
Write-Host "Cadenas (enmascaradas):" -ForegroundColor Yellow
Write-Host "  PrintQueue: $(Mask-ConnectionString $printQueue)"
Write-Host "  SapHana:    $(Mask-ConnectionString $sapHanaCs)"

if ($SkipConnectionTest) {
    Write-Host ""
    Write-Host "Prueba ODBC omitida (-SkipConnectionTest)." -ForegroundColor Gray
    exit ($(if ($ok) { 0 } else { 1 }))
}

if ([string]::IsNullOrWhiteSpace($sapHanaCs)) {
    Write-Host ""
    Write-Host "SapHana:ConnectionString vacío. Defina variable de entorno SapHana__ConnectionString para probar ODBC." -ForegroundColor Yellow
    exit ($(if ($ok) { 0 } else { 1 }))
}

Write-Host ""
Write-Host "Prueba ODBC (SapHana)..." -ForegroundColor Yellow

try {
    Add-Type -AssemblyName System.Data
    $connection = New-Object System.Data.Odbc.OdbcConnection($sapHanaCs)
    $connection.Open()

    $ping = $connection.CreateCommand()
    $ping.CommandText = "SELECT CURRENT_UTCTIMESTAMP FROM DUMMY"
    $serverUtc = $ping.ExecuteScalar()

    $countSourceCmd = $connection.CreateCommand()
    $countSourceCmd.CommandText = "SELECT COUNT(*) FROM `"$schema`".`"$table`""
    $sourceCount = $countSourceCmd.ExecuteScalar()

    $countPrintCmd = $connection.CreateCommand()
    $countPrintCmd.CommandText = "SELECT COUNT(*) FROM `"$schema`".`"printer_print_job`""
    $printCount = $countPrintCmd.ExecuteScalar()

    $connection.Close()

    Write-Host "  [OK] Conexión ODBC HANA" -ForegroundColor Green
    Write-Host "  Server UTC: $serverUtc" -ForegroundColor Gray
    Write-Host "  $schema.$table : $sourceCount filas" -ForegroundColor Gray
    Write-Host "  $schema.printer_print_job : $printCount filas" -ForegroundColor Gray
}
catch {
    Write-Host "  [FAIL] $($_.Exception.Message)" -ForegroundColor Red
    $ok = $false
}

Write-Host ""
Write-Host "Con la API en marcha (JWT admin), también puede usar:" -ForegroundColor Cyan
Write-Host "  GET /diagnostics" -ForegroundColor White
Write-Host "  GET /diagnostics/hana" -ForegroundColor White

exit ($(if ($ok) { 0 } else { 1 }))
