$ErrorActionPreference = "Stop"

$root = "H:\muse dash hwa\hwa\Star Trace"
$bmsPath = Join-Path $root "hwa2.bms"

Write-Host "=========================================================="
Write-Host "Updating Speed 2 Hold WAVs: dt1.27 -> dt1.33"
Write-Host "Hypothesis: 40 frames / 30fps = 1.333s hold bar length"
Write-Host "=========================================================="

# 1. Speed 2 홀드 파일 리네임 (dt1.27 -> dt1.33)
$s2Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드2 wav폴더$' }
$s2HoldRenamed = 0
foreach ($dir in $s2Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*홀드*dt1.27*.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt1\.27\.wav$', '_dt1.33.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s2HoldRenamed++
        }
    }
}
Write-Host "Speed 2 hold files renamed: $s2HoldRenamed"

# 2. hwa2.bms 업데이트
if (Test-Path $bmsPath) {
    $backupPath = "$bmsPath.hold_dt133.bak"
    Copy-Item -Path $bmsPath -Destination $backupPath -Force
    Write-Host "BMS Backup created: $backupPath"

    $lines = [System.IO.File]::ReadAllLines($bmsPath, [System.Text.Encoding]::UTF8)
    $updatedCount = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line -match '^#WAV' -and $line -match '스피드2.+홀드.+_dt1\.27\.wav') {
            $newLine = $line -replace '_dt1\.27\.wav$', '_dt1.33.wav'
            $lines[$i] = $newLine
            $updatedCount++
        }
    }
    [System.IO.File]::WriteAllLines($bmsPath, $lines, [System.Text.Encoding]::UTF8)
    Write-Host "Updated $updatedCount lines in $bmsPath"
}

Write-Host "SPEED 2 HOLD DT UPDATE (dt1.33) COMPLETED SUCCESSFULLY!"
