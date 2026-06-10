# Crea una regla de enrutado
# Ejecutar desde: ImpresorasServiceV1
# Uso: .\scripts\crear-regla-enrutado.ps1 <printerId> [storeId] [documentType]
#   printerId: obligatorio
#   storeId: opcional, default 101 (coincide con pestaña Prueba)
#   documentType: opcional, default TICKET (coincide con pestaña Prueba)

$baseUrl = "http://localhost:5105"
$printerId = $args[0]
$storeId = if ($args[1]) { [int]$args[1] } else { 101 }
$documentType = if ($args[2]) { $args[2] } else { "TICKET" }

if (-not $printerId) {
    Write-Host "Listando impresoras disponibles:" -ForegroundColor Cyan
    $printers = Invoke-RestMethod -Uri "$baseUrl/api/printers" -Method Get
    $printers | Format-Table printerId, printerName, spoolQueue, storeId -AutoSize
    Write-Host ""
    Write-Host "Uso: .\scripts\crear-regla-enrutado.ps1 <printerId> [storeId] [documentType]" -ForegroundColor Yellow
    Write-Host "  Ejemplo (regla para pestaña Prueba): .\scripts\crear-regla-enrutado.ps1 1" -ForegroundColor Gray
    Write-Host "  Ejemplo (regla custom): .\scripts\crear-regla-enrutado.ps1 1 1 FACTURA" -ForegroundColor Gray
    exit 0
}

$body = @{
    priority = 10
    storeId = $storeId
    documentType = $documentType
    channel = "DEFAULT"
    printerId = [int]$printerId
    isActive = $true
    createdBy = "admin"
} | ConvertTo-Json -Depth 3

try {
    $r = Invoke-RestMethod -Uri "$baseUrl/api/routingrules" -Method Post -Body $body -ContentType "application/json"
    Write-Host "Regla creada. RuleId: $($r.ruleId)" -ForegroundColor Green
} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
}
