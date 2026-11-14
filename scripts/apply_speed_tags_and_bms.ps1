<#
.SYNOPSIS
    스피드1~3 WAV 파일에 한국어 속도 태그를 부여하고 BMS 파일의 #WAV 정의를 스피드 2·3까지 확장합니다.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Root = "H:\muse dash hwa\hwa\Star Trace",
    [string]$BmsName = "hwa2.bms"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
    throw "루트 폴더가 없습니다: $Root"
}

$bmsPath = Join-Path $Root $BmsName

Write-Host "=========================================================="
Write-Host "Star Trace Speed Tagging & BMS #WAV Full Integration"
Write-Host "Target Root: $Root"
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
$speedDirs = @(Get-ChildItem -LiteralPath $Root -Directory |
    Where-Object { $_.Name -match '^(\d+)번 씬 스피드([123]) wav폴더$' } |
    Sort-Object { if ($_.Name -match '^(\d+)번 씬 스피드([123])') { [int]$Matches[1] * 10 + [int]$Matches[2] } else { 0 } })

$fileRenameCount = 0
$fileMapOldToNew = New-Object 'System.Collections.Generic.Dictionary[string, string]' ([System.StringComparer]::OrdinalIgnoreCase)

foreach ($dir in $speedDirs) {
    $dir.Name -match '스피드([123])' | Out-Null
    $speedNum = [int]$Matches[1]

    $files = @(Get-ChildItem -LiteralPath $dir.FullName -Filter "*.wav" -File)
    foreach ($file in $files) {
        $newName = Get-SpeedTaggedName $file.Name $speedNum
        if ($newName -ne $file.Name) {
            $destPath = Join-Path $dir.FullName $newName
            $relOld = Join-Path $dir.Name $file.Name
            $relNew = Join-Path $dir.Name $newName

            $fileMapOldToNew[$relOld] = $relNew
            $fileMapOldToNew[$file.Name] = $newName

            if ($PSCmdlet.ShouldProcess($file.FullName, "Rename to $newName")) {
                Move-Item -LiteralPath $file.FullName -Destination $destPath
            }
            $fileRenameCount++
        }
    }
}

Write-Host "Renamed $fileRenameCount WAV files to include '스피드1~3' in Korean names."

# 2. BMS 파일 업데이트 및 스피드 2, 3 신규 키음 추가 (BOM·줄바꿈 보존)
if (Test-Path -LiteralPath $bmsPath) {
    Write-Host "`nUpdating '$bmsPath'..."
    $bytes = [System.IO.File]::ReadAllBytes($bmsPath)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $encoding = New-Object System.Text.UTF8Encoding($hasBom, $true)
    $offset = if ($hasBom) { 3 } else { 0 }
    $rawText = $encoding.GetString($bytes, $offset, $bytes.Length - $offset)
    $newline = if ($rawText.Contains("`r`n")) { "`r`n" } else { "`n" }
    $lines = $rawText -split '\r?\n'

    $headerLines = New-Object System.Collections.Generic.List[string]
    $existingWavLines = New-Object System.Collections.Generic.List[string]
    $chartLines = New-Object System.Collections.Generic.List[string]

    $maxBase36Num = 0
    $registeredPaths = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)

    $inWavSection = $false

    foreach ($line in $lines) {
        if ($line -match "^#WAV([0-9A-Za-z]{3})([ \t]+)(.+)$") {
            $inWavSection = $true
            $key = $Matches[1]
            $sep = $Matches[2]
            $val = $Matches[3].Trim()

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
            $existingWavLines.Add("#WAV$key$sep$val")
        }
        else {
            if ($inWavSection) {
                $chartLines.Add($line)
            } else {
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
        $speedFolders = @(Get-ChildItem -LiteralPath $Root -Directory |
            Where-Object { $_.Name -match "^(\d+)번 씬 스피드${speed} wav폴더$" } |
            Sort-Object { if ($_.Name -match '^(\d+)') { [int]$Matches[1] } else { 0 } })

        foreach ($folder in $speedFolders) {
            $wavFiles = @(Get-ChildItem -LiteralPath $folder.FullName -Filter "*.wav" -File | Sort-Object Name)
            foreach ($wf in $wavFiles) {
                $relPath = Join-Path $folder.Name $wf.Name
                if ($fileMapOldToNew.ContainsKey($relPath)) {
                    $relPath = $fileMapOldToNew[$relPath]
                }
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

    if ($PSCmdlet.ShouldProcess($bmsPath, "Write $($existingWavLines.Count + $newWavLines.Count) #WAV entries")) {
        $backupPath = "$bmsPath.speed_tags.bak"
        Copy-Item -LiteralPath $bmsPath -Destination $backupPath -Force
        Write-Host "Backup created: $backupPath"

        # 파일 전체 조립
        $finalLines = New-Object System.Collections.Generic.List[string]
        $finalLines.AddRange($headerLines)
        $finalLines.AddRange($existingWavLines)
        $finalLines.AddRange($newWavLines)
        $finalLines.AddRange($chartLines)

        $updatedText = [string]::Join($newline, $finalLines)
        $body = $encoding.GetBytes($updatedText)
        $preamble = $encoding.GetPreamble()
        if ($preamble.Length -gt 0) {
            $out = New-Object byte[] ($preamble.Length + $body.Length)
            [System.Buffer]::BlockCopy($preamble, 0, $out, 0, $preamble.Length)
            [System.Buffer]::BlockCopy($body, 0, $out, $preamble.Length, $body.Length)
        } else {
            $out = $body
        }
        [System.IO.File]::WriteAllBytes($bmsPath, $out)

        Write-Host "`nTotal #WAV definitions in BMS: $($existingWavLines.Count + $newWavLines.Count)"
        Write-Host "BMS file updated successfully!"
    }
}

Write-Host "`nALL DONE!"
