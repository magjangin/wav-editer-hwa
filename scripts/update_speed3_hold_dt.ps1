$ErrorActionPreference = "Stop"

$root = "H:\muse dash hwa\hwa\Star Trace"
$bmsPath = Join-Path $root "hwa2.bms"

Write-Host "=========================================================="
Write-Host "Updating Speed 3 Hold WAVs: dt1.07 -> dt1.10"
Write-Host "Reason: Muse Dash Speed 3 hold body renderer assumes 33 frames (1.100s)"
Write-Host "=========================================================="

# 1. Speed 3 홀드 파일 리네임 (dt1.07 -> dt1.10)
$s3Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드3 wav폴더$' }
$s3HoldRenamed = 0
foreach ($dir in $s3Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*홀드*dt1.07*.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt1\.07\.wav$', '_dt1.10.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s3HoldRenamed++
        }
    }
}
Write-Host "Speed 3 hold files renamed: $s3HoldRenamed"

# 2. hwa2.bms 업데이트
if (Test-Path $bmsPath) {
    $backupPath = "$bmsPath.hold_dt110.bak"
    Copy-Item -Path $bmsPath -Destination $backupPath -Force
    Write-Host "BMS Backup created: $backupPath"

    $lines = [System.IO.File]::ReadAllLines($bmsPath, [System.Text.Encoding]::UTF8)
    $updatedCount = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line -match '^#WAV' -and $line -match '스피드3.+홀드.+_dt1\.07\.wav') {
            $newLine = $line -replace '_dt1\.07\.wav$', '_dt1.10.wav'
            $lines[$i] = $newLine
            $updatedCount++
        }
    }
    [System.IO.File]::WriteAllLines($bmsPath, $lines, [System.Text.Encoding]::UTF8)
    Write-Host "Updated $updatedCount lines in $bmsPath"
}

Write-Host "SPEED 3 HOLD DT UPDATE (dt1.10) COMPLETED SUCCESSFULLY!"
