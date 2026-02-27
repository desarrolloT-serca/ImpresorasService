# Crea una regla de enrutado para storeId=1, FACTURA, DEFAULT
# Ejecutar desde: ImpresorasServiceV1

$baseUrl = "http://localhost:5105"
$printerId = $args[0]  # Pasar el PrinterId como argumento: .\crear-regla-enrutado.ps1 1

if (-not $printerId) {
    Write-Host "Listando impresoras disponibles:" -ForegroundColor Cyan
    $printers = Invoke-RestMethod -Uri "$baseUrl/api/printers" -Method Get
    $printers | Format-Table printerId, printerName, spoolQueue, storeId -AutoSize
    Write-Host ""
    Write-Host "Uso: .\scripts\crear-regla-enrutado.ps1 <printerId>" -ForegroundColor Yellow
    Write-Host "Ejemplo: .\scripts\crear-regla-enrutado.ps1 1" -ForegroundColor Yellow
    exit 0
}

$body = @{
    priority = 10
    storeId = 1
    documentType = "FACTURA"
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
