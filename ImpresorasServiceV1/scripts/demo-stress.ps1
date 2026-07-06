# Inyeccion de trabajos etiquetados para demo y prueba de stress
# Cada PDF lleva el texto de la etiqueta impreso para verificar routing fisicamente.
#
# Uso:
#   .\scripts\demo-stress.ps1                     # configuracion por defecto
#   .\scripts\demo-stress.ps1 -Multiplier 10      # 10x mas trabajos por escenario
#   .\scripts\demo-stress.ps1 -Token "eyJ..."     # si la API requiere JWT (AdminOnly)

param(
    [string]$ApiBase    = "http://localhost:5105",
    [string]$Token      = "",
    [int]   $Multiplier = 1   # multiplica el Count de cada escenario
)

# =============================================================================
# ESCENARIOS — ajusta StoreId a los IDs reales de tu entorno
#
#   StoreId   -> id en printer_store
#   DocType   -> valor libre; debe coincidir exactamente con la routing_rule
#   Count     -> trabajos base (x Multiplier)
#   Label     -> texto que aparece impreso en el papel (solo ASCII, sin acentos)
#
# El ultimo escenario (StoreId=99) no tiene regla -> va a ROUTE_NOT_FOUND.
# Util para verificar que los errores se registran correctamente.
# =============================================================================
$Scenarios = @(
    @{ StoreId = 1;  DocType = "ALBARAN"; Count = 5; Label = "TIENDA 1 ALBARAN"   },
    @{ StoreId = 1;  DocType = "FACTURA"; Count = 5; Label = "TIENDA 1 FACTURA"   },
    @{ StoreId = 2;  DocType = "ALBARAN"; Count = 5; Label = "TIENDA 2 ALBARAN"   },
    @{ StoreId = 3;  DocType = "ALBARAN"; Count = 5; Label = "TIENDA 3 ALBARAN"   },
    @{ StoreId = 99; DocType = "ALBARAN"; Count = 2; Label = "TIENDA99 SIN REGLA" }
)
# =============================================================================

function New-LabeledPdf([string]$Label) {
    # PDF minimo valido con texto visible.
    # Usa Helvetica (Type1 estandar PDF, no requiere embedding).
    # Solo caracteres ASCII en la etiqueta — sin tildes ni enies.
    $enc    = [System.Text.Encoding]::ASCII
    $stream = "BT /F1 18 Tf 20 60 Td ($Label) Tj ET"
    $sLen   = $stream.Length   # ASCII: longitud en chars == longitud en bytes

    $hdr  = "%PDF-1.4`n"
    $obj1 = "1 0 obj`n<</Type/Catalog/Pages 2 0 R>>`nendobj`n"
    $obj2 = "2 0 obj`n<</Type/Pages/Kids[3 0 R]/Count 1>>`nendobj`n"
    $obj3 = "3 0 obj`n<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 100]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>`nendobj`n"
    $obj4 = "4 0 obj`n<</Length $sLen>>`nstream`n${stream}`nendstream`nendobj`n"
    $obj5 = "5 0 obj`n<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>`nendobj`n"

    # Offsets de cada objeto (en bytes; ASCII => chars == bytes)
    $o1 = $hdr.Length
    $o2 = $o1 + $obj1.Length
    $o3 = $o2 + $obj2.Length
    $o4 = $o3 + $obj3.Length
    $o5 = $o4 + $obj4.Length
    $xo = $o5 + $obj5.Length   # offset donde empieza xref

    $pdf = $hdr + $obj1 + $obj2 + $obj3 + $obj4 + $obj5 +
        "xref`n0 6`n" +
        "0000000000 65535 f`r`n" +
        ("{0:D10} 00000 n`r`n" -f $o1) +
        ("{0:D10} 00000 n`r`n" -f $o2) +
        ("{0:D10} 00000 n`r`n" -f $o3) +
        ("{0:D10} 00000 n`r`n" -f $o4) +
        ("{0:D10} 00000 n`r`n" -f $o5) +
        "trailer`n<</Size 6/Root 1 0 R>>`nstartxref`n$xo`n%%EOF"

    return $enc.GetBytes($pdf)
}

# ── Setup ─────────────────────────────────────────────────────────────────────
$hdrs = @{ "Content-Type" = "application/json" }
if ($Token) { $hdrs["Authorization"] = "Bearer $Token" }

$ts   = (Get-Date).ToString("yyyyMMddHHmmss")
$ok   = 0
$fail = 0

Write-Host ""
Write-Host "=== DEMO STRESS TEST  $(Get-Date -Format 'HH:mm:ss') ===" -ForegroundColor Cyan
Write-Host "API: $ApiBase   Multiplier: ${Multiplier}x" -ForegroundColor Gray
Write-Host ""

# ── Bucle de inyeccion ────────────────────────────────────────────────────────
foreach ($s in $Scenarios) {
    $count  = $s.Count * $Multiplier
    $slug   = $s.Label.ToUpper().Replace(' ', '')
    $pdfB64 = [Convert]::ToBase64String((New-LabeledPdf $s.Label))

    Write-Host "  Tienda=$($s.StoreId)  DocType=$($s.DocType)  [$count trabajos]" -ForegroundColor Yellow

    for ($i = 1; $i -le $count; $i++) {
        $seq   = '{0:D3}' -f $i
        $extId = "DEMO-$slug-$seq-$ts"

        $body = @{
            sourceSystem  = "DEMO"
            externalJobId = $extId
            storeId       = $s.StoreId
            documentType  = $s.DocType
            channel       = "DEFAULT"
            pdfBlob       = $pdfB64
        } | ConvertTo-Json

        try {
            $null = Invoke-RestMethod -Uri "$ApiBase/api/sourceprintjobs/test" `
                -Method Post -Body $body -Headers $hdrs -ErrorAction Stop
            $ok++
            Write-Host "    $seq OK" -ForegroundColor DarkGreen
        } catch {
            $status = try { [int]$_.Exception.Response.StatusCode } catch { 0 }

            if ($status -eq 401) {
                Write-Host "    401 No autorizado. Pasa tu JWT con -Token 'eyJ...'" -ForegroundColor Red
                exit 1
            }
            if ($status -eq 409) {
                # ExternalJobId ya existe — no es un error de verdad si se relanza el mismo ts
                Write-Host "    $seq DUPLICADO (ya existe, se ignora)" -ForegroundColor DarkYellow
                $ok++
            } else {
                $fail++
                Write-Host "    $seq ERROR $status — $($_.Exception.Message)" -ForegroundColor Red
            }
        }
    }
    Write-Host ""
}

# ── Resumen ───────────────────────────────────────────────────────────────────
$color = if ($fail -gt 0) { "Yellow" } else { "Green" }
Write-Host "── Resultado ──────────────────────────────────" -ForegroundColor Cyan
Write-Host "   OK=$ok   FAIL=$fail   Total=$($ok + $fail)" -ForegroundColor $color
Write-Host ""
Write-Host "Monitoriza el estado con:" -ForegroundColor Gray
Write-Host "  .\scripts\verificar-estado.ps1" -ForegroundColor Gray
Write-Host ""
