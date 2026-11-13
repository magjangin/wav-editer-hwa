# PowerRename Avalonia (WAV Editor & BMS Sync Mode)

Windows PowerToys의 **PowerRename** 기능을 Avalonia UI (C# / .NET 10 / MVVM)로 클린 룸 재구현하고, **리듬 게임 커스텀 차트(BMS) 및 대량 WAV 에셋 관리에 특화된 BMS 자동 동기화 기능**을 추가한 배치 리네이머입니다.

---

## 🚀 주요 기능

1. **WAV 에셋 특화 & 대형 UI 레이아웃**
   - 창 크기 대폭 확장 (`1420 x 860`) 및 긴 한국어 파일명 겹침 없는 1:1 대칭 그리드
   - `🎵 WAV 파일만 대상 (*.wav)` 기본 활성화로 BMS/PNG/OGG 등 차트 핵심 파일 안전 보호
   - 하위 씬 폴더(예: `1번 씬 wav폴더`, `2번 씬...`) 재귀 탐색 기본 활성화

2. **🎼 BMS 차트 동기화 (킬러 기능)**
   - WAV 파일이 있는 폴더나 상위 폴더의 `*.bms` 파일(예: `hwa2.bms`)을 **자동 감지 및 연동**
   - WAV 파일 이름을 변경할 때 BMS 내부의 `#WAVxxx <경로\파일명>`도 **실시간 자동 치환 및 갱신**
   - 원본 BMS 파일 자동 백업 (`*.bms.bak`) 생성 및 실행 취소(Undo) 시 BMS까지 원복
   - 원본 인코딩(UTF-8 BOM 유무, CP949)·줄바꿈(LF/CRLF) 그대로 보존

3. **강력한 정규식 및 치환 엔진**
   - 정규식 그룹 캡처 (`$1`, `$2`), 일반 치환, 대/소문자 구분
   - 적용 범위: 파일 이름 + 확장명 / 파일 이름만 / 확장명만
   - 텍스트 서식 변환 (`aa`, `AA`, `Aa`, `Aa Aa`, `⟲`)

4. **실시간 미리보기 & 안전망**
   - 변경될 새 파일명 실시간 확인
   - 동일 폴더 내 이름 충돌(Conflict) 사전 감지
   - NTFS 2-Pass Move로 Windows 대소문자 리네임 충돌 방지
   - 작업 실행 취소 (Undo)

---

## 🤖 AI Agent & MCP (Model Context Protocol) 연동

Antigravity 및 Claude Code와 같은 AI 코딩 어시스턴트가 직접 호출하여 대량 WAV 파일 및 BMS 차트를 점검하고 리네임할 수 있도록 **MCP 서버**가 내장되어 있습니다.

- **실행 명령:** `PowerRenameAvalonia.exe mcp` (stdio JSON-RPC 2.0)
- **제공 도구 (Tools):**
  1. `scan_wav_assets`: 대상 폴더 내 모든 WAV 파일 및 연동된 BMS 차트(#WAV 정의)의 누락/매칭 상태 스캔 및 진단
  2. `preview_rename`: 실제 파일 변경 없이 정규식/문자열 치환 및 대소문자 변환 결과를 안전하게 시뮬레이션
  3. `apply_rename`: 디스크 파일 일괄 리네임 + BMS 차트 파일(#WAV) 내부 정의 일괄 자동 동기화
  4. `sync_bms_chart`: BMS 파일의 #WAV 정의를 제공된 치환 맵에 따라 직접 갱신

---

## 🛠️ 빌드 및 실행

```bash
# 디버그 실행
dotnet run

# 자체 무결성 테스트 (엔진 + 파일 이동 + BMS 동기화 + GUI 흐름, 31개)
dotnet run -- --test

# 대량 WAV 성능 측정 (기본 2,000개)
dotnet run -c Release -- --bench

# 릴리즈 배포 (MCP 등록 경로)
dotnet publish -c Release -o Publish
```

## 📜 dt 일괄 변경 스크립트

```powershell
# 먼저 -WhatIf 로 대상 파일/BMS 행 수 확인 후 실행
.\scripts\Rename-DtSuffix.ps1 -Root "H:\muse dash hwa\hwa\Star Trace" -FolderPattern '스피드3 wav폴더$' -From 1.16 -To 1.18 -WhatIf
```

이미 실행한 일회성 스크립트는 `scripts/history/`에 기록용으로 보관되어 있습니다.
