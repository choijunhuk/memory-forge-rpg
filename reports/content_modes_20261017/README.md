# 콘텐츠 모드 진행기 — §11.5 성장 던전 · §11.6 주간 보스 · §11.7 기록 균열 · §11.13 일정 — 2026-10-17

기준 커밋 `a646bcc`(브랜치 `claude/content-modes-22c90c`). Claude Opus 5.5(`claude-opus-5-5`) 루트 하위 세션이 구현·시험했고, Codex `gpt-6.1-sol`이 읽기 전용으로 독립 리뷰했다. 원문은 [11장](../../docs/source_v0.8.1/11_chapter.md)과 [13장 §13.3](../../docs/source_v0.8.1/13_chapter.md)이다.

## 무엇을 만들었나

| 파일 | 역할 |
|---|---|
| [engine/content-modes.js](../../engine/content-modes.js) | 새 JS 참조 모듈. 계정 원장 순수 함수(입력은 복사하고 바꾸지 않음)와 `Battle` 하위 클래스 `ModeBattle` |
| [data/content_modes.json](../../data/content_modes.json) | 원문에 적힌 규칙·수치만 옮긴 보조 데이터. 원문에 없는 항목은 `DECISION_PENDING`과 사유 |
| [engine/tests/content_modes.test.mjs](../../engine/tests/content_modes.test.mjs) | 시험 22개. `npm test`에 자동 포함 |

`engine/rules-core.js`와 `data/game_data.json`은 한 바이트도 바꾸지 않았다. `rules-core.js`의 sha256을 JS·C# 벡터 고정 자료 10여 개가 묶고 있어서, 고치면 전부 재생성해야 한다. 그래서 전투 규칙 훅은 하위 클래스에 두었다. `Battle#command`가 후보 상태를 `new Battle`로 만들기 때문에 `ModeBattle`은 그 메서드를 생성자 한 곳만 바꿔 복사했다. 시험이 두 소스를 비교하므로 원본 `command`가 바뀌면 바로 실패한다. 규칙 없는 `ModeBattle`이 `Battle`과 매 명령 같은 상태를 내는지도 시험한다.

## 원문 대비

### §11.5 성장 던전 — `ContentModes.Dungeon`

| 원문 | 구현 |
|---|---|
| 6종, 계정 레벨 해금(§11.1 표) | `progression.dungeons[].unlock` 검사 → `LOCKED` |
| 단계 10~60, 보상 25~100% | `progression.dungeonTiers`, 60단계 값 × pct, 정수 버림(기존 데이터 주석) |
| 저장 덱 단일 전투(1~3웨이브) | 저장 기억(없으면 기본 덱) + 무기 카드, 단계 레벨 배율. 웨이브를 다 잡으면 같은 전투 안에서 다음 웨이브가 나온다 |
| 별 3 = 승리 + 4턴 이내 + 체력 50% 이상 | 별 = 1(승리) + 4턴 이내 + `hp×100 ≥ maxHp×50` |
| 3성 후 소탕, 같은 보상·같은 집중, 최대 12회 | 그 단계 최고 별 3일 때만, 1~12회, 회당 집중 20·보상 동일 |
| 집중 20/회(§11.4), 집중 1당 계정 XP 1(§11.9) | 승리 정산·소탕에서 집중 차감, `accountXp` 지급. 패배 정산은 보류(아래) |

### §11.6 주간 보스 — `ContentModes.WeeklyBoss`

| 원문 | 구현 |
|---|---|
| 해금 계정 15, 보스 4종 | `b_bell`·`b_notary`·`b_garden`·`b_archivist`만 허용 |
| 난이도 1~5(표) | 단계 레벨 배율, 5단계는 체력 130%·피해 120% 추가(`Stats.combine`) |
| 보상 주 3회, 모든 보스 공유, 월요일 05:00 KST 초기화 | 주 키 = 월요일 05:00 KST, 공유 카운터 최대 3 |
| 회당 승급 핵 2·크레딧 20,000·보스 토큰 40, 계정 XP 100(§11.9) | 그대로 지급 |
| 보상 없는 연습·재도전 무제한, 실패 미차감 | 시작은 카운터를 보지 않음. 패배·`practice` 전투는 카운터 불변 |
| 승리 기록과 주간 카운터를 한 트랜잭션에서 확인 | `settle`이 둘을 함께 확인하고 전투 상태에 `settled`를 남긴다. 한도를 넘긴 승리도 보상 없이 정산되므로 다음 주로 미룰 수 없다 |

### §11.7 기록 균열 — `ContentModes.RiftRun` / `ContentModes.Rift`

| 원문 | 구현 |
|---|---|
| 해금 계정 20, 6층, 층 레벨·6층 추가 배율 | `progression.recordRift.floors` |
| 14일 주기, 시즌 1·15·29일 11:00(§13.3) | `Time.riftCycle(시즌 시작일, 현재)` → 주기 번호·키(주기 시작일). 시즌 시작일은 운영 설정 입력 |
| 층마다 3연전: 1·3전 1파티, 2전 2파티(서로 다른 6명) | 전투 순서 `[0,1,0]`, 두 팀 모두 3명(`TEAM_SIZE`), 두 팀 중복 금지 |
| 파티별 체력은 층 안에서 이어짐 | 팀 HP를 전투 사이에 넘김(1파티는 1전 남은 HP로 3전) |
| 덱·예약·게이지는 전투마다 초기화 | 전투마다 저장 덱으로 새 `ModeBattle` |
| 보유 6명 미만이면 재출전 허용, 그 층 별 최대 2 | 보유 < 6일 때만 중복 허용, 그때 별 상한 2 |
| 보상: 층별 결정, 주기당 1회, 별 1·2·3에 60%·20%·20% | 별 1 = 3전 모두 승리, 별 2 = 총 턴 기준, 별 3 = 남은 체력 기준. 층·주기마다 별 몫을 한 번씩만 지급하고, 같은 주기에 더 잘 깨면 아직 안 받은 몫만 준다 → 층 보상·주기 800을 넘지 않음 |
| 규칙 6종(§11.1 표) | 아래 표 |

| 규칙 | 구현 |
|---|---|
| `rift_reserve` 시간의 틈 | 예약으로 해결된 피해 +3000bps, 즉시 피해 −1000bps(필살기는 즉시) |
| `rift_break` 금 간 거울 | 모든 적·보스 2페이즈의 균열 게이지 ×1.5(반올림, 최소 1), 균열 피해 +5000bps(기본 2500 대신) |
| `rift_ink` 번지는 잉크 | 매 턴 시작에 얼룩 토큰 1장을 버린 더미에 넣음, 소멸 카드 1장당 피해 +300bps(다른 규칙과 같은 소멸 수: 실제 소멸 + 시온 공명의 시작 소멸 수) |
| `rift_debt` 이자의 밤 | 유물·공명 반영 뒤 부채 상한 +2, 부채가 있으면 파티의 카드·필살기 피해 +1500bps, 반격·화상·유물 피해도 ×1.15(한 번만 버림) |
| `rift_bloom` 서리 온실 | 공명 반영 뒤 성장 상한 +2, 성장 카드 해결 시 방어 +3(온실 파워와 같은 고정 방어) |
| `rift_echo` 공명하는 무대 | 공명 반영 뒤 잔향 최대 +2, 서로 다른 캐릭터 카드 연속 3장 → 행동력 +1(턴당 1회) |

### §11.13 일정 — `WeeklyBoss` · `RiftRun(policy)` · `ContentModes.Daily`

- 주간 보스 공유 3회·실패 미차감·연습 무제한: 위 §11.6 구현.
- 균열 통합 목표(1~3층 1팀, 4~6층 2팀): `policy: 'target'`으로 고르면 1~3층은 한 팀이 세 전투를 HP를 이어 가며 치른다. 현행 기본값은 §11.7 본문대로 전 층 2팀(`activeTeamPolicy: "v0.8.1"`)이다. 기본값 전환은 정책 결정이라 바꾸지 않았다.
- 일일 활동: 05:00 KST 초기화, 100점 상한, 20·40·60·80·100점 이정표 1일 1회 지급, 100점 도달 시 계정 XP 300(§11.9). 현행 정책은 §11.11의 5개 임무라서 레벨 상한 계정은 80점에서 멈춘다(시험으로 확인). 선택형 목표 정책의 구조(어떤 조합이든 100점까지)는 있고, 임무 목록은 보류다.

## 해석 기록

원문이 정하지 않은 동작은 아래처럼 읽었다. 수치는 새로 만들지 않았다.

1. 던전 승리는 정산 때 집중 20을 차감한다. 패배는 차감 규칙이 없어 정산 자체를 막는다(무료 패배도, 임의 차감도 만들지 않음).
2. 다음 웨이브는 현재 웨이브를 다 잡은 순간 같은 전투에 나온다. 같은 카드의 남은 효과와 대기 중인 예약은 새 웨이브에 이어서 적용되고, 턴 수·HP·손패는 이어진다.
3. 주간 보스는 끝난 전투마다 정확히 한 번 정산한다. 주 키는 정산 시각 기준이다. 서버는 마지막 명령과 같은 트랜잭션에서 정산해야 한다.
4. 균열 목표 정책의 1팀 층은 같은 팀이 세 전투를 치르는 것으로 읽었다.
6. 원문에 없는 잠금은 만들지 않았다. 던전은 던전 단위 계정 레벨로만 열고(단계별 잠금 없음, 높은 단계는 스테이지 배율로 어려워짐), 균열 층은 서로 잠그지 않는다. 데이터에는 `NO_RULE_IN_SOURCE`로 적었다.
5. `rift_debt`의 "모든 피해"는 파티가 주는 모든 피해(카드·필살기·반격·화상·유물)로 읽었다(받는 피해 제외). `rift_echo` 연쇄는 주인 캐릭터가 없는 카드(공용·저주·토큰)에서 끊기고, 필살기는 영향이 없으며, 턴마다 초기화한다. `rift_break`의 1.5배는 적 체력 배율과 같은 `Math.round`를 쓴다.

## 보류 항목 (DECISION_PENDING)

원문·데이터에 근거가 없어 구조만 만들고 엔진이 `CONTENT_NOT_AUTHORED`로 막는 항목이다. 값을 정하면 [data/content_modes.json](../../data/content_modes.json)의 해당 `null`만 채우면 된다.

| 항목 | 근거 | 현재 동작 |
|---|---|---|
| 던전·단계별 웨이브 적 구성 | §11.5는 "1~3웨이브"만 적음 | `Dungeon.start` 거절 |
| 던전 패배 시 집중 차감 | §11.4·§11.5에 없음 | 패배 정산 거절(`CONTENT_NOT_AUTHORED`) |
| 균열 주기별 층 규칙 배정·적 조합 | §11.7 "14일마다 바뀐다"뿐, 표 없음 | `RiftRun.create` 거절 |
| 균열 별 2·3 기준(총 턴·남은 체력 수치) | §11.7에 기준 이름만 있음 | 클리어한 층의 별 계산·정산 거절. 실패한 층(0성)은 정산 가능 |
| 선택형 일일 임무 목록·이벤트·점수 | §11.13은 대체 행동 예시만 적음 | `target` 정책 거절 |

## 시험 결과

2026-10-18 00:20 KST 무렵, 이 worktree에서 직접 실행했다(Node 22, macOS).

| 시험 | 결과 |
|---|---|
| `node --test engine/tests/content_modes.test.mjs` | PASS 22/22 |
| `npm test` (위 22개 포함 전체 엔진 시험) | PASS 355/355 |
| `npm run test:proposals` | PASS 14/14 |
| `python3 -m unittest discover -s tests` | PASS 57/57 |
| `npm run test:docs` | PASS |
| `node tools/gen_run_create_vectors.mjs --check` | PASS (2131 사례, 고정 자료 재생성 불필요) |
| `npm run test:handoff` | 30/33. 실패 3개는 로컬에 `fitz`·`pypdf` 모듈이 없어 PDF 시험을 못 연 것(환경, 이번 변경과 무관). 의존성은 설치하지 않았다 → PDF 3개 NOT_RUN |
| `npm run verify:manifest` | FAIL(기존). 오류는 모두 배포 매니페스트 이후 이미 바뀐 기존 파일(`engine/rules-core.js`·`AGENTS.md` 등)의 `CHANGED`다. 이번에 만들거나 고친 파일은 매니페스트 대상이 아니어서 결과에 영향이 없다 |

`engine/rules-core.js`·`data/game_data.json`·매니페스트·벡터 고정 자료는 diff 0이다. 시험의 웨이브·회전표·별 기준·선택형 임무 값은 시험 전용 대역이며 배포 데이터는 `null`이다. 시험은 실제 전투를 끝까지 돌린다(욕심쟁이 봇): 웨이브 던전 승리, 약화한 보스 복사본 승리, 균열 한 층 3연전 클리어. 수치 비교는 같은 설정의 규칙 없는 전투와 견준다.

C# Core·서버·Unity·실기기 시험은 이번 범위가 아니어서 NOT_RUN이다.

## Codex 리뷰

공식 Codex CLI 0.162.1, `command codex exec -s read-only -m gpt-6.1-sol -c model_reasoning_effort=xhigh`. 호출 전 `ROOT_START_CHECK` 통과·`codex login status` ChatGPT 로그인을 확인했다. Codex는 파일을 바꾸지 않았다. 전체 기록: [ops/ai/tasks/CONTENT-MODES-CODEX-REVIEW.result.json](../../ops/ai/tasks/CONTENT-MODES-CODEX-REVIEW.result.json).

| 회차 | thread | 판정 | 지적과 처리 |
|---|---|---|---|
| 1 | `01a14a66-9641-7052-94e2-f4aaa601b1ea` | CHANGES_REQUESTED | implementation_defect: `rift_debt`가 반격·화상·유물 피해를 빠뜨림 → `scaleAtk` 재정의(한 번만 버림)·회귀 시험. design_mismatch: 보류인 던전 패배가 무료로 정산됨 → 패배 정산 거절. design_mismatch: 2팀 층이 6명을 강제하지 않음(`[[sera],[yuri]]`로 별3) → 두 팀 모두 3명 강제. real_constraint: 주간 이월 방지는 서버 원자 정산에 달림 → 수용(아래) |
| 2 | `01a14a6c-92fc-7ba1-bf28-fa42c51d916c` | CHANGES_REQUESTED | 1회차 수정 3건 확인. implementation_defect: `rift_ink`가 시온 공명의 `exhaustStart`를 무시 → 공용 소멸 수 사용·회귀 시험. design_mismatch: 별 기준 보류인데 클리어 층이 별1을 지급 → 별 계산·정산 거절(상태 불변 시험). 단계별 던전 잠금·층 순차 잠금은 원문에 규칙이 없다는 근거로 `NO_RULE_IN_SOURCE`로 재분류 |
| 3 | `01a14a72-3f9b-7f23-9303-4031d57e4460` | **APPROVE** | 2회차 수정 2건 확인(각 기준 단독 누락·실패 층 0성 정산까지 독립 검증). `NO_RULE_IN_SOURCE` 해석 수용(optional_opinion). 핵심 결함 0 |

남은 real_constraint(수용): 같은 계정 스냅샷으로 동시 정산하거나 정산을 늦추면 초기화 경계를 넘을 수 있다. 서버는 마지막 전투 명령·정산 표시·주간 카운터·보상을 한 트랜잭션에 저장해야 한다. 이 JS 참조 모듈 단독으로는 이월 방지를 보장하지 않는다.

## 세션 감사

[root_session_audit.json](root_session_audit.json): 세션 `3c0050fa-61fb-4389-9a84-ee7838647aac`, 모델 `claude-opus-5-5`만, `ROOT_START_CHECK` 통과·현재, Codex 호출 3회(규칙 형식), Unity 잠금 없음(Editor 미사용), **pass**. 예외 1건을 그대로 적는다: `env | grep -c …`로 공급자 변수 이름 3개의 존재만 세려던 명령이 `permissions.deny`에 막혀 실행되지 않았고, 감사가 이를 `env_dump`로 잡아 `--waive`로 사유를 남겼다. 미검증 항목(첫 도구 목록·MCP 하위 프로세스 환경·인터프리터 코드 의미·계정 화면 Usage credits OFF)은 감사 미검증이다.

## 다음 단계

- C# Core 이식: `ModeBattle`(웨이브·6규칙)·원장 함수를 `MemoryForge.Core`로 옮기고 이 모듈로 JS→C# 벡터를 만든다. 이번 범위 밖이다.
- 서버: `battle`·`weekly_claim_counter`·`rift_progress`·`dungeon_clear` 테이블([schema.sql](../../server/schema.sql))에 정산을 한 트랜잭션으로 연결하고 `/battles`·`/dungeons/{id}/sweep` API를 붙인다.
- Unity UI: 던전·주간 보스·균열 화면과 공개 웨이브/규칙 표시.
- 보류 항목 값 확정(사용자/설계 결정).
