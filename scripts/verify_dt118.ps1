$root = "H:\muse dash hwa\hwa\Star Trace"
$s3Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -like "*스피드3*" }
$f118Total = 0
$f116Total = 0
foreach ($d in $s3Dirs) {
    $f118 = Get-ChildItem -Path $d.FullName -Filter "*dt1.18*.wav"
    $f116 = Get-ChildItem -Path $d.FullName -Filter "*dt1.16*.wav"
    $f118Total += $f118.Count
    $f116Total += $f116.Count
}
Write-Host "WAV Files -> dt1.18: $f118Total / dt1.16: $f116Total"

$bmsPath = Join-Path $root "hwa2.bms"
if (Test-Path $bmsPath) {
    $lines = [System.IO.File]::ReadAllLines($bmsPath, [System.Text.Encoding]::UTF8)
    $bms118 = ($lines | Where-Object { $_ -match '스피드3.+dt1\.18' }).Count
    $bms116 = ($lines | Where-Object { $_ -match '스피드3.+dt1\.16' }).Count
    Write-Host "BMS Speed 3 Lines -> dt1.18: $bms118 / dt1.16: $bms116"
}
