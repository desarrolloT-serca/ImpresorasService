# Script para probar la impresion end-to-end
# Requisitos: API y Worker ya en ejecucion (en otras ventanas)
# Ejecutar desde: ImpresorasServiceV1 (donde esta este script)

$baseUrl = "http://localhost:5105"   # Cambia si tu API usa otro puerto

Write-Host "=== Prueba de impresion ===" -ForegroundColor Cyan
Write-Host "Asegurate de que la API y el Worker estan corriendo." -ForegroundColor Yellow
Write-Host ""

# Paso 1: Crear trabajo de origen
Write-Host "1. Creando trabajo de prueba..." -ForegroundColor Green
$extId = "JOB-PRUEBA-$(Get-Date -Format 'yyyyMMddHHmmss')"
# pdfBlob en Base64. PDF mínimo válido (~67 bytes) para pruebas. System.Text.Json espera Base64 para byte[]
$minimalPdfBytes = [byte[]](0x25,0x50,0x44,0x46,0x2D,0x31,0x2E,0x0D,0x74,0x72,0x61,0x69,0x6C,0x65,0x72,0x3C,0x3C,0x2F,0x52,0x6F,0x6F,0x74,0x3C,0x3C,0x2F,0x50,0x61,0x67,0x65,0x73,0x3C,0x3C,0x2F,0x4B,0x69,0x64,0x73,0x5B,0x3C,0x3C,0x2F,0x4D,0x65,0x64,0x69,0x61,0x42,0x6F,0x78,0x5B,0x30,0x20,0x30,0x20,0x33,0x20,0x33,0x5D,0x3E,0x3E,0x5D,0x3E,0x3E,0x3E,0x3E,0x3E,0x3E)
$pdfBase64 = [Convert]::ToBase64String($minimalPdfBytes)
$body = @{sourceSystem="TEST"; externalJobId=$extId; storeId=1; documentType="FACTURA"; channel="DEFAULT"; pdfBlob=$pdfBase64} | ConvertTo-Json

try {
    $r1 = Invoke-RestMethod -Uri "$baseUrl/api/sourceprintjobs/test" -Method Post -Body $body -ContentType "application/json"
    Write-Host "   OK. Id creado: $($r1.id)" -ForegroundColor Green
} catch {
    Write-Host "   ERROR: $_" -ForegroundColor Red
    exit 1
}

# Paso 2: Esperar ingesta
Write-Host "2. Esperando 6 segundos (ingesta del Worker)..." -ForegroundColor Green
Start-Sleep -Seconds 6

# Paso 3: Obtener el jobId del trabajo
Write-Host "3. Buscando trabajo en cola..." -ForegroundColor Green
$jobs = Invoke-RestMethod -Uri "$baseUrl/api/printjobs" -Method Get
$pending = $jobs | Where-Object { $_.status -eq 0 } | Select-Object -First 1  # 0 = Pending, tomar solo el primero
if (-not $pending) {
    $pending = $jobs | Select-Object -First 1
}
if (-not $pending) {
    Write-Host "   ERROR: No hay trabajos en la cola." -ForegroundColor Red
    exit 1
}

$jobId = $pending.jobId
Write-Host "   JobId encontrado: $jobId" -ForegroundColor Green

# Paso 4: Enrutar el trabajo
Write-Host "4. Enrutando trabajo a impresora..." -ForegroundColor Green
try {
    $r2 = Invoke-RestMethod -Uri "$baseUrl/api/printjobs/$jobId/route" -Method Post
    Write-Host "   OK. Estado: $($r2.status)" -ForegroundColor Green
} catch {
    Write-Host "   ERROR: $_" -ForegroundColor Red
    Write-Host "   ¿Tienes una regla de enrutado para storeId=1, FACTURA, DEFAULT?" -ForegroundColor Yellow
    exit 1
}

# Paso 5: Esperar y verificar
Write-Host "5. Esperando 8 segundos (Worker procesa cada 5s)..." -ForegroundColor Green
Start-Sleep -Seconds 8

Write-Host ""
Write-Host "6. Verificando estado de la cola:" -ForegroundColor Green
& "$PSScriptRoot\verificar-estado.ps1"
