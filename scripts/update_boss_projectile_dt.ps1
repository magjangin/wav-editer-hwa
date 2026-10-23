$ErrorActionPreference = "Stop"

$root = "H:\muse dash hwa\hwa\Star Trace"
$bmsPath = Join-Path $root "hwa2.bms"

Write-Host "=========================================================="
Write-Host "Updating Star Trace Boss Projectile dt values:"
Write-Host " Speed 1: dt0.8 (unchanged)"
Write-Host " Speed 2: dt0.8 -> dt0.7"
Write-Host " Speed 3: dt0.8 -> dt0.6"
Write-Host "=========================================================="

# 1. Speed 3 폴더의 보스 발사체 파일 리네임 (dt0.8 -> dt0.6)
$s3Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드3 wav폴더$' }
$s3Renamed = 0
foreach ($dir in $s3Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*_dt0.8.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt0\.8\.wav$', '_dt0.6.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s3Renamed++
        }
    }
}
Write-Host "Speed 3 boss projectile files renamed: $s3Renamed"

# 2. Speed 2 폴더의 보스 발사체 파일 리네임 (dt0.8 -> dt0.7)
$s2Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드2 wav폴더$' }
$s2Renamed = 0
foreach ($dir in $s2Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*_dt0.8.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt0\.8\.wav$', '_dt0.7.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s2Renamed++
        }
    }
}
Write-Host "Speed 2 boss projectile files renamed: $s2Renamed"

# 3. hwa2.bms 업데이트
if (Test-Path $bmsPath) {
    $backupPath = "$bmsPath.boss_dt.bak"
    Copy-Item -Path $bmsPath -Destination $backupPath -Force
    Write-Host "BMS Backup created: $backupPath"

    $lines = [System.IO.File]::ReadAllLines($bmsPath, [System.Text.Encoding]::UTF8)
    $updatedCount = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line -match '^#WAV') {
            if ($line -match '스피드3.+_dt0\.8\.wav$') {
                $newLine = $line -replace '_dt0\.8\.wav$', '_dt0.6.wav'
                $lines[$i] = $newLine
                $updatedCount++
            } elseif ($line -match '스피드2.+_dt0\.8\.wav$') {
                $newLine = $line -replace '_dt0\.8\.wav$', '_dt0.7.wav'
                $lines[$i] = $newLine
                $updatedCount++
            }
        }
    }
    [System.IO.File]::WriteAllLines($bmsPath, $lines, [System.Text.Encoding]::UTF8)
    Write-Host "Updated $updatedCount lines in $bmsPath"
}

Write-Host "ALL BOSS PROJECTILE DT UPDATES COMPLETED SUCCESSFULLY!"
