# Crea el esquema completo de ImpresorasService (scripts\sql\crear-esquema.sql) en la base de datos
# PostgreSQL de ConnectionStrings:PrintQueue. Para montar un entorno nuevo o cambiar de base de datos.
#
# Uso (desde ImpresorasServiceV1):
#   .\scripts\crear-esquema.ps1
#
# Conexion, por este orden: -ConnectionString, variable de entorno ConnectionStrings__PrintQueue,
# fichero .env de la raiz del repo. La contrasena puede ir en la cadena (Password=) o en PGPASSWORD.
# El esquema es el "Search Path" de la cadena (public si no lo lleva); se crea si no existe.
# Si el usuario no puede crear esquemas, que lo cree el DBA antes:
#   CREATE SCHEMA impresoras AUTHORIZATION <usuario>;
#
# Todo va en una transaccion: o se crea entero o no queda nada. Si el esquema ya tiene tablas
# printer_* se detiene sin tocar nada. Requiere psql (viene con PostgreSQL).
# Copia del esquema real:  pg_dump --schema-only -n <esquema>  (mismas variables PG*).

param(
    [string]$ConnectionString,
    [string]$SqlFile = (Join-Path (Join-Path $PSScriptRoot "sql") "crear-esquema.sql")
)

$ErrorActionPreference = "Stop"

# .env de la raiz: mismas reglas que DotEnv.cs (no pisa variables ya definidas).
$envFile = Join-Path (Join-Path $PSScriptRoot "..") ".env"
if (Test-Path $envFile) {
    foreach ($line in Get-Content $envFile) {
        $t = $line.Trim(); $eq = $t.IndexOf("=")
        if ($t.StartsWith("#") -or $eq -le 0) { continue }
        $key = $t.Substring(0, $eq).Trim()
        if (-not [Environment]::GetEnvironmentVariable($key)) { [Environment]::SetEnvironmentVariable($key, $t.Substring($eq + 1).Trim()) }
    }
}
if (-not $ConnectionString) { $ConnectionString = $env:ConnectionStrings__PrintQueue }
if (-not $ConnectionString) { throw "No hay conexion. Define ConnectionStrings__PrintQueue (en .env o como variable) o pasa -ConnectionString." }

# set_ y no "= ": PowerShell trata el builder como diccionario y crearia una clave "ConnectionString".
$b = New-Object System.Data.Common.DbConnectionStringBuilder
$b.set_ConnectionString($ConnectionString)
function Pick([string[]]$Keys) { foreach ($k in $Keys) { if ($b.ContainsKey($k)) { return [string]$b[$k] } }; return "" }

$env:PGHOST     = Pick @("Host", "Server")
$env:PGPORT     = Pick @("Port"); if (-not $env:PGPORT) { $env:PGPORT = "5432" }
$env:PGDATABASE = Pick @("Database")
$env:PGUSER     = Pick @("Username", "User ID", "User", "UserName")
$password       = Pick @("Password")
if ($password) { $env:PGPASSWORD = $password }
$schema         = Pick @("Search Path", "SearchPath"); if (-not $schema) { $schema = "public" }
if (-not $env:PGHOST -or -not $env:PGDATABASE -or -not $env:PGUSER) { throw "La cadena debe llevar Host, Database y Username." }

$psql = (Get-Command psql -ErrorAction SilentlyContinue).Source
if (-not $psql) {
    $psql = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" -ErrorAction SilentlyContinue |
        Sort-Object { [int]$_.Directory.Parent.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $psql) { throw "No se encuentra psql. Instala el cliente de PostgreSQL." }

# psql escribe avisos por stderr (p. ej. "authenticated with an MD5-encrypted password") y Windows
# PowerShell 5.1 los convierte en error fatal con ErrorActionPreference=Stop. Se muestran sin cortar
# y se decide solo por $LASTEXITCODE; la salida devuelta es unicamente stdout.
function Invoke-Psql {
    $ErrorActionPreference = "Continue"
    & $psql @args 2>&1 | ForEach-Object {
        if ($_ -is [System.Management.Automation.ErrorRecord]) {
            if ("$_" -notmatch "MD5") { Write-Host "  psql: $_" -ForegroundColor DarkYellow }
        } else { $_ }
    }
}

$schemaIdent   = '"' + $schema.Replace('"', '""') + '"'
$schemaLiteral = "'" + $schema.Replace("'", "''") + "'"

Write-Host "=== Crear esquema ImpresorasService ===" -ForegroundColor Cyan
Write-Host "Servidor: $($env:PGHOST):$($env:PGPORT)  BD: $($env:PGDATABASE)  Usuario: $($env:PGUSER)  Esquema: $schema" -ForegroundColor Gray

$existing = Invoke-Psql -w -tA -v ON_ERROR_STOP=1 -c "SELECT count(*) FROM information_schema.tables WHERE table_schema = $schemaLiteral AND table_name LIKE 'printer\_%'"
if ($LASTEXITCODE -ne 0) { throw "No se pudo conectar (revisa host, puerto, usuario y PGPASSWORD)." }
if ([int]$existing -gt 0) {
    Write-Host "El esquema $schema ya tiene $existing tablas printer_*. No se toca nada." -ForegroundColor Red
    exit 1
}

# CREATE SCHEMA solo si falta: PostgreSQL exige permiso CREATE sobre la BD incluso con IF NOT EXISTS,
# y lo normal es que el DBA cree el esquema con AUTHORIZATION <usuario> y el usuario no tenga ese permiso.
$schemaExists = Invoke-Psql -w -tA -c "SELECT count(*) FROM pg_namespace WHERE nspname = $schemaLiteral"
$psqlArgs = @("-w", "-q", "-v", "ON_ERROR_STOP=1", "-1")
if ([int]$schemaExists -eq 0) { $psqlArgs += @("-c", "CREATE SCHEMA $schemaIdent") }
Invoke-Psql @psqlArgs -c "SET search_path TO $schemaIdent" -f $SqlFile
if ($LASTEXITCODE -ne 0) {
    Write-Host "Fallo: no se ha aplicado nada (transaccion revertida)." -ForegroundColor Red
    exit 1
}

$created = Invoke-Psql -w -tA -c "SELECT count(*) FROM information_schema.tables WHERE table_schema = $schemaLiteral AND table_name LIKE 'printer\_%'"
Write-Host "[OK] $created tablas printer_* creadas en $schema." -ForegroundColor Green
Write-Host "Siguiente: primer admin con POST /bootstrap/first-admin (Api en Development, Bootstrap__EnableFirstAdminEndpoint=true)." -ForegroundColor Cyan
