<#
.SYNOPSIS
    씬 폴더의 WAV 키음 파일명 dt 접미사(_dtX.wav → _dtY.wav)를 일괄 변경하고 BMS #WAV 정의를 동기화합니다.

.DESCRIPTION
    scripts/history/ 에 있는 일회성 update_*_dt*.ps1 스크립트들의 공통 로직을 하나로 합친 범용 버전입니다.
    - -WhatIf 로 실행하면 디스크를 건드리지 않고 대상 파일 수·BMS 행 수만 보고합니다 (inspect/verify 대용).
    - 대상 이름이 이미 존재하면 아무것도 바꾸기 전에 중단합니다.
    - BMS는 원본 인코딩(BOM 유무)과 줄바꿈(LF/CRLF)을 그대로 유지하고, 변경 전 <BMS>.<BackupTag>.bak 백업을 만듭니다.

.EXAMPLE
    # 스피드 3 일반 노트 dt1.16 -> dt1.18 (먼저 -WhatIf 로 확인)
    .\Rename-DtSuffix.ps1 -Root "H:\muse dash hwa\hwa\Star Trace" -FolderPattern '스피드3 wav폴더$' -From 1.16 -To 1.18 -WhatIf

.EXAMPLE
    # 특정 UID(보스 발사체)만: summertime 5번 씬 dt0.8 -> dt1.07
    .\Rename-DtSuffix.ps1 -Root "H:\muse dash hwa\hwa\麦吉_Maggie × 盖盖Nyan - summertime (Arrange ver.)" `
        -FolderPattern '^5번 씬 wav폴더$' -NamePattern '^(050701|050704|050801|050804)' -From 0.8 -To 1.07
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    # 차트 루트 폴더 (BMS 파일과 씬 wav 폴더들이 있는 곳)
    [Parameter(Mandatory)][string]$Root,
    # 바꿀 dt 값 (예: 1.16)
    [Parameter(Mandatory)][string]$From,
    # 새 dt 값 (예: 1.18)
    [Parameter(Mandatory)][string]$To,
    # 대상 하위 폴더 이름 정규식 (예: '스피드3 wav폴더$')
    [string]$FolderPattern = 'wav폴더$',
    # 대상 파일명 추가 조건 정규식 (예: '^(xx0203|xx0206)')
    [string]$NamePattern = '.*',
    # 동기화할 BMS 파일 이름
    [string]$BmsName = 'hwa2.bms',
    # 백업 파일 꼬리표 (기본: dt<To>, 예: hwa2.bms.dt118.bak)
    [string]$BackupTag = ('dt' + ($To -replace '\.', ''))
)

$ErrorActionPreference = 'Stop'

$fromSuffixRegex = [regex]::Escape("_dt$From.wav") + '$'
$toSuffix = "_dt$To.wav"

if (-not (Test-Path -LiteralPath $Root -PathType Container)) { throw "루트 폴더가 없습니다: $Root" }

# 1. 대상 WAV 수집 + 충돌 사전 검사 (하나라도 충돌하면 아무것도 바꾸지 않음)
$dirs = @(Get-ChildItem -LiteralPath $Root -Directory | Where-Object { $_.Name -match $FolderPattern })
$targets = @(foreach ($dir in $dirs) {
    Get-ChildItem -LiteralPath $dir.FullName -File |
        Where-Object { $_.Name -match $fromSuffixRegex -and $_.Name -match $NamePattern }
})

$plans = @(foreach ($file in $targets) {
    $dest = Join-Path $file.DirectoryName ($file.Name -replace $fromSuffixRegex, $toSuffix)
    if (Test-Path -LiteralPath $dest) { throw "대상 파일이 이미 존재합니다 (중단): $dest" }
    [pscustomobject]@{ Source = $file.FullName; Dest = $dest }
})

Write-Host "대상 폴더 $($dirs.Count)개, dt$From -> dt$To 대상 WAV $($targets.Count)개"

# 2. 파일 리네임
$renamed = 0
foreach ($plan in $plans) {
    if ($PSCmdlet.ShouldProcess($plan.Source, "Rename to $(Split-Path -Leaf $plan.Dest)")) {
        Move-Item -LiteralPath $plan.Source -Destination $plan.Dest
        $renamed++
    }
}
Write-Host "WAV 리네임: $($renamed)개"

# 3. BMS #WAV 동기화 (폴더·파일명 조건이 같은 행만)
$bmsPath = Join-Path $Root $BmsName
if (-not (Test-Path -LiteralPath $bmsPath)) {
    Write-Warning "BMS 파일이 없어 동기화를 건너뜁니다: $bmsPath"
    return
}

$bytes = [System.IO.File]::ReadAllBytes($bmsPath)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$encoding = New-Object System.Text.UTF8Encoding($hasBom, $true)
$offset = if ($hasBom) { 3 } else { 0 }
$text = $encoding.GetString($bytes, $offset, $bytes.Length - $offset)

$state = @{ Count = 0 }
$lineRegex = New-Object System.Text.RegularExpressions.Regex('^(#WAV[0-9A-Za-z]{2,3}[ \t]+)(.+?)[ \t]*(?=\r?$)', 'Multiline, IgnoreCase')
$updated = $lineRegex.Replace($text, [System.Text.RegularExpressions.MatchEvaluator] {
    param($m)
    $path = $m.Groups[2].Value
    $parts = $path -split '[\\/]'
    $fileName = $parts[-1]
    $folder = if ($parts.Count -ge 2) { $parts[-2] } else { '' }
    if ($folder -match $FolderPattern -and $fileName -match $fromSuffixRegex -and $fileName -match $NamePattern) {
        $state.Count++
        return $m.Groups[1].Value + ($path -replace $fromSuffixRegex, $toSuffix)
    }
    return $m.Value
})

Write-Host "BMS 대상 #WAV 행: $($state.Count)개 ($bmsPath)"

if ($state.Count -gt 0 -and $PSCmdlet.ShouldProcess($bmsPath, "Update $($state.Count) #WAV lines")) {
    $backupPath = "$bmsPath.$BackupTag.bak"
    Copy-Item -LiteralPath $bmsPath -Destination $backupPath -Force
    $body = $encoding.GetBytes($updated)
    $preamble = $encoding.GetPreamble()
    if ($preamble.Length -gt 0) {
        $out = New-Object byte[] ($preamble.Length + $body.Length)
        [System.Buffer]::BlockCopy($preamble, 0, $out, 0, $preamble.Length)
        [System.Buffer]::BlockCopy($body, 0, $out, $preamble.Length, $body.Length)
    } else {
        $out = $body
    }
    [System.IO.File]::WriteAllBytes($bmsPath, $out)
    Write-Host "BMS 갱신 완료 (백업: $backupPath)"
}
