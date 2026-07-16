# Capture DPC/ISR 30s + analyse xperf (methodologie identique a dpcisr-analysis.txt)
$root  = "C:\Users\User\Desktop\bt"
$xperf = "C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\xperf.exe"
$etl   = Join-Path $root "tools\trace-after.etl"
$out   = Join-Path $root "tools\dpcisr-analysis-after.txt"
$log   = Join-Path $root "tools\measure-after.log"

wpr -cancel | Out-Null   # purge une eventuelle session residuelle

try {
    Write-Host "Demarrage de la capture (30 secondes)..."
    wpr -start "$root\tools\dpc-trace.wprp!DPC" -filemode
    if ($LASTEXITCODE -ne 0) { throw "wpr -start a echoue (code $LASTEXITCODE)" }

    Start-Sleep -Seconds 30

    wpr -stop $etl
    if ($LASTEXITCODE -ne 0) { throw "wpr -stop a echoue (code $LASTEXITCODE)" }

    Write-Host "Analyse xperf..."
    & $xperf -i $etl -o $out -a dpcisr
    if ($LASTEXITCODE -ne 0) { throw "xperf a echoue (code $LASTEXITCODE)" }

    "OK $(Get-Date -Format o)" | Set-Content $log -Encoding utf8
    Write-Host "Termine. Resultat : $out"
} catch {
    "ERREUR: $($_.Exception.Message)" | Set-Content $log -Encoding utf8
    wpr -cancel | Out-Null
}
