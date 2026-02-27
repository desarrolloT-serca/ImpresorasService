# Verifica el estado de los trabajos en la cola
# Ejecutar desde: ImpresorasServiceV1

$baseUrl = "http://localhost:5105"

$statusNames = @{
    0 = "Pending"      # Pendiente de enrutar
    1 = "Routed"      # Enrutado, esperando impresion
    2 = "Printing"    # Imprimiendo ahora
    3 = "SpoolAccepted"  # Spooler acepto - EXITO
    4 = "PrintedConfirmed"
    5 = "PrintedUnknown"
    6 = "RetryScheduled"  # Reintento programado
    7 = "Cancelled"
    8 = "ErrorFinal"  # Fallo definitivo
}

Write-Host "=== Estado de la cola ===" -ForegroundColor Cyan
Write-Host ""

try {
    $jobs = Invoke-RestMethod -Uri "$baseUrl/api/printjobs" -Method Get
} catch {
    Write-Host "ERROR: No se pudo conectar a la API en $baseUrl" -ForegroundColor Red
    Write-Host "   ¿Esta la API en ejecucion?" -ForegroundColor Yellow
    exit 1
}

$jobs = @($jobs)
if ($jobs.Count -eq 0) {
    Write-Host "La cola esta vacia." -ForegroundColor Yellow
    exit 0
}

$jobs | ForEach-Object {
    $status = $statusNames[$_.status]
    if (-not $status) { $status = "Status $($_.status)" }
    
    $color = "White"
    if ($_.status -eq 3 -or $_.status -eq 4 -or $_.status -eq 5) { $color = "Green" }   # Exito
    if ($_.status -eq 8) { $color = "Red" }   # Error
    if ($_.status -eq 6) { $color = "Yellow" }   # Reintento
    
    Write-Host "  JobId: $($_.jobId)" -ForegroundColor $color
    Write-Host "    Estado: $status" -ForegroundColor $color
    Write-Host "    StoreId: $($_.storeId) | DocumentType: $($_.documentType) | Intentos: $($_.attemptCount)" -ForegroundColor Gray
    if ($_.lastErrorCode) { Write-Host "    Error: $($_.lastErrorCode) - $($_.lastErrorMessage)" -ForegroundColor Red }
    Write-Host ""
}

Write-Host "=== Resumen ===" -ForegroundColor Cyan
$spoolAccepted = ($jobs | Where-Object { $_.status -eq 3 }).Count
$errorFinal = ($jobs | Where-Object { $_.status -eq 8 }).Count
$routed = ($jobs | Where-Object { $_.status -eq 1 }).Count

if ($spoolAccepted -gt 0) {
    Write-Host "  $spoolAccepted trabajo(s) enviado(s) al spooler correctamente." -ForegroundColor Green
}
if ($routed -gt 0) {
    Write-Host "  $routed trabajo(s) en Routed - el Worker los procesara en los proximos segundos." -ForegroundColor Yellow
}
if ($errorFinal -gt 0) {
    Write-Host "  $errorFinal trabajo(s) en ErrorFinal." -ForegroundColor Red
}
