$ErrorActionPreference = "Stop"

$root = "H:\muse dash hwa\hwa\Star Trace"
$bmsPath = Join-Path $root "hwa2.bms"

Write-Host "=========================================================="
Write-Host "Updating Star Trace Speed dt values:"
Write-Host " Speed 1: dt1.48 / dt1.47 (unchanged)"
Write-Host " Speed 2: dt1.28 -> dt1.18 / dt1.27 -> dt1.17"
Write-Host " Speed 3: dt1.08 -> dt0.98 / dt1.07 -> dt0.97"
Write-Host "=========================================================="

# 1. Speed 3 폴더 파일 리네임 (dt1.08 -> dt0.98, dt1.07 -> dt0.97)
$s3Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드3 wav폴더$' }
$s3Renamed = 0
foreach ($dir in $s3Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*.wav"
    foreach ($f in $files) {
        $newName = $f.Name
        if ($newName -match '_dt1\.08\.wav$') {
            $newName = $newName -replace '_dt1\.08\.wav$', '_dt0.98.wav'
        } elseif ($newName -match '_dt1\.07\.wav$') {
            $newName = $newName -replace '_dt1\.07\.wav$', '_dt0.97.wav'
        }
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s3Renamed++
        }
    }
}
Write-Host "Speed 3 files renamed: $s3Renamed"

# 2. Speed 2 폴더 파일 리네임 (dt1.28 -> dt1.18, dt1.27 -> dt1.17)
$s2Dirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '스피드2 wav폴더$' }
$s2Renamed = 0
foreach ($dir in $s2Dirs) {
    $files = Get-ChildItem -Path $dir.FullName -Filter "*.wav"
    foreach ($f in $files) {
        $newName = $f.Name
        if ($newName -match '_dt1\.28\.wav$') {
            $newName = $newName -replace '_dt1\.28\.wav$', '_dt1.18.wav'
        } elseif ($newName -match '_dt1\.27\.wav$') {
            $newName = $newName -replace '_dt1\.27\.wav$', '_dt1.17.wav'
        }
        if ($newName -ne $f.Name) {
            $dest = Join-Path $dir.FullName $newName
            Move-Item -Path $f.FullName -Destination $dest
            $s2Renamed++
        }
    }
}
Write-Host "Speed 2 files renamed: $s2Renamed"

# 3. hwa2.bms 업데이트
if (Test-Path $bmsPath) {
    $backupPath = "$bmsPath.dt_update2.bak"
    Copy-Item -Path $bmsPath -Destination $backupPath -Force
    Write-Host "BMS Backup created: $backupPath"

    $lines = [System.IO.File]::ReadAllLines($bmsPath, [System.Text.Encoding]::UTF8)
    $updatedCount = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line -match '^#WAV') {
            if ($line -match '스피드3') {
                $newLine = $line -replace '_dt1\.08\.wav', '_dt0.98.wav' -replace '_dt1\.07\.wav', '_dt0.97.wav'
                if ($newLine -ne $line) {
                    $lines[$i] = $newLine
                    $updatedCount++
                }
            } elseif ($line -match '스피드2') {
                $newLine = $line -replace '_dt1\.28\.wav', '_dt1.18.wav' -replace '_dt1\.27\.wav', '_dt1.17.wav'
                if ($newLine -ne $line) {
                    $lines[$i] = $newLine
                    $updatedCount++
                }
            }
        }
    }
    [System.IO.File]::WriteAllLines($bmsPath, $lines, [System.Text.Encoding]::UTF8)
    Write-Host "Updated $updatedCount lines in $bmsPath"
}

Write-Host "ALL DT UPDATES (1.48 / 1.18 / 0.98) COMPLETED SUCCESSFULLY!"
