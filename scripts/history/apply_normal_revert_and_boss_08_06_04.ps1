$ErrorActionPreference = "Stop"

$root = "H:\muse dash hwa\hwa\Star Trace"
$bmsPath = Join-Path $root "hwa2.bms"

Write-Host "=========================================================="
Write-Host "1. Reverting Normal Notes: 1.48 - 1.28 - 1.08"
Write-Host "   Speed 2: dt1.27 -> dt1.28"
Write-Host "   Speed 3: dt1.11 -> dt1.08"
Write-Host "2. Updating Boss Projectiles: 0.8 - 0.6 - 0.4"
Write-Host "   Speed 1: dt0.8 (unchanged)"
Write-Host "   Speed 2: dt0.7 -> dt0.6"
Write-Host "   Speed 3: dt0.6 -> dt0.4"
Write-Host "=========================================================="

# 1. 보스 발사체 리네임 (Speed 3 dt0.6 -> dt0.4 먼저 처리하여 충돌 방지)
$s3Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드3 wav폴더$' }
$s3BossRenamed = 0
foreach ($dir in $s3Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*_dt0.6.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt0\.6\.wav$', '_dt0.4.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s3BossRenamed++
        }
    }
}
Write-Host "Speed 3 boss projectile files renamed: $s3BossRenamed"

# 2. 보스 발사체 리네임 (Speed 2 dt0.7 -> dt0.6)
$s2Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드2 wav폴더$' }
$s2BossRenamed = 0
foreach ($dir in $s2Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*_dt0.7.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt0\.7\.wav$', '_dt0.6.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s2BossRenamed++
        }
    }
}
Write-Host "Speed 2 boss projectile files renamed: $s2BossRenamed"

# 3. 일반 노트 리네임 (Speed 2 dt1.27 -> dt1.28)
$s2NormalRenamed = 0
foreach ($dir in $s2Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*_dt1.27.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt1\.27\.wav$', '_dt1.28.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s2NormalRenamed++
        }
    }
}
Write-Host "Speed 2 normal files renamed: $s2NormalRenamed"

# 4. 일반 노트 리네임 (Speed 3 dt1.11 -> dt1.08)
$s3NormalRenamed = 0
foreach ($dir in $s3Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*_dt1.11.wav"
    foreach ($f in $files) {
        $newName = $f.Name -replace '_dt1\.11\.wav$', '_dt1.08.wav'
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s3NormalRenamed++
        }
    }
}
Write-Host "Speed 3 normal files renamed: $s3NormalRenamed"

# 5. hwa2.bms 업데이트
if (Test-Path $bmsPath) {
    $backupPath = "$bmsPath.normal128_boss0604.bak"
    Copy-Item -Path $bmsPath -Destination $backupPath -Force
    Write-Host "BMS Backup created: $backupPath"

    $lines = [System.IO.File]::ReadAllLines($bmsPath, [System.Text.Encoding]::UTF8)
    $bossUpdated = 0
    $normalUpdated = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line -match '^#WAV') {
            # Speed 3 Boss (dt0.6 -> dt0.4)
            if ($line -match '스피드3.+_dt0\.6\.wav$') {
                $lines[$i] = $line -replace '_dt0\.6\.wav$', '_dt0.4.wav'
                $bossUpdated++
            }
            # Speed 2 Boss (dt0.7 -> dt0.6)
            elseif ($line -match '스피드2.+_dt0\.7\.wav$') {
                $lines[$i] = $line -replace '_dt0\.7\.wav$', '_dt0.6.wav'
                $bossUpdated++
            }
            # Speed 2 Normal (dt1.27 -> dt1.28)
            elseif ($line -match '스피드2.+_dt1\.27\.wav$') {
                $lines[$i] = $line -replace '_dt1\.27\.wav$', '_dt1.28.wav'
                $normalUpdated++
            }
            # Speed 3 Normal (dt1.11 -> dt1.08)
            elseif ($line -match '스피드3.+_dt1\.11\.wav$') {
                $lines[$i] = $line -replace '_dt1\.11\.wav$', '_dt1.08.wav'
                $normalUpdated++
            }
        }
    }
    [System.IO.File]::WriteAllLines($bmsPath, $lines, [System.Text.Encoding]::UTF8)
    Write-Host "Updated $bossUpdated boss lines and $normalUpdated normal lines in $bmsPath"
}

Write-Host "ALL UPDATES COMPLETED SUCCESSFULLY!"
