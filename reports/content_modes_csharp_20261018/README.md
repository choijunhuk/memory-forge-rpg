# 콘텐츠 모드 C# 이식 — §11.5 성장 던전 · §11.6 주간 보스 · §11.7 기록 균열 · §11.13 일일 활동 · §13.3 초기화 — 2026-10-18

기준 커밋 `377b080`(루트 브랜치 `codex/late-actions-balance`, JS 참조 `236b66e` 포함), 브랜치 `claude/content-modes-csharp`. Claude Opus 5.5(`claude-opus-5-5`) 루트 하위 세션이 이식·시험했고 Codex `gpt-6.1-sol`이 읽기 전용으로 독립 리뷰했다. JS 참조는 [engine/content-modes.js](../../engine/content-modes.js)와 [이전 보고](../content_modes_20261017/README.md)다.

## 요구사항·완료 조건

| 항목 | 내용 |
|---|---|
| 요구사항 절 | 원문 §11.5·§11.6·§11.7·§11.13·§13.3, JS 참조 `engine/content-modes.js`(JS 단계에서 Codex R3 승인) |
| 게임 내 역할 | 서버·Unity가 쓸 C# Core에서 성장 던전·주간 보스·기록 균열 전투(웨이브·균열 규칙 6종)와 계정 원장(보상·별·소탕·주간/주기/일일 키)을 JS와 같은 결과로 실행 |
| 완료 조건 | CoreChecks 전체 PASS(새 검사 포함) · `npm test` PASS · Codex 읽기 전용 리뷰 핵심 결함 0 · 루트 감사 PASS · 별도 브랜치 커밋(push 안 함) |
| 손대지 않은 것 | `engine/rules-core.js`·`data/game_data.json`·`data/content_modes.json`·매니페스트·기존 벡터 고정 자료(전부 0 diff) |

## 무엇을 만들었나

| 파일 | 역할 |
|---|---|
| [core/MemoryForge.Core/Battle.Modes.cs](../../core/MemoryForge.Core/Battle.Modes.cs) | `Battle.CreateMode`(=`ModeBattle.create`), `ModeData`, 웨이브, 균열 규칙 6종 훅. `BattleState.Mode`가 없으면 전부 무동작 |
| [core/MemoryForge.Core/ContentModes.cs](../../core/MemoryForge.Core/ContentModes.cs) | 타입 데이터·계정 원장 행, `Time`·`PartySetup`·`StageDiff`·`Dungeon`·`WeeklyBoss`·`Rift`·`Daily`, `RiftRun` |
| Battle.cs · Battle.Effects.cs · Battle.PlayerActions.cs | 훅 호출 한 줄씩: `CheckEnd`(다음 웨이브), `StartTurn`(rift_ink), `ScaleAttack`(rift_debt), `DamageBonus`(reserve/ink/debt), `Play` 끝(rift_echo), `OnGrowthResolved`(rift_bloom) |
| BattleState.cs · BattleDefinitions.cs | `BattleState.Mode`/`BattleMode`, 이벤트 `wave`/`left`, `BattleRules.Copy`, `BattleData.WithRiftBreak`(분리된 깊은 사본) |
| Run.cs · Run.Progression.cs | `Run.Derive` 추출(동작 동일), `ActorSetup`/`CardSpec` 내부 공개 — 콘텐츠 파티가 탐사와 같은 변환을 쓴다 |
| [tools/gen_content_modes_vectors.mjs](../../tools/gen_content_modes_vectors.mjs) | JS 오라클. 60개 op 스크립트를 실제 JS 모듈로 실행해 결과·오류·상태 해시·체크포인트 상태를 기록 |
| [engine/tests/vectors/content_modes_vectors.json.gz](../../engine/tests/vectors/content_modes_vectors.json.gz) | 생성 고정 자료(482 KB) |
| [engine/tests/content_modes_vectors.test.mjs](../../engine/tests/content_modes_vectors.test.mjs) | 고정 자료가 현재 소스로 다시 만든 것과 같은지 검사(`npm test` 포함) |
| [ContentModesChecks.cs](../../core/MemoryForge.CoreChecks/ContentModesChecks.cs) · [ContentModesDataLoader.cs](../../core/MemoryForge.CoreChecks/ContentModesDataLoader.cs) | C# 재생 검사와 호스트 데이터 로더 |

JS는 `rules-core.js` sha256 고정 때문에 하위 클래스로 훅을 걸었지만, C#은 `partial class Battle`이라 같은 메서드 안에 훅을 두었다. JS 호출 지점과 C# `CheckEnd` 호출 지점 14곳이 일대일로 같아서 웨이브 생성 시점이 같다. 기존 C# 검사(전투·탐사·기억 전부)가 훅 추가 후에도 같은 수로 통과해 일반 전투가 바뀌지 않았음을 확인했다.

## JS와 대조한 방법

생성기는 op 스크립트(`time`·`partySetup`·`stageDiff`·`modeData`·`battleCreate/Cmd`·`dungeon*`·`weekly*`·`rift*`·`daily*`)를 JS로 실행해 기록하고, C# 검사가 같은 op를 C# 객체로 재생한다. 이전 op 결과(계정·전투 상태·균열 상태)는 슬롯 참조로 이어 써서 C#은 자기 출력으로 연쇄한다. 명령마다 canonical 상태 해시·log/ev 해시를 비교하고 6번째 명령마다와 전투 종료 시 전체 상태 그래프를 비교한다. 원장 op마다 입력 계정이 바뀌지 않았는지도 확인한다.

**TEST-ONLY 변형.** 원문에 없는 웨이브 적 구성·균열 회전표·별 2/3 기준·선택형 일일 목록은 고정 자료 안의 이름 붙은 modes 변형(`dungeonWaves`·`riftPending/Strict/Lenient/Mid/Target/Bad/UnknownEnemy`·`dailyTarget`·`dailyTargetDefault`·`resetsAlt/Odd`)으로만 넣었다. `shipped` 변형은 `data/content_modes.json`과 바이트 단위로 같아야 하며 C#에서도 보류 항목이 `CONTENT_NOT_AUTHORED`를 던진다. 데이터 변형은 `weakBell`(시험용 b_bell 체력 6)과 `late`(제안 [late_enemies](../../proposals/late_enemies.cjs) 덧붙임, b_nameless 2페이즈) 두 가지다.

**규칙 가지 증인.** 생성 중 JS `ModeBattle` 메서드를 계측해 각 규칙 가지가 실제로 실행됐는지 센다(고정 자료 `witness`): rift_reserve 예약/즉시/필살기, rift_ink 소멸 보너스·턴 시작 얼룩, rift_debt 카드 피해·raw 피해, rift_bloom 방어, rift_echo 체인 끊김, 웨이브 생성. 이벤트 `wave`·`riftEcho`·`insert`·`phase`·`break`도 전부 나온다.

## 결과 (2026-10-18, .NET SDK 10.0.402, Node 25.9.0)

| 검사 | 결과 |
|---|---|
| `ops/ai/local/dotnet-sdk10/dotnet run --project core/MemoryForge.CoreChecks -c Release -- "$PWD"` | 전체 PASS, 새 **ContentModesChecks 10141/10141**: 60사례·2838 op·172 정확한 거부·1835 전투/균열 명령 ([전문](corechecks.txt)) |
| 기존 C# 검사 | 202·87·24·2221·29232·128244·87991·5451·215677·873·21902·2189·1562·1194·7589·553 전부 PASS(수 변화 없음) |
| `npm test` | 357/357 PASS ([요약](npm_test.txt)) |
| `node tools/gen_content_modes_vectors.mjs --check` | 고정 자료 최신 |
| `npm run test:docs` / `npm run test:handoff` | 9/9 PASS / 30/33 — PDF 시험 3개는 이 환경에 `fitz`·`pypdf`가 없어 ERROR(NOT_RUN 성격, PDF 변경 없음) |
| 변이 시험 | rift_debt bps 제거·주 요일 −1·균열 기지급 무시 → 311건 실패(debt 전투·시간·균열/주간 정산), 정확한 유리수 StageDiff → 5건 실패, Int32 debt 합 → 4건 실패(Codex 재현). 모두 복구 후 PASS |

## 의도적 C# 차이(타입 진단, parity와 구분)

| 경우 | JS | C# |
|---|---|---|
| 균열 규칙 params 일부 누락 | 그대로 생성 후 NaN 계산 | `UNKNOWN_RULE`로 생성 거부(규칙별 필요 키) |
| `nowMs`가 2^53 초과 | 정밀도 손실 후 계산/RangeError | `INVALID_TIME`(정확한 JS 수가 없음) |
| ±8.64e15 ms Date 한계 | `RangeError` | 같은 이름의 `MemoryBoundaryException("RangeError")` — 고정 자료로 대조 |
| `constructor` 같은 프로토타입 키 ID | `ix.*[id]`가 함수로 참이 됨 | 없는 ID로 처리 |
| 계정 | 전체 객체 복제 | 원장이 읽고 쓰는 필드만 담는 `ContentAccount`(나머지는 서버 행) |
| 소수 집중/횟수/이벤트 n | 거부 | 타입이 정수라 표현 불가 |

JS와 똑같이 남긴 동작: `RiftRun.Next`는 전투를 만들기 전에 층 RNG를 진행하므로 잘못된 적 구성이면 RNG만 진행되고 READY로 남는다(`rift-unknown-enemy-next`가 대조). 주간 키는 과거 시각 정산도 키를 바꾸며(JS 그대로), 균열 주기만 `STALE_CYCLE`로 막는다.

## 남은 것

- 보류 데이터(던전 웨이브·패배 집중·균열 회전표/적 조합·별 2/3 기준·선택형 일일 목록)는 원문 작성 전까지 C#에서도 `CONTENT_NOT_AUTHORED`.
- 서버 한 트랜잭션 정산(종료 전투 명령과 같은 트랜잭션), 저장된 `RiftRun` 상태 복원(C# `Run`과 같은 이유로 복원 미노출), Unity UI·실기기는 이번 범위 밖(NOT_RUN).

## 독립 리뷰

공식 Codex CLI 0.162.1, `command codex exec -s read-only -m gpt-6.1-sol -c model_reasoning_effort=xhigh --json`. 호출 전 `ROOT_START_CHECK` 통과·`codex login status` ChatGPT 로그인을 확인했다. Codex는 파일을 바꾸지 않았다. 전체 기록: [ops/ai/tasks/CONTENT-MODES-CSHARP-CODEX-REVIEW.result.json](../../ops/ai/tasks/CONTENT-MODES-CSHARP-CODEX-REVIEW.result.json).

| 회차 | 판정 | 지적 | 조치 |
|---|---|---|---|
| R1 | NO_BLOCKING_DEFECT | P2 3건: StageDiff가 JS binary64 반올림과 다름(41단계·12920 → JS 161), debt bps Int32 합 오버플로, rift_break 사본이 원본 목록 공유 | binary64 순서·Math.round 재현, BigInteger 승격+극단 params 전투 벡터, 깊은 사본+원본 불변 고유 검사 |
| R2 | NO_BLOCKING_DEFECT | R1 3건 해결 확인. P2 1건: 격자에 구분 행이 없어 옛 계산도 통과 | 1–60단계 × 1–30000 bps 전수에서 갈리는 3행을 모두 기록, 옛 계산 변이 5건 실패 확인 |
| R3 | NO_BLOCKING_DEFECT | R2 지적 해결 확인(독립 BigInt 전수로 같은 3행), 추가 P1/P2/P3 없음, `git diff --check` 깨끗 | — |

루트 감사: [root_session_audit.json](root_session_audit.json) — `python3 tools/ai/audit_root_session.py --out …` PASS, Codex 호출 3, 위반 0. 면제 1건을 사유와 함께 기록했다: 사용량 한도 직전의 로컬 WIP 커밋 하나가 이 세션의 첫 `ROOT_START_CHECK`보다 먼저 실행됐다(push·Codex 호출·설정 접근 없음). 이후 시작 점검을 통과한 뒤 Codex 호출과 나머지 커밋을 했다.
