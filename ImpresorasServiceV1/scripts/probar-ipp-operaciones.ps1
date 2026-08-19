# Gate I-1: preguntar a la impresora que operaciones IPP soporta.
#
# La pregunta del gate es si podemos confirmar la impresion POR TRABAJO en vez de por el estado
# global de la impresora. Eso depende de que soporte Get-Job-Attributes (0x09) o Get-Jobs (0x0A).
# En vez de deducirlo, se lo preguntamos: operations-supported es un atributo estandar (RFC 8011).
#
# Solo lectura: no encola nada ni cambia nada en la impresora.

param(
    [Parameter(Mandatory = $true)][string]$Ip,
    [string]$Path = "/ipp/printer",
    [int]$TimeoutMs = 5000
)

$ErrorActionPreference = "Stop"

# --- Codificacion IPP (RFC 8010), mismo formato que IppConfirmationService ---
function Write-IppAttr([System.IO.MemoryStream]$ms, [byte]$tag, [string]$name, [string]$value) {
    $ms.WriteByte($tag)
    $nb = [Text.Encoding]::ASCII.GetBytes($name)
    $ms.WriteByte([byte](($nb.Length -shr 8) -band 0xFF)); $ms.WriteByte([byte]($nb.Length -band 0xFF))
    if ($nb.Length) { $ms.Write($nb, 0, $nb.Length) }
    $vb = [Text.Encoding]::UTF8.GetBytes($value)
    $ms.WriteByte([byte](($vb.Length -shr 8) -band 0xFF)); $ms.WriteByte([byte]($vb.Length -band 0xFF))
    if ($vb.Length) { $ms.Write($vb, 0, $vb.Length) }
}

$ms = New-Object System.IO.MemoryStream
$ms.WriteByte(0x01); $ms.WriteByte(0x01)          # version 1.1
$ms.WriteByte(0x00); $ms.WriteByte(0x0B)          # Get-Printer-Attributes
$ms.Write([byte[]](0,0,0,1), 0, 4)                # request-id
$ms.WriteByte(0x01)                                # operation-attributes
Write-IppAttr $ms 0x47 "attributes-charset" "utf-8"
Write-IppAttr $ms 0x48 "attributes-natural-language" "en-us"
Write-IppAttr $ms 0x45 "printer-uri" ("ipp://" + $Ip + ":631" + $Path)
Write-IppAttr $ms 0x44 "requested-attributes" "operations-supported"
Write-IppAttr $ms 0x44 "" "printer-make-and-model"
Write-IppAttr $ms 0x44 "" "ipp-versions-supported"
$ms.WriteByte(0x03)                                # end-of-attributes
$payload = $ms.ToArray()

Write-Output ("--> POST http://" + $Ip + ":631" + $Path + "  (" + $payload.Length + " bytes)")

Add-Type -AssemblyName System.Net.Http
$handler = New-Object System.Net.Http.HttpClientHandler
$http = New-Object System.Net.Http.HttpClient($handler)
$http.Timeout = [TimeSpan]::FromMilliseconds($TimeoutMs)
$content = New-Object System.Net.Http.ByteArrayContent(,$payload)
$content.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue("application/ipp")

try {
    $resp = $http.PostAsync(("http://" + $Ip + ":631" + $Path), $content).GetAwaiter().GetResult()
} catch {
    Write-Output ("FALLO de transporte: " + $_.Exception.GetBaseException().Message)
    exit 2
}

Write-Output ("<-- HTTP " + [int]$resp.StatusCode)
$bytes = $resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
Write-Output ("<-- " + $bytes.Length + " bytes de respuesta")
if ($bytes.Length -lt 9) { Write-Output "respuesta demasiado corta"; exit 2 }

$status = ($bytes[2] -shl 8) -bor $bytes[3]
Write-Output ("<-- status-code IPP = 0x" + $status.ToString("X4") + $(if ($status -lt 0x0100) { "  (successful)" } else { "  (error)" }))

# --- Parseo: recorrer atributos y quedarnos con operations-supported ---
$i = 8
$currentName = ""
$ops = @()
$info = @{}
while ($i -lt $bytes.Length) {
    $tag = $bytes[$i]
    if ($tag -eq 0x03) { break }                       # end-of-attributes
    if ($tag -le 0x05) { $i++; continue }              # begin-attribute-group
    $i++
    $nameLen = ($bytes[$i] -shl 8) -bor $bytes[$i+1]; $i += 2
    $name = if ($nameLen) { [Text.Encoding]::ASCII.GetString($bytes, $i, $nameLen) } else { $currentName }
    $i += $nameLen
    $valLen = ($bytes[$i] -shl 8) -bor $bytes[$i+1]; $i += 2
    if ($nameLen) { $currentName = $name }

    if ($name -eq "operations-supported" -and $tag -eq 0x23 -and $valLen -eq 4) {
        $ops += (($bytes[$i] -shl 24) -bor ($bytes[$i+1] -shl 16) -bor ($bytes[$i+2] -shl 8) -bor $bytes[$i+3])
    } elseif ($name -in @("printer-make-and-model", "ipp-versions-supported") -and $valLen -gt 0) {
        $v = [Text.Encoding]::UTF8.GetString($bytes, $i, $valLen)
        if ($info.ContainsKey($name)) { $info[$name] += ", $v" } else { $info[$name] = $v }
    }
    $i += $valLen
}

Write-Output ""
foreach ($k in $info.Keys) { Write-Output ($k + " = " + $info[$k]) }

$names = @{
    0x02 = "Print-Job"; 0x04 = "Validate-Job"; 0x05 = "Create-Job"; 0x06 = "Send-Document";
    0x08 = "Cancel-Job"; 0x09 = "Get-Job-Attributes"; 0x0A = "Get-Jobs"; 0x0B = "Get-Printer-Attributes";
    0x0C = "Hold-Job"; 0x0D = "Release-Job"; 0x0E = "Restart-Job"; 0x10 = "Pause-Printer";
    0x11 = "Resume-Printer"; 0x12 = "Purge-Jobs"
}
Write-Output ""
Write-Output ("operations-supported (" + $ops.Count + "):")
foreach ($o in $ops) {
    $n = if ($names.ContainsKey([int]$o)) { $names[[int]$o] } else { "(0x" + ([int]$o).ToString("X2") + ")" }
    Write-Output ("   0x" + ([int]$o).ToString("X2") + "  " + $n)
}

Write-Output ""
Write-Output "=============== VEREDICTO GATE I-1 ==============="
$hasJobAttrs = $ops -contains 0x09
$hasGetJobs  = $ops -contains 0x0A
Write-Output ("Get-Job-Attributes (0x09) : " + $(if ($hasJobAttrs) { "SI" } else { "NO" }))
Write-Output ("Get-Jobs           (0x0A) : " + $(if ($hasGetJobs)  { "SI" } else { "NO" }))
if ($hasJobAttrs -or $hasGetJobs) {
    Write-Output "=> La confirmacion POR TRABAJO es posible en esta impresora."
} else {
    Write-Output "=> NO se puede confirmar por trabajo: solo queda el estado global de la impresora."
}
