# Verifica que API y Worker usen la misma BD
# Ejecutar desde: ImpresorasServiceV1

$dbPath = Join-Path $PSScriptRoot ".." "impresoras-local.db"
$dbPath = [System.IO.Path]::GetFullPath($dbPath)

Write-Host "=== Verificacion de base de datos ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Ruta esperada de la BD (cuando ejecutas desde ImpresorasServiceV1):" -ForegroundColor Yellow
Write-Host "  $dbPath" -ForegroundColor White
Write-Host ""

if (-not (Test-Path $dbPath)) {
    Write-Host "La BD no existe aun. Se creara al iniciar API o Worker." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "IMPORTANTE: Ejecuta API y Worker desde el directorio ImpresorasServiceV1:" -ForegroundColor Green
    Write-Host "  cd ImpresorasServiceV1" -ForegroundColor White
    Write-Host "  dotnet run --project src/ImpresorasService.Api" -ForegroundColor White
    Write-Host "  dotnet run --project src/ImpresorasService.Worker" -ForegroundColor White
    exit 0
}

Write-Host "BD encontrada." -ForegroundColor Green
$size = (Get-Item $dbPath).Length
Write-Host "  Tamano: $size bytes" -ForegroundColor Gray
Write-Host ""

# Contar registros si sqlite3 esta disponible
$sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
if ($sqlite) {
    $sourceCount = sqlite3 $dbPath "SELECT COUNT(*) FROM SourcePrintJobs;" 2>$null
    $printCount = sqlite3 $dbPath "SELECT COUNT(*) FROM PrintJobs;" 2>$null
    Write-Host "  SourcePrintJobs: $sourceCount" -ForegroundColor Gray
    Write-Host "  PrintJobs: $printCount" -ForegroundColor Gray
} else {
    Write-Host "  (Instala sqlite3 para ver conteos)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Laravel NO usa esta BD. Laravel llama a la API (localhost:5105)." -ForegroundColor Cyan
Write-Host "API y Worker comparten esta BD SQLite." -ForegroundColor Cyan
