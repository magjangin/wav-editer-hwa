$ErrorActionPreference = "Stop"

$root = "H:\muse dash hwa\hwa\Star Trace"
$bmsPath = Join-Path $root "hwa2.bms"

Write-Host "=========================================================="
Write-Host "Star Trace Speed Tagging & BMS #WAV Full Integration"
Write-Host "Target Root: $root"
Write-Host "=========================================================="

function Get-SpeedTaggedName($name, $speedNum) {
    if ($name -match '스피드[123]') {
        return $name
    }

    if ($name -match '^([0-9A-Za-z]{6})_(.+?)(_boss)?_(dt[0-9\.]+)\.wav$') {
        $uid = $Matches[1]
        $desc = $Matches[2]
        $bossPart = $Matches[3]
        $dtPart = $Matches[4]

        $xx = $uid.Substring(2, 2)
        # 씬 전환 및 보스 동작은 속도가 없으므로 태그 미적용
        if ($uid.StartsWith("0004") -or $xx -eq "01") {
            return $name
        }

        return "${uid}_${desc} 스피드${speedNum}${bossPart}_${dtPart}.wav"
    }

    return $name
}

function ConvertTo-Base36($num) {
    $chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
    $c3 = $chars[$num % 36]
    $num = [math]::Floor($num / 36)
    $c2 = $chars[$num % 36]
    $num = [math]::Floor($num / 36)
    $c1 = $chars[$num % 36]
    return "$c1$c2$c3"
}

function ConvertFrom-Base36($str) {
    $chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
    $val = 0
    foreach ($c in $str.ToCharArray()) {
        $idx = $chars.IndexOf($c)
        if ($idx -lt 0) { return -1 }
        $val = $val * 36 + $idx
    }
    return $val
}

# 1. 디스크 상의 모든 스피드 폴더 내 WAV 파일 리네임
$speedDirs = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '^(\d+)번 씬 스피드([123]) wav폴더$' } | Sort-Object { [int]($_.Name -replace '\D') }

$fileRenameCount = 0
$fileMapOldToNew = @{}

foreach ($dir in $speedDirs) {
    $dir.Name -match '스피드([123])' | Out-Null
    $speedNum = [int]$Matches[1]

    $files = Get-ChildItem -Path $dir.FullName -Filter "*.wav"
    foreach ($file in $files) {
        $newName = Get-SpeedTaggedName $file.Name $speedNum
        if ($newName -ne $file.Name) {
            $destPath = Join-Path $dir.FullName $newName
            $relOld = Join-Path $dir.Name $file.Name
            $relNew = Join-Path $dir.Name $newName

            $fileMapOldToNew[$relOld] = $relNew
            $fileMapOldToNew[$file.Name] = $newName

            Move-Item -Path $file.FullName -Destination $destPath
            $fileRenameCount++
        }
    }
}

Write-Host "Renamed $fileRenameCount WAV files to include '스피드1~3' in Korean names."

# 2. BMS 파일 업데이트 및 스피드 2, 3 신규 키음 추가
if (Test-Path $bmsPath) {
    Write-Host "`nUpdating '$bmsPath'..."
    $backupPath = "$bmsPath.speed_tags.bak"
    Copy-Item -Path $bmsPath -Destination $backupPath -Force
    Write-Host "Backup created: $backupPath"

    $lines = [System.IO.File]::ReadAllLines($bmsPath, [System.Text.Encoding]::UTF8)

    $headerLines = New-Object System.Collections.Generic.List[string]
    $existingWavLines = New-Object System.Collections.Generic.List[string]
    $chartLines = New-Object System.Collections.Generic.List[string]

    $maxBase36Num = 0
    $registeredPaths = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)

    $inWavSection = $false
    $pastWavSection = $false

    foreach ($line in $lines) {
        if ($line -match "^#WAV([0-9A-Za-z]{3})\s+(.+)$") {
            $inWavSection = $true
            $key = $Matches[1]
            $val = $Matches[2].Trim()

            $num = ConvertFrom-Base36 $key
            if ($num -gt $maxBase36Num) { $maxBase36Num = $num }

            # 기존 스피드 1 경로가 리네임 매핑에 있으면 갱신
            $fileNameOnly = [System.IO.Path]::GetFileName($val)
            if ($fileMapOldToNew.ContainsKey($val)) {
                $val = $fileMapOldToNew[$val]
            } elseif ($fileMapOldToNew.ContainsKey($fileNameOnly)) {
                $dirPart = [System.IO.Path]::GetDirectoryName($val)
                $val = if ([string]::IsNullOrEmpty($dirPart)) { $fileMapOldToNew[$fileNameOnly] } else { Join-Path $dirPart $fileMapOldToNew[$fileNameOnly] }
            }

            $registeredPaths.Add($val) | Out-Null
            $existingWavLines.Add("#WAV$key $val")
        }
        else {
            if ($inWavSection) {
                # WAV 섹션 이후
                $pastWavSection = $true
                $chartLines.Add($line)
            } else {
                # WAV 섹션 이전 (TITLE, BPM 등)
                $headerLines.Add($line)
            }
        }
    }

    Write-Host "Existing #WAV entries: $($existingWavLines.Count) (Max Key: $(ConvertTo-Base36 $maxBase36Num))"

    # 스피드 2 및 스피드 3의 파일들을 새로운 #WAV 키로 추가
    $newWavLines = New-Object System.Collections.Generic.List[string]
    $currentKeyNum = $maxBase36Num + 1

    # 씬 번호 순서대로 스피드 2, 스피드 3 파일들 수집
    for ($speed = 2; $speed -le 3; $speed++) {
        $speedFolders = Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match "^(\d+)번 씬 스피드${speed} wav폴더$" } | Sort-Object { [int]($_.Name -replace '\D') }

        foreach ($folder in $speedFolders) {
            $wavFiles = Get-ChildItem -Path $folder.FullName -Filter "*.wav" | Sort-Object Name
            foreach ($wf in $wavFiles) {
                $relPath = Join-Path $folder.Name $wf.Name
                # 아직 등록되지 않은 파일만 추가 (보스 등장 등 스피드1과 동일한 중복 파일 방지)
                if (-not $registeredPaths.Contains($relPath)) {
                    $keyStr = ConvertTo-Base36 $currentKeyNum
                    $newWavLines.Add("#WAV$keyStr $relPath")
                    $registeredPaths.Add($relPath) | Out-Null
                    $currentKeyNum++
                }
            }
        }
    }

    Write-Host "New #WAV entries created for Speed 2 & 3: $($newWavLines.Count)"

    # 파일 전체 조립
    $finalLines = New-Object System.Collections.Generic.List[string]
    $finalLines.AddRange($headerLines)
    $finalLines.AddRange($existingWavLines)
    $finalLines.AddRange($newWavLines)
    $finalLines.AddRange($chartLines)

    [System.IO.File]::WriteAllLines($bmsPath, $finalLines, [System.Text.Encoding]::UTF8)

    Write-Host "`nTotal #WAV definitions in BMS: $($existingWavLines.Count + $newWavLines.Count)"
    Write-Host "BMS file updated successfully!"
}

Write-Host "`nALL DONE!"
