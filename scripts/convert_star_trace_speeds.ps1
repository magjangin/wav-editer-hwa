$ErrorActionPreference = "Stop"

$root = "H:\muse dash hwa\hwa\Star Trace"
$bmsPath = Join-Path $root "hwa2.bms"

Write-Host "=========================================================="
Write-Host "Star Trace Scene Speed Splitter & Renamer (Speed 1, 2, 3)"
Write-Host "Target Root: $root"
Write-Host "=========================================================="

function Transform-WavName($name, $targetSpeed) {
    if ($name -match '^([0-9A-Za-z]{6})_(.+)_(dt[0-9\.]+)\.wav$') {
        $uid = $Matches[1]
        $desc = $Matches[2]
        $dtPart = $Matches[3]

        $xx = $uid.Substring(2, 2)
        # 씬 전환(0004xx) 및 보스 동작(xx=01)은 속도가 없으므로 원본 유지
        if ($uid.StartsWith("0004") -or $xx -eq "01") {
            return $name
        }

        $yy = [int]$uid.Substring(4, 2)
        if ($yy -lt 1 -or $yy -gt 18) {
            return $name
        }

        # yy 규칙: (yy - 1) / 3 = 레인/모션 그룹, 속도 1,2,3
        $laneGroup = [int][math]::Floor(($yy - 1) / 3)
        $newYy = [int]($laneGroup * 3 + $targetSpeed)
        $yyFormatted = "{0:D2}" -f $newYy
        $newUid = $uid.Substring(0, 4) + $yyFormatted

        # dt 값 변경 (일반: 1.48/1.28/1.08, 홀드: 1.47/1.27/1.07, 보스 발사체: 0.8/0.7/0.6)
        $newDtPart = $dtPart
        if ($dtPart -eq "dt1.48") {
            if ($targetSpeed -eq 1) { $newDtPart = "dt1.48" }
            elseif ($targetSpeed -eq 2) { $newDtPart = "dt1.28" }
            elseif ($targetSpeed -eq 3) { $newDtPart = "dt1.08" }
        }
        elseif ($dtPart -eq "dt1.47") {
            if ($targetSpeed -eq 1) { $newDtPart = "dt1.47" }
            elseif ($targetSpeed -eq 2) { $newDtPart = "dt1.27" }
            elseif ($targetSpeed -eq 3) { $newDtPart = "dt1.07" }
        }
        elseif ($dtPart -eq "dt0.8") {
            if ($targetSpeed -eq 1) { $newDtPart = "dt0.8" }
            elseif ($targetSpeed -eq 2) { $newDtPart = "dt0.7" }
            elseif ($targetSpeed -eq 3) { $newDtPart = "dt0.6" }
        }

        return "${newUid}_${desc}_${newDtPart}.wav"
    }
    return $name
}

# 1. 씬 폴더 검색
$sceneDirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '^(\d+)번 씬 wav폴더$' } | Sort-Object { [int]($_.Name -replace '\D') }

Write-Host "Found $($sceneDirs.Count) scene folders to process:"
foreach ($d in $sceneDirs) {
    Write-Host " - $($d.Name)"
}

$totalProcessed = 0
$totalSpeed2Created = 0
$totalSpeed3Created = 0

foreach ($dir in $sceneDirs) {
    $dirName = $dir.Name
    $sceneNum = ($dirName -replace '번 씬 wav폴더', '')
    Write-Host "`n>>> Processing: $dirName (Scene $sceneNum) <<<"

    $speed1Dir = Join-Path $root "${sceneNum}번 씬 스피드1 wav폴더"
    $speed2Dir = Join-Path $root "${sceneNum}번 씬 스피드2 wav폴더"
    $speed3Dir = Join-Path $root "${sceneNum}번 씬 스피드3 wav폴더"

    # 1. 기존 폴더를 '스피드1' 폴더로 리네임 (이미 변경된 경우 대비)
    if (Test-Path $dir.FullName) {
        if (-not (Test-Path $speed1Dir)) {
            Write-Host "  [Rename] '$dirName' -> '${sceneNum}번 씬 스피드1 wav폴더'"
            Move-Item -Path $dir.FullName -Destination $speed1Dir
        }
    }

    # 2. 스피드 2 & 3 디렉토리 생성
    if (-not (Test-Path $speed2Dir)) {
        New-Item -ItemType Directory -Path $speed2Dir | Out-Null
    }
    if (-not (Test-Path $speed3Dir)) {
        New-Item -ItemType Directory -Path $speed3Dir | Out-Null
    }

    # 3. 스피드 1의 파일들을 읽어서 스피드 2 및 스피드 3으로 변환 복사
    $sourceFiles = Get-ChildItem -Path $speed1Dir -Filter "*.wav"
    Write-Host "  Copying & Transforming $($sourceFiles.Count) files into Speed 2 & 3..."

    foreach ($file in $sourceFiles) {
        $origName = $file.Name

        # 스피드 2 변환 및 복사
        $s2Name = Transform-WavName $origName 2
        $s2Dest = Join-Path $speed2Dir $s2Name
        if (-not (Test-Path $s2Dest)) {
            Copy-Item -Path $file.FullName -Destination $s2Dest
            $totalSpeed2Created++
        }

        # 스피드 3 변환 및 복사
        $s3Name = Transform-WavName $origName 3
        $s3Dest = Join-Path $speed3Dir $s3Name
        if (-not (Test-Path $s3Dest)) {
            Copy-Item -Path $file.FullName -Destination $s3Dest
            $totalSpeed3Created++
        }

        $totalProcessed++
    }
}

Write-Host "`n=========================================================="
Write-Host "File generation completed!"
Write-Host "Total original files processed: $totalProcessed"
Write-Host "Speed 2 files created: $totalSpeed2Created"
Write-Host "Speed 3 files created: $totalSpeed3Created"

# 4. hwa2.bms 파일 경로 동기화 (기존 'X번 씬 wav폴더' -> 'X번 씬 스피드1 wav폴더')
if (Test-Path $bmsPath) {
    Write-Host "`nUpdating BMS chart headers in '$bmsPath'..."
    $bmsBackup = "$bmsPath.speed_split.bak"
    Copy-Item -Path $bmsPath -Destination $bmsBackup -Force
    Write-Host "Backup created: $bmsBackup"

    $bmsContent = [System.IO.File]::ReadAllText($bmsPath, [System.Text.Encoding]::UTF8)
    # 정규식으로 'X번 씬 wav폴더\' 를 'X번 씬 스피드1 wav폴더\' 로 일괄 교체
    $updatedBms = [System.Text.RegularExpressions.Regex]::Replace(
        $bmsContent,
        '(#WAV[0-9A-Za-z]{2,3}\s+)(\d+번 씬 )wav폴더(\\)',
        '${1}${2}스피드1 wav폴더${3}'
    )

    [System.IO.File]::WriteAllText($bmsPath, $updatedBms, [System.Text.Encoding]::UTF8)
    Write-Host "BMS #WAV headers updated to '스피드1 wav폴더' successfully!"
}

Write-Host "`nALL TASKS COMPLETED SUCCESSFULLY!"
