# 20. 단계별 제작 계획 (AI 에이전트용)

## 20.1 작업 원칙

- 모든 작업은 이 저장소에서 시작한다. 규칙·수치는 `data/game_data.json`(작성은 `content/*.mjs`), 동작 기준은 `engine/rules-core.js`.
- 한 단계의 "완료 조건"을 모두 통과해야 다음 단계로 간다. 통과 증거(실행 명령·출력)를 `reports/`에 남긴다.
- 역할(기본): **Codex** = C# 코어·서버·자동 시험·도구, **Claude** = Unity UI·콘텐츠 데이터·문구·서사·문서. 작성자가 아닌 쪽이 교차 검토한다. 같은 Unity 씬·프리팹 동시 편집 금지(브랜치·worktree 분리).
- 금지: 운영 배포, 실결제, 비밀 키 열람, 운영 DB 접근, 확률·가격·재화 지급량 임의 변경(변경은 ADR 초안 → 사람 승인).

## 20.2 단계

| 단계 | 산출물 | 완료 조건 | 담당 |
|---|---|---|---|
| **P0-1 사람 플레이 테스트** | 프로토타입 플레이 기록 10판 이상, 설문(재미·난이도·예약/균열 이해도) | 예약·균열 이해 80%, "다시 하고 싶다" 70% | 디렉터 |
| **P0-2 조정** | 카드·적 수치 조정, ADR | §19.4 표 유지, 모든 시험 통과 | Claude + Codex |
| **P1-1 C# 전투·탐사 이식** | `core/MemoryForge.Core`에 Battle·Run·기억 저장 | JS 시나리오 1,000개 상태 해시 일치(§16.2), `CoreChecks` 통과, netstandard2.1/C# 8 | Codex |
| **P1-2 서버 골격** | ASP.NET Core: 인증(게스트·dev), 계정, 콘텐츠 버전, 모집, 캐릭터·무기 성장, 탐사·전투 명령, 기억 저장 | OpenAPI 계약 시험, §19.3 서버 시험(동시성·멱등), 마이그레이션은 `server/schema.sql`에서 시작 | Codex |
| **P2-1 구역 3~5 콘텐츠** | 일반·엘리트 20종 + 보스 3(무명 2페이즈)·조우표, 새 적 행동 6종(§10.4) | 엔진·C# 동시 구현, 퍼즈 예외 0, §19.4 목표, 스토리 4~6장 `authored: true` | Claude(데이터) + Codex(엔진) |
| **P2-3 고난도 전투 규칙** | 전투 수정자 6종(§11.1의 "기록 균열 규칙" 표), 기록 균열 연전 진행기(3연전·2파티·파티별 체력 이월·전투마다 덱 초기화), 성장 던전 웨이브(1~3) | JS·C# 동시 구현, 단위 시험·트레이스 일치, 퍼즈 예외 0 | Codex |
| **P2-2 서사·대사** | 1부 대본(프롤로그~6장), 인연 에피소드 40편, 이벤트 2편, 캐릭터 대사 600줄, 현지화 키 | §4.5 규칙 검수(10대 플라토닉), 분량 가이드 | Claude |
| **P3-1 Unity 수직 슬라이스** | 로비 → 편성 → 지도 → 전투 → 보상 → 기억 저장, 서버 연동 | 프로토타입과 같은 흐름, 오류 코드 문구, 48dp, 60fps(중급기) | Claude(UI) + Codex(네트워크) |
| **P3-2 Unity 전체 화면** | 모집·캐릭터·무기·도감·임무·우편·상점·설정 | §14 화면 명세, 접근성 설정 | Claude |
| **P4 아트·사운드** | §15.3 자산 전부(10명), 적·배경·UI 아이콘, Spine 리그, 사운드 | §15.5 검수 100%, provenance 기록 | 아트(사람) + 이미지 생성 |
| **P5 라이브 기능** | 임무·패스·월정액·상점·Play 결제 검증·이벤트·관리 도구·텔레메트리 | 결제 서버 검증·acknowledge·무효 구매 처리 시험, 텔레메트리 사전(§16.6) | Codex + Claude |
| **P6 출시 준비** | 비공개 테스트, 등급·확률 고지·개인정보 문서, 성능·안정성 | §18.4 체크리스트 전부, 크래시 없는 세션 99.5% | 사람 승인 |

## 20.3 복사해서 쓰는 작업 지시문

### P1-1 (Codex) — C# 전투·탐사 이식

```text
저장소 memory-forge-rpg에서 작업한다. 먼저 AGENTS.md, docs/design_v0.7.md의 §5(전투), §7(탐사), §8(기억 저장), §16.2를 읽어라.
목표: engine/rules-core.js의 Battle·Run·memoryChanges·commitMemory를 core/MemoryForge.Core(netstandard2.1, C# 8, 외부 패키지 없음)로
1:1 이식한다. 부동소수점 금지(정수·System.Numerics.BigInteger), 반올림은 JS와 같게(Math.round 동치 = (2a+b)/(2b), 내림 = 정수 나눗셈).
1) tools/gen_traces.mjs를 새로 만들어 JS 무작위 봇으로 시나리오 1,000개(짧게 700·정규 300)의 {seed, party, 명령열, 명령별 상태 해시}를
   engine/tests/vectors/traces.json으로 저장한다. 상태 해시 = 키 정렬 JSON의 SHA-256(로그 필드 log·ev 제외).
2) C#에 같은 직렬화와 해시를 구현하고 CoreChecks에 trace 재생 검사를 추가한다.
3) 완료 조건: dotnet run --project core/MemoryForge.CoreChecks -- . 가 기존 202개 + trace 1,000개 전부 통과. node --test 그대로 통과.
결과 명령과 출력 요약을 reports/csharp_checks.txt에 남겨라. 규칙이 문서와 엔진에서 다르면 엔진을 바꾸지 말고 docs/decisions.md에 질문을 남겨라.
```

### P1-2 (Codex) — 서버 골격

```text
AGENTS.md와 docs/design_v0.7.md §16·§17·§18, server/openapi.yaml, server/schema.sql을 읽어라.
ASP.NET Core(.NET LTS) 프로젝트 server/MemoryForge.Server를 만들고 core/MemoryForge.Core를 참조한다.
구현: /auth/session(guest, dev), /account, /content/version, /gacha/*(CSPRNG roll, 천장 상태 기계는 Core.Gacha 재사용),
/characters/*, /weapons/*, /memory-decks, /runs/*, /battles/*. 모든 쓰기는 §17.2 트랜잭션 골격(영수증 → 고정 잠금 순서 → 규칙 → 원장 → 영수증 → 커밋).
DB는 server/schema.sql을 첫 마이그레이션으로 쓴다. 운영 환경에서 mock 결제·dev 인증은 시작 시 거부.
완료 조건: (1) OpenAPI 계약 시험(모든 경로 200/4xx 스키마 일치) (2) 같은 계정 모집 100병렬 요청 → 이중 차감 0, 천장 일관
(3) 같은 requestId 재시도 = 같은 응답, 다른 본문 = 409 (4) 주간 보스 4회째 422 WEEKLY_LIMIT (5) tools/check_schema.sh 통과.
```

### P2-1 (Claude 데이터 + Codex 엔진) — 구역 3~5

```text
docs/design_v0.7.md §10.3(예산)·§10.4(새 행동·적 목록)·§19.4(목표)를 읽어라.
Codex: engine/rules-core.js의 enemyMove에 apDrain·sealSlot·delayRes·silence·eatName·phase를 §10.4 정의 그대로 추가하고 의도 텍스트·C# 이식·단위 시험을 붙인다.
Claude: content/world.mjs에 구역 3·4·5 적·엘리트·보스(b_usurer, b_overture, b_nameless 2페이즈)와 encounters를 추가하고
content/progression.mjs storyChapters 4~6을 authored: true로 바꾼다.
완료 조건: node content/build.mjs data, node --test, fuzz_runs(구역 지정) 예외 0, balance_probe가 §19.4 범위(구역 3~5 목표는 2구역 값에서
턴 +10%·체력 손실 +5%p까지 허용), node tools/gen_docs.mjs 성공.
```

### P3-1 (Claude) — Unity 수직 슬라이스

```text
docs/design_v0.7.md §14(UI)·§5(전투)·§17.4(공개 DTO)를 읽고 prototype/index.html을 직접 플레이해 흐름을 확인하라.
Unity 6 LTS(URP 2D, IL2CPP ARM64) 프로젝트 client/를 만든다. 화면: 로비, 편성, 지도, 전투, 보상, 휴식/공방/상점/사건, 기억 저장, 결과.
전투 화면은 §14.3 와이어프레임과 상태표를 따르고, 미리보기는 core DLL로 상태 사본에서 계산한다. 권위 상태는 서버 응답.
오류는 부록 §23.2 문구. 터치 48dp, 글자 크기 3단계, 동작 줄이기.
완료 조건: dev 서버와 연결해 짧은 탐사 1판 완주 + 기억 저장, 프로토타입과 같은 결과(같은 시드·명령이면 같은 상태), 중급기 60fps.
```

### P4 (아트) — 이미지 생성

```text
docs/design_v0.7.md §9.1(디자인 기준)·§15(아트)를 읽어라. 캐릭터마다 §15.6의 [SHEET]로 디자인 시트를 먼저 확정하고,
이후 [KEYART][CUTIN][SD][EXPR]은 확정 시트를 참조 이미지로 넣어 생성한다. 10대 캐릭터에는 [10대 규칙] 블록을 반드시 유지한다.
생성물은 §15.5 체크리스트로 선별하고 art/provenance.csv에 프롬프트·참조·도구·날짜를 기록한다. 파일 이름은 §15.3 규칙.
```

## 20.4 저장소 규칙

- 브랜치: `p1-core`, `p1-server`, `p2-zones`, `p3-client`… 작업마다 PR, CI(§18.2) 통과 필수.
- 커밋 전: `node content/build.mjs data && node tools/gen_vectors.mjs && node tools/gen_docs.mjs` 후 변경분까지 함께 커밋(생성물 불일치 금지).
- 보고: 실행한 명령, 결과, 시드, 바꾼 규칙, 하지 않은 것을 PR 본문에 적는다. "소스 작성·컴파일·단위 시험·Play Mode·실기기"를 구분한다.
