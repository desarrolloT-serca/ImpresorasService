# Extrae el DDL real de las tablas de ImpresorasService desde SAP HANA y lo escribe en
# scripts/sql/schema/, un fichero por tabla.
#
# Es SOLO LECTURA: consulta el catálogo de HANA (SYS.TABLES, SYS.TABLE_COLUMNS, SYS.CONSTRAINTS,
# SYS.INDEXES), no modifica nada.
#
# Uso (desde ImpresorasServiceV1):
#   .\scripts\extraer-ddl-hana.ps1
#
# Toma la cadena ODBC y el esquema de, por este orden:
#   1. Los parámetros -ConnectionString / -Schema
#   2. Las variables de entorno SapHana__ConnectionString / SapHana__Schema
#   3. appsettings.Development.json de la Api (sección SapHana)
# Es el mismo origen que usa scripts\verificar-hana.ps1.
#
# POR QUÉ SE RECONSTRUYE EN VEZ DE PEDIR EL DDL A HANA
# GET_OBJECT_DEFINITION normaliza los nombres a mayúsculas, y las tablas de este esquema están
# creadas en minúsculas (nombres entrecomillados), así que no las encuentra. El DDL que genera este
# script es equivalente y ejecutable, pero reconstruido: si algún día se añaden claves ajenas o
# triggers, habrá que ampliarlo.

param(
    [string]$ConnectionString,
    [string]$Schema,
    # Join-Path anidado: en PowerShell 5.1 solo admite dos rutas por llamada.
    [string]$OutputDir = (Join-Path (Join-Path $PSScriptRoot "sql") "schema")
)

$ErrorActionPreference = "Stop"

$tables = @(
    "printer_print_job",
    "printer_print_job_event",
    "printer_source_print_job",
    "printer_printer",
    "printer_routing_rule",
    "printer_store",
    "printer_user",
    "printer_dashboard_threshold",
    "printer_telegram_config",
    "printer_telegram_chat",
    "printer_alert_state",
    "printer_worker_lock"
)

# Tipos que llevan (longitud) o (longitud, escala) en su declaración.
$typesWithLength = @("NVARCHAR", "VARCHAR", "ALPHANUM", "VARBINARY", "SHORTTEXT")
$typesWithPrecision = @("DECIMAL", "SMALLDECIMAL")
$textTypes = @("NVARCHAR", "VARCHAR", "CHAR", "NCHAR", "ALPHANUM", "SHORTTEXT", "CLOB", "NCLOB")

# UTF-8 sin BOM: hdbsql y algunos clientes SQL tratan el BOM como parte de la primera sentencia.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# ── Resolver conexión y esquema ──────────────────────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = [Environment]::GetEnvironmentVariable("SapHana__ConnectionString")
}
if ([string]::IsNullOrWhiteSpace($Schema)) {
    $Schema = [Environment]::GetEnvironmentVariable("SapHana__Schema")
}

$devConfig = Join-Path (Join-Path (Join-Path (Join-Path $PSScriptRoot "..") "src") "ImpresorasService.Api") "appsettings.Development.json"
if ((Test-Path $devConfig) -and ([string]::IsNullOrWhiteSpace($ConnectionString) -or [string]::IsNullOrWhiteSpace($Schema))) {
    $json = Get-Content $devConfig -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($ConnectionString) -and $json.SapHana) { $ConnectionString = [string]$json.SapHana.ConnectionString }
    if ([string]::IsNullOrWhiteSpace($Schema) -and $json.SapHana) { $Schema = [string]$json.SapHana.Schema }
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    Write-Host "No hay cadena de conexion ODBC." -ForegroundColor Red
    Write-Host "Pasala con -ConnectionString, o define la variable de entorno SapHana__ConnectionString." -ForegroundColor Yellow
    exit 1
}
if ([string]::IsNullOrWhiteSpace($Schema)) {
    Write-Host "No hay esquema. Pasalo con -Schema o define SapHana__Schema." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Force $OutputDir | Out-Null }

$schemaLiteral = $Schema.Replace("'", "''")

Write-Host "=== Extraccion de DDL desde HANA ===" -ForegroundColor Cyan
Write-Host "Esquema: $Schema" -ForegroundColor Gray
Write-Host "Destino: $OutputDir" -ForegroundColor Gray
Write-Host ""

Add-Type -AssemblyName System.Data
$connection = New-Object System.Data.Odbc.OdbcConnection($ConnectionString)
$connection.Open()

function Invoke-Rows {
    param([string]$Sql)
    $cmd = $connection.CreateCommand()
    $cmd.CommandText = $Sql
    $reader = $cmd.ExecuteReader()
    $rows = @()
    while ($reader.Read()) {
        $row = @{}
        for ($i = 0; $i -lt $reader.FieldCount; $i++) { $row[$reader.GetName($i)] = [string]$reader[$i] }
        $rows += [pscustomobject]$row
    }
    $reader.Close()
    return $rows
}

function Format-DefaultValue {
    param($Column)
    $value = $Column.DEFAULT_VALUE
    $isText = $textTypes -contains $Column.DATA_TYPE_NAME
    # El catalogo devuelve el default sin comillas: 'DEFAULT' (el canal por defecto) saldria como
    # DEFAULT DEFAULT y no compilaria. Se citan los de tipo texto, salvo funciones como CURRENT_*.
    if ($isText -and -not $value.StartsWith("CURRENT_")) {
        return "'" + $value.Replace("'", "''") + "'"
    }
    return $value
}

function Format-ColumnType {
    param($Column)
    $type = $Column.DATA_TYPE_NAME
    if ($typesWithPrecision -contains $type) {
        if (-not [string]::IsNullOrWhiteSpace($Column.SCALE)) { return "$type($($Column.LENGTH),$($Column.SCALE))" }
        return "$type($($Column.LENGTH))"
    }
    if ($typesWithLength -contains $type) { return "$type($($Column.LENGTH))" }
    return $type
}

try {
    $written = 0
    $missing = @()

    $allColumns = Invoke-Rows "SELECT TABLE_NAME, COLUMN_NAME, POSITION, DATA_TYPE_NAME, LENGTH, SCALE, IS_NULLABLE, DEFAULT_VALUE FROM SYS.TABLE_COLUMNS WHERE SCHEMA_NAME='$schemaLiteral' ORDER BY TABLE_NAME, POSITION"
    $allConstraints = Invoke-Rows "SELECT TABLE_NAME, COLUMN_NAME, POSITION, CONSTRAINT_NAME, IS_PRIMARY_KEY, IS_UNIQUE_KEY FROM SYS.CONSTRAINTS WHERE SCHEMA_NAME='$schemaLiteral' ORDER BY TABLE_NAME, CONSTRAINT_NAME, POSITION"
    $allTables = Invoke-Rows "SELECT TABLE_NAME, IS_COLUMN_TABLE FROM SYS.TABLES WHERE SCHEMA_NAME='$schemaLiteral'"

    foreach ($table in $tables) {
        $meta = $allTables | Where-Object { $_.TABLE_NAME -ceq $table }
        $columns = $allColumns | Where-Object { $_.TABLE_NAME -ceq $table }

        if (-not $meta -or $columns.Count -eq 0) {
            Write-Host "  [--] $table : no existe en $Schema" -ForegroundColor DarkYellow
            $missing += $table
            continue
        }

        $kind = if ($meta.IS_COLUMN_TABLE -eq "TRUE") { "COLUMN TABLE" } else { "ROW TABLE" }

        $lines = @()
        foreach ($col in $columns) {
            $line = '    "' + $col.COLUMN_NAME + '" ' + (Format-ColumnType $col)
            if (-not [string]::IsNullOrWhiteSpace($col.DEFAULT_VALUE)) { $line += " DEFAULT " + (Format-DefaultValue $col) }
            if ($col.IS_NULLABLE -eq "FALSE") { $line += " NOT NULL" }
            $lines += $line
        }

        $constraints = $allConstraints | Where-Object { $_.TABLE_NAME -ceq $table }

        $pkCols = $constraints | Where-Object { $_.IS_PRIMARY_KEY -eq "TRUE" }
        if ($pkCols.Count -gt 0) {
            $names = ($pkCols | ForEach-Object { '"' + $_.COLUMN_NAME + '"' }) -join ", "
            $lines += "    PRIMARY KEY ($names)"
        }

        $body = $lines -join ",`r`n"
        $ddl = "CREATE $kind `"$Schema`".`"$table`" (`r`n$body`r`n);`r`n"

        # UNIQUE que no son la clave primaria: se emiten como indice unico aparte.
        $uniqueGroups = $constraints |
            Where-Object { $_.IS_UNIQUE_KEY -eq "TRUE" -and $_.IS_PRIMARY_KEY -ne "TRUE" } |
            Group-Object CONSTRAINT_NAME
        foreach ($group in $uniqueGroups) {
            $names = ($group.Group | Sort-Object { [int]$_.POSITION } | ForEach-Object { '"' + $_.COLUMN_NAME + '"' }) -join ", "
            $ddl += "`r`nCREATE UNIQUE INDEX `"$($group.Name)`" ON `"$Schema`".`"$table`" ($names);`r`n"
        }

        $header = "-- SAP HANA - esquema real de $Schema, extraido el $(Get-Date -Format 'yyyy-MM-dd').`r`n" +
                  "-- Generado por scripts\extraer-ddl-hana.ps1 a partir del catalogo. No editar a mano:`r`n" +
                  "-- si el esquema cambia en HANA, vuelve a ejecutar el script y commitea el resultado.`r`n" +
                  "-- Reconstruido del catalogo: cubre columnas, tipos, defaults, PK y unicos.`r`n`r`n"

        [System.IO.File]::WriteAllText((Join-Path $OutputDir "$table.sql"), ($header + $ddl), $utf8NoBom)
        Write-Host "  [OK] $table ($($columns.Count) columnas)" -ForegroundColor Green
        $written++
    }

    # ── Inventario compacto: comparable de un vistazo entre entornos ──
    $inventory = New-Object System.Text.StringBuilder
    [void]$inventory.AppendLine("-- Inventario de columnas, claves e indices de $Schema el $(Get-Date -Format 'yyyy-MM-dd').")
    [void]$inventory.AppendLine("-- Generado por scripts\extraer-ddl-hana.ps1.")
    [void]$inventory.AppendLine("")
    [void]$inventory.AppendLine("-- === COLUMNAS ===  tabla | columna | tipo | longitud | nullable")
    foreach ($col in $allColumns) {
        if ($col.TABLE_NAME -notlike "printer_*") { continue }
        [void]$inventory.AppendLine("-- $($col.TABLE_NAME) | $($col.COLUMN_NAME) | $($col.DATA_TYPE_NAME) | $($col.LENGTH) | $($col.IS_NULLABLE)")
    }
    [void]$inventory.AppendLine("")
    [void]$inventory.AppendLine("-- === CLAVES Y UNICOS ===  tabla | constraint | columna | PK | UNIQUE")
    foreach ($con in $allConstraints) {
        if ($con.TABLE_NAME -notlike "printer_*") { continue }
        [void]$inventory.AppendLine("-- $($con.TABLE_NAME) | $($con.CONSTRAINT_NAME) | $($con.COLUMN_NAME) | $($con.IS_PRIMARY_KEY) | $($con.IS_UNIQUE_KEY)")
    }

    $indexes = Invoke-Rows "SELECT i.TABLE_NAME, i.INDEX_NAME, i.INDEX_TYPE, ic.COLUMN_NAME, ic.POSITION FROM SYS.INDEXES i JOIN SYS.INDEX_COLUMNS ic ON ic.SCHEMA_NAME = i.SCHEMA_NAME AND ic.INDEX_NAME = i.INDEX_NAME AND ic.TABLE_NAME = i.TABLE_NAME WHERE i.SCHEMA_NAME='$schemaLiteral' ORDER BY i.TABLE_NAME, i.INDEX_NAME, ic.POSITION"
    [void]$inventory.AppendLine("")
    [void]$inventory.AppendLine("-- === INDICES ===  tabla | indice | tipo | columna")
    foreach ($idx in $indexes) {
        if ($idx.TABLE_NAME -notlike "printer_*") { continue }
        [void]$inventory.AppendLine("-- $($idx.TABLE_NAME) | $($idx.INDEX_NAME) | $($idx.INDEX_TYPE) | $($idx.COLUMN_NAME)")
    }

    [System.IO.File]::WriteAllText((Join-Path $OutputDir "_inventario.sql"), $inventory.ToString(), $utf8NoBom)
    Write-Host "  [OK] _inventario.sql" -ForegroundColor Green

    Write-Host ""
    Write-Host "$written tablas escritas en $OutputDir" -ForegroundColor Cyan
    if ($missing.Count -gt 0) {
        Write-Host "Tablas del modelo que NO existen en $Schema : $($missing -join ', ')" -ForegroundColor Yellow
        Write-Host "Si alguna deberia existir, es deriva de esquema entre el codigo y la base de datos." -ForegroundColor Yellow
    }
    Write-Host "Revisa el resultado y commitea la carpeta." -ForegroundColor Cyan
}
finally {
    $connection.Close()
}
