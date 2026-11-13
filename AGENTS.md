# AGENTS.md - PowerRename Avalonia Project

## 1. 개요 및 설계 의도 (Rationale)
- **목적:** Windows PowerToys의 `PowerRename` 기능을 Avalonia UI (C# / .NET 10 / MVVM) 기반으로 클린 룸 재구현한 독립 일괄 파일 이름 변경 유틸리티.
- **주요 활용 처:** Muse Dash 커스텀 차트 등 대량의 오디오 에셋(WAV/OGG 등), BMS 차트 에셋 정리 및 리네이밍.
- **클린 룸 재구현 핵심 포인트:**
  - 외부 바이너리 의존성 없이 C# .NET 내장 정규식 엔진 및 파일 I/O를 활용하여 플랫폼 독립적 구현.
  - Windows NTFS 파일 시스템의 특성(대소문자 미구분 충돌 방지: `file.wav` -> `FILE.wav`)을 안전하게 처리하기 위한 임시 가이드 2-Pass Move 알고리즘 적용 (`FileRenamer`, 2단계 실패 시 원래 이름으로 원복).
  - **BMS 연동 기능 (BmsSyncService):** WAV 파일명을 변경할 때 BMS 차트 파일(`*.bms`) 내부의 `#WAVxxx` 헤더 경로/파일명도 자동으로 감지하여 동기화 갱신하고 백업(`.bms.bak`)을 생성.
  - 긴 한국어 파일명 겹침 방지를 위해 그리드 컬럼 격리 및 창 크기 최적화.
- **코드에 드러나지 않는 설계 결정 (2026-11 리팩토링):**
  - **BMS 동기화 입력은 `RenamedPath(Old, New)` 목록 하나뿐.** "디스크에서 실제로 성공한 이동"만 넘긴다. ViewModel 상태(`IsModified` 등)를 서비스가 읽게 하면 적용 직후 상태가 바뀌어 동기화가 어긋난다 (아래 Dead Ends 1번).
  - **BMS는 바이트 단위로 보존.** 원본 BOM 유무·줄바꿈(LF/CRLF)·`#WAV` 뒤 구분 공백(탭 포함)을 그대로 유지하고 경로 부분만 바꾼다. UTF-8이 아니면 시스템 ANSI 코드 페이지(한국어 Windows=CP949)로 읽되, 재인코딩 결과가 원본 바이트와 다르면 **수정하지 않고 오류**를 낸다 (Shift-JIS 차트 손상 방지).
  - **`.bak` 백업은 실제로 바뀐 행이 있을 때만** 만든다 (변경 없는 호출이 직전 백업을 덮어쓰지 않도록). 실행 취소는 `.bak`이 아니라 메모리에 보관한 원본 바이트로 복구한다.
  - **정규식은 미리보기 1회당 1번만 컴파일** (`RenameEngine.Create`)하고 매치 타임아웃 1초를 건다 — 파국적 백트래킹 패턴이 UI를 멈추지 않게.
  - **`DisplayItems`는 필터가 바뀔 때 컬렉션째 교체**한다. 항목을 하나씩 Clear/Add 하면 ListBox에 N개 이벤트가 가서 2,000개 추가 시 O(n²) (약 200만 건)이었다. 단건 삭제만은 스크롤 위치 유지를 위해 `Remove`로 처리.
  - **대량 선택 변경은 `RunBatch`로 묶어** 항목별 카운트·충돌 재계산을 막고 끝에서 한 번만 한다. 선택이 바뀌면 충돌도 다시 계산한다 (한쪽 선택 해제 시 충돌 해제).
  - **적용/실행 취소의 디스크 I/O는 `Task.Run`** 에서 수행하고, 항목 상태 갱신은 UI 스레드로 돌아와서 한다.
  - MCP `apply_rename`도 GUI와 같은 규칙: 같은 대상으로 모이는 변경은 충돌로 건너뛰고, 실패한 항목은 BMS에 반영하지 않는다. 불리언 인자는 `"true"` 문자열도 허용, 출력 JSON은 한글을 이스케이프하지 않는다.

## 2. 프로젝트 아키텍처
- **Framework:** .NET 10.0 (SDK 10.0.401에서 빌드 확인) + Avalonia UI 12.1.3 (Fluent Theme Dark)
- **MVVM Toolkit:** `CommunityToolkit.Mvvm` 8.4.2
- **핵심 컴포넌트:**
  - `Models/RenameOptions.cs`: 치환 설정 레코드(`RenameOptions`)와 성공한 이동 한 건(`RenamedPath`). GUI·MCP 공용 입력 형식.
  - `Services/RenameEngine.cs`: `RenameEngine.Create(options)`로 규칙을 한 번 컴파일한 뒤 `Transform(name, isDirectory, out error)` 반복 호출. 정규식/문자열 치환, 대소문자 변환, 범위(`Scope`) 계산.
  - `Services/FileRenamer.cs`: 2-Pass Move 포함 단일 이동 루틴 (GUI 적용·실행 취소·MCP 공용).
  - `Services/BmsSyncService.cs`: BMS 자동 감지, `#WAV` 정의 읽기(`ReadWavDefinitions`), 동기화(`SyncBmsFile(bms, IReadOnlyCollection<RenamedPath>, out error)`), 인코딩 판별.
  - `ViewModels/MainViewModel.cs`: 목록 상태, 필터링, 실시간 미리보기, 충돌 감지, 비동기 적용/실행 취소(Undo) 스택.
  - `ViewModels/RenameItemViewModel.cs`: 개별 파일 상태, 상태 표시 색상, `TargetPath`, `MarkMovedTo`.
  - `Views/MainWindow.axaml`: 다크 Fluent 테마 2열 분할 레이아웃, 드래그 앤 드롭, 균등 분할 리스트 뷰.
  - `Mcp/McpServer.cs`, `Mcp/McpTools.cs`: stdio JSON-RPC 서버와 4개 도구.
  - `Tests/SelfTests.cs`, `Tests/Benchmark.cs`: 배포본에서도 실행 가능한 자체 테스트와 성능 측정.

## 3. MCP (Model Context Protocol) 서버 기능
- **실행 명령:** `PowerRenameAvalonia.exe mcp`
- **프로토콜:** stdio 기반 JSON-RPC 2.0
- **제공 도구:**
  - `scan_wav_assets`: 디렉토리 내 WAV 파일 및 BMS #WAV 헤더 연동/누락 상태 진단.
  - `preview_rename`: 정규식/치환/서식 변환 결과 사전 시뮬레이션 (디스크 미변경, 충돌 목록 포함).
  - `apply_rename`: 실제 파일 일괄 리네임 + BMS #WAV 자동 동기화 (성공한 이동만 반영).
  - `sync_bms_chart`: BMS 차트 파일 #WAV 정의 직접 매핑 갱신 (키 = 파일명 또는 BMS 기준 상대경로).
- **등록 설정 (둘 다 `Publish\PowerRenameAvalonia.exe mcp` 를 가리킴):**
  - Antigravity: `~/.gemini/config/mcp_config.json`
  - Claude Code: `~/.claude.json`
- **주의:** MCP 클라이언트가 떠 있으면 `Publish\PowerRenameAvalonia.exe`가 잠겨 재배포(publish)가 실패한다. Claude Code/Antigravity를 모두 닫은 뒤 publish 할 것.

## 4. 빌드 및 테스트 명령
- **앱 실행:** `dotnet run`
- **무결성 자체 테스트 (엔진·이동·BMS·GUI 흐름 31개 검증):** `dotnet run -- --test`
- **성능 측정 (기본 2,000개 WAV):** `dotnet run -c Release -- --bench [개수]`
  - 2026-11-14 기준 2,000개: 목록 추가 27ms·알림 4건 / 정규식 키 입력 6회 24ms / 전체 선택 토글 2회 2ms (리팩토링 전: 433ms·알림 200만 건 / 66ms / 520ms).
- **릴리즈 배포(Publish):** `dotnet publish -c Release -o Publish`
- **빌드 출력물:** `Publish/PowerRenameAvalonia.exe`

## 5. 시도했다가 실패한 것 / 고친 버그 (Dead Ends)
1. **GUI BMS 동기화가 0행이던 버그 (2026-11 수정):** 예전 `ApplyRenameAsync`는 이동 직후 `item.OriginalName = item.NewName`으로 덮어쓴 뒤 `SyncBmsFile`에 항목을 넘겼고, 서비스는 `!item.IsModified`인 항목을 건너뛰었다 → 성공한 항목은 전부 무시되고 **실패한 항목만** BMS에 반영되었다. 그래서 그동안 dt 변경은 PowerShell 스크립트로 BMS를 따로 고쳤다. 지금은 성공한 `RenamedPath` 목록을 넘기며 `--test`의 GUI 흐름 테스트가 회귀를 막는다.
2. MCP `apply_rename`도 이동에 실패한 항목까지 BMS에 넣어 BMS가 없는 파일을 가리킬 수 있었다 → 성공분만 반영.
3. 실행 취소가 BMS를 `ReadAllText`/`WriteAllText`로 복구해 BOM이 사라졌다 → 원본 바이트 보관으로 변경.
4. PowerShell 스크립트에서 `"$renamed개"`처럼 변수 뒤에 한글을 붙이면 한글까지 변수 이름으로 읽힌다 → `"$($renamed)개"`로 쓸 것. 한글 리터럴이 있는 `.ps1`은 Windows PowerShell 5.1 호환을 위해 **UTF-8 BOM**으로 저장할 것.

## 6. 부가 자동화 스크립트 (Scripts)
- `scripts/Rename-DtSuffix.ps1` (**범용, 새 dt 변경은 이것을 사용**):
  - 씬 폴더(`-FolderPattern`)의 `_dt<From>.wav` → `_dt<To>.wav` 일괄 리네임 + BMS `#WAV` 동기화. `-NamePattern`으로 UID 한정, `-WhatIf`로 사전 점검(대상 파일·BMS 행 수만 출력).
  - 대상 이름 충돌 시 아무것도 바꾸기 전에 중단, BMS는 BOM·줄바꿈 보존, `<bms>.dt<To>.bak` 백업.
  - 예: `.\scripts\Rename-DtSuffix.ps1 -Root "H:\muse dash hwa\hwa\Star Trace" -FolderPattern '스피드3 wav폴더$' -From 1.16 -To 1.18 -WhatIf`
- `scripts/apply_speed_tags_and_bms.ps1`:
  - Muse Dash 커스텀 차트용 씬 폴더(예: `Star Trace`)의 WAV 키음을 속도 1~3별(UID `yy` 오프셋 및 `dt1.48`/`1.28`/`1.08`)로 분할 복제하고, 한국어 이름에 `스피드1~3` 태그 부여.
  - 해당 차트의 `*.bms` 파일 `#WAV` 정의를 실시간으로 1,860개(기존 628개 유지 갱신 + 스피드 2~3 추가)로 확장하여 키음 팔레트 에디터와 동기화.
- `scripts/convert_star_trace_speeds.ps1`: Star Trace 씬 속도 1~3 분할·리네임 (초기 변환용).
- `scripts/history/` — **이미 실행 완료된 일회성 기록** (재실행 금지, 값의 근거 보존용):
  - `update_speed_dt.ps1`: 일반 노트(`1.48` / `1.28` / `1.18`), 홀드(`1.47` / `1.26` / `1.10`) 및 보스 발사체(`0.8` / `0.76` / `0.6`) 규격 동기화.
  - `update_speed3_normal_dt118.ps1`: 스피드 3 일반 노트 374개를 `dt1.16` -> `dt1.18`로 변경 및 `hwa2.bms` 동기화 (최종 확정). 점검용 `inspect_speed3_dt116.ps1`, `verify_dt118.ps1` 동반.
  - `update_speed3_hold_dt.ps1`: 스피드 3 홀드 44개(`xx0203`, `xx0206`)를 `dt1.07` -> `dt1.10`으로 변경 및 `hwa2.bms` 동기화 (Spine 33프레임 1.100s 막대 렌더 꼬리 조기 통과 버그 해결).
  - `update_speed2_hold_dt.ps1`: 스피드 2 홀드 44개(`xx0202`, `xx0205`)를 `dt1.26`으로 변경 및 `hwa2.bms` 동기화 (38프레임 1.2667s 실기 검증 완료 - 홀드 꼬리 렌더 일치 정답 확정).
  - `update_boss_projectile_dt.ps1`: 보스 발사체(`1006yy`, `1007yy`, `1008yy` 등) 속도별 dt 일괄 변경 및 `hwa2.bms` 동기화 (실기 검증 완료 정답 확정): 스피드 1 `dt0.8`(유지) / 스피드 2 `dt0.76` / 스피드 3 `dt0.6`.
  - `update_summertime_boss_dt107.ps1`: *summertime (Arrange ver.)* 5번 씬 보스 발사체2·3(`050701`, `050704`, `050801`, `050804`) 8개 파일 `dt0.8` -> `dt1.07` 리네임 및 `hwa2.bms` 동기화. 5번 씬 보스 발사체2·3은 31프레임으로 다른 씬(23프레임)보다 8프레임 길어 속도 1 기준 계산값 `0.8 + 8/30 ≈ 1.07` 적용.
  - `update_speed3_normal_dt.ps1`, `update_normal_dt.ps1`, `apply_normal_revert_and_boss_08_06_04.ps1`: 확정 이전 단계의 중간 시도 값(1.13/1.16, 1.27/1.11, 0.6/0.4 등) 기록.
