# Segunda mitad del gate I-1: ver QUE devuelve Get-Jobs.
#
# Que la impresora soporte Get-Job-Attributes no basta. Para confirmar un trabajo hace falta su
# job-id, y ese lo asigna la impresora al RECIBIRLO. Nosotros no imprimimos por IPP: mandamos por
# SumatraPDF al spooler de Windows, asi que ese id no lo vemos nunca.
#
# La pregunta real es si Get-Jobs deja correlacionar de otra forma: por nombre de documento, por
# usuario, por hora. Esto lo responde.
#
# Solo lectura.

param(
    [Parameter(Mandatory = $true)][string]$Ip,
    [string]$WhichJobs = "completed",
    [int]$TimeoutMs = 5000
)

$ErrorActionPreference = "Stop"

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
$ms.WriteByte(0x01); $ms.WriteByte(0x01)
$ms.WriteByte(0x00); $ms.WriteByte(0x0A)          # Get-Jobs
$ms.Write([byte[]](0,0,0,1), 0, 4)
$ms.WriteByte(0x01)
Write-IppAttr $ms 0x47 "attributes-charset" "utf-8"
Write-IppAttr $ms 0x48 "attributes-natural-language" "en-us"
Write-IppAttr $ms 0x45 "printer-uri" ("ipp://" + $Ip + ":631/ipp/printer")
Write-IppAttr $ms 0x42 "which-jobs" $WhichJobs
Write-IppAttr $ms 0x44 "requested-attributes" "job-id"
Write-IppAttr $ms 0x44 "" "job-name"
Write-IppAttr $ms 0x44 "" "job-state"
Write-IppAttr $ms 0x44 "" "job-originating-user-name"
Write-IppAttr $ms 0x44 "" "time-at-completed"
Write-IppAttr $ms 0x44 "" "job-impressions-completed"
$ms.WriteByte(0x03)
$payload = $ms.ToArray()

Add-Type -AssemblyName System.Net.Http
$http = New-Object System.Net.Http.HttpClient
$http.Timeout = [TimeSpan]::FromMilliseconds($TimeoutMs)
$content = New-Object System.Net.Http.ByteArrayContent(,$payload)
$content.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue("application/ipp")
$resp = $http.PostAsync(("http://" + $Ip + ":631/ipp/printer"), $content).GetAwaiter().GetResult()
$bytes = $resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()

$status = ($bytes[2] -shl 8) -bor $bytes[3]
Write-Output ("which-jobs=" + $WhichJobs + "  ->  HTTP " + [int]$resp.StatusCode + ", status IPP 0x" + $status.ToString("X4") + ", " + $bytes.Length + " bytes")

$jobStates = @{ 3="pending"; 4="pending-held"; 5="processing"; 6="processing-stopped"; 7="canceled"; 8="aborted"; 9="completed" }

$i = 8
$currentName = ""
$job = $null
$n = 0
while ($i -lt $bytes.Length) {
    $tag = $bytes[$i]
    if ($tag -eq 0x03) { break }
    if ($tag -le 0x05) {
        if ($tag -eq 0x02) {   # begin job-attributes group => empieza un job
            if ($job) { $n++; Write-Output ("  job " + $n + ": " + (($job.GetEnumerator() | ForEach-Object { $_.Key + "=" + $_.Value }) -join "  ")) }
            $job = @{}
        }
        $i++; continue
    }
    $i++
    $nameLen = ($bytes[$i] -shl 8) -bor $bytes[$i+1]; $i += 2
    $name = if ($nameLen) { [Text.Encoding]::ASCII.GetString($bytes, $i, $nameLen) } else { $currentName }
    $i += $nameLen
    $valLen = ($bytes[$i] -shl 8) -bor $bytes[$i+1]; $i += 2
    if ($nameLen) { $currentName = $name }
    if ($job -ne $null -and $valLen -gt 0) {
        $val = if ($tag -eq 0x21 -or $tag -eq 0x23) {
            $num = 0; for ($k = 0; $k -lt $valLen; $k++) { $num = ($num -shl 8) -bor $bytes[$i+$k] }
            if ($name -eq "job-state" -and $jobStates.ContainsKey([int]$num)) { "$num($($jobStates[[int]$num]))" } else { "$num" }
        } else { [Text.Encoding]::UTF8.GetString($bytes, $i, $valLen) }
        $job[$name] = $val
    }
    $i += $valLen
}
if ($job -and $job.Count) { $n++; Write-Output ("  job " + $n + ": " + (($job.GetEnumerator() | ForEach-Object { $_.Key + "=" + $_.Value }) -join "  ")) }
if ($n -eq 0) { Write-Output "  (ningun trabajo devuelto)" }
