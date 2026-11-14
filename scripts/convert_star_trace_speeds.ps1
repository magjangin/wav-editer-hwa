<#
.SYNOPSIS
    씬 폴더의 WAV 키음을 속도 1~3(스피드1~3) 폴더로 분할·복제하고 BMS #WAV 경로를 동기화합니다.
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
Write-Host "Star Trace Scene Speed Splitter & Renamer (Speed 1, 2, 3)"
Write-Host "Target Root: $Root"
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

        # 최종 실기 검증된 속도별 dt 규격 (일반: 1.48/1.28/1.18, 홀드: 1.47/1.26/1.10, 보스 발사체: 0.8/0.76/0.6)
        $newDtPart = switch ($dtPart) {
            "dt1.48" { @("dt1.48", "dt1.28", "dt1.18")[$targetSpeed - 1] }
            "dt1.47" { @("dt1.47", "dt1.26", "dt1.10")[$targetSpeed - 1] }
            "dt0.8"  { @("dt0.8",  "dt0.76", "dt0.6")[$targetSpeed - 1] }
            default  { $dtPart }
        }

        return "${newUid}_${desc}_${newDtPart}.wav"
    }
    return $name
}

# 1. 씬 폴더 검색
$sceneDirs = @(Get-ChildItem -LiteralPath $Root -Directory |
    Where-Object { $_.Name -match '^(\d+)번 씬 wav폴더$' } |
    Sort-Object { if ($_.Name -match '^(\d+)') { [int]$Matches[1] } else { 0 } })

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

    $speed1Dir = Join-Path $Root "${sceneNum}번 씬 스피드1 wav폴더"
    $speed2Dir = Join-Path $Root "${sceneNum}번 씬 스피드2 wav폴더"
    $speed3Dir = Join-Path $Root "${sceneNum}번 씬 스피드3 wav폴더"

    # 1. 기존 폴더를 '스피드1' 폴더로 리네임 (이미 변경된 경우 대비)
    if ((Test-Path -LiteralPath $dir.FullName) -and -not (Test-Path -LiteralPath $speed1Dir)) {
        if ($PSCmdlet.ShouldProcess($dir.FullName, "Rename to ${sceneNum}번 씬 스피드1 wav폴더")) {
            Move-Item -LiteralPath $dir.FullName -Destination $speed1Dir
        }
    }

    # 2. 스피드 2 & 3 디렉토리 생성
    foreach ($targetDir in @($speed2Dir, $speed3Dir)) {
        if (-not (Test-Path -LiteralPath $targetDir) -and $PSCmdlet.ShouldProcess($targetDir, "Create directory")) {
            New-Item -ItemType Directory -Path $targetDir | Out-Null
        }
    }

    # 3. 스피드 1의 파일들을 읽어서 스피드 2 및 스피드 3으로 변환 복사
    $sourceRoot = if (Test-Path -LiteralPath $speed1Dir) { $speed1Dir } else { $dir.FullName }
    $sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Filter "*.wav" -File)
    Write-Host "  Copying & Transforming $($sourceFiles.Count) files into Speed 2 & 3..."

    foreach ($file in $sourceFiles) {
        $origName = $file.Name

        $s2Name = Transform-WavName $origName 2
        $s2Dest = Join-Path $speed2Dir $s2Name
        if (-not (Test-Path -LiteralPath $s2Dest)) {
            if ($PSCmdlet.ShouldProcess($s2Dest, "Copy Speed 2 from $origName")) {
                Copy-Item -LiteralPath $file.FullName -Destination $s2Dest
            }
            $totalSpeed2Created++
        }

        $s3Name = Transform-WavName $origName 3
        $s3Dest = Join-Path $speed3Dir $s3Name
        if (-not (Test-Path -LiteralPath $s3Dest)) {
            if ($PSCmdlet.ShouldProcess($s3Dest, "Copy Speed 3 from $origName")) {
                Copy-Item -LiteralPath $file.FullName -Destination $s3Dest
            }
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

# 4. BMS 파일 경로 동기화 (기존 'X번 씬 wav폴더' -> 'X번 씬 스피드1 wav폴더', BOM·줄바꿈 보존)
if (Test-Path -LiteralPath $bmsPath) {
    Write-Host "`nUpdating BMS chart headers in '$bmsPath'..."
    $bytes = [System.IO.File]::ReadAllBytes($bmsPath)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $encoding = New-Object System.Text.UTF8Encoding($hasBom, $true)
    $offset = if ($hasBom) { 3 } else { 0 }
    $bmsContent = $encoding.GetString($bytes, $offset, $bytes.Length - $offset)

    $updatedBms = [System.Text.RegularExpressions.Regex]::Replace(
        $bmsContent,
        '(?im)^(#WAV[0-9A-Za-z]{2,3}[ \t]+)(\d+번 씬 )wav폴더([\\/])',
        '${1}${2}스피드1 wav폴더${3}'
    )

    if ($updatedBms -ne $bmsContent -and $PSCmdlet.ShouldProcess($bmsPath, "Update #WAV folder prefixes to 스피드1 wav폴더")) {
        $bmsBackup = "$bmsPath.speed_split.bak"
        Copy-Item -LiteralPath $bmsPath -Destination $bmsBackup -Force
        Write-Host "Backup created: $bmsBackup"

        $body = $encoding.GetBytes($updatedBms)
        $preamble = $encoding.GetPreamble()
        if ($preamble.Length -gt 0) {
            $out = New-Object byte[] ($preamble.Length + $body.Length)
            [System.Buffer]::BlockCopy($preamble, 0, $out, 0, $preamble.Length)
            [System.Buffer]::BlockCopy($body, 0, $out, $preamble.Length, $body.Length)
        } else {
            $out = $body
        }
        [System.IO.File]::WriteAllBytes($bmsPath, $out)
        Write-Host "BMS #WAV headers updated to '스피드1 wav폴더' successfully!"
    }
}

Write-Host "`nALL TASKS COMPLETED SUCCESSFULLY!"