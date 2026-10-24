# AGENTS.md - PowerRename Avalonia Project

## 1. 개요 및 설계 의도 (Rationale)
- **목적:** Windows PowerToys의 `PowerRename` 기능을 Avalonia UI (C# / .NET 10 / MVVM) 기반으로 클린 룸 재구현한 독립 일괄 파일 이름 변경 유틸리티.
- **주요 활용 처:** Muse Dash 커스텀 차트 등 대량의 오디오 에셋(WAV/OGG 등), BMS 차트 에셋 정리 및 리네이밍.
- **클린 룸 재구현 핵심 포인트:**
  - 외부 바이너리 의존성 없이 C# .NET 내장 정규식 엔진 및 파일 I/O를 활용하여 플랫폼 독립적 구현.
  - Windows NTFS 파일 시스템의 특성(대소문자 미구분 충돌 방지: `file.wav` -> `FILE.wav`)을 안전하게 처리하기 위한 임시 가이드 2-Pass Move 알고리즘 적용.
  - **BMS 연동 기능 (BmsSyncService):** WAV 파일명을 변경할 때 BMS 차트 파일(`*.bms`) 내부의 `#WAVxxx` 헤더 경로/파일명도 자동으로 감지하여 동기화 갱신하고 백업(`.bms.bak`)을 생성.
  - 긴 한국어 파일명 겹침 방지를 위해 그리드 컬럼 격리 및 창 크기 최적화.

## 2. 프로젝트 아키텍처
- **Framework:** .NET 10.0 + Avalonia UI 12.1.3 (Fluent Theme Dark)
- **MVVM Toolkit:** `CommunityToolkit.Mvvm` 8.4.2
- **핵심 컴포넌트:**
  - `Services/RenameEngine.cs`: 정규식/문자열 치환, 대소문자 변환, 범위(`Scope`) 계산.
  - `Services/BmsSyncService.cs`: BMS 차트 파일 자동 감지, `#WAV` 정의 라인 정규식 파싱 및 일괄 교체, 백업/복원.
  - `ViewModels/MainViewModel.cs`: 목록 상태 관리, 필터링, 실시간 미리보기 갱신, BMS 동기화, 실행 취소(Undo) 스택.
  - `ViewModels/RenameItemViewModel.cs`: 개별 파일 상태, 충돌 감지, 상태 표시 색상.
  - `Views/MainWindow.axaml`: 다크 Fluent 테마 2열 분할 레이아웃, 드래그 앤 드롭, 균등 분할 리스트 뷰.

## 3. MCP (Model Context Protocol) 서버 기능
- **실행 명령:** `PowerRenameAvalonia.exe mcp`
- **프로토콜:** stdio 기반 JSON-RPC 2.0
- **제공 도구:**
  - `scan_wav_assets`: 디렉토리 내 WAV 파일 및 BMS #WAV 헤더 연동/누락 상태 진단.
  - `preview_rename`: 정규식/치환/서식 변환 결과 사전 시뮬레이션 (디스크 미변경).
  - `apply_rename`: 실제 파일 일괄 리네임 + BMS #WAV 자동 동기화.
  - `sync_bms_chart`: BMS 차트 파일 #WAV 정의 직접 매핑 갱신.
- **등록 설정:**
  - Antigravity: `~/.gemini/config/mcp_config.json`
  - Claude Code: `~/.claude.json`

## 4. 빌드 및 테스트 명령
- **앱 실행:** `dotnet run`
- **무결성 자체 테스트 (엔진 + BMS 동기화 6개 테스트):** `dotnet run -- --test`
- **릴리즈 배포(Publish):** `dotnet publish -c Release -o Publish`
- **빌드 출력물:** `Publish/PowerRenameAvalonia.exe`

## 5. 부가 자동화 스크립트 (Scripts)
- `scripts/apply_speed_tags_and_bms.ps1`:
  - Muse Dash 커스텀 차트용 씬 폴더(예: `Star Trace`)의 WAV 키음을 속도 1~3별(UID `yy` 오프셋 및 `dt1.48`/`1.28`/`1.08`)로 분할 복제하고, 한국어 이름에 `스피드1~3` 태그 부여.
  - 해당 차트의 `*.bms` 파일 `#WAV` 정의를 실시간으로 1,860개(기존 628개 유지 갱신 + 스피드 2~3 추가)로 확장하여 키음 팔레트 에디터와 동기화.
- `scripts/update_speed_dt.ps1`:
  - 일반 노트(`1.48` / `1.28` / `1.08`), 홀드(`1.47` / `1.26` / `1.10`) 및 보스 발사체(`0.8` / `0.6` / `0.4`) 0.20s 등간격 감속 체계 구축 및 BMS 동기화.
- `scripts/update_speed3_hold_dt.ps1`:
  - 스피드 3 홀드 44개(`xx0203`, `xx0206`)를 `dt1.07` -> `dt1.10`으로 변경 및 `hwa2.bms` 동기화 (Spine 33프레임 1.100s 막대 렌더 꼬리 조기 통과 버그 해결).
- `scripts/update_speed2_hold_dt.ps1`:
  - 스피드 2 홀드 44개(`xx0202`, `xx0205`)를 `dt1.26`으로 변경 및 `hwa2.bms` 동기화 (38프레임 1.2667s 실기 검증 완료 - 홀드 꼬리 렌더 일치 정답 확정).
- `scripts/update_boss_projectile_dt.ps1`:
  - 보스 발사체(`1006yy`, `1007yy`, `1008yy` 등) 속도별 dt 일괄 변경 및 `hwa2.bms` 동기화:
    - 스피드 1: `dt0.8` (유지)
    - 스피드 2: `dt0.75`
    - 스피드 3: `dt0.65`

