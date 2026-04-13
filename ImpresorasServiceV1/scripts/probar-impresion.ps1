# Script para probar la impresion end-to-end
# Requisitos: API y Worker ya en ejecucion (en otras ventanas)
# Ejecutar desde: ImpresorasServiceV1 (donde esta este script)
#
# Uso: .\probar-impresion.ps1 [storeId] [documentType] [-PdfPath "C:\ruta\mi.pdf"]
#   -PdfPath: opcional. Si lo pasas, usa TU PDF en lugar del minimo. Ejemplo:
#     .\probar-impresion.ps1 -PdfPath "C:\Users\Yo\Documents\ticket.pdf"

param(
    [int]$storeId = 101,
    [string]$documentType = "TICKET",
    [string]$PdfPath = ""
)

$baseUrl = "http://localhost:5105"   # Cambia si tu API usa otro puerto

Write-Host "=== Prueba de impresion ===" -ForegroundColor Cyan
Write-Host "StoreId=$storeId DocumentType=$documentType" -ForegroundColor Gray
if ($PdfPath) { Write-Host "PDF personalizado: $PdfPath" -ForegroundColor Cyan }
else { Write-Host "PDF: minimo por defecto" -ForegroundColor Gray }
Write-Host "Asegurate de que la API y el Worker estan corriendo." -ForegroundColor Yellow
Write-Host ""

# Paso 1: Crear trabajo de origen
Write-Host "1. Creando trabajo de prueba..." -ForegroundColor Green
$extId = "JOB-PRUEBA-$(Get-Date -Format 'yyyyMMddHHmmss')"

$bodyHash = @{
    sourceSystem = "TEST"
    externalJobId = $extId
    storeId = $storeId
    documentType = $documentType
    channel = "DEFAULT"
}
if ($PdfPath -and (Test-Path $PdfPath)) {
    $pdfBytes = [System.IO.File]::ReadAllBytes($PdfPath)
    $bodyHash["pdfBlob"] = [Convert]::ToBase64String($pdfBytes)
    Write-Host "   Usando PDF: $PdfPath ($($pdfBytes.Length) bytes)" -ForegroundColor Gray
} elseif ($PdfPath) {
    Write-Host "   AVISO: No existe $PdfPath - usando PDF minimo" -ForegroundColor Yellow
}
$body = $bodyHash | ConvertTo-Json

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

# Paso 3: Obtener el jobId del trabajo que acabamos de crear
Write-Host "3. Buscando trabajo en cola (externalJobId=$extId)..." -ForegroundColor Green
$jobs = Invoke-RestMethod -Uri "$baseUrl/api/printjobs" -Method Get
$jobs = @($jobs)
# Buscar por externalJobId para asegurar que es el que creamos
$target = $jobs | Where-Object { ($_.externalJobId -eq $extId) -or ($_.ExternalJobId -eq $extId) } | Select-Object -First 1
if (-not $target) { $target = $jobs | Where-Object { $_.status -eq 0 -or $_.status -eq 1 } | Select-Object -First 1 }
if (-not $target) { $target = $jobs | Select-Object -First 1 }
if (-not $target) {
    Write-Host "   ERROR: No hay trabajos en la cola." -ForegroundColor Red
    exit 1
}

$jobId = $target.jobId
$statusNames = @{0="Pending";1="Routed";2="Printing";3="SpoolAccepted";8="ErrorFinal"}
$statusName = if ($statusNames.ContainsKey($target.status)) { $statusNames[$target.status] } else { "Status $($target.status)" }
Write-Host "   JobId: $jobId | Estado actual: $statusName" -ForegroundColor Green

# Paso 4: Enrutar solo si esta Pending
if ($target.status -eq 0) {
    Write-Host "4. Enrutando trabajo a impresora..." -ForegroundColor Green
    try {
        $r2 = Invoke-RestMethod -Uri "$baseUrl/api/printjobs/$jobId/route" -Method Post
        Write-Host "   OK. Estado: $($r2.status)" -ForegroundColor Green
    } catch {
        Write-Host "   ERROR: $_" -ForegroundColor Red
        Write-Host "   Crea una regla: .\scripts\crear-regla-enrutado.ps1 1" -ForegroundColor Yellow
        exit 1
    }
} else {
    Write-Host "4. Trabajo ya enrutado (auto-routing), esperando impresion..." -ForegroundColor Green
}

# Paso 5: Esperar y verificar
Write-Host "5. Esperando 8 segundos (Worker procesa cada 5s)..." -ForegroundColor Green
Start-Sleep -Seconds 8

Write-Host ""
Write-Host "6. Verificando estado de la cola:" -ForegroundColor Green
& "$PSScriptRoot\verificar-estado.ps1"
