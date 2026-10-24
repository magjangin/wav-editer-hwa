$ErrorActionPreference = "Stop"

$root = "H:\muse dash hwa\hwa\Star Trace"
$bmsPath = Join-Path $root "hwa2.bms"

Write-Host "=========================================================="
Write-Host "Updating Normal Note dt values to: 1.48 - 1.27 - 1.11"
Write-Host " Speed 1: dt1.48 (unchanged)"
Write-Host " Speed 2: dt1.28 -> dt1.27 (Hold dt1.26 + 0.01)"
Write-Host " Speed 3: dt1.08 -> dt1.11 (Hold dt1.10 + 0.01)"
Write-Host "=========================================================="

# 1. Speed 2 일반 노트 리네임: dt1.28 -> dt1.27
$s2Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드2 wav폴더$' }
$s2NormalRenamed = 0
foreach ($dir in $s2Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*_dt1.28.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt1\.28\.wav$', '_dt1.27.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s2NormalRenamed++
        }
    }
}
Write-Host "Speed 2 normal files renamed: $s2NormalRenamed"

# 2. Speed 3 일반 노트 리네임: dt1.08 -> dt1.11
$s3Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드3 wav폴더$' }
$s3NormalRenamed = 0
foreach ($dir in $s3Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*_dt1.08.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt1\.08\.wav$', '_dt1.11.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s3NormalRenamed++
        }
    }
}
Write-Host "Speed 3 normal files renamed: $s3NormalRenamed"

# 3. hwa2.bms 업데이트
if (Test-Path $bmsPath) {
    $backupPath = "$bmsPath.normal_dt127_111.bak"
    Copy-Item -Path $bmsPath -Destination $backupPath -Force
    Write-Host "BMS Backup created: $backupPath"

    $lines = [System.IO.File]::ReadAllLines($bmsPath, [System.Text.Encoding]::UTF8)
    $updatedCount = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line -match '^#WAV') {
            if ($line -match '스피드2.+_dt1\.28\.wav$') {
                $newLine = $line -replace '_dt1\.28\.wav$', '_dt1.27.wav'
                $lines[$i] = $newLine
                $updatedCount++
            } elseif ($line -match '스피드3.+_dt1\.08\.wav$') {
                $newLine = $line -replace '_dt1\.08\.wav$', '_dt1.11.wav'
                $lines[$i] = $newLine
                $updatedCount++
            }
        }
    }
    [System.IO.File]::WriteAllLines($bmsPath, $lines, [System.Text.Encoding]::UTF8)
    Write-Host "Updated $updatedCount lines in $bmsPath"
}

Write-Host "ALL NORMAL NOTE DT UPDATES (1.48 - 1.27 - 1.11) COMPLETED SUCCESSFULLY!"
