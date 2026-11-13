$ErrorActionPreference = "Stop"

$root = "H:\muse dash hwa\hwa\Star Trace"
$bmsPath = Join-Path $root "hwa2.bms"

Write-Host "=========================================================="
Write-Host "Updating Star Trace Boss Projectile dt values:"
Write-Host " Speed 1: dt0.8 (unchanged)"
Write-Host " Speed 2: dt0.75 -> dt0.76"
Write-Host " Speed 3: dt0.65 -> dt0.6"
Write-Host "=========================================================="

# 1. Speed 2 폴더의 보스 발사체 파일 리네임 (dt0.75 -> dt0.76)
$s2Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드2 wav폴더$' }
$s2BossRenamed = 0
foreach ($dir in $s2Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*_dt0.75.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt0\.75\.wav$', '_dt0.76.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s2BossRenamed++
        }
    }
}
Write-Host "Speed 2 boss projectile files renamed: $s2BossRenamed"

# 2. Speed 3 폴더의 보스 발사체 파일 리네임 (dt0.65 -> dt0.6)
$s3Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드3 wav폴더$' }
$s3BossRenamed = 0
foreach ($dir in $s3Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*_dt0.65.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt0\.65\.wav$', '_dt0.6.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s3BossRenamed++
        }
    }
}
Write-Host "Speed 3 boss projectile files renamed: $s3BossRenamed"

# 3. hwa2.bms 업데이트
if (Test-Path $bmsPath) {
    $backupPath = "$bmsPath.boss_dt_076_06.bak"
    Copy-Item -Path $bmsPath -Destination $backupPath -Force
    Write-Host "BMS Backup created: $backupPath"

    $lines = [System.IO.File]::ReadAllLines($bmsPath, [System.Text.Encoding]::UTF8)
    $updatedCount = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line -match '^#WAV') {
            if ($line -match '스피드2.+_dt0\.75\.wav$') {
                $newLine = $line -replace '_dt0\.75\.wav$', '_dt0.76.wav'
                $lines[$i] = $newLine
                $updatedCount++
            } elseif ($line -match '스피드3.+_dt0\.65\.wav$') {
                $newLine = $line -replace '_dt0\.65\.wav$', '_dt0.6.wav'
                $lines[$i] = $newLine
                $updatedCount++
            }
        }
    }
    [System.IO.File]::WriteAllLines($bmsPath, $lines, [System.Text.Encoding]::UTF8)
    Write-Host "Updated $updatedCount lines in $bmsPath"
}

Write-Host "ALL BOSS PROJECTILE DT UPDATES (0.8 - 0.76 - 0.6) COMPLETED SUCCESSFULLY!"
