$ErrorActionPreference = "Stop"

$root = "H:\muse dash hwa\hwa\麦吉_Maggie × 盖盖Nyan - summertime (Arrange ver.)"
$bmsPath = Join-Path $root "hwa2.bms"
$wavDir = Join-Path $root "5번 씬 wav폴더"

Write-Host "=========================================================="
Write-Host "Updating summertime Scene 5 Boss Projectile 2/3 dt values:"
Write-Host " Target: 050701, 050704, 050801, 050804"
Write-Host " dt0.8 -> dt1.07"
Write-Host "=========================================================="

# 1. 5번 씬 wav폴더 내 발사체2, 3 (속도1: 01, 04) 파일 리네임
if (-not (Test-Path $wavDir)) {
    throw "WAV directory not found: $wavDir"
}

$targetFiles = Get-ChildItem -Path $wavDir | Where-Object {
    $_.Name -match '^(050701|050704|050801|050804).+_dt0\.8\.wav$'
}

$renamedCount = 0
foreach ($f in $targetFiles) {
    $newName = $f.Name -replace '_dt0\.8\.wav$', '_dt1.07.wav'
    $dest = Join-Path $wavDir $newName
    Write-Host "Renaming: $($f.Name) -> $newName"
    Move-Item -Path $f.FullName -Destination $dest
    $renamedCount++
}
Write-Host "Total WAV files renamed: $renamedCount"

# 2. hwa2.bms 백업 및 동기화
if (Test-Path $bmsPath) {
    $backupPath = "$bmsPath.boss_dt107.bak"
    Copy-Item -Path $bmsPath -Destination $backupPath -Force
    Write-Host "BMS Backup created: $backupPath"

    $encoding = [System.Text.UTF8Encoding]::new($true)
    $lines = [System.IO.File]::ReadAllLines($bmsPath, $encoding)
    $updatedCount = 0

    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line -match '^#WAV[0-9A-Za-z]{2,3}\s+(050701|050704|050801|050804).+_dt0\.8\.wav$') {
            $newLine = $line -replace '_dt0\.8\.wav$', '_dt1.07.wav'
            $lines[$i] = $newLine
            $updatedCount++
            Write-Host "Updated BMS Line: $newLine"
        }
    }

    [System.IO.File]::WriteAllLines($bmsPath, $lines, $encoding)
    Write-Host "Updated $updatedCount lines in $bmsPath"
} else {
    throw "BMS file not found: $bmsPath"
}

Write-Host "=========================================================="
Write-Host "SUCCESS: 5번 씬 보스 발사체2·3 dt1.07 변경 및 BMS 동기화 완료!"
Write-Host "=========================================================="
