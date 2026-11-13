$ErrorActionPreference = "Stop"

$root = "H:\muse dash hwa\hwa\Star Trace"
$bmsPath = Join-Path $root "hwa2.bms"

Write-Host "=========================================================="
Write-Host "Finalizing Speed 3 Normal WAVs: dt1.13 -> dt1.16"
Write-Host "Finalized Normal Notes: 1.48 - 1.28 - 1.16"
Write-Host "=========================================================="

# 1. Speed 3 일반 노트 파일 리네임 (dt1.13 -> dt1.16)
$s3Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드3 wav폴더$' }
$s3NormalRenamed = 0
foreach ($dir in $s3Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*_dt1.13.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt1\.13\.wav$', '_dt1.16.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s3NormalRenamed++
        }
    }
}
Write-Host "Speed 3 normal files renamed: $s3NormalRenamed"

# 2. hwa2.bms 업데이트
if (Test-Path $bmsPath) {
    $backupPath = "$bmsPath.normal_dt116_final.bak"
    Copy-Item -Path $bmsPath -Destination $backupPath -Force
    Write-Host "BMS Backup created: $backupPath"

    $lines = [System.IO.File]::ReadAllLines($bmsPath, [System.Text.Encoding]::UTF8)
    $updatedCount = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line -match '^#WAV' -and $line -match '스피드3.+_dt1\.13\.wav$') {
            $newLine = $line -replace '_dt1\.13\.wav$', '_dt1.16.wav'
            $lines[$i] = $newLine
            $updatedCount++
        }
    }
    [System.IO.File]::WriteAllLines($bmsPath, $lines, [System.Text.Encoding]::UTF8)
    Write-Host "Updated $updatedCount lines in $bmsPath"
}

Write-Host "SPEED 3 NORMAL DT UPDATE (dt1.16) FINALIZED AND COMPLETED SUCCESSFULLY!"
