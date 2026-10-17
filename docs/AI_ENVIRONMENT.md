# AI·Unity 실행 환경과 확인 경계 — 2026-10-15

실행 계약은 [AI_WORKFLOW.md §10](AI_WORKFLOW.md), 요구사항별 상태는 [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md)다. 이 문서는 실제 프로젝트 설정·정제된 실행 결과의 안내이며, 원본 설계나 계정 인증 자료를 대체하지 않는다. 원본 배포의 `ops/ai/policy.json`은 당시 미호출 기준선으로 보존하고 현재 계약은 [runtime-policy.json](../ops/ai/runtime-policy.json)으로 구분한다.

## 허용 모델·과금·노력

| 항목 | 프로젝트 기본값 | 현재 확인한 범위 |
|---|---|---|
| Codex | `gpt-6.1-sol`, `high`, ChatGPT 로그인 | [.codex/config.toml](../.codex/config.toml)의 프로젝트 유효 설정을 읽어 확인. 주 작업 세션은 같은 모델 `max`로 기록됨. 기본값과 현재 턴은 별개다. |
| Claude | `claude-opus-5-5`, `high`, claude.ai 로그인 | [.claude/settings.json](../.claude/settings.json)에 Opus만 허용, fallback 없음, 자동 모델 전환 OFF. 세션 Max는 공식 CLI `--effort max`/지원 환경 변수로 요청한다. |
| 사용량 | 양쪽 기존 5배 구독 포함 사용량만 | API·Console·구매 크레딧·추가 사용·자동 충전·외부 종량제 금지. 로그인 성공과 추가 과금 OFF는 각각 확인한다. |
| 품질 | 기본 High, 핵심 작업과 최종 검토에 Max 적극 사용 | Medium 이하·다른 모델로 절약하지 않는다. 노력 선택은 담당 AI가 자율 수행한다. |

Codex 핵심 전투·예약·카드/부품·기억 저장·경제·인증·서버·이관·아키텍처·복잡한 버그·중요 계획·최종 검증, Claude 종합 계획·서사·캐릭터·최종 검토는 Max를 적극 사용한다. 네이티브 자식 작업도 `gpt-6.1-sol`만 명시적으로 선택하며 설치된 역할표의 다른 모델로 라우팅하지 않는다. Astra·Fable·Luna 및 그 밖의 모델과 자동 fallback은 금지한다.

[환경 확인 기록](../reports/ai_environment_20261015/verification.json)의 Codex CLI는 `0.162.1`, Claude Code는 `2.1.285`다. Codex `model_reasoning_effort = "high"`/`"max"`와 Claude `--effort high|max` 지원을 확인한 버전이다. 버전이 바뀌면 설치 CLI 도움말과 유효 설정을 재확인한다. Claude 영구 `modelSettings`의 High와 세션 요청 Max를 구분하며, 지원하지 않는 영구 Max 값을 저장하지 않는다. 설정 파일이나 프롬프트 문장만으로 실행 중 모델·노력이 변경됐다고 보고하지 않는다.

## 공식 Claude CLI 작업 경로

최신 사용자 제공 잔여량과 초기화 시점에 따라 [§11의 Claude 구현 우선 배분](AI_WORKFLOW.md)을 적용한다. 새 구현·원인 분석·테스트 작성은 Opus CLI, Codex는 독립 리뷰·통합·Unity 최종 확인·이미지 담당이다. [runtime-policy.json](../ops/ai/runtime-policy.json)의 `work_routing`은 작업 배정 계약이며 제공자의 고정 토큰 비율 강제 설정이 아니다. 기존 High/Max와 반대 AI 리뷰·구독 전용 경계를 유지한다.

[run_claude.py](../tools/ai/run_claude.py)는 별도 branch/worktree의 범위가 정해진 작업을 실행한다. 실제 계정 인증 파일·토큰·계정 식별자·과금 화면 원문은 문서나 Git에 싣지 않는다.

1. 신선한 계정 UI 확인 기록에서 claude.ai 구독, Usage credits OFF, 자동 충전 OFF를 검사한다. 현재 구현은 24시간 이내 확인과 화면 이메일/CLI 계정의 로컬 SHA-256 일치를 요구한다. 식별자·지문은 로컬에만 두며 결과에는 일치 여부만 남긴다. 이것을 서버 청구 강제 차단 장치라고 부르지 않는다.
2. 같은 저장소의 별도 worktree·별도 브랜치와 허용 파일을 검사한다. 자식 프로세스 환경에서 API·프록시·클라우드·모델 덮어쓰기 경로를 제외하고 전역 키는 변경하지 않는다.
3. `--restricted --setting-sources ''`와 검사한 프로젝트 설정으로 공식 CLI 버전과 `auth status`를 비추론 확인한다. claude.ai/firstParty의 Pro 또는 Max 구독 인증만 받는다. `--bare`·토큰 추출·SDK 재사용·재귀 호출은 금지한다. CLI 파일 도구 범위와 자격 파일 거부를 적용하지만 OS샌드박스로 부르지 않는다. 관리형 정책은 별도 적용된다.
4. 명시 Opus, `--effort high|max`, 구조화 JSON 결과를 사용한다. 기본 도구는 읽기·검색이며 편집은 명시 파일에만 허용한다. 기본(`--mcp-profile none`, `--allow-test` 없음) 실행은 임의 쉘·중첩 AI·MCP를 열지 않는다. MCP와 지정 시험 명령은 아래 2026-10-17 절의 고정 opt-in으로만 열린다.
5. 작업 ID·run ID·요청 모델·요청 노력·실제 종료 코드·반환 세션 ID·`modelUsage` 모델 키를 결과에 기록한다. Opus 외 모델이나 성공 조건 누락이면 거부한다. 반환 성공도 `RECEIVED_UNVERIFIED`이며 diff·시험·독립 검토 후에 통합한다.
6. 시간 초과·한도·실패는 체크포인트로 남긴다. 자동 유료 우회나 중복 재실행을 하지 않는다. 차단된 작업 외의 독립 개발은 현재 세션에서 계속한다.

예시 형식(요청 파일과 worktree는 실제 배정값을 사용):

```sh
python3 tools/ai/run_claude.py ops/ai/tasks/TASK_ID.md \
  --worktree <별도-worktree> --effort max
```

실행기 회귀 시험은 [tests/test_ai_runner.py](../tests/test_ai_runner.py)에 있다. 현재20개 모의 시험이 통과했으며 이것은 실제 Opus 호출 성공 증거와 구분한다. 실제 [검토 수신](../ops/ai/tasks/P1-CORE-AND-RUNNER-REVIEW.result.json)과 [Unity MCP 수신](../ops/ai/tasks/ENV-CLAUDE-UNITY-MCP.result.json)을 보관했다. 과거 `P1-BALANCE-ROOTCAUSE.result.json`의 종료 성공에 새 실행기의 `requestedEffort` 증거를 소급해서 붙이지 않는다. `CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1`로 문서화된 보조 제목 모델 경로를 끄고 노력 예산 덮어쓰기 환경변수도 제외한다.

`requestedEffort: "max"`는 CLI 요청값이다. 반환 `modelUsage`는 실제 Opus 모델 근거이며 [CLI 세션 메타데이터](../reports/ai_environment_20261015/claude_effort.json)에서도 `effort=max`, `perTurnEffort=max`를 확인했다. MCP 연결 시험은 같은 Opus의 High로 관측됐다. CLI 적용과 제공자 서버 내부 추론 계측은 구분하며 내부 사고 내용이나 비밀 토큰을 확인하려 하지 않는다.

## Unity 실제 연결과 검증

| 항목 | 현재 근거와 범위 |
|---|---|
| Editor·프로젝트 | Unity `6000.0.46f1`, `client/MemoryForgeUnity`, 실제 연결 인스턴스 `MemoryForgeUnity@4034a4e2fe236d6e`. 공식 Universal 2D 템플릿·URP 17.0.3. |
| MCP 고정 | 무료 로컬 Coplay Unity MCP `10.3.0`, commit `aa5fc638d623f56178d50329d9ad8c541a57fe66`. [manifest](../client/MemoryForgeUnity/Packages/manifest.json)와 lock에 같은 Git commit을 고정했다. |
| 접속 설정 | Codex `.codex/config.toml`, Claude `.mcp.json` 모두 `http://127.0.0.1:8080/mcp`. 프로젝트 설정 등록과 클라이언트 실제 접속 성공은 구분한다. |
| 부가 서비스 | [runtime-policy.json](../ops/ai/runtime-policy.json)에서 telemetry OFF·유료 `asset_gen` 비활성. 별도 유료 AI 자산 공급자를 사용하지 않는다. |
| 씬·프리팹 | `Assets/Scenes/MemoryForgeMcpSmoke.unity`, `Assets/Prefabs/MemoryForgeConnectionCanvas.prefab`를 실제 Editor에서 생성·저장·확인했다. 연결 검증용 Canvas다. |
| Play | [실제 Play 캡처](../reports/ai_environment_20261015/unity_mcp_playmode.png), [화면 판정](../reports/ai_environment_20261015/unity_visual.json). 게임 전투나 전체 UI 완성 증거로 세지 않는다. |
| EditMode | [unity_editmode_final.json](../reports/ai_environment_20261015/unity_editmode_final.json): 실제3/3 PASS. URP 2D renderer·overlay prefab·저장 씬의 Canvas/Camera를 검사한다. 초기 시도와 수정 기록도 보존한다. |
| 로컬 빌드 | [unity_build_final.json](../reports/ai_environment_20261015/unity_build_final.json): 공유 Core 연결 후 `StandaloneOSX`, `succeeded`, 오류·경고0. 연결 검증 앱 빌드이며 Android AAB/실기기·스토어 검증은 아니다. |
| Claude MCP | [실제 공식 CLI 도구 호출](../reports/ai_environment_20261015/claude_unity_mcp.json): 인스턴스 선택·Console 읽기2회 모두 성공, 오류0개. Opus/High 세션 메타데이터 확인. 일반 범위 실행기는 계속 빈 strict MCP를 쓰고 이 연결 시험만 명시된 읽기 도구를 허용했다. |
| 공유 Core | 기존 `core/MemoryForge.Core`를 로컬 UPM으로 직접 참조한다. [PlayMode2/2](../reports/ai_environment_20261015/unity_core_playmode.json)에서 난수 상태 재개·절대 만기 예약·동일 canonical hash를 실제 확인했다. Battle/Run은 아직 이식 중이다. |

실제 Play·EditMode·빌드·CLI MCP 결과를 위 표의 개별 증거로 확인하며 설치·등록만으로 연결을 주장하지 않는다. 현재 Unity 씬·프리팹·Editor Play 제어의 쓰기 담당은 주 Codex1명이다. 다른 작업자는 읽기와 독립 파일만 담당하고 별도 worktree를 같은 Editor 동시 쓰기 권한으로 취급하지 않는다. 로컬 패키지 manifest 편집 후에는 MCP의 `resolve_packages`와 컴파일·Console 확인까지 수행한다.

Unity MCP는 실제 Scene·Prefab·UI·Animation·Console·Play 시험·임포트·빌드에 적극 사용한다. 아직 실행하지 않은 Animation·게임 리소스·게임 수직 슬라이스까지 이번 연결 시험의 PASS로 확장하지 않는다. 이미지 생성·재생성·내용 수정은 Codex 고정이며 Claude는 브리프·검수·비생성형 적용을 맡는다. 구독 이미지 도구가 없으면 해당 자산만 보류한다.

무료 Microsoft .NET10 SDK는 전역 SDK/PATH를 바꾸지 않고 로컬 제외 폴더에 격리했다. [설치·SHA512 검증](../reports/ai_environment_20261015/dotnet.json), [기존 Core202개](../reports/ai_environment_20261015/core_checks.txt), [C# 직렬화·2,000스냅샷 검사](../reports/cross_language_20261015/csharp_snapshot_checks.txt)를 구분한다. JS1,000시나리오 재생 성공도 C# Battle/Run 명령 재생 완료가 아니다.

로컬 PDF 검사에는 기존 작업용 Python 가상환경의 fitz/pypdf를 사용했다. Playwright도 기존 번들과 설치 Chrome을 사용했으며 누락 환경 경로로 시작하지 못한 시도는 감사 로그에 남겼다. 출고 검사·매니페스트 파일 선택은 [delivery_files.py](../tools/ai/delivery_files.py)를 공유하며 입력 매니페스트를 새로 생성하지 않았다. Git 없는 추출 ZIP은 실제 payload 전체에서 로컬 계정 증거·환경파일을 찾아 거부하며, 매니페스트 생성도 사전검사 실패 시 기존 파일을 보존한다.

## 개발 반복과 중단 경계

주요 작업 전에 원문 절·게임 내 역할·완료 조건·담당 파일을 기록하고 개발 → 시험 → 반대 AI 독립 검토 → 수정 → 재검증 → 통합 → 다음 미차단 요구사항 순서로 진행한다. 핵심 결함·명세 위반은 해결·재검토 전 통합하지 않는다. 두 차례 미해결 쟁점은 대안·근거·위험을 남기고 그 항목만 보류한다.

P1/P2나 연결 작업이 끝났다는 이유로 전체 개발을 끝내지 않는다. 정책·권한·환경·구독 한도로 막힌 작업만 보류하고 가능한 다음 작업을 선택한다. 현재 세션 밖에서 개발이 계속된다고 약속하지 않는다. 원본 설계·수치·정책·콘텐츠·비공개 원격·기존 변경을 보존하고 기능·시험 삭제로 통과시키지 않는다.

공유 Core의 기본 .NET 명령도 패키지 안에 산출물을 만들지 않도록 [Directory.Build.props](../core/Directory.Build.props)에 출력 경로를 고정했다. [기본 명령 실제 실행](../reports/ai_environment_20261015/default_dotnet_run.txt)·[파일 배치](../reports/ai_environment_20261015/shared_core_layout.json)·[Editor 오류 전용 조회](../reports/ai_environment_20261015/shared_core_errors_only.json)를 확인했다. 도메인 리로드의 MCP 전송 경고1개는 [합산 Console 기록](../reports/ai_environment_20261015/shared_core_console.json)에 보존한다. [최종 한정 재검토](../ops/ai/tasks/P1-UNITY-OUTPUT-VERIFY.result.json)는 차단 해제 판정이다.

전투 시작 이식 후 같은 공유 Core의 [PlayMode5/5](../reports/csharp_opening_20261015/unity_playmode.json)와 [EditMode3/3](../reports/csharp_opening_20261015/unity_editmode.json)를 새로 실행했다. 5개 중3개가 실제 Battle 시작·요청 재시도·사망/봉투 생존이다. [Console 오류0/전송 경고1](../reports/csharp_opening_20261015/unity_console.json)을 구분하며 이전 macOS 연결 앱 빌드를 새 전투 게임 빌드로 재분류하지 않는다.

반복 턴 구현 후 [실제 PlayMode7/7](../reports/csharp_turns_20261015/unity_playmode.json)과 [EditMode3/3](../reports/csharp_turns_20261015/unity_editmode.json)을 다시 확인했다. 적 행동 뒤2·3턴과 이미 사용한 봉투 뒤2턴 회로 사망을 같은 공유 Core로 실행했다. 새 게임UI·Android·실기기 수용은 별도다.

전체 카드 질의 A0에서도 [PlayMode8/8](../reports/player_queries_20261015/unity_playmode.json)·[EditMode3/3](../reports/player_queries_20261015/unity_editmode.json)를 실제 실행했다. 새 시험은 반환 카드 정의·모드 목록을 수정한 후 원본과 진행 상태/로그/이벤트가 보존됨을 확인한다. [Console 오류0/전송 경고1](../reports/player_queries_20261015/unity_console.json)을 분리하며 실제 카드 실행·게임UI·Android 수용은 아니다.

A1의 [PlayMode12/EditMode3](../reports/player_actions_20261015/verification.json)는 실제 카드/필살기·예약/불굴·소멸 재섞기를 같은 공유 Core로 실행한다. [새 macOS빌드](../reports/player_actions_20261015/unity_build.json)는0오류/0경고이며 [Metal 기술씬 시작](../reports/player_actions_20261015/unity_standalone_startup.json) 후 해당 프로세스만 강제종료했다. 최초NullGPU timeout은 미수용, 정상종료/전체게임UI/Android 수용은 미검증이다. 공식 [Opus Max 승인](../reports/player_actions_20261015/opposite_ai_acceptance.json)과 실제effort108개·16소스 해시를 확인했으며 reviewer실행과 Root실행은 구분한다. M0/R0의 독립 개발은 계속되며 현재 세션 종료 후 자동 지속을 약속하지 않는다.

## Extra High 지원과 자율 선택 보완

이전 High/Max 지원 기록을 두 수준만 허용한다는 뜻으로 사용하지 않는다. 로컬 `gpt-6.1-sol` 모델 카탈로그는 `xhigh`를 포함하고, [공식 Claude 지원표](https://code.claude.com/docs/en/model-config#adjust-effort-level)도 Opus5.5의 xhigh를 명시한다. 설치 CLI2.1.285의 기존 범위 실행기에 xhigh 인자/환경/결과 경로를 추가했다. 현재 Codex 턴의 Max 기록은 과거/현재 실행 증거이며 설정 문서만으로 xhigh로 변경됐다고 주장하지 않는다. 이후 실제 호출마다 요청값과 적용 세션 메타데이터를 확인한다. 운영 선택은 [§12](AI_WORKFLOW.md)를 따른다.

M0에서도 실제Play16/Edit3을 확인했다([근거](../reports/memory_rules_20261015/)). Claude High의 두 기억시험을 Codex가 독립검토·실행했고, Opus xhigh가 찾은 배열이관P2를 Claude xhigh가 수정한 뒤 Codex가 재검토·전체실행했다. xhigh는 실제세션 effort/perTurnEffort로 확인했다. Root .NET5451/5451·1072호출과 전체Run명령0을 구분하며 Editor설정을 실제API/SaveAssets로 원래값에 복원했다. Console오류0/전송경고1을 기록하고 전체게임빌드/Android/실기기수용으로 확대하지 않는다.

R0는 [실제18Play/3Edit](../reports/run_create_20261015/unity_playmode18.json)에서 같은 공유Core 생성/지도/조회/기억키를 실행했다. 처음새시험이없던16개결과를 별도보존하고 명시ImportAsset/Refresh/compilation/반사타입등록 후18개를 확인했다. Console0오류/0경고, 원래Editor설정복원, Root215645/215645 및 Node286을 기록한다. 새macOS/Android/실기기/전체게임UI수용은 아니다.

## 실제 표시된 전투 연습 / 추가 검증

[MemoryForgeBattle 실행안내](../client/MemoryForgeUnity/README.md)에 따라 실제C#Core의 카드·예약·턴·의도/상태를 표시했다. [현재26Play](../reports/unity_client_20261015/playmode26_pass.json)·[3Edit](../reports/unity_client_20261015/editmode3_pass.json)는 초기중단/23개통과기록과 구분한다. Claude High콘텐츠/표시수정과 xhigh교차검토를 Codex가 호출·회수·검증했으며, Core값/원문/기존자산은 보존했다. OFL폰트의 허용은 한 경로/정확한바이너리/라이선스체크섬에만 한정하고 계정파일차단은 유지했다. 현재Mac잠금으로 최종시각재확인/새standalone빌드확인만보류하며 기존기술씬빌드로 대신하지 않는다.

2026-10-16 카드흐름 후속: [실제11개공식Opus모델/High·xhigh·Max세그먼트](../reports/card_flow_20261016/actual_cli_metadata.json), [112Play/3Edit/새macOS지도시작](../reports/card_flow_20261016/root_acceptance.md)를 확인했다. 호스트 프로세스 중단 뒤 같은원본프로젝트/설치된무료MCP10.3.0/Unity6000.0.46f1을복구했고 임시복구훅은시험전에제거했다. Root한명만Editor를쓰고 Claude범위CLI는MCP없이분리worktree에서구현했다. Max작업2개시간종료는한도도달이아니며같은구독Opus세션xhigh로회수했다. 새물리입력/Android/서버복구는확인하지않았다.

2026-10-16 PostgreSQL 후속: 사용자가 무료 Npgsql10.0.3 추가를 명시 승인했다. [실제 OpusMax81출력 기록](../reports/postgres_persistence_20261016/actual_cli_metadata.json)과 별도 worktree의9개 파일을 회수해 Codex가 독립 소스 리뷰·루트 빌드·파일251/실제PG202·장애27/HTTP24를 검증했다. 공식 CLI는 작성 말미 **구독 session limit**으로 종료1이었고 원래 FAILED 결과와 작성자NOT_RUN을 보존한다. 자정KST 초기화 안내 뒤 추가 호출/유료/새 세션/다른 모델 우회는 하지 않았다. 새 Root PG CI 실행기는 반대AI 리뷰 전 별도 보관하며 실제PG 로컬 성공과 원격CI의 파일 검사 성공을 구별한다.

같은 후속에서 만료된 MCP HTTP 세션을 재초기화하고 원정 씬을 실제 Play·게임 화면 캡처·Console·stop으로 확인했다([실제 지도](../reports/postgres_persistence_20261016/unity_play_map.png), [범위/도구 영수증](../reports/postgres_persistence_20261016/unity_play_smoke.json)). Play 캡처 중 Console오류0/메모리리스 depth surface 경고2를 관찰했다. Editor상태 캐시는 Play 중 stale이라 준비 완료 근거로 쓰지 않는다. 이번 서버 변경은 Core/Unity 소스·씬·프리팹을 바꾸지 않으며 새 전체Play 자동시험/네이티브빌드/기기/Unity 네트워크 복구 수용은 아니다.

2026-10-17 재개: 같은 계정의 공식 claude.ai Max/firstParty 인증과 기존24시간 이내 과금 UI 증거를 대조한 뒤 CLI를 호출했다. [실제 Max61/High6 출력·종료0](../reports/postgres_ci_20261017/actual_cli_metadata.json)으로 CI 리뷰 협업 재개를 확인했으며 이전 session-limit FAILED는 고치지 않았다. 추가 과금/API/다른 모델로 우회하지 않았다. 별도 Linux 검증 컨테이너의 PG16.15/.NET10.0.402에서 새 CI 실행기188판정·locked restore·오류 출력 비밀 비노출을 실제 확인했다. 새 원격 CI는 별도 확인 대상이다. 기존 C1은 CI 읽기 리뷰, 새 관리 worktree `claude-public-battle`는 공개 조회13파일 구현으로 쓰기 범위를 분리했다. 한 Editor의 쓰기 담당은 계속 Root이며 두 작업 모두 CLI MCP/Bash/하위 AI 호출은 허용하지 않는다.


## 2026-10-17 공개 전투 조회 — 실제 협업·Editor 증거

[정제 CLI 메타데이터](../reports/public_battle_query_20261017/actual_cli_metadata.json)에서 Opus5.5의 실제 Max181, xhigh(서버27+경계38+Core104+보강25), High12 기록을 확인했다. 최초Max작업30분시간초과를 한도/성공으로 바꾸지 않았고, 뒤의 범위분리작업은 exit0으로 회수했다. Codex GPT-6.1 Sol Max가 독립 리뷰·실행·통합했고 반대편의 테스트 보강도 두 번째 리뷰에서 승인했다. 기존계정 구독인증·계정일치·24시간이내 creditsOFF/autoreloadOFF UI증거를 적용했으며 API/추가크레딧/유료우회·다른모델은 사용하지 않았다. 원시인증/과금/응답로그는 공개보고서에 복사하지 않는다.

UnityMCP의 실제 MemoryForgeUnity@4034a4e2fe236d6e /6000.0.46f1 Editor에 새Core·공유정책·Mono시험을 임포트하고 Play165/Edit3·skip0·Console0오류/0경고를 확인했다([근거](../reports/public_battle_query_20261017/unity_verification.json)). Scene/Prefab 쓰기는 Codex단독이며 기존설정바이트를 복구했다. Headless Core553·파일952·실제PG859와 이 Editor 결과는 서로 다른 검증이다. 새native/실기기·Unity네트워크/프로세스복구·운영API 완료로 확대하지 않는다. 이미지작업은 없고 Codex전담·GitHub비공개·기존설계/수치/보존정책을 유지한다.


## 2026-10-17 미확정 요청 보존 — 실제 실행 경계

[원문 보존 컴포넌트](../reports/practice_command_outbox_20261017/root_acceptance.md)는 공식OpusMax로 시작했고, Codex의 실제Mono잠금반증 뒤 같은세션xhigh 구현/시험/수정·High한줄보강으로 회수했다. 원래중단FAILED는그대로이고과금/모델우회가아니다. [실제CLI모델·노력기록](../reports/practice_command_outbox_20261017/actual_cli_metadata.json)과구독인증/계정일치·유효creditsOFF/autoreloadOFF증거를구분한다. CodexGPT-6.1SolMax의독립리뷰·실행과두번째좁은승인을완료했으며Root저장/HTTP25·새파일0600Mac/Linux·실제Mono전체Play182/Edit3을확인했다. 전체클라이언트전송/UI/기기복구·전원장애가아니며기존설계/수치/이미지Codex담당/Private정책을유지한다.


## 2026-10-17 Claude 실행기 opt-in — MCP 프로필·지정 시험, 실제 연결 NOT_RUN

계약은 [AI_WORKFLOW.md §14·§16](AI_WORKFLOW.md), 고정값은 [run_claude.py](../tools/ai/run_claude.py) 상수와 [runtime-policy.json](../ops/ai/runtime-policy.json)의 `claude_mcp_profiles`·`claude_test_commands`·`claude_autonomy`다. [test_handoff.py](../tools/ai/test_handoff.py)의 모의 시험이 기본 동작 보존, 고정 구성·허용/금지, 잠금, 사전 검사, 스트림 감사, 과금·계정·모델·재귀 차단 유지, 공유 설정 일치를 확인한다. 이 시험과 기존 [test_ai_runner.py](../tests/test_ai_runner.py)는 작성 세션의 도구 제한(Read/Glob/Grep/Edit)으로 NOT_RUN이다.

| 인자 | 고정 범위 | 잠금 / 실행 전 검사 |
|---|---|---|
| `--mcp-profile none` (기본) | MCP 없음, JSON 결과 — 이전과 같은 명령 | worktree 잠금 |
| `--mcp-profile unity-read` | `unityMCP` → `http://127.0.0.1:8080/mcp`, 읽기·진단 6개 + MCP 리소스 읽기 | worktree 잠금 / 로컬 포트 수신 |
| `--mcp-profile unity-write` | 같은 서버, 읽기 + Editor 쓰기 15개 + `execute_code` | + 전역 제어 잠금 / 로컬 포트 수신 |
| `--mcp-profile desktop` | `peekaboo` → `npx -y @steipete/peekaboo@4.9.5 mcp`, 실제 26개 중 21개 허용·`analyze`/`agent`/`clipboard`/`paste`/`browser` 금지 | + 전역 제어 잠금 / macOS·`npx`·Peekaboo 키/설정 파일 부재 |
| `--allow-test NAME` | `node`·`proposals`·`memory-ui`·`late-ui`·`python`·`docs`·`handoff`·`manifest` 중 지정분만 정확한 `Bash(명령)` | — |

- 유료 `asset_gen`, 서버 측 일괄/사용자 도구·임의 메뉴·패키지 변경·C#/셰이더 소스 편집 도구는 모든 Claude 프로필에서 제외한다. ImagineArt를 포함한 이미지 생성 도구는 Claude에 공급하지 않으며 ImagineArt **무료 크레딧** 예외는 Codex 전용이다([§15](AI_WORKFLOW.md)).
- `.mcp.json`은 대화형 세션용 Unity 서버 하나를 유지하고 desktop 서버를 넣지 않는다(대화형 세션에 화면 조작을 암묵적으로 열지 않기 위해). 대화형 세션의 도구 승인은 이 실행기 보장 밖이다. 사용자별 절대경로·비밀값은 저장소에 없고 `npx`는 PATH로 찾으며 desktop만 첫 다운로드를 위해 `MCP_TIMEOUT=120000`을 둔다.
- MCP·시험 작업은 `stream-json`으로 받아 CLI 자체의 init·호출 기록을 결과 `mcp`/`tests`에 남긴다. 원시 stream(`ops/ai/local/`)에는 화면 캡처가 들어갈 수 있으므로 공개 보고서에 복사하지 않는다. 비정상 종료로 남은 잠금은 run_id·pid를 확인한 뒤 Codex가 수동으로 정리한다.
- Peekaboo 실제 도구 목록은 Codex의 provider 환경변수 없는 로컬 stdio initialize/tools-list 결과다. Claude 세션 노출·화면 호출, Unity 실제 tools/list·`execute_code` 호출, 허용 규칙 문법(`Bash(… 공백 …)` 포함)의 CLI 적용은 모두 NOT_RUN이다. Codex의 부팅이 `~/.peekaboo/config.json`을 만들었다면 desktop 사전 검사가 BLOCKED로 멈추며, 내용을 출력하지 않고 AI 공급자 설정을 정리한 뒤 다시 시험한다.

### 최초 실제 실행과 노출 경계 보강 — 2026-10-17

- 첫 실제 CLI 실행(세션 `ab266f26-8e4e-49d9-9745-cc4047c562de`, Opus5.5 High, `apiKeySource=none`, 권한 거부0)에서 Claude가 직접 Bash로 `npm run test:handoff` 31/31을 실행했고, `unity-read`로 Unity state 리소스와 `read_console`만 호출했다. AI·이미지·과금 호출은 없었다. 이것은 전송·읽기 호출 관찰이며 Editor/화면 기능 수용이 아니다.
- 같은 init에서 허용 목록 밖 같은 서버 도구 17개가 문맥에 남았다: `blender_bridge`, `debug_request_context`, `find_in_file`, `generate_audio`, `generate_image`, `generate_model`, `import_model`, `import_model_file`, `manage_camera`, `manage_graphics`, `manage_physics`, `manage_probuilder`, `manage_profiler`, `manage_script_capabilities`, `manage_tools`, `unity_docs`, `unity_reflect`. `asset_gen` 하나만 막아서는 설치본의 생성·가져오기 도구가 빠지지 않았다. 원래 결과의 노출 기록은 그대로 둔다.
- 보강: 위 이름을 두 Unity 프로필에서 정확한 deny로 제외했다(생성·가져오기 6개는 이미지 Codex 담당·유료 공급자 경계, 나머지 11개는 미배정 — 필요하면 무료 여부와 쓰기 성격으로 따로 배정). 와일드카드 허용은 없다. 감사도 같은 서버의 미허용 도구 노출(`exposed_not_allowed`)을 기록만 하지 않고 FAILED로 처리한다. 결과의 `transport_connected`(init 서버 상태)와 `connection_observed`(허용 도구의 오류 없는 응답)는 각각 CLI 관찰이며 Editor/화면 효과의 독립 확인을 대신하지 않는다.
- 이 보강 작업 세션에서 CLI가 정확한 시험 규칙 밖의 읽기 전용 `git status`/`git log`/`grep`을 자동 허용한 것을 관찰했다. 따라서 `dontAsk`+정확한 `Bash(명령)` 규칙만으로 셸이 지정 시험으로 한정된다고 보지 않는다. 실행기 감사는 그런 성공 호출을 목록 밖 셸로 FAILED 처리한다. 보강 후 `unity-read` 재probe, `unity-write`·`execute_code`·desktop native probe는 NOT_RUN이다.

### 다음 실제 연결 시험 (NOT_RUN)

1. `python3 -m unittest discover -s tests -v`와 `npm run test:handoff`(PDF 의존성 venv)로 실행기 시험을 돌린다.
2. 지정 시험: 작은 작업에 `--allow-test handoff`를 주고 실행한다. 통과 기준은 exit0·`RECEIVED_UNVERIFIED`, `tests.runs`에 `npm run test:handoff` 기록, 목록 밖 명령 0, `permission_denials` 확인이다.
3. Unity: Editor·MCP 서버를 켠 뒤 인스턴스 선택·Console 오류 조회만 하는 작업을 `--mcp-profile unity-read`로 실행한다. 기준은 `mcp.observed.servers.unityMCP="connected"`, `mcp.connection_observed=true`, `api_key_source="none"`, 금지 도구 미노출이다. `mcp.observed.exposed_tools`·`exposed_not_allowed`와 서버 tools/list 원문을 허용/금지 목록과 대조하고 차이는 리뷰된 변경으로 반영한다. `unity-write`·`execute_code`는 Codex가 Editor 쓰기를 넘긴 작업에서만 실행하고 루트 Unity diff·Console·Play를 Codex가 확인한다.
4. 화면: 호스트 앱의 화면 기록·손쉬운 사용 권한을 사용자가 허용한 뒤 `permissions`·`app`(list)·지정 창 `see` 한 번만 하는 작업을 `--mcp-profile desktop`으로 실행한다. 같은 기준에 더해 AI 인자 0건과 잠금 해제를 확인한다.

## 2026-10-17 최종 실제 운용 확인 — 위 NOT_RUN은 작성 당시 이력

[수용 기록](../reports/claude_tools_20261017/README.md)·[원래 CLI 결과](../reports/claude_tools_20261017/actual_cli_metadata.json): Codex가 fresh Python57·handoff31을 독립 실행했고, Claude도 지정 Bash로 같은31/57을 직접 실행했다. 실행기의 정확한 Bash 허용 규칙과 별개로 CLI는 일부 읽기 `git/grep` 묶음을 자동 허용했으며 감사 FAILED 원본과 실제 거절을 보존했다. CLI 권한은 OS 격리나 사전 과금 강제 차단이 아니다.

수정 후 **unity-write 실제 연결과 Play→Pause→Resume→Stop**, 각 단계 C# 상태 재조회, 원래 씬/정지/dirty=false 복구·Console0오류를 확인했다. init의 미허용 노출0개·apiKeySource none이며 Codex가 별도 조회로 재검증했다. 무료 Peekaboo4.9.5는 실제 권한3종/Unity 창 캡처에 성공했다. fresh snapshot을 쓴 native 좌표 클릭2회는 runtime-host canonical-result 오류로 미확인이며 키 입력은 실행하지 않았다. 해당 포인터 경로만 보류하고 실제 Unity 조작은 전용 MCP로 수행한다. 설정을 연결 성공으로 세거나 native 캡처를 마우스 조작 성공으로 세지 않는다.

저장 기본값은 GPT-6.1 Sol High / Opus5.5 High, 실제 Claude Max·xhigh·High 요청·모델 반환·종료는 개별 기록을 따른다. 첫 Max 시간 종료·셸 감사 FAILED를 성공으로 바꾸지 않았다. 구독 계정 일치·credits/자동 충전 OFF 증거, 이미지 Codex 전담·비공개 원격을 유지했다. ImagineArt는 도구/무료 차감 경로가 확인되지 않아 미사용이며 별도 유료 fallback은 없다.


## 2026-10-17 서버 기억 저장 UI — 실제 CLI·검증

- 공식 Claude Code 2.1.285 / `claude-opus-5-5`만 사용했다. 별도 `codex/claude-save-transport-20261017`과 `codex/claude-save-view-20261017`의 첫 구현은 xhigh, 누락 응답/정확한 조회 문구/스크롤 보완은 High다. 실제 작성 세션은 각각 `5b4cc1ba-9519-47ef-88f7-930ce53c643b`, `2218f250-bd9b-4666-8537-1fe216702d61`이며 실행기 결과의 모델 집계도 Opus만이다. [작업·검토·실행 근거](../reports/server_memory_save_ui_20261017/README.md)를 따른다.
- 추론 전 기존 구독 인증/동일 계정/유효한 Usage credits·자동 충전 OFF 계정 UI 증거를 재사용·검사했다. 추가 API/크레딧/모델·재귀 호출은 없고 Codex의 포함 사용량도 실제 조회에서27%가 남았다. 이 사용량은 그 조회 시점이며 고정 배분/과금 서버 차단 보장이 아니다.
- 이 단위의 Claude는 겹치지 않는 소스/시험 파일 작성 담당이고 Editor 쓰기는 Codex 독점이다. 기존 실제 MCP 연결을 사용해 GUI Play304/304·Edit3/3, 실제 Core HTTP/uGUI62판정·원래 옵션0의 Play 종료/재시작/같은 pending 본문·다음 기억 덱16개를 확인했다. Console 오류0/기존 Metal 경고2이며 물리 입력·실기기/운영과 구분한다. 이미지 생성은 없었다.
