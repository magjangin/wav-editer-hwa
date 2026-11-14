<#
.SYNOPSIS
    스피드2·3 폴더에 복제된 "속도 없는" 키음(#WAV)을 스피드1 키 하나로 합칩니다.

.DESCRIPTION
    convert_star_trace_speeds.ps1(2026-11-14 이전)은 보스 등장·퇴장·원거리 액션(uid xx=01)과
    씬 전환(0004xx)까지 스피드2·3 폴더로 복사했고, apply_speed_tags_and_bms.ps1은 폴더가 다르다는
    이유로 그 파일들을 새 #WAV 키로 또 등록했습니다. 그래서 키음 팔레트에 같은 보스 동작이
    두 세트(예: #0BA 와 #0SV = 080107 원거리1 액션 시작)로 잡히고, 채보 안에서도 시작은 한 세트,
    끝은 다른 세트 키로 섞여 찍혔습니다.

    이 스크립트는 파일명이 같은 속도 없는 키음 중 가장 먼저 등록된 키(스피드1 쪽)만 남기고,
    채보 데이터 줄에서 나머지 키를 그 키로 바꾼 뒤 중복 #WAV 줄을 지웁니다.
    WAV 파일 자체는 지우지 않습니다. BOM·줄바꿈은 원본대로 두고 <bms>.speed_dedupe.bak 백업을 만듭니다.

.EXAMPLE
    .\Remove-SpeedDuplicateWavs.ps1 -Root "H:\muse dash hwa\hwa" -WhatIf
    .\Remove-SpeedDuplicateWavs.ps1 -Root "H:\muse dash hwa\hwa"
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    # 곡 폴더 하나 또는 곡 폴더들이 들어 있는 상위 폴더
    [string]$Root = "H:\muse dash hwa\hwa"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
    throw "폴더가 없습니다: $Root"
}

function Test-SpeedIndependentWav($fileName) {
    if ($fileName -match '^([0-9A-Za-z]{6})_') {
        $uid = $Matches[1]
        return $uid.StartsWith("0004") -or $uid.Substring(2, 2) -eq "01"
    }
    return $false
}

# 오브젝트 목록이 아닌 채널: 02 마디 길이, 03 BPM(16진), 08 확장 BPM, 09 STOP
$nonObjectChannels = @("02", "03", "08", "09")

$bmsFiles = @(Get-ChildItem -LiteralPath $Root -Filter "*.bms" -File -Recurse)
$totalRemoved = 0

foreach ($bms in $bmsFiles) {
    $bytes = [System.IO.File]::ReadAllBytes($bms.FullName)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $encoding = New-Object System.Text.UTF8Encoding($hasBom, $true)
    $offset = if ($hasBom) { 3 } else { 0 }
    try {
        $text = $encoding.GetString($bytes, $offset, $bytes.Length - $offset)
    } catch {
        Write-Warning "UTF-8이 아니라 건너뜀: $($bms.FullName)"
        continue
    }
    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $lines = $text -split '\r?\n'

    # 1. 파일명 → 처음 등록된 키, 중복 키 → 남길 키
    $firstKeyByName = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $remap = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $keyWidth = 0
    foreach ($line in $lines) {
        if ($line -match '^#WAV([0-9A-Za-z]{2,3})[ \t]+(.+)$') {
            $key = $Matches[1].ToUpperInvariant()
            $keyWidth = [Math]::Max($keyWidth, $key.Length)
            $fileName = [System.IO.Path]::GetFileName($Matches[2].Trim())
            if (-not (Test-SpeedIndependentWav $fileName)) { continue }
            if ($firstKeyByName.ContainsKey($fileName)) {
                $remap[$key] = $firstKeyByName[$fileName]
            } else {
                $firstKeyByName[$fileName] = $key
            }
        }
    }

    if ($remap.Count -eq 0) { continue }

    # 2. 데이터 줄의 키를 바꾸고 중복 #WAV 줄을 뺀다
    $replacedObjects = 0
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line -match '^#WAV([0-9A-Za-z]{2,3})[ \t]+' -and $remap.ContainsKey($Matches[1].ToUpperInvariant())) {
            continue
        }
        if ($line -match '^(#\d{3}([0-9A-Za-z]{2}):)(.*)$' -and $nonObjectChannels -notcontains $Matches[2]) {
            $prefix = $Matches[1]
            $data = $Matches[3]
            if ($data.Length % $keyWidth -eq 0) {
                $sb = New-Object System.Text.StringBuilder
                for ($i = 0; $i -lt $data.Length; $i += $keyWidth) {
                    $obj = $data.Substring($i, $keyWidth)
                    $upper = $obj.ToUpperInvariant()
                    if ($remap.ContainsKey($upper)) {
                        [void]$sb.Append($remap[$upper])
                        $replacedObjects++
                    } else {
                        [void]$sb.Append($obj)
                    }
                }
                $line = $prefix + $sb.ToString()
            }
        }
        $out.Add($line)
    }

    Write-Host "$($bms.FullName): 중복 #WAV $($remap.Count)개 제거, 채보 오브젝트 $($replacedObjects)개 키 교체"
    $totalRemoved += $remap.Count

    if ($PSCmdlet.ShouldProcess($bms.FullName, "Remove $($remap.Count) duplicate #WAV entries")) {
        Copy-Item -LiteralPath $bms.FullName -Destination "$($bms.FullName).speed_dedupe.bak" -Force
        $body = $encoding.GetBytes([string]::Join($newline, $out))
        $preamble = $encoding.GetPreamble()
        $result = New-Object byte[] ($preamble.Length + $body.Length)
        [System.Buffer]::BlockCopy($preamble, 0, $result, 0, $preamble.Length)
        [System.Buffer]::BlockCopy($body, 0, $result, $preamble.Length, $body.Length)
        [System.IO.File]::WriteAllBytes($bms.FullName, $result)
    }
}

Write-Host "`n합계: 중복 #WAV $($totalRemoved)개"
