$root = "H:\muse dash hwa\hwa\Star Trace"
$s3Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -like "*스피드3*" }
Write-Host "Found Speed 3 directories: $($s3Dirs.Count)"
foreach ($d in $s3Dirs) {
    Write-Host "Dir: $($d.FullName)"
    $f116 = Get-ChildItem -Path $d.FullName -Filter "*dt1.16*.wav"
    Write-Host "  dt1.16 count: $($f116.Count)"
    if ($f116.Count -gt 0) {
        Write-Host "  Sample: $($f116[0].Name)"
    }
}

$bmsPath = Join-Path $root "hwa2.bms"
if (Test-Path $bmsPath) {
    $lines = [System.IO.File]::ReadAllLines($bmsPath, [System.Text.Encoding]::UTF8)
    $bms116 = $lines | Where-Object { $_ -match 'dt1\.16' }
    Write-Host "BMS dt1.16 references count: $($bms116.Count)"
    if ($bms116.Count -gt 0) {
        Write-Host "  Sample BMS line: $($bms116[0])"
    }
}
