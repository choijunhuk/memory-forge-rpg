# 요구사항별 구현·검증 장부 — 2026-10-15

이 장부는 원본 [0~23장 전체](source_v0.8.1/)의 위치·게임 내 역할·현재 구현·남은 인수 조건을 연결한다. 원본 본문·표·10명 전체 시트·173개 카드·50개 프롬프트를 축약하거나 대체하지 않는다. 표의 절 제목은 원문 그대로이며 해당 파일 안의 같은 절 전체가 요구사항이다. 숫자·가격·확률·나이·보상·정책 변경 권한을 이 장부에서 만들지 않는다.

`IMPLEMENTED`는 명시한 JS/HTML/데이터/문서 범위의 구현을 뜻한다. `PARTIAL`은 일부 코드·데이터·계약만 있음, `NOT_IMPLEMENTED`는 해당 제품/진행기/서버가 없음, `DECISION_PENDING`은 정책 결정이 남음, `IN_PROGRESS`는 현재 작업 중이다. 코드 존재·시험 PASS·독립 검토·통합·Unity Play·빌드·실기기 수용을 같은 완료로 처리하지 않는다. 날짜가 다른 보고서는 각 실행의 증거이며 이번 장부 작성 중 재실행한 시험으로 세지 않는다.

현재 공통 검토 상태: P0와 후반 행동의 기존 Claude 작업 결과는 보존돼 있다. 새 실행기·전투 수정·트레이스/직렬화 기반·공유Core연결의 반대AI최종검토를 실제회수했고 결함수정 후통합가능으로확인했다. 각 코드 행의 `IMPLEMENTED`는 이 새 최종 검토까지 승인됐다는 뜻이 아니다. 실제 런타임·모델·과금·Unity 근거는 [AI_ENVIRONMENT.md](AI_ENVIRONMENT.md)를 따른다.

## 다음 미차단 작업과 통합 차단

| 작업 | 상태 | 정확한 요구/완료 조건 |
|---|---|---|
| P1 JS→C# 트레이스 생성기 | IN_PROGRESS | §16.2·§20.9 P1-1. `tools/gen_traces.mjs`와 회귀9개·전체 출력/재생 검증 완료. 실제700short+300full=1000개·273748명령·275748해시 재생 PASS. [전체 증거](../reports/cross_language_20261015/replay.json). 상대 검토·통합을 거쳐 C# 실제 명령 재생으로 이어감. JS oracle 성공은 C# 동치 아님. |
| C# 전체 Battle·Run·기억 저장 | IN_PROGRESS | C#은 Rng/Gacha/Stats/ReservationBook·canonical 기반과 실제 Battle.Create/begin을 구현했다. [전투 시작267](../reports/csharp_opening_20261015/)과 [실제반복턴356사례/1740요청·29232검사](../reports/csharp_turns_20261015/), Unity PlayMode7개 및 Opus Max APPROVE. Git/CI는 해당 head별 별도 기록. 시작/종료2000해시 PASS와 구별하며 전체1000 Run 트레이스 명령 재생은0개. [전체카드정의/순수질의702사례·128244검사](../reports/player_queries_20261015/) 완료. play/ult·예약/부채/파워/균열 생산·전체 전투/탐사/기억 저장은 미구현. netstandard2.1/C#8·정수/BigInteger·컬렉션 순서·동일 명령별 해시1000개와 기존 CoreChecks를 모두 수용해야 함. |
| 후반 본편 활성화 | BLOCKED_ACCEPTANCE | JS 여섯 행동·HTML·23종 후보 있음. 최신40시나리오/조우 확대는44조우×40×3=5280회, 목표 미달21개. C#·서사/연출·목표 미달 해소 전 `authored` 활성화/본편 완료 선언 금지. |
| 실제 Unity 게임 수직 슬라이스 | PARTIAL(환경만) | 실제 Editor의 연결 검증 씬·프리팹·Play·EditMode3개·macOS 연결 앱 빌드는 확인. 로비→편성→지도→전투→보상→저장·서버·실기기는 미완료. |
| 서버 앱·저장v2 접합 | NOT_IMPLEMENTED | §17 전체 및 §8.11·§17.5: 소유권·버전·고정 잠금·원장·이력·멱등·rollback·다기기 동시성. 스키마/JS 로컬 저장으로 대체 불가. |
| 반대 AI 최종 검토 | IN_PROGRESS | 실제Opus Max검토·결함수정·재시험·범위재검토 완료. 다음C#opening단위는 별도검토/통합. 두 차례 미해결은 해당 쟁점만 보류. |

P1/P2 완료로 개발을 끝내지 않는다. 이 표와 아래 장별 요구에서 의존성이 풀린 다음 작업을 고른다. 정책·권한·환경·구독 한도는 해당 항목만 보류하고 현재 세션의 독립 작업을 이어간다. 세션 밖 자동 개발은 약속하지 않는다.

## 장 00 — 기억 공방 / Memory Forge — v0.8.1 복구·검증 통합 설계서

게임 내 역할: 문서·데이터·실행 근거의 권위를 구분하고 원본을 보존한다. 전체 원문: [00_chapter.md](source_v0.8.1/00_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§0.1 먼저 볼 결론** | PARTIAL | [현재 상태][current]·[P0 증거][p0]·[Unity 증거][env] | 배포 당시 요약과 현재 구현을 구분; Unity 게임·서버·C# 전체는 별도 수용 필요 |
| **§0.2 문서·데이터의 권위와 변경 범위** | IMPLEMENTED(문서) | [결정 장부][decisions]·[콘텐츠][content]·[JS][js] | 원문·실행 수치·제안 계층을 계속 보존 |
| **§0.3 재현과 인계** | IMPLEMENTED(문서) | [시작 지시][start]·[문서 검사][doc-check] | 재배포 때 전체 PDF·입력 보존 검사를 재실행 |
| **§0.4 확인한 것과 확인하지 않은 것** | PARTIAL | [P0 증거][p0]·[후반 증거][late]·[Unity 증거][env] | 각 실행의 날짜·범위를 유지; C#/DB/실기기/결제는 별도 |
| **§0.5 표기 규칙** | IMPLEMENTED(JS) | [JS][js]·[엔진 시험][engine-tests] | C# 전체 이식에서 정수·절대 만기·개체 키 동일성 검증 |

## 장 01 — 1. 3분 요약

게임 내 역할: 예약·균열·캐릭터 규칙과 탐사→기억 저장이라는 핵심 제품 경험을 연결한다. 전체 원문: [01_chapter.md](source_v0.8.1/01_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§1.1 한 문장** | PARTIAL | [JS Battle/Run][js]·[브라우저 UI][ui] | 게임 제품의 서사·성장·고난도·Unity·서버 연결 |
| **§1.2 무엇이 우리 게임인가** | PARTIAL | [데이터][data]·[JS][js] | 수집/운영 전체 루프와 저장 덱 고난도 사용 수용 |
| **§1.3 핵심 수치** | PARTIAL | [데이터][data]·[경제 시험][economy-tests] | 핵심 수치 유지; 목표 용량·최종 난도·등급은 별도 미완료 |
| **§1.4 v0.6 대비 바뀐 것 (요약)** | PARTIAL | [기존 C# 코어][core]·[엔진 시험][engine-tests] | 과거 교차 검사 수를 C# 전체 전투 완료로 확대하지 않음 |
| **§1.5 지금 바로 해 볼 수 있는 것** | IMPLEMENTED(JS/HTML) | [UI][ui]·[P0 브라우저 로그][p0-browser] | Android/Unity 실제 탐사 한 판은 별도 |
| **§1.6 v0.8.1에서 복구한 범위** | IMPLEMENTED(문서) | [원문 전체][source]·[문서 검사][doc-check] | 기존 본문을 장부 요약으로 대체하지 않음 |

## 장 02 — 2. 콘셉트와 방향

게임 내 역할: Android 우선 제품 방향·디자인 기둥·비전투 주인공·로스터 원칙을 지킨다. 전체 원문: [02_chapter.md](source_v0.8.1/02_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§2.1 제품 정의** | PARTIAL | [Unity 설정][env]·[JS][js] | Android 가로·서버·현지화·제품 세션 성능 수용 |
| **§2.2 디자인 기둥** | PARTIAL | [JS][js]·[저장 v2 시험][memory-tests] | 고난도 저장 덱·전 구간 공명0 수용·사람 플레이 |
| **§2.3 참고와 차별점** | PARTIAL | [Run][js]·[콘텐츠][content] | 장르 비교는 구현 증거가 아님; 계정/라이브 루프 연결 |
| **§2.4 주인공 원칙** | PARTIAL(문서/데이터) | [인물 대사 원본][notes]·[원문 2장](source_v0.8.1/02_chapter.md) | 성별 중립·비전투 공방장 원칙을 완성 대본/연출에서 검수 |
| **§2.5 등장인물 원칙** | PARTIAL(문서/데이터) | [캐릭터 데이터][characters]·[브리프][briefs] | 현재 로스터·나이 보존; 실제 제작물 표현 검수 |

## 장 03 — 3. 게임 루프와 콘텐츠 지도

게임 내 역할: 한 판·일일·주간·시즌 콘텐츠와 성장/저장 덱의 순환을 연결한다. 전체 원문: [03_chapter.md](source_v0.8.1/03_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§3.1 핵심 루프** | PARTIAL | [Run][js]·[UI][ui]·[저장 v2 시험][memory-tests] | 탐사→저장→재탐사는 연결, 계정/고난도는 미연결 |
| **§3.2 시간 단위별 루프** | PARTIAL(데이터) | [성장 데이터][progression]·[경제 시험][economy-tests] | 일일/주간/시즌 실제 진행·지급·리셋 서버 필요 |
| **§3.3 콘텐츠 지도** | PARTIAL(데이터) | [성장 데이터][progression]·[Run][js] | 성장 던전·균열·인연·이벤트 진행기를 각각 구현 |
| **§3.4 첫 7일 (온보딩·리텐션 목표)** | NOT_IMPLEMENTED | [온보딩 원문](source_v0.8.1/03_chapter.md)·[성장 데이터][progression] | 첫 7일 계정 시뮬레이션과 실제 온보딩·잔존 측정 |
| **§3.5 현행과 목표 루프의 차이** | PARTIAL | [P0 증거][p0]·[목표 정책][policy-data] | 저장 v2 연결만 완료; 선택형 일일·초기1팀 미구현 |

## 장 04 — 4. 세계관과 이야기

게임 내 역할: 레테·세력·무명·기억 회수·본편/인연/이벤트 이야기를 실제 콘텐츠로 만든다. 전체 원문: [04_chapter.md](source_v0.8.1/04_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§4.1 무대: 종탑도시 레테** | PARTIAL(문서/데이터) | [세계 데이터][world]·[원문 4장](source_v0.8.1/04_chapter.md) | 도시/구역의 실제 장면·배경·플레이 연출 |
| **§4.2 세력과 NPC** | PARTIAL(문서) | [원문 4장](source_v0.8.1/04_chapter.md)·[인물 원본][notes] | 세력·NPC 대본/리소스·클라이언트 연결 |
| **§4.3 적대자: 무명(無名)** | PARTIAL(문서) | [원문 4장](source_v0.8.1/04_chapter.md)·[인연 데이터][progression] | 무명·기억 카드 앞뒤 기록의 대본/보상 연결 |
| **§4.4 1부 「멈춘 시계의 공방」 줄거리** | PARTIAL(데이터/초안) | [storyChapters][progression]·[세계 데이터][world] | 프롤로그~6장·시즌1 두 이벤트 완성 대본/게임 흐름 |
| **§4.5 서사 작성 규칙** | PARTIAL(제작 규칙) | [원문 4장](source_v0.8.1/04_chapter.md) | 장별 분량·선택지·관계·예약/균열 연출 검수 |
| **§4.6 본편 결말과 수집 보상의 분리 — 목표** | NOT_IMPLEMENTED | [목표 정책][policy-data]·[성장 데이터][progression] | 본편6인장·인연20조각 아이템/보상/엔딩 분기 동시 연결 |
| **§4.7 중반 위기와 4~6장 서사 보완 — 대본 초안** | PARTIAL(대본 초안) | [원문 4장](source_v0.8.1/04_chapter.md) | 4~6장 완성 대본·현지화·연출·전체 플레이 |

## 장 05 — 5. 전투 규칙 (완전 명세)

게임 내 역할: 전투 순서·입력 검증·예약·균열·상태·피해·종료·결정론을 동일하게 판정한다. 전체 원문: [05_chapter.md](source_v0.8.1/05_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§5.1 기본 수치 (`data.rules`)** | IMPLEMENTED(JS) | [rules][data]·[Battle][js]·[엔진 시험][engine-tests] | C#·서버 판정 동치 |
| **§5.2 전투 구성** | IMPLEMENTED(JS) | [Battle.create][js]·[엔진 시험][engine-tests] | [C# Create/begin267사례](../reports/csharp_opening_20261015/) 완료; 전체 전투 동치는 별도 |
| **§5.3 턴 진행 순서** | IMPLEMENTED(JS) | [startTurn/endTurn][js]·[첫 턴 회귀][review-tests] | [C# begin/end·반복턴](../reports/csharp_turns_20261015/) 확인; 예약/파워 등 실제 생산 경계 남음 |
| **§5.4 카드 사용** | IMPLEMENTED(JS) | [check/precheck/resolveCard][js]·[엔진 시험][engine-tests] | C# 실패 무변경·해결 중 종료 동치 |
| **§5.5 시간 예약** | PARTIAL(JS/C# 유틸) | [Battle][js]·[ReservationBook][reservation]·[후반 시험][late-tests] | C# 전체 Battle의 절대 만기·최신 지연/봉인 동치 |
| **§5.6 균열과 속성** | IMPLEMENTED(JS) | [Battle][js]·[엔진 시험][engine-tests] | 균열 해제·행동 취소·AP 보너스 C# 동치 |
| **§5.7 적 의도와 행동** | IMPLEMENTED(JS) | [enemyMove][js]·[후반 시험][late-tests] | [C# 라이브20종 전체 패턴 두 바퀴](../reports/csharp_turns_20261015/) 확인; 후보후반·실제카드효과·서버 공개 의도 남음 |
| **§5.8 상태와 키워드** | IMPLEMENTED(JS) | [Battle][js]·[콘텐츠][world]·[엔진 시험][engine-tests] | 모든 상태/키워드의 C# 동치 |
| **§5.9 방어·반격·체력** | IMPLEMENTED(JS) | [Battle][js]·[치명 다단 시험][lethal-tests] | 반격·반사·치명 피해 경계 독립 최종 검토 및 C# 동치 |
| **§5.10 필살기 게이지** | IMPLEMENTED(JS) | [Battle][js]·[반사 필살기 시험][ult-tests] | 카드/효과 게이지 상한·필살기 종료 경계 C# 동치 |
| **§5.11 캐릭터 고유 규칙 요약** | IMPLEMENTED(JS) | [캐릭터][characters]·[Battle][js]·[성장 회귀][review-tests] | 10명 전체 규칙의 C#·제품 조합 수용 |
| **§5.12 피해·방어 수식** | IMPLEMENTED(JS) | [Battle/Stats][js]·[벡터][vectors]·[엔진 시험][engine-tests] | C# 전체 피해/방어/회복 BigInteger·반올림 동치 |
| **§5.13 손패·더미** | IMPLEMENTED(JS) | [Battle][js]·[성장 회귀][review-tests] | 더미 보존·무료 토큰·메아리의 C# 동치 |
| **§5.14 종료** | IMPLEMENTED(JS) | [치명 다단 시험][lethal-tests]·[플레이어 반사 시험][player-reflect] | 종료 뒤 효과 중단·예약 회수 C# 동치 |
| **§5.15 명령·결정론·공개 정보** | PARTIAL | [command/publicView][js]·[엔진 시험][engine-tests] | JS 및 C# begin 원자성/멱등·공개 정보 검사 있음; [JS 상속 rid 수정·26회귀](../reports/receipt_keys_20261015/) 완료. 서버 비공개 상태·CSPRNG·동시성 미구현 |
| **§5.16 첫 턴 경계의 회귀 기준** | IMPLEMENTED(JS) | [첫 턴 회귀][review-tests]·[Battle][js] | [C# 첫 턴·시작 성장267사례](../reports/csharp_opening_20261015/) 확인; [반복턴·시작 방어해제](../reports/csharp_turns_20261015/) 확인; 실제 보루 카드 경계는 남음 |
| **§5.17 진화 카드의 성장 계열 판정** | IMPLEMENTED(JS) | [growthRoot/isGrowthInst][js]·[성장 회귀][review-tests] | 유효 진화 계보·토큰 복제 C# 동치 |
| **§5.18 성장 이벤트와 공생 이끼** | IMPLEMENTED(JS) | [applyGrowth][js]·[성장 회귀][review-tests] | 이끼 실제 성장 이벤트·효과별 상한 C# 동치 |

## 장 06 — 6. 카드·효과 DSL·개조

게임 내 역할: 모든 카드·효과 DSL·강화·부품·공용 카드·저주의 완전 데이터를 실행한다. 전체 원문: [06_chapter.md](source_v0.8.1/06_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§6.1 카드 데이터 형식** | IMPLEMENTED(JS/데이터) | [helpers][helpers]·[cardText][js]·[문서 생성][gen-docs] | [C# 전체173카드/효과 정의와 질의](../reports/player_queries_20261015/) 확인; 실제효과/텍스트 실행 이식 남음 |
| **§6.2 강화(+)** | IMPLEMENTED(JS) | [upgradeOps][js]·[엔진 시험][engine-tests] | [C# 유효정의 강화13/10·공명순서](../reports/player_queries_20261015/) 확인; 실제 실행·Run 강화 명령 남음 |
| **§6.3 효과 DSL 참조** | PARTIAL | [execOp/validateOps/opText][js]·[문서 생성][gen-docs] | JS DSL과 [C#39op/35필드·조건/scale 정의](../reports/player_queries_20261015/) 보존; 실제실행 A1 남음. base0는 원문§6.3/§9 충돌 미결 |
| **§6.4 카드 수치 예산(작성 가이드)** | PARTIAL(가이드/측정) | [밸런스 측정][balance-code]·[최신 확대 결과][balance-latest] | 기존 예산/목표를 유지하며 미달 원인 검토·사람 플레이 |
| **§6.5 카드 풀과 보상** | IMPLEMENTED(JS/데이터) | [캐릭터][characters]·[세계 데이터][world]·[엔진 시험][engine-tests] | 전체 카드의 C#·제품 보상 수용 |
| **§6.6 부품(카드 개조)** | IMPLEMENTED(JS) | [Run/Battle][js]·[부품 데이터][world]·[저장 v2 시험][memory-tests] | 두 영구 슬롯·임시 제외·교체 비용 C#·서버 동치 |
| **§6.7 공용 카드와 저주** | IMPLEMENTED(JS/데이터) | [공용/저주 데이터][world]·[Battle][js] | C# 공용/저주·종료 손실 동치 |
| **§6.8 카드 ID 완전 목록** | IMPLEMENTED(데이터/문서) | [173개 카드][data]·[문서 검사][doc-check] | 카드 ID/표 행/완전 목록 보존 계속 검사 |

## 장 07 — 7. 탐사(로그라이트)

게임 내 역할: 지도·노드·보상·사건·유물·귀환/패배를 거쳐 기억 저장으로 이어진다. 전체 원문: [07_chapter.md](source_v0.8.1/07_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§7.1 탐사 생성** | IMPLEMENTED(JS) | [Run.create][js]·[엔진 시험][engine-tests] | 서버가 보유권·장비·시드 결정; C# Run 이식 |
| **§7.2 지도 생성(구역마다)** | IMPLEMENTED(JS) | [Run][js]·[지도 300시드 시험][engine-tests] | C# 지도 생성·경로 동치 |
| **§7.3 노드** | IMPLEMENTED(JS/HTML) | [Run][js]·[UI][ui]·[스모크][smoke] | 각 노드의 C#·Unity 실제 입력 수용 |
| **§7.4 전투 보상** | IMPLEMENTED(JS) | [Run][js]·[엔진 시험][engine-tests] | 보상 제시 공정성·진화 선택 C# 동치 |
| **§7.5 구역 사이(기록소)** | IMPLEMENTED(JS/HTML) | [Run][js]·[UI][ui]·[스모크][smoke] | 귀환/계속·회복·패배 C#·Unity 동치 |
| **§7.6 종료와 결과** | IMPLEMENTED(JS) | [Run][js]·[저장 v2 시험][memory-tests] | 서버 포기·종료 표식·한번 저장 수용 |
| **§7.7 사건** | IMPLEMENTED(JS/데이터) | [8개 사건][world]·[Run][js]·[스모크][smoke] | 플레이어 카드 선택·비용·무기 제외 C#·Unity 수용 |
| **§7.8 유물** | IMPLEMENTED(JS/데이터) | [32개 유물][world]·[성장 회귀][review-tests] | 전체 훅·상한·소유 효과 C# 이식 |
| **§7.9 난이도와 배율** | PARTIAL | [Stats/Run][js]·[성장 데이터][progression]·[측정][balance-code] | 난도/레벨 배율은 참조 구현; 미완성 콘텐츠·사람 난도 수용 |
| **§7.10 패배와 이전 자산의 관계** | PARTIAL | [체크포인트 UI][ui]·[저장 브라우저 시험][memory-ui] | 브라우저 유지 경로 있음; 서버/Unity 앱 복귀·통신 실패 수용 |

## 장 08 — 8. 기억 저장: 현재 참조와 보존형 v2 목표

게임 내 역할: 영구 기억 덱의 개체·변화·최종 비용·보존·이력·이관을 보호한다. 전체 원문: [08_chapter.md](source_v0.8.1/08_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§8.1 개념** | IMPLEMENTED(JS/HTML) | [Run][js]·[저장 v2 시험][memory-tests] | 저장 덱을 주간/균열/성장 콘텐츠에 연결 |
| **§8.2 용량** | IMPLEMENTED(현행 수치) | [Stats][js]·[성장 데이터][progression] | 현행 공명 포함 용량 유지; 무료 경로는 §8.13 별도 |
| **§8.3 변경과 비용 (`data.rules.memoryCost`)** | IMPLEMENTED(JS) | [MemoryV2][memory-module]·[저장 v2 시험][memory-tests] | 기존 비용표 유지·C#/서버 최종 비용 재검증 |
| **§8.4 변경 목록 계산 (`Run.memoryChanges`)** | IMPLEMENTED(legacy 참조) | [Run.memoryChanges][js]·[legacy 회귀][review-tests] | legacy 인덱스 경로를 v2 stable ID와 혼동하지 않음 |
| **§8.5 저장 규칙 (`Run.commitMemory`)** | PARTIAL | [Run.commitMemory][js]·[저장 v2 시험][memory-tests] | legacy 회귀 보존; 서버 트랜잭션/이력 미구현 |
| **§8.6 저장 화면 UX** | PARTIAL(HTML) | [UI][ui]·[저장 브라우저 시험][memory-ui] | v2 화면 연결; Unity 저장 화면 수용 별도 |
| **§8.7 설계 의도와 경계** | PARTIAL | [MemoryV2][memory-module]·[Run][js] | 영구 덱 보존/신규 변화만 제한은 v2; 무료 용량은 미전환 |
| **§8.8 현재 구현과 목표 정책** | IMPLEMENTED(JS/HTML) | [P0 증거][p0]·[Run][js]·[UI][ui] | 원문 당시 미연결 기록 보존; 서버·C#까지 완료 의미 아님 |
| **§8.9 카드 개체와 네 가지 스냅샷** | IMPLEMENTED(JS) | [MemoryV2][memory-module]·[개체/이관 시험][memory-edges] | 서버 출발 스냅샷·소유권·C# 동치 |
| **§8.10 변경 목록과 비용** | IMPLEMENTED(JS) | [MemoryV2][memory-module]·[저장 v2 시험][memory-tests] | 완성 result 전체 비용·슬롯 교체·중복 개체 서버 재검증 |
| **§8.11 명령과 원자성** | PARTIAL(로컬 어댑터) | [commitMemoryV2][js]·[저장 v2 시험][memory-tests] | 계정/탐사/덱 잠금·DB 원자성·다기기 CAS·서버 영수증 |
| **§8.12 저장 화면** | IMPLEMENTED(HTML) | [UI][ui]·[저장 브라우저 시험][memory-ui] | Unity 전후 미리보기·통신 재시도·앱 복귀 수용 |
| **§8.13 용량 성장의 통합 목표** | NOT_IMPLEMENTED | [현행 Stats][js]·[목표 정책][policy-data] | 무료6+1+2 전환·공명 전투 효과 보존·이관·벡터 동시 변경 |
| **§8.14 이관과 인수 조건** | PARTIAL | [P0 증거][p0]·[개체/이관 시험][memory-edges] | M1~M5 JS/HTML 근거 있음; M6 서버·M7 C# 미완료 |
| **§8.15 외부 저장 패치를 채택하지 않은 이유** | IMPLEMENTED(거절 기록) | [외부 패치 재현][reproduce-memory]·[원문 8장](source_v0.8.1/08_chapter.md) | 거절 패치 재도입 금지; 신규 v2와 혼동하지 않음 |
| **§8.16 마이그레이션 인수 시험과 남은 경계** | PARTIAL | [개체/이관 시험][memory-edges]·[P0 증거][p0] | 로컬 결정적 이관/백업·격리 있음; 운영 dry-run·DB·C# 미완료 |

## 장 09 — 9. 캐릭터

게임 내 역할: 10명 각자의 전투 규칙·시트·성격·외형·대사·인연·아트 제작 기준을 보존한다. 전체 원문: [09_chapter.md](source_v0.8.1/09_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§9.1 통합 캐릭터 제작 기준** | PARTIAL(브리프) | [캐릭터 브리프][briefs]·[원문 9장](source_v0.8.1/09_chapter.md) | 현재 인물 기준으로 실제 시트/키아트/연출 검수 |
| **§9.2 출시 로스터** | IMPLEMENTED(데이터) | [10명 캐릭터][characters]·[데이터][data]·[문서 검사][doc-check] | 출시 자산/대본/밸런스 수용 별도 |
| **§9.3 캐릭터 시트** | PARTIAL(데이터/시트) | [캐릭터][characters]·[인물 원본][notes]·[브리프][briefs] | 10명별 원본 시트 전부 유지; 완성 리그·음성·에피소드 미제작 |
| **§9.4 신규 캐릭터 제작 템플릿 (시즌 2 이후)** | PARTIAL(템플릿) | [원문 9장](source_v0.8.1/09_chapter.md) | 시즌2 캐릭터 제작 묶음·조합 QA는 미착수 |
| **§9.5 연령 구성 대안과 변경 승인 범위** | DECISION_PENDING | [비교안][age-option]·[원문 9장](source_v0.8.1/09_chapter.md) | 승인 전 현재 나이/연혁/표현 기준 유지 |

## 장 10 — 10. 무기·적·구역 데이터

게임 내 역할: 무기·적·조우·후반 여섯 행동·2페이즈를 예산과 콘텐츠 준비 상태에 맞춰 연결한다. 전체 원문: [10_chapter.md](source_v0.8.1/10_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§10.1 무기 체계** | IMPLEMENTED(JS/데이터) | [무기 데이터][world]·[Gacha/Stats/Battle][js] | 22개 무기·패시브 C# Battle 이식·제품 장착 서버 |
| **§10.2 적 목록과 조우표 (구역 1·2: 데이터 완성)** | IMPLEMENTED(JS/데이터) | [라이브20종·조우][world]·[엔진 시험][engine-tests] | 구역1/2 참조 데이터; 사람 난도·Unity 연출 별도 |
| **§10.3 적 설계 예산 (기준 전투 기준)** | PARTIAL(예산/측정) | [밸런스 측정][balance-code]·[최신 확대 결과][balance-latest] | 미달 지표 해소·독립 검토·사람 플레이 |
| **§10.4 구역 3~5 명세 (P2 콘텐츠 작업)** | PARTIAL(후보) | [23종 후보][late-data]·[후반 시험][late-tests] | C#·목표 밸런스·서사/연출 준비 전 라이브 합류/활성화 안 함 |
| **§10.5 후반 데이터와 실제 구현 상태** | PARTIAL | [후보 overlay][late-data]·[성장 데이터][progression] | 4~6장 authored 미활성; 전투/서사/연출 준비 각각 증거 필요 |
| **§10.6 침묵·지연·2페이즈의 상세 구현 계약** | IMPLEMENTED(JS/HTML) | [후반 시험][late-tests]·[후반 UI 시험][late-ui]·[후반 근거][late] | 침묵 무료 우선·지연 due만·2페이즈 상세 계약 C# 동치 |

## 장 11 — 11. 계정 성장

게임 내 역할: 전투 밖 성장·집중·고난도·스토리·인연·임무·용량·업적을 계정 단위로 관리한다. 전체 원문: [11_chapter.md](source_v0.8.1/11_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§11.1 전투 스탯 모델** | PARTIAL | [Stats][js]·[Stats.cs][stats]·[벡터][vectors] | 전투 수식/데이터 있음; 계정 성장 명령·서버 원장 미구현 |
| **§11.2 레벨·경험치** | PARTIAL | [Stats][js]·[성장 데이터][progression]·[경제 시험][economy-tests] | 실제 아이템 최소조합·상한 미소모·승급 명령 서버 |
| **§11.3 1명 완성 비용과 수급 속도** | IMPLEMENTED(분석) | [경제 검산][economy-tool]·[경제 시험][economy-tests] | 시뮬레이션 비용과 실제 계정 수급을 구분 |
| **§11.4 집중(스태미나)** | PARTIAL(데이터) | [stamina][progression]·[경제 시험][economy-tests] | 회복 시각·상한·충전 한도·차감 서버 |
| **§11.5 성장 던전** | NOT_IMPLEMENTED(진행기) | [dungeons 데이터][progression] | 1~3웨이브·별 판정·소탕/집중 원장 실제 구현 |
| **§11.6 주간 보스** | NOT_IMPLEMENTED(게임/API) | [weeklyBoss 데이터][progression]·[DB 제약][schema] | 주간 공유3회·실패 미차감·연습·승리 검증 트랜잭션 |
| **§11.7 기록 균열 (2주 순환 고난도)** | NOT_IMPLEMENTED(진행기) | [recordRift 데이터][progression] | 6개 수정자·3연전·팀별 HP 이월·덱 초기화·별 지급 |
| **§11.8 메인 스토리 탐사** | PARTIAL | [storyChapters][progression]·[Run][js] | 4~6장 후보·대본·보상·잠금 수용, 첫 클리어 서버 |
| **§11.9 계정 레벨** | PARTIAL(데이터/분석) | [accountLevel][progression]·[경제 시험][economy-tests] | 계정 XP·해금·승급 서버 명령 |
| **§11.10 인연(호감도)** | PARTIAL(데이터) | [affinity][progression]·[인물 원본][notes] | 선물/일일 상한·4편씩40편 에피소드·보상/로비 연결 |
| **§11.11 일일·주간 임무** | PARTIAL(데이터) | [missions][progression] | 실제 진척·일일/주간 지급·리셋·멱등 원장 |
| **§11.12 계정 공통 재료와 다인 육성** | IMPLEMENTED(분석) | [독립 경제 시험][economy-tests]·[검산 도구][economy-tool] | 다인 육성의 실제 해금/집중 배분 계정 모델 수용 |
| **§11.13 주간 보스·기록 균열·일일 활동** | NOT_IMPLEMENTED(목표) | [현행/목표 정책][policy-data]·[성장 데이터][progression] | 초기1팀·후반2팀·선택형100점·주간 공유 보상 구현 |
| **§11.14 최대 기억 용량의 무료 해금 — 목표** | NOT_IMPLEMENTED | [현행 Stats][js]·[목표 정책][policy-data] | 무료 승급 최대9·저장 이관·UI·C# 벡터 |
| **§11.15 업적 — 구판 요구 보존, 최신 데이터 이식 전** | DECISION_PENDING | [구판 보존](archive/v0.6_materials/)·[원문11장](source_v0.8.1/11_chapter.md) | 120개 업적 ID/조건/소급/보상원장·총합 확정 전 지급 없음 |

## 장 12 — 12. 모집(가챠)·재화·상품

게임 내 역할: 모집 확률/천장·중복·재화·상점·상품·지급 예외를 투명하고 일관되게 처리한다. 전체 원문: [12_chapter.md](source_v0.8.1/12_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§12.1 배너와 규칙** | PARTIAL | [Gacha JS][js]·[Gacha.cs][gacha-cs]·[모집 데이터][gacha-data] | 상태기계/시뮬레이터 있음; 서버 CSPRNG·무료/유료 원장·동시성 필요 |
| **§12.2 확률 곡선과 정확 기대값** | IMPLEMENTED(참조/분석) | [모집 데이터][gacha-data]·[경제 검산][economy-tool]·[벡터][vectors] | 운영 공개 확률표와 서버 설정 해시 일치·분포 검증 별도 |
| **§12.3 중복: 공명과 정련** | IMPLEMENTED(참조) | [Gacha][js]·[Gacha.cs][gacha-cs]·[엔진 시험][engine-tests] | 공명/정련/중복 보상 서버 원장 연결 |
| **§12.4 재화** | PARTIAL(데이터/시뮬레이터) | [성장 데이터][progression]·[Gacha][js]·[DB 스키마][schema] | 유료/무료 재화·이벤트 만료 환전 서버 원장 |
| **§12.5 교환소** | PARTIAL(데이터) | [exchangeShops][progression]·[UI][ui] | 한도/리셋/보유권·실제 교환 서버; UI 시뮬레이터는 운영 상점 아님 |
| **§12.6 무과금 수급(42일)과 v0.6 대비 영향** | IMPLEMENTED(분석) | [경제 검산][economy-tool]·[경제 시험][economy-tests] | 현행 수급 유지; 실제 배너별 지급 시각·계정 접근성 검증 |
| **§12.7 상품과 결제** | PARTIAL(상품 데이터) | [products][progression]·[API 계약][api] | Play 검증·환불 부채·토큰 중복/ack·상품 UI 미구현 |
| **§12.8 수급 확률과 배너 기간의 해석** | IMPLEMENTED(분석) | [독립 경제 분석][review-economy]·[경제 시험][economy-tests] | 한정권 시점/중단 전략 가정을 실제 라이브 일정과 구분 |
| **§12.9 단가 환산과 실제 결제액** | IMPLEMENTED(분석) | [독립 경제 분석][review-economy]·[경제 시험][economy-tests] | 단가 환산/장바구니 구분; 실제 상품 판매 검증 아님 |
| **§12.10 사업성 검증과 변경 승인** | DECISION_PENDING | [원문12장](source_v0.8.1/12_chapter.md)·[경제 검산][economy-tool] | 수급/확률/상품 변경은 독립 계산·명시 승인; 현행 유지 |
| **§12.11 월정액·패스의 미확정 세부 계약** | DECISION_PENDING | [products][progression]·[원문12장](source_v0.8.1/12_chapter.md) | 미접속·시즌종료 미수령·플러스 차액·재료 구성 결정 |
| **§12.12 구판 패스 1~50레벨 보상표 — 보존·대조용** | DECISION_PENDING | [구판50레벨 자료](archive/v0.6_materials/)·[원문12장](source_v0.8.1/12_chapter.md) | 현행 총량과 일치하는 새 표 승인 전 구판 보상 활성화 금지 |

## 장 13 — 13. 라이브 운영

게임 내 역할: 시즌/이벤트 일정·캐릭터 제작 묶음·운영 지표·제작 부하를 실제 운영에 연결한다. 전체 원문: [13_chapter.md](source_v0.8.1/13_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§13.1 시즌 구조 (42일)** | PARTIAL(배너/일정 데이터) | [모집][gacha-data]·[성장][progression] | 서버 시각·배너 교체/이월·점검/복각 운영 구현 |
| **§13.2 이벤트 구성 (이벤트 1개, 21일)** | NOT_IMPLEMENTED(진행기) | [원문13장](source_v0.8.1/13_chapter.md) | 이벤트2개·이벤트 유물/사건·체험·상점·고난도 구현 |
| **§13.3 주간·일간 일정** | NOT_IMPLEMENTED(서버) | [성장 데이터][progression]·[DB 스키마][schema] | KST 일/주/월/균열 초기화 실제 실행·경계 시험 |
| **§13.4 로드맵 (출시 후 4시즌)** | PARTIAL(로드맵) | [원문13장](source_v0.8.1/13_chapter.md) | 후속 시즌 캐릭터/구역/프리셋·균열 확장은 별도 제작 |
| **§13.5 캐릭터 1명 제작 묶음 (출시 기준)** | PARTIAL(데이터/브리프) | [캐릭터][characters]·[브리프][briefs]·[provenance][provenance] | 캐릭터1명 전체 그림·리그·음성·대본·QA 묶음 완성 |
| **§13.6 운영 지표** | NOT_IMPLEMENTED(운영 측정) | [원문13장](source_v0.8.1/13_chapter.md) | 실제 사용자 지표/대시보드·분포/이탈 측정; 가설은 실적 아님 |
| **§13.7 상품·캐릭터 제작 부하의 검토 근거** | PARTIAL(분석) | [독립 경제 분석][review-economy]·[원문13장](source_v0.8.1/13_chapter.md) | 실제 제작 시간/견적·완료율·구매/잔존 실험 |

## 장 14 — 14. UI·UX

게임 내 역할: 플레이어가 상태를 읽고 안전하게 선택·미리보기·확정·재시도하도록 한다. 전체 원문: [14_chapter.md](source_v0.8.1/14_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§14.1 정보 구조** | PARTIAL(HTML) | [UI][ui]·[스모크][smoke] | 참조 탐사/모집/도감/기억/설정 외 전체 메뉴·Unity 연결 |
| **§14.2 공통 규칙** | PARTIAL(HTML) | [UI][ui]·[P0 브라우저][p0-browser] | 실기기48dp·안전영역·라이선스·Unity 접근성 검증 |
| **§14.3 전투 화면 (가로 16:9)** | PARTIAL(HTML) | [UI][ui]·[후반 UI 시험][late-ui] | Unity 전투 화면·이벤트 연출·예약 해소·입력 잠금 |
| **§14.4 화면별 명세** | PARTIAL(HTML) | [UI][ui]·[스모크][smoke] | 성장/인연/도전/우편/상점/계정삭제 등 미구현 화면과 서버 |
| **§14.5 온보딩 순서** | NOT_IMPLEMENTED | [원문14장](source_v0.8.1/14_chapter.md) | 단계별 실제 튜토리얼·강제 첫 저장 안내·지급/해금 |
| **§14.6 접근성·현지화** | PARTIAL | [UI][ui]·[브라우저 시험][memory-ui] | 전체 키 기반 번역·영/일 길이·Unity 색약/아이콘 수용 |
| **§14.7 정보 밀도와 안전한 입력** | PARTIAL(HTML) | [후반 UI 시험][late-ui]·[P0 브라우저][p0-browser] | 최악 정보량·실기기 입력/가림·실서버 재시도·순수 미리보기 |
| **§14.8 작은 화면의 기존 관찰과 추가 인수 시험** | PARTIAL(브라우저) | [화면 증거][late-screens]·[브라우저 시험][memory-ui] | 640×360·노치·글자130%·터치·헤더 겹침 실기기 별도 |

## 장 15 — 15. 아트·연출·사운드

게임 내 역할: 전체 아트/연출/음성의 방향·규격·생성 소유권·검수·출처를 관리한다. 전체 원문: [15_chapter.md](source_v0.8.1/15_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§15.1 아트 디렉션** | PARTIAL(아트 방향) | [브리프][briefs]·[프롬프트][prompts] | 실제 캐릭터/적/배경 자산의 화풍·실루엣 검수 |
| **§15.2 통합 아트 기준** | PARTIAL(제작 기준) | [원문15장](source_v0.8.1/15_chapter.md)·[브리프][briefs] | 실제 시트·동적 키아트·SD 목적별 검수 |
| **§15.3 자산 목록과 규격** | NOT_IMPLEMENTED(최종 자산) | [규격 원문](source_v0.8.1/15_chapter.md)·[provenance][provenance] | 전10명·적·배경·UI·PSD·리그·컷신 전체 자산 제작 |
| **§15.4 이미지 생성 운영 (기본: Codex의 이미지 생성, 선택: 로컬 모델)** | PARTIAL(운영 계약) | [최신 이미지 소유권][workflow]·[provenance][provenance] | Codex 구독 이미지 도구 확인 후 시트부터 제작; 구판 로컬모델 대안 자동 전환 금지 |
| **§15.5 아트 검수** | NOT_IMPLEMENTED(자산 수용) | [검수 원문](source_v0.8.1/15_chapter.md)·[provenance][provenance] | 최종 자산별 품질/일관성/기록 검수 |
| **§15.6 캐릭터별 제작 프롬프트 50개** | IMPLEMENTED(프롬프트) | [50개 프롬프트][prompts]·[문서 검사][doc-check] | 프롬프트는 완성 그림/PSD/리그 증거 아님 |
| **§15.7 사운드·음성** | NOT_IMPLEMENTED | [사운드 원문](source_v0.8.1/15_chapter.md) | BGM·효과음·한국어 음성·믹싱/동시재생 수용 |

## 장 16 — 16. 기술 설계

게임 내 역할: JS 참조→공유 C#→서버/Unity의 동치·공개 정보·성능·버전 계약을 연결한다. 전체 원문: [16_chapter.md](source_v0.8.1/16_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§16.1 구성** | PARTIAL | [콘텐츠 빌드][content-build]·[JS][js]·[기존 C#][core] | 공유 전체 C# DLL·권위 서버·콘텐츠 해시 배포 연결 |
| **§16.2 규칙 코어 이식 (JS → C#)** | IN_PROGRESS | [트레이스 생성기][traces]·[트레이스 시험][trace-tests]·[기존 C#][core] | JS oracle1000개 재생·C#2000스냅샷/정렬JSON 검사와 [실제begin267사례](../reports/csharp_opening_20261015/) 완료; [실제end/적행동1740요청](../reports/csharp_turns_20261015/) 확인; 전체 Battle/Run/저장 명령 재생 동치는 미구현 |
| **§16.3 결정론과 숨겨진 정보** | PARTIAL | [publicView][js]·[API 계약][api] | 서버 비공개 RNG/뽑을더미·모집 CSPRNG·문의 재생 구현 |
| **§16.4 Unity 클라이언트** | PARTIAL(연결 검증) | [Unity 프로젝트][unity]·[실행 증거][env] | 게임 전체 가로 UI·서버·Addressables/리그·Android IL2CPP·성능 수용 |
| **§16.5 서버** | NOT_IMPLEMENTED(서버 앱) | [스키마][schema]·[API 계약][api] | 인증/세션·무결성·명령·원장·속도제한 실제 ASP.NET 앱 |
| **§16.6 텔레메트리(이벤트 사전)** | NOT_IMPLEMENTED(전송/운영) | [이벤트 사전](source_v0.8.1/16_chapter.md)·[JS 이벤트][js] | 서버/클라이언트 계측·보관·개인정보 계약; MCP telemetry OFF와 별개 |
| **§16.7 버전 세 종류를 구분한다** | IMPLEMENTED(버전 구분) | [현재 상태][current]·[목표 정책][policy-data] | 문서0.8.1/데이터0.7.0/JS 수정·목표 활성 상태 계속 구분 |

## 장 17 — 17. 서버: 데이터베이스·트랜잭션·API

게임 내 역할: 계정·재화·상태·영수증을 권위 서버와 DB 트랜잭션으로 보호한다. 전체 원문: [17_chapter.md](source_v0.8.1/17_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§17.1 테이블** | PARTIAL(스키마) | [schema.sql][schema]·[SQL 제약 시험][schema-tests] | 현재 코드 존재/과거 적용 증거; 서버 앱 연결·v2 스키마 이관 필요 |
| **§17.2 핵심 트랜잭션** | NOT_IMPLEMENTED(앱 트랜잭션) | [트랜잭션 명세](source_v0.8.1/17_chapter.md)·[SQL][schema] | 고정 잠금·상태/원장/영수증 커밋·동시성/rollback 실행 시험 |
| **§17.3 API 요약** | PARTIAL(계약) | [OpenAPI][api] | 27경로 실제 핸들러·요청/응답 계약 시험 |
| **§17.4 공개 상태 DTO (전투)** | PARTIAL(JS 공개 뷰) | [publicView][js]·[DTO 원문](source_v0.8.1/17_chapter.md) | 서버 어댑터·공개 비용/방식 DTO·숨김 정보 시험 |
| **§17.5 저장 v2와 기존 API·DB의 접합 작업** | NOT_IMPLEMENTED(서버 v2) | [legacy 스키마][schema]·[API][api]·[v2 JS][js] | selectedChangeIds/version/stable key·이력/소유권/잠금 접합 |

## 장 18 — 18. 운영·배포·정책 준수

게임 내 역할: 환경 분리·배포·복구·출시 요건을 실제 운영의 증거로 확인한다. 전체 원문: [18_chapter.md](source_v0.8.1/18_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§18.1 환경** | PARTIAL(로컬) | [Unity 로컬][env]·[CI][ci] | dev/staging/production 계정·DB·키·발급자 분리 미구현 |
| **§18.2 CI/CD** | PARTIAL(CI) | [GitHub workflow][ci]·[로컬 검사][checks] | 현재 head CI와 배포를 별도 확인; staging/production 배포 미수행 |
| **§18.3 백업·장애** | NOT_IMPLEMENTED(운영) | [백업/장애 원문](source_v0.8.1/18_chapter.md) | PITR·복구 리허설·차단 스위치·계정별 원장 보정 |
| **§18.4 출시 요건 확인 — 미검증 체크리스트** | NOT_IMPLEMENTED(출시 수용) | [출시 체크리스트](source_v0.8.1/18_chapter.md) | 출시일 공식 조건·AAB/서명/16KB·결제·등급·개인정보·권리 검증 |

## 장 19 — 19. 시험·검증·밸런스 목표

게임 내 역할: 참조 시험·제품 수용·밸런스·문서 보존의 각 통과 조건을 구분한다. 전체 원문: [19_chapter.md](source_v0.8.1/19_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§19.1 이번 재검증 상태** | PARTIAL | [P0][p0]·[후반][late]·[현재 환경][env] | 과거 PASS를 새 실행에 합산하지 않음; 제품 단계 수용 별도 |
| **§19.2 엔진 단위 시험이 보장하는 것** | IMPLEMENTED(JS 시험) | [엔진 시험][engine-tests]·[회귀][review-tests]·[204개 최신 로그][receipt-engine] | 테스트 범위 밖 C#/서버/사람 플레이는 별도 |
| **§19.3 제품 단계 인수 시험 (P1 이후 추가)** | NOT_IMPLEMENTED(제품 수용) | [제품 수용 원문](source_v0.8.1/19_chapter.md) | C#1000·서버100병렬/구매·분포·실기기/성능 인수 |
| **§19.4 밸런스 목표와 현재값 (기준 전투, 시작 덱)** | PARTIAL(목표 FAIL) | [최신 확대 결과][balance-latest] | 5280회 중 미달21개; 사람10판·공명/장비 전구간 수용 필요 |
| **§19.5 문서 보존·릴리스 검사** | IMPLEMENTED(문서 검사) | [보존 검사][doc-check]·[인계/PDF 검사][handoff] | 원문·카드·시트·프롬프트·표 행·음성 시험을 재배포마다 유지 |
| **§19.6 같은 조건으로 비교하는 밸런스 측정** | IMPLEMENTED(측정 도구) | [짝지은 프로브][balance-code]·[최신 확대 결과][balance-latest] | 측정 성공과 목표 FAIL 구분; 사람 난도 인증 별도 |

## 장 20 — 20. Claude·Codex·Unity MCP 제작 실행 계획

게임 내 역할: 원본 P0~P6 작업별 산출물·검토·인수 조건을 지속 개발로 실행한다. 전체 원문: [20_chapter.md](source_v0.8.1/20_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§20.1 읽기 순서와 실제 기준** | IMPLEMENTED(문서) | [현재 실행 지시][workflow]·[현재 상태][current] | 최신 사용자 지시 우선·수치 원본/제안 구분 유지 |
| **§20.2 역할과 편집권** | PARTIAL(실행 체계) | [환경 안내][env]·[작업 결과][tasks] | 별도 worktree·한 씬 writer·실제 반대 AI 검토 증거 유지 |
| **§20.3 P0: 저장 정책을 실제 흐름으로 연결** | IMPLEMENTED(JS/HTML) | [P0][p0]·[저장 브라우저 시험][memory-ui] | 서버/C#·실기기 완료와 구분 |
| **§20.4 P1: C#·서버** | IN_PROGRESS | [트레이스][traces]·[기존 C#][core]·[서버 계약][api] | 전체 Battle/Run/저장·서버 구현/통합은 미완료 |
| **§20.5 P2: 콘텐츠와 화면** | PARTIAL | [후반 JS][late-tests]·[후보 데이터][late-data]·[Unity][unity] | 무료 용량·본편 인장·고난도/일일·Unity 게임 수직슬라이스 |
| **§20.6 P3: 상용 운영 전환** | NOT_IMPLEMENTED | [상용 전환 명세](source_v0.8.1/20_chapter.md) | 운영 기능·실결제·환경 분리·승인된 운영 배포 |
| **§20.7 완료 보고 양식** | PARTIAL(기록 체계) | [작업 결과][tasks]·[환경 증거][env] | 각 작업의 실행/화면/미완료·검토/통합 상태를 계속 기록 |
| **§20.8 원본 단계·인수 조건의 보존 및 순서 대응** | PARTIAL(로드맵) | [본 장부](IMPLEMENTATION_STATUS.md)·[원문20장](source_v0.8.1/20_chapter.md) | 원본 P0~P6 산출물/조건 그대로 추적; 번호 완료만으로 종료 안 함 |
| **§20.9 원본 작업별 상세 지시 — 현재 우선순위로 실행** | IN_PROGRESS | [트레이스][traces]·[후보][late-data]·[Unity][unity] | 원본 작업별 전체 수용 조건은 아래 다음 작업 표와 원문 유지 |
| **§20.10 저장소 규칙** | PARTIAL(저장소) | [CI][ci]·[작업 결과][tasks] | 현재 branch/head·PR·생성물 일치·CI 결과를 통합 전에 확인 |

## 장 21 — 21. v0.6 평가 반영 목록

게임 내 역할: v0.6 평가17개 항목과 v0.8 편집 결함의 수정·보존 근거를 추적한다. 전체 원문: [21_chapter.md](source_v0.8.1/21_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§21.1 v0.8 편집 결함과 이번 복구** | IMPLEMENTED(문서 보존) | [복구 검사][doc-check]·[기존 감사](../reports/audit_v081/) | 손상 입력 음성 시험·이력 보존; 장 전체17개 개선 항목도 아래 별도 추적 |

## 장 22 — 22. 남은 결정과 위험

게임 내 역할: 미확정 정책과 구현/사업/표현/동시성 위험을 숨기지 않고 관리한다. 전체 원문: [22_chapter.md](source_v0.8.1/22_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§22.1 미확정 결정** | DECISION_PENDING | [결정 장부][decisions]·[목표 정책][policy-data] | 아래 결정 대기 목록 및 구현 목표를 분리; 수치 임의 활성화 없음 |
| **§22.2 위험** | PARTIAL(위험 추적) | [검증/밸런스][balance-latest]·[환경][env] | 온보딩·경제 체감·아트 일정·C# 동치·결제 동시성·권리 실제 완화 |

## 장 23 — 23. 부록

게임 내 역할: 용어·오류·레벨·경로·참고자료·보존 위치·과거 요구의 추적성을 유지한다. 전체 원문: [23_chapter.md](source_v0.8.1/23_chapter.md).

| 원문 요구사항 | 현재 상태 | 코드·시험·기록 근거 | 남은 인수 조건/한계 |
|---|---|---|---|
| **§23.1 용어** | IMPLEMENTED(문서) | [용어 원문](source_v0.8.1/23_chapter.md) | 구현/번역에서 용어·cardId/key 구분 유지 |
| **§23.2 오류 코드와 사용자 문구** | PARTIAL | [문서 생성][gen-docs]·[UI 오류][ui] | 현재 JS 오류 커버리지; 서버/Unity 코드·문구 동기화 필요 |
| **§23.3 레벨 표 (1~60)** | IMPLEMENTED(데이터/분석) | [Stats][js]·[Stats.cs][stats]·[벡터][vectors] | 1~60 원문 표/반올림 유지; 계정 성장 실행은 별도 |
| **§23.4 저장소 지도** | PARTIAL(경로 지도) | [현재 파일 지도](../README.md)·[본 장부](IMPLEMENTATION_STATUS.md) | 원문 당시 경로 설명 보존; 새 Unity/실행기/트레이스는 현재 근거 참조 |
| **§23.5 참고 자료** | PARTIAL(참고문헌) | [원문 참고자료](source_v0.8.1/23_chapter.md) | 출시일 공식 규정/정책 재검증 전 현재 요건이라고 확정 안 함 |
| **§23.6 이번 입력·원본 보존 위치** | IMPLEMENTED(보존) | [원문 전체][source]·[archive](archive/)·[문서 검사][doc-check] | 과거 파일 보존과 현재 본문 완전성 각각 유지 |
| **§23.7 오류의 의미와 검증 근거** | IMPLEMENTED(감사 기록) | [기존 감사](../reports/audit_v081/)·[원문23장](source_v0.8.1/23_chapter.md) | 오류 로그만으로 최종 성공/실패 추정 금지 |
| **§23.8 이전 요구와 최신 상태의 추적** | PARTIAL/DECISION_PENDING | [구판 자료](archive/v0.6_materials/)·[결정 장부][decisions] | 업적·패스·전승 정책/원장 확정; 구판 수치 임의 재적용 금지 |

## 9장 개별 시트·21장 원본 개선 항목의 추가 추적

§9.3의 각 전체 시트는 아래 범위로 따로 유지한다. 12개 고유 카드·진화/토큰 해당분·패시브·필살기·공명6단계·전용무기·외형·기존 대사·제작용 보완 브리프를 하나도 삭제하지 않는다. 공통 상태는 데이터/문서 구현, 최종 일러스트·리그·음성·인연 에피소드·Unity 적용 미완료다.

| 원문 시트 | 실행 데이터 ID | 코드/문서 근거 |
|---|---|---|
| §9.3.1 세라 | sera | [characters][characters]·[전체 시트](source_v0.8.1/09_chapter.md) |
| §9.3.2 니아 | nia | [characters][characters]·[전체 시트](source_v0.8.1/09_chapter.md) |
| §9.3.3 유리 | yuri | [characters][characters]·[성장/계보 회귀][review-tests]·[전체 시트](source_v0.8.1/09_chapter.md) |
| §9.3.4 라비엔 | ravien | [characters][characters]·[전체 시트](source_v0.8.1/09_chapter.md) |
| §9.3.5 하린 | harin | [characters][characters]·[반격 치명 경계][lethal-tests]·[전체 시트](source_v0.8.1/09_chapter.md) |
| §9.3.6 리엔 | rien | [characters][characters]·[전체 시트](source_v0.8.1/09_chapter.md) |
| §9.3.7 에일라 | aila | [characters][characters]·[전체 시트](source_v0.8.1/09_chapter.md) |
| §9.3.8 시온 | sion | [characters][characters]·[전체 시트](source_v0.8.1/09_chapter.md) |
| §9.3.9 카야 | kaya | [characters][characters]·[전체 시트](source_v0.8.1/09_chapter.md) |
| §9.3.10 루미 | lumi | [characters][characters]·[전체 시트](source_v0.8.1/09_chapter.md) |

§21 본문의 번호1~17은 원문 표의 각 문제·조치·확인 방법 전체를 유지한다. 1·14 문서 생성/오류 커버리지는 [문서 생성][gen-docs]/[보존 검사][doc-check], 2 레벨 상한은 [Stats][stats]/[progression][progression], 3·4 독립 경제/쓰기 경계는 [경제 시험][economy-tests], 5~7 C# 유틸리티/벡터는 [core][core]/[CoreChecks][corechecks], 8·16·17 모집 상태 기계는 [Gacha][gacha-cs]/[엔진 시험][engine-tests], 9 DB 제약은 [SQL 시험][schema-tests], 10 캐릭터 데이터는 [characters][characters], 11·12 텍스트/미리보기/의도는 [JS][js]/[UI][ui], 13 실제 브라우저 완주는 [스모크][smoke], 15 지도 보장은 [엔진 시험][engine-tests]에 대응한다. 과거 C#202/202·DB 적용 기록을 이번 장부에서 새 실행 PASS로 재분류하지 않는다.

## 결정 대기와 실행 목표의 구분

보존형 저장의 JS/HTML 연결은 이미 수행됐지만 무료 최대 용량 전환·본편6인장/인연20조각 분리·초기 균열 한 팀·선택형 일일은 별도 미구현 목표다. 기존 승인 범위 안의 구현과 새 정책 승인을 섞지 않는다.

업적 개별 목록/원장, 현행 패스50레벨 배정·미접속/미수령/업그레이드 예외, 무기 전승·해체·중복 정련/환급, 연령 구성 대안·세로 게임·추가 음성·프리셋 확장은 해당 원문과 [결정 장부][decisions]에서 미결정을 유지한다. 구판 수치/보상·대안을 현재 경제에 임의로 더하지 않는다. 사람 플레이10판·이해도/재플레이 의향·실기기 조작/성능·실결제/운영 배포는 자동 봇이나 연결 검증 앱으로 대체하지 않는다.

## 증거 읽는 법과 갱신

[밸런스 단위 엔진 로그][engine-final]는168개 PASS이며, 다음 [C# opening 단위](../reports/csharp_opening_20261015/node_tests.txt)는178개 PASS다. 각 로그의 소스와 실행 범위를 구별한다. [최신 확대 결과][balance-latest]의 목표 미달21개는 이전 [후반 결과][late]의1320회/32개와 표본 수가 다르므로 동일 조건 개선율로 섞지 않는다. 라이브 적20종·후보23종·4~6장 잠금을 유지한다.

이 장부 작성 시 문서/인계 검사 결과는 주 담당자에게 회수한다. 새로운 증거가 생기면 해당 행의 구현/시험/검토/통합 상태와 경로를 갱신하고, 기존 실행 기록은 삭제하지 않는다. 근거가 없는 칸은 NOT_RUN/미구현/대기로 남긴다.

[env]: AI_ENVIRONMENT.md
[workflow]: AI_WORKFLOW.md
[current]: CURRENT.md
[decisions]: decisions_v0.8.1.md
[start]: START_CODEX.md
[source]: source_v0.8.1/
[content]: ../content/
[content-build]: ../content/build.mjs
[data]: ../data/game_data.json
[policy-data]: ../data/design_policy_v0.8.json
[characters]: ../content/characters.mjs
[notes]: ../content/character_notes.mjs
[world]: ../content/world.mjs
[progression]: ../content/progression.mjs
[helpers]: ../content/helpers.mjs
[gacha-data]: ../content/gacha.mjs
[js]: ../engine/rules-core.js
[ui]: ../prototype/src/ui.js
[engine-tests]: ../engine/tests/engine.test.mjs
[review-tests]: ../engine/tests/review_v071.test.mjs
[memory-tests]: ../engine/tests/memory_v2_integration.test.mjs
[memory-edges]: ../engine/tests/memory_v2_edges.test.mjs
[memory-ui]: ../prototype/tests/memory_v2.mjs
[memory-module]: ../proposals/memory_v2.cjs
[late-data]: ../proposals/late_enemies.cjs
[late-tests]: ../engine/tests/late_enemy_actions.test.mjs
[late-ui]: ../prototype/tests/late_enemy_actions.mjs
[lethal-tests]: ../engine/tests/enemy_lethal_multihit.test.mjs
[ult-tests]: ../engine/tests/reflect_ultimate.test.mjs
[player-reflect]: ../engine/tests/player_lethal_reflect.test.mjs
[smoke]: ../prototype/tests/smoke.mjs
[economy-tests]: ../tests/test_economy.py
[economy-tool]: ../tools/verify_economy.py
[review-economy]: ../tools/review/economy_audit.py
[reproduce-memory]: ../tools/review/reproduce_rejected_memory.py
[vectors]: ../engine/tests/vectors/rng_gacha_vectors.json
[core]: ../core/MemoryForge.Core/
[stats]: ../core/MemoryForge.Core/Stats.cs
[gacha-cs]: ../core/MemoryForge.Core/Gacha.cs
[reservation]: ../core/MemoryForge.Core/ReservationBook.cs
[corechecks]: ../core/MemoryForge.CoreChecks/Program.cs
[traces]: ../tools/gen_traces.mjs
[trace-tests]: ../engine/tests/trace_export.test.mjs
[schema]: ../server/schema.sql
[schema-tests]: ../server/tests/schema_checks.sql
[api]: ../server/openapi.yaml
[unity]: ../client/MemoryForgeUnity/
[ci]: ../.github/workflows/ci.yml
[checks]: ../tools/run_all_checks.sh
[doc-check]: ../tools/check_integrated_release.py
[gen-docs]: ../tools/gen_docs.mjs
[handoff]: ../tools/ai/test_handoff.py
[briefs]: ../art/briefs/characters_v0.8.json
[prompts]: ../art/briefs/prompts_v0.8.1.json
[provenance]: ../art/provenance.csv
[age-option]: ../proposals/roster_age_option.json
[tasks]: ../ops/ai/tasks/
[p0]: ../reports/development_20261015/verification.json
[p0-browser]: ../reports/development_20261015/browser.txt
[late]: ../reports/late_actions_20261015/verification.json
[late-screens]: ../reports/late_actions_20261015/screens/
[balance-code]: ../engine/tests/balance_probe.mjs
[balance-latest]: ../reports/balance_audit_20261015/expanded_after_summary.json
[engine-final]: ../reports/balance_audit_20261015/engine_final.txt
[receipt-engine]: ../reports/receipt_keys_20261015/node_tests.txt

현재 반복 턴 단위의 [JS222 로그](../reports/csharp_turns_20261015/node_tests.txt)·[C# 실제명령](../reports/csharp_turns_20261015/root_checks.txt)·[Opus 승인](../ops/ai/tasks/P1-CSHARP-TURNS-CROSS-REVIEW.result.json)을 이전 단위와 구분한다. 다음 [플레이어 행동 경계](../ops/ai/tasks/P1-CSHARP-PLAYER-ACTIONS-BOUNDARY.md)는 구현 지시서이며 완료 증거가 아니다.

A0 [실제128244 질의검사](../reports/player_queries_20261015/root_checks.txt)·[JS234](../reports/player_queries_20261015/node_tests.txt)·[Opus A0승인](../ops/ai/tasks/P1-CSHARP-PLAYER-QUERIES-CROSS-REVIEW.result.json)을 기록한다. 다음 [A1 실행 작업](../ops/ai/tasks/P1-CSHARP-PLAYER-EXECUTION-IMPLEMENTATION.md)은 전체카드·모드·필살기·생산된 상태의 실제 실행이며 조회 성공과 구별한다.
