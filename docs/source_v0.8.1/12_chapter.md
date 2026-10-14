# <a id="ch12"></a>12. 모집(가챠)·재화·상품

데이터: `content/gacha.mjs`(확률·풀·배너), `content/progression.mjs`(수급·상품·교환). 엔진: `Gacha`(JS)·`MemoryForge.Core.Gacha`(C#). 정확 계산: `tools/verify_economy.py`.

## 12.1 배너와 규칙

| 배너 종류 | 재화 | 천장 공유 | 규칙 |
|---|---|---|---|
| 한정 캐릭터 | 한정 모집권 또는 결정 160 | 모든 한정 캐릭터 배너가 **카운트와 확정 상태를 공유·이월** | SSR이 나오면 50% 확률로 픽업. 픽업이 아니면(픽뚫) 다음 SSR은 픽업 확정. SR은 50% 확률로 픽업 SR 2명 중 1명, 픽업이 아니면 다음 SR 픽업 확정 |
| 무기 | 무기 모집권 또는 결정 160 | 모든 무기 배너가 공유·이월(캐릭터와 별개) | SSR이 나오면 75% 확률로 전용 무기. 아니면 다음 SSR 확정. SR 75%/확정 동일 구조 |
| 상시 | 상시 모집권 또는 결정 160 | 별개 | SSR = 상시 SSR 캐릭터 4명 균등. 상시+초심자 누적 300회에 **상시 SSR 1명 선택권**(계정당 1회) |
| 초심자 | 상시 모집권 8장 = 10회 | 별개(상시 300회 누적에는 포함) | 10회 단위만, 총 50회, 50회 안에 SSR 1명 확정 |

공통:

- 1회 또는 10회만 모집한다(`BAD_COUNT`). 10회는 1회를 순서대로 10번 처리하고 매 회차에 천장 규칙을 적용한다. 10회 할인은 없다.
- **SR 이상 10회 보장**: 9회 연속 SR 이상이 없으면 10번째는 SR 이상(이때도 SSR 확률은 현재 천장 곡선 그대로). SSR도 "SR 이상"으로 센다.
- 결제 순서: 해당 모집권 → 결정. 결정은 무료 결정을 먼저 쓰고 유료 결정을 나중에 쓴다(유료 결정 잔액을 환불 가능 상태로 최대한 남김). 단, 유료 전용 상품(있다면)은 유료 결정만.
- **무작위는 서버 CSPRNG**로 뽑는다. 전투용 시드 RNG를 모집에 쓰지 않는다. 천장 상태 기계는 JS·C#·서버가 같고, 같은 난수열을 넣으면 같은 결과가 나오는지 교차 벡터로 검사한다(`engine/tests/vectors`).
- 결과·차감·중복 보상·천장 상태·원장·영수증을 **한 트랜잭션**에서 저장한 뒤 연출을 시작한다(§17.2). 연출을 건너뛰거나 앱이 꺼져도 결과는 보존된다.

## 12.2 확률 곡선과 정확 기대값

| 풀 | SSR 기본 | 소프트 시작 | 소프트 증가/회 | 하드 | SR 기본 | SR 보장 | 픽업 SSR | 픽업 SR |
|---|---|---|---|---|---|---|---|---|
| 한정 캐릭터 | 0.8% | 66 | +7% | 80 | 6% | 10회 | 50% | 50% |
| 무기 | 0.9% | 60 | +8% | 70 | 7% | 10회 | 75% | 75% |
| 상시 | 0.8% | 66 | +7% | 80 | 6% | 10회 | - | - |
| 초심자 | 0.8% | - | - | 50 | 6% | 10회 | - | - |

**회차별 SSR 확률(직전 SSR 이후 k번째 뽑기)**

| k | 캐릭터(하드 80) | 무기(하드 70) |
|---|---|---|
| 1~59 | 0.8% | 0.9% |
| 60 | 0.8% | 8.9% |
| 61 | 0.8% | 16.9% |
| 62 | 0.8% | 24.9% |
| 63 | 0.8% | 32.9% |
| 64 | 0.8% | 40.9% |
| 65 | 0.8% | 48.9% |
| 66 | 7.8% | 56.9% |
| 67 | 14.8% | 64.9% |
| 68 | 21.8% | 72.9% |
| 69 | 28.8% | 80.9% |
| 70 | 35.8% | 100% |
| 71 | 42.8% | - |
| 72 | 49.8% | - |
| 73 | 56.8% | - |
| 74 | 63.8% | - |
| 75 | 70.8% | - |
| 76 | 77.8% | - |
| 77 | 84.8% | - |
| 78 | 91.8% | - |
| 79 | 98.8% | - |
| 80 | 100% | - |

**정확 계산 결과** (tools/verify_economy.py, 마르코프 연쇄로 정확 계산 — 표본 추출 아님)

| 지표 | 한정 캐릭터 | 무기 |
|---|---|---|
| SSR 1개 평균 뽑기 수 | 53.41 | 48.30 |
| 종합 SSR 확률(1/평균) | 1.872% | 2.070% |
| 픽업 1개 평균 뽑기 수 | 80.12 | 60.37 |
| 픽업 확정 최대(풀천장) | 160 | 140 |
| 50회 안에 픽업 | 19.6% | 29.1% |
| 80회 안에 픽업 | 61.2% | 82.8% |
| 96회(무과금 1시즌 최대) 안에 픽업 | 68.9% | 86.6% |
| 120회 안에 픽업 | 78.0% | 91.1% |
| 140회 안에 픽업 | 95.4% | 100.0% |

**SSR 누적 확률(픽업 여부 무관, 한정 캐릭터 곡선)**: 10회 7.7% · 30회 21.4% · 50회 33.1% · 60회 38.2% · 66회 45.3% · 70회 83.3% · 75회 99.8% · 80회 100.0%

- SR 기본 확률: 캐릭터·상시·초심자 6%, 무기 7%(10회 보장 별도). R(잔향 기록: 경험치·크레딧·연마석 재료) 나머지.
- 표시 의무: 배너마다 "기본 확률, 회차별 확률표, 종합 확률, 천장, 픽업 비율, 등장 항목별 개별 확률"을 구매 전 화면에 공개한다(§12.7).

## 12.3 중복: 공명과 정련

| 대상 | 규칙 | 기억 결정체 |
|---|---|---|
| 캐릭터 | 첫 획득 = 공명 0. 2번째~7번째 획득마다 공명 +1(최대 6). 효과는 캐릭터 시트 공명 표(§9.3) | 첫 획득 0, 공명 단계 중복 SSR 10 · SR 2, 공명 6 이후 SSR 25 · SR 5 |
| 무기 | 첫 획득 = 정련 R1. 2~5번째 획득마다 +1(최대 R5). 패시브 수치가 R1~R5 값으로 오른다 | 매 획득 SSR 10 · SR 2, R5 이후 SSR 25 · SR 5 |
| R 결과 | 재료(잔향 기록) | 잔광 15 |

공명 설계 원칙(§9.4 템플릿과 같음): R1 고유 카드 개조(수치·횟수·균열), R2 패시브 수치, R3 카드 스킬 +2, R4 전투 시작 효과·규칙 확장·훅 + 기억 용량 +1, R5 필살기 스킬 +2, R6 대형 훅(필살기 추가·변형). 세라만 R1이 훅(유리탄 예약 해결 시 뽑기), R4가 카드 개조(시한 포격 균열 +2)다. **공명 0으로 모든 콘텐츠를 깰 수 있어야 한다**(공명은 편의·속도·연출).

## 12.4 재화

| 재화 | 획득 | 사용 | 비고 |
|---|---|---|---|
| 결정(유료/무료) | 상품 / 임무·이벤트·패스·스토리 | 모집 160/회, 집중 충전, 스킨 일부 | 유료·무료 분리 원장 |
| 한정·무기·상시 모집권 | 이벤트 상점·패스·임무·교환 | 해당 배너 1회 | 배너 종류 고정 |
| 기억 결정체 | 중복 | 결정체 교환소 | 월간 한도 |
| 잔광 | R 결과 | 잔광 교환소 | 월간 한도 |
| 크레딧 | 던전·임무·보스 | 모든 육성 | — |
| 보스 토큰 | 주간 보스 보상 | 보스 토큰 교환소 | 주간 한도 |
| 이벤트 토큰 | 이벤트 탐사·임무 | 이벤트 상점 | 이벤트 종료 후 7일간 상점 유지, 이후 크레딧으로 자동 환전(1:100) |
| 동전 | 탐사 안 | 탐사 상점·사건 | 탐사가 끝나면 사라짐 |

## 12.5 교환소

| 교환소 | 항목(비용, 한도) |
|---|---|
| 기억 결정체(월간) | 한정 모집권(5, 5장), 무기 모집권(5, 5장), 의상 색 변경(30, 1회) |
| 잔광(월간) | 상시 모집권(75, 5장), 한정 모집권(75, 5장), 크레딧 400(1, 500회), 선물 중(20, 20개) |
| 보스 토큰(주간) | 제련핵(60, 2개) — 기본(R) 무기 정련, 공방 교본(10, 6개), 연마재(5, 20개) |
| 시즌 이벤트(이벤트마다) | 한정 모집권(400, 3장/2장), 무료 결정 80(100, 6회), 승급 핵(300, 2개), 선물 대(60, 10개), 이벤트 유물 상시화(500, 1회) |

## 12.6 무과금 수급(42일)과 v0.6 대비 영향

| 출처 | 단가 | 횟수 | 결정 | 비고 |
|---|---|---|---|---|
| 일일 임무 | 60 | 42 | 2,520 |  |
| 주간 임무 | 420 | 6 | 2,520 |  |
| 주간 탐사 첫 완주 | 200 | 6 | 1,200 |  |
| 주간 누적 접속 | 160 | 6 | 960 |  |
| 시즌 이벤트 | 2880 | 1 | 2,880 |  |
| 무료 패스 | 1440 | 1 | 1,440 |  |
| 기록 균열(최대) | 800 | 3 | 2,400 | 고난도(선택) |

합계 결정 **13,920**(기록 균열 제외 11,520) + 한정 모집권 9(시즌 이벤트 상점 5 + 무료 패스 4) + 상시 모집권 12(주간 임무 2×6).
한정 배너 환산 **96.0회**(균열 제외 81.0회). 천장 0에서 시작할 때 이 안에 픽업을 얻을 확률 68.9%(균열 제외 61.7%).

| 비교(천장 0, 픽업 1명 목표) | v0.6 | v0.7 |
|---|---|---|
| 규칙 | 픽업 1.2%, 80회 픽업 확정 | SSR 0.8%(소프트 66~), 80회 SSR 확정, 픽업 50%·풀천장 160 |
| 픽업 평균 뽑기 수 | 51.61 | 80.12 |
| 무과금 1시즌(96회) 안에 픽업 확보 | 100% | 68.9% |
| 최악 비용(최고 효율 팩 단가, 보유 재화 0) | 80회 ≈ 160,900원 | 160회 ≈ 321,800원 |

v0.7은 디렉터 요청(반천장·중복 성능·무기 모집)을 반영해 **픽업 확보가 더 어려워졌다**. 출시 전 사업 검토에서 다음 조정 손잡이 중 무엇을 쓸지 결정한다(§22): ① 시즌 수급을 한정 110~120회 수준으로 상향 ② 픽업 확률 55% ③ 픽뚫 누적 보정(예: 직전 픽업 성공 뒤 50:50에서 진 횟수만큼 다음 50:50 확률 +10%p, 확정 규칙은 그대로) ④ 첫 한정 배너 픽업 확률 상향(신규 이용자 한정). 어떤 손잡이든 `content/gacha.mjs` 수정 → `tools/verify_economy.py`로 표를 다시 만든다. 현재 값은 **가설**이며 시뮬레이터로 체감을 먼저 확인한다.

## 12.7 상품과 결제

| 상품 | 가격(원) | 결정 | 첫 구매 보너스 | 1회 뽑기당 원 | 첫 구매 시 1회당 원 |
|---|---|---|---|---|---|
| pk_160 | 2,400 | 160 | 160 | 2,400 | 1,200 |
| pk_800 | 11,000 | 800 | 800 | 2,200 | 1,100 |
| pk_1680 | 22,000 | 1,680 | 1,680 | 2,095 | 1,048 |
| pk_3440 | 44,000 | 3,440 | 3,440 | 2,047 | 1,023 |
| pk_7000 | 88,000 | 7,000 | 7,000 | 2,011 | 1,006 |

- **공방 정기권** 6,900원/30일: 즉시 유료 결정 300 + 매일 무료 결정 90 = 3,000 (1회당 약 368원). 자동 갱신 없음, 최대 180일 누적.
- **시즌 패스** 12,900원: 50레벨(레벨당 1000 XP), 결정 2,560 포함(1회당 약 806원). 플러스 24,900원: 즉시 10레벨 + 한정 의상 1(패스 전용).
- **스킨**: 1단계 9,900원(일러스트·로비 모델 색/소품 변경) / 2단계 19,800원(새 의상(로비·전투 모델·일러스트)) / 3단계 29,800원(새 의상 + 필살기 연출·전용 대사).
- 참고: 최고 효율 팩 기준 하드 천장 80회 ≈ 160,914원, 풀천장 160회 ≈ 321,829원(첫 구매 보너스 제외).

원칙:

- 확률형 상품은 모집 하나뿐이다. 패키지·상자 등 다른 무작위 유료 상품을 만들지 않는다.
- 모든 상품 페이지에 구성·가격·기간·구매 한도·확률(해당 시)을 표시한다. 한국 게임산업법(확률형 아이템 정보 공개, 2024-03 시행)과 Google Play 정책을 출시 전 다시 확인한다.
- 유료 결정은 사용 전 청약 철회 대상이다. 환불·취소(Play 무효 구매 알림)는 원장에서 회수하고, 잔액이 부족하면 마이너스 대신 **환불 부채** 상태로 표시해 다음 획득분에서 차감한다(잔액 0 미만 금지 제약과 별개 테이블).
- 미성년(만 18세 미만) 자기 신고 계정은 월 결제 한도 설정(기본 7만 원, 보호자 변경 가능)을 둔다.
- 월정액은 자동 갱신하지 않는다. 최대 180일까지 미리 쌓을 수 있다.
- 결제 검증은 서버에서 Play Developer API로 하고, 구매 토큰 다이제스트로 중복 지급을 막는다. 운영 서버는 시작 시와 요청마다 모의(mock) 검증을 거부한다.

## 12.8 수급 확률과 배너 기간의 해석

현재 68.8886%는 천장 0·확정 아님에서 시작해 특정 픽업이 열려 있는 동안 96회를 사용할 수 있을 때 그 픽업을 적어도 한 번 얻는 확률이다. 시즌에는 21일씩 서로 다른 신규 픽업 두 명이 있다. 전반 배너가 끝난 뒤 받은 재화를 전반 캐릭터에 쓸 수 없으므로 42일 총량을 모든 배너의 가용량으로 취급하지 않는다.

| 질문 | 사용할 계산 |
| --- | --- |
| 한 목표에 96회 사용 | 68.8886%, 기존 곡선 그대로 |
| 시즌 두 신규 모두 확보 | 각 배너 지급 시점과 뽑기 중단·이월 정책을 포함한 상태 분포 |
| 첫 21일 목표 확보 | D21까지 수령·사용 가능한 재화만 |
| 신규 계정 체감 | 해금일·초심자·스토리 일회성·고난도 참여율을 별도 모델 |
| 캐릭터와 무기 함께 확보 | 분리된 천장과 동일 지갑의 지출 배분 |

기존 원장의 D21 결정 6,560에 한정권 5장이 도착한다고 가정하면 전반 46회, 시즌 96회다. 저축·이전 천장·확정·무기 지출 없이 A를 얻으면 멈추고 B로 이월하는 **분석 시나리오**에서는 A 18.07%, B 59.13%, 모두 8.31%, 둘 중 하나 이상 68.89%다. 한정권 5장의 시점은 실제 라이브 일정 확정 전의 가정이며 이 수치를 모든 계정의 확보율로 광고하지 않는다.

| 한 목표 가용량 | 현행 픽업 50% | 비교안 55% |
| --- | --- | --- |
| 81회 | 61.75% | 65.57% |
| 96회 | 68.89% | 72.00% |
| 110회 | 74.53% | 77.08% |
| 115회 | 76.33% | 78.70% |
| 120회 | 78.03% | 80.23% |
| 160회 | 100% | 100% |

위 55%·110~120회는 비교안이다. 이 통합 과정에서 확률·가격·무료 지급량은 변경하지 않는다. ‘마지막 픽업 이후 두 번 픽뚫이면 보정’은 현재 다음 SSR 확정·픽업 시 초기화 규칙에서 두 번에 도달할 수 없어 채택하지 않는다. 새로운 연패 보호를 만들 때는 비확정 판정만 세는 별도 상태 기계를 명세해야 한다.

## 12.9 단가 환산과 실제 결제액

| 목표 | 필요 결정 | 보너스·기존 재화 제외 최소 장바구니 | 구성 |
| --- | --- | --- | --- |
| 80회 | 12,800 | 165,000원 | 3,440×3 + 1,680 + 800 |
| 160회 | 25,600 | 326,200원 | 7,000×2 + 3,440×3 + 800 + 160×3 |

최대 팩의 결정당 가격을 곱한 비례 환산은 결제 합계가 아니다. 최초 구매 보너스, 보유 결정, 월정액 잔여 지급, 패스 수령률을 적용하면 별도 계산한다. 모든 상품 화면은 실제 결제 원화와 무료·유료 결정의 구분, 적용 기간, 수령 조건, 스킨 변경 부위를 명시한다.

## 12.10 사업성 검증과 변경 승인

월정액·패스·스킨의 가격표는 판매 검증 전 가안이다. 한 시즌 신규 두 명은 카드 외에 원화·리그·키아트·인연·음성·모든 기존 조합 QA를 필요로 한다. 무료 접근성을 낮추면 매출이 자동으로 오른다는 가정을 두지 않는다. 구매 전환, 잔존, 환불, 제작비는 실제 테스트와 견적으로 검증한다.

수치 변경은 제안 → 독립 계산 → 클라이언트·서버·확률표 일치 시험 → 사람 승인 → 버전 변경의 순서다. AI는 확인되지 않은 디렉터 요청이나 기존 댓글을 근거로 가격·천장·재화 지급량을 바꾸지 않는다.

## 12.11 월정액·패스의 미확정 세부 계약

위 가격·총량은 현행 데이터 그대로다. 월정액 미접속일 소급 지급, 패스 시즌 종료 후 미수령 보상, 플러스 업그레이드 차액, 단계별 패스 재료 구성은 운영 API가 구현된 것이 아니다. 이전 v0.6의 패스 50레벨 표는 §12.12에 **구판 보존안**으로 복원한다. v0.7에서 무료권·상시권 배정이 바뀌었으므로 구판 보상을 그대로 활성화하면 중복 지급이 생길 수 있다. 최신 지급 원장과 대조한 신규 표를 승인한 뒤 서버에 연결한다.

## 12.12 구판 패스 1~50레벨 보상표 — 보존·대조용

**현재 상품의 확정 지급표가 아니다.** 원본 v0.6 CSV를 그대로 보존한 자료다. 신규 표는 현행 무료 결정1,440·무료 한정권4장·유료 결정2,560 등 현재 배정과 맞춰 별도 승인해야 한다. 이 표의 표준권·성장 재료를 현행 수급에 임의 합산하지 않는다. 원시 CSV/JSON은 `docs/archive/v0.6_materials/`.

| ﻿level | track | item | quantity |
| --- | --- | --- | --- |
| 1 | free | credits | 6000 |
| 1 | paid_additional | credits | 16000 |
| 2 | free | credits | 6000 |
| 2 | free | character_xp_records | 12 |
| 2 | paid_additional | credits | 16000 |
| 2 | paid_additional | character_xp_records | 30 |
| 3 | free | credits | 6000 |
| 3 | free | weapon_xp_stones | 6 |
| 3 | paid_additional | credits | 16000 |
| 3 | paid_additional | weapon_xp_stones | 15 |
| 4 | free | credits | 6000 |
| 4 | free | skill_books | 3 |
| 4 | paid_additional | credits | 16000 |
| 4 | paid_additional | skill_books | 6 |
| 5 | free | credits | 6000 |
| 5 | free | equipment_polish | 6 |
| 5 | paid_additional | credits | 16000 |
| 5 | paid_additional | equipment_polish | 12 |
| 6 | free | credits | 6000 |
| 6 | free | character_xp_records | 12 |
| 6 | paid_additional | credits | 16000 |
| 6 | paid_additional | character_xp_records | 30 |
| 7 | free | credits | 6000 |
| 7 | free | weapon_xp_stones | 6 |
| 7 | paid_additional | credits | 16000 |
| 7 | paid_additional | weapon_xp_stones | 15 |
| 8 | free | credits | 6000 |
| 8 | free | skill_books | 3 |
| 8 | paid_additional | credits | 16000 |
| 8 | paid_additional | skill_books | 6 |
| 9 | free | credits | 6000 |
| 9 | free | equipment_polish | 6 |
| 9 | paid_additional | credits | 16000 |
| 9 | paid_additional | equipment_polish | 12 |
| 10 | free | credits | 6000 |
| 10 | free | crystals | 160 |
| 10 | free | standard_tickets | 1 |
| 10 | paid_additional | credits | 16000 |
| 10 | paid_additional | crystals | 320 |
| 10 | paid_additional | pickup_tickets | 1 |
| 10 | paid_additional | stamina_batteries_60 | 1 |
| 11 | free | credits | 6000 |
| 11 | paid_additional | credits | 16000 |
| 12 | free | credits | 6000 |
| 12 | free | character_xp_records | 12 |
| 12 | paid_additional | credits | 16000 |
| 12 | paid_additional | character_xp_records | 30 |
| 13 | free | credits | 6000 |
| 13 | free | weapon_xp_stones | 6 |
| 13 | paid_additional | credits | 16000 |
| 13 | paid_additional | weapon_xp_stones | 15 |
| 14 | free | credits | 6000 |
| 14 | free | skill_books | 3 |
| 14 | paid_additional | credits | 16000 |
| 14 | paid_additional | skill_books | 6 |
| 15 | free | credits | 6000 |
| 15 | free | equipment_polish | 6 |
| 15 | paid_additional | credits | 16000 |
| 15 | paid_additional | equipment_polish | 12 |
| 16 | free | credits | 6000 |
| 16 | free | character_xp_records | 12 |
| 16 | paid_additional | credits | 16000 |
| 16 | paid_additional | character_xp_records | 30 |
| 17 | free | credits | 6000 |
| 17 | free | weapon_xp_stones | 6 |
| 17 | paid_additional | credits | 16000 |
| 17 | paid_additional | weapon_xp_stones | 15 |
| 18 | free | credits | 6000 |
| 18 | free | skill_books | 3 |
| 18 | paid_additional | credits | 16000 |
| 18 | paid_additional | skill_books | 6 |
| 19 | free | credits | 6000 |
| 19 | free | equipment_polish | 6 |
| 19 | paid_additional | credits | 16000 |
| 19 | paid_additional | equipment_polish | 12 |
| 20 | free | credits | 6000 |
| 20 | free | crystals | 240 |
| 20 | free | standard_tickets | 1 |
| 20 | free | stamina_batteries_60 | 1 |
| 20 | paid_additional | credits | 16000 |
| 20 | paid_additional | crystals | 320 |
| 20 | paid_additional | pickup_tickets | 1 |
| 20 | paid_additional | stamina_batteries_60 | 1 |
| 21 | free | credits | 6000 |
| 21 | paid_additional | credits | 16000 |
| 22 | free | credits | 6000 |
| 22 | free | character_xp_records | 12 |
| 22 | paid_additional | credits | 16000 |
| 22 | paid_additional | character_xp_records | 30 |
| 23 | free | credits | 6000 |
| 23 | free | weapon_xp_stones | 6 |
| 23 | paid_additional | credits | 16000 |
| 23 | paid_additional | weapon_xp_stones | 15 |
| 24 | free | credits | 6000 |
| 24 | free | skill_books | 3 |
| 24 | paid_additional | credits | 16000 |
| 24 | paid_additional | skill_books | 6 |
| 25 | free | credits | 6000 |
| 25 | free | equipment_polish | 6 |
| 25 | paid_additional | credits | 16000 |
| 25 | paid_additional | equipment_polish | 12 |
| 26 | free | credits | 6000 |
| 26 | free | character_xp_records | 12 |
| 26 | paid_additional | credits | 16000 |
| 26 | paid_additional | character_xp_records | 30 |
| 27 | free | credits | 6000 |
| 27 | free | weapon_xp_stones | 6 |
| 27 | paid_additional | credits | 16000 |
| 27 | paid_additional | weapon_xp_stones | 15 |
| 28 | free | credits | 6000 |
| 28 | free | skill_books | 3 |
| 28 | paid_additional | credits | 16000 |
| 28 | paid_additional | skill_books | 6 |
| 29 | free | credits | 6000 |
| 29 | free | equipment_polish | 6 |
| 29 | paid_additional | credits | 16000 |
| 29 | paid_additional | equipment_polish | 12 |
| 30 | free | credits | 6000 |
| 30 | free | crystals | 240 |
| 30 | free | standard_tickets | 1 |
| 30 | paid_additional | credits | 16000 |
| 30 | paid_additional | crystals | 320 |
| 30 | paid_additional | pickup_tickets | 1 |
| 30 | paid_additional | stamina_batteries_60 | 1 |
| 30 | paid_additional | craftable_weapon_selector_lv1_r1 | 1 |
| 31 | free | credits | 6000 |
| 31 | paid_additional | credits | 16000 |
| 32 | free | credits | 6000 |
| 32 | free | character_xp_records | 12 |
| 32 | paid_additional | credits | 16000 |
| 32 | paid_additional | character_xp_records | 30 |
| 33 | free | credits | 6000 |
| 33 | free | weapon_xp_stones | 6 |
| 33 | paid_additional | credits | 16000 |
| 33 | paid_additional | weapon_xp_stones | 15 |
| 34 | free | credits | 6000 |
| 34 | free | skill_books | 3 |
| 34 | paid_additional | credits | 16000 |
| 34 | paid_additional | skill_books | 6 |
| 35 | free | credits | 6000 |
| 35 | free | equipment_polish | 6 |
| 35 | paid_additional | credits | 16000 |
| 35 | paid_additional | equipment_polish | 12 |
| 36 | free | credits | 6000 |
| 36 | free | character_xp_records | 12 |
| 36 | paid_additional | credits | 16000 |
| 36 | paid_additional | character_xp_records | 30 |
| 37 | free | credits | 6000 |
| 37 | free | weapon_xp_stones | 6 |
| 37 | paid_additional | credits | 16000 |
| 37 | paid_additional | weapon_xp_stones | 15 |
| 38 | free | credits | 6000 |
| 38 | free | skill_books | 3 |
| 38 | paid_additional | credits | 16000 |
| 38 | paid_additional | skill_books | 6 |
| 39 | free | credits | 6000 |
| 39 | free | equipment_polish | 6 |
| 39 | paid_additional | credits | 16000 |
| 39 | paid_additional | equipment_polish | 12 |
| 40 | free | credits | 6000 |
| 40 | free | crystals | 320 |
| 40 | free | standard_tickets | 1 |
| 40 | free | stamina_batteries_60 | 1 |
| 40 | paid_additional | credits | 16000 |
| 40 | paid_additional | crystals | 320 |
| 40 | paid_additional | pickup_tickets | 1 |
| 40 | paid_additional | stamina_batteries_60 | 1 |
| 41 | free | credits | 6000 |
| 41 | paid_additional | credits | 16000 |
| 42 | free | credits | 6000 |
| 42 | free | character_xp_records | 12 |
| 42 | paid_additional | credits | 16000 |
| 42 | paid_additional | character_xp_records | 30 |
| 43 | free | credits | 6000 |
| 43 | free | weapon_xp_stones | 6 |
| 43 | paid_additional | credits | 16000 |
| 43 | paid_additional | weapon_xp_stones | 15 |
| 44 | free | credits | 6000 |
| 44 | free | skill_books | 3 |
| 44 | paid_additional | credits | 16000 |
| 44 | paid_additional | skill_books | 6 |
| 45 | free | credits | 6000 |
| 45 | free | equipment_polish | 6 |
| 45 | paid_additional | credits | 16000 |
| 45 | paid_additional | equipment_polish | 12 |
| 46 | free | credits | 6000 |
| 46 | free | character_xp_records | 12 |
| 46 | paid_additional | credits | 16000 |
| 46 | paid_additional | character_xp_records | 30 |
| 47 | free | credits | 6000 |
| 47 | free | weapon_xp_stones | 6 |
| 47 | paid_additional | credits | 16000 |
| 47 | paid_additional | weapon_xp_stones | 15 |
| 48 | free | credits | 6000 |
| 48 | free | skill_books | 3 |
| 48 | paid_additional | credits | 16000 |
| 48 | paid_additional | skill_books | 6 |
| 49 | free | credits | 6000 |
| 49 | free | equipment_polish | 6 |
| 49 | paid_additional | credits | 16000 |
| 49 | paid_additional | equipment_polish | 12 |
| 50 | free | credits | 6000 |
| 50 | free | crystals | 480 |
| 50 | free | standard_tickets | 2 |
| 50 | free | completion_cores | 2 |
| 50 | free | profile_decor | 1 |
| 50 | paid_additional | credits | 16000 |
| 50 | paid_additional | crystals | 320 |
| 50 | paid_additional | pickup_tickets | 2 |
| 50 | paid_additional | profile_decor | 1 |
