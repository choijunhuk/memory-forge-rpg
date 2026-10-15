# P1-UNITY-BATTLE-PRESENTATION-FIX — B1/B2 표시 수정 (Claude Opus, High)

- 변경 파일: `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeBattleClient.cs`, `client/MemoryForgeUnity/Assets/Tests/PlayMode/BattleClientPlayModeTests.cs`
- Core·데이터·로더·익스포터·asmdef·Scene은 변경하지 않았다.
- **검증 상태:** 컴파일·Play·시험은 모두 **NOT_RUN**이다(Bash·Editor 없음). 해시와 실행 결과는 기재하지 않았다. 독립 검토와 실제 실행은 Root가 맡는다.
- 범위: 데스크톱 Editor의 전투 연습 화면만 다룬다. 모바일 dp, Run 전체, 아트, 서버는 포함하지 않는다.

## B1 — 시간표 (`TimelineCells` + `Timeline()`)

**근거**
- Core `Battle.SlotCapacity`가 호출하는 `ReservationBook.CalculateCapacity`(`ReservationBook.cs:52–62`)는 internal이라 UI에서 부를 수 없다.
- Core 용량 규칙은 다음과 같다.
  - 다음 턴: `min(nextMax, base+temp)`에서 봉인(`SealedUntil ≥ Turn`) 시 −1, 하한 0.
  - 다다음 턴: `min(afterMax, base+temp)`.
- HTML 기능 참조 `prototype/src/ui.js:518–538`(`slotsView`)는 칸을 이 순서로 채운다: 예약 → 빈 칸(용량까지) → 봉인 1칸 → 사용 불가 칸(최대치까지).

**구현**
- 읽기 전용 순수 함수 `TimelineCells(BattleState, BattleRules)`가 위 규칙을 그대로 표시용으로 계산한다.
  - 입력은 `SlotsBase`, `SlotsTemp`, `SealedUntil`, 실제 `Rules.NextSlotsMax/AfterSlotsMax`다. 최대치를 하드코딩하지 않는다.
  - 그룹당 칸 수는 `max(실제 예약 수, 최대치, 용량+봉인)`이다. 그래서 어떤 예약도 잘리지 않는다.
  - 예약은 `Due` → `Order` 순으로 정렬한다.
  - 다음 턴 그룹은 `Due ≤ Turn+1`, 다다음 턴 그룹은 그 이후 전부다. 그래서 만기가 이례적인 예약도 누락되지 않는다.
- 렌더링
  - 칸 상태는 카드 이름 / `빈 칸` / `봉인` / `-`로 표시한다. 봉인 칸은 선 색 배경에 위험색 글자를 쓴다.
  - 기존 표기 `"다음  {카드}"`를 유지했다.
  - 칸 폭은 `min(220, 1308/칸 수)`다. 원본 최대 5칸(3+2)이면 기존 패널 1536 안에 들어간다.

## B2 — 적 의도·상태 (`IntentDamage` / `IntentText` / `StatusText` / `PartyStatusText`)

**근거**
- `Battle.cs:608–611` `EnemyHit`의 공식: `Round(d × Or(DifficultyDamage,100), 100) + Strength`, 그 뒤 `Status.Weak > 0`이면 `Floor(× (10000 − WeakBps), 10000)`.
  - `Round(n,d) = Floor(2n + d, 2d)`(`Battle.cs:344`)를 정수 그대로 옮겼다.
  - 다단 공격 수는 `Or(Hits ?? 1, 1)`이다.
- HTML `ui.js:470`은 난이도와 힘을 반영하고, `expose → 피해`(482)를 보여 준다.
- 상태칩과 순서는 `ui.js:503–507`(균열 +25%, 취약·약화·표식·화상·힘·반사)을 따른다.
- 파티 약화와 버프 이름은 `ui.js:551, 560`과 같다.
- 약화가 적 피해를 줄이는 것은 Core가 실제로 적용한다. 그래서 '이번' 값에는 약화까지 반영했다. HTML은 약화를 칩으로만 보여 주므로, 이 부분은 Core를 기준으로 삼았다.

**표시 규칙**
- **이번:** 현재 상태(힘·약화)로 계산한 예상값이다. 예: `공격 9`, `공격 13 × 2`.
  - 공격 라벨이 원본에 있으면 앞에 붙인다(예: `망각 낭독 공격 N`).
  - 노출은 `약점 노출 → 공격 N`, 약화 공격은 `· 파티 약화 n`, 방어+힘은 `방어 b · 힘 +s`로 보여 준다.
- **다음:** 다음 의도까지 힘·약화가 바뀔 수 있으므로 `기본 공격 N`(난이도만 반영)으로 구분해 표시한다.
  - 충전 뒤에 오는 공격도 이 줄에 나온다. Core의 `charge`는 이벤트만 내고 수치 효과가 없다(`Battle.cs:553–554`).
- 적 패널에서 상태칩 한 줄을 의도 위에 추가했다. 위쪽 행들은 몇 px씩 위로 옮겼고, 버튼 위치는 바꾸지 않았다.
- 상단 바에 파티 약화와 활성 버프(`Until ≥ Turn`)를 표시한다. 예: 정조준 → `충전`.
- 숨은 드로우·난수 정보는 노출하지 않는다. 수치·데이터는 바꾸지 않았다.
- 기존 확정 단계·모달·미리보기 수정(H1/M1/M5/M6)은 그대로 유지했다.

## 시험 (기존 6개 유지, 2개 추가, 시험 5 보강) — 전부 NOT_RUN

**시험 5 보강**
- 오버레이를 열기 전에 같은 화면 좌표로 레이캐스트해서, 최상단 히트의 Button이 실제 확정 버튼과 **같은 객체**인지 먼저 확인한다(양성 대조).
- 기존의 차단·불변·확정 단언은 그대로다.

**시험 7 `TimelineKeepsEveryReservationAfterSeraUltimateAndLabelsSealedSlots`**
- 먼저 원본 근거를 단언한다: 세라 필살기에 `slot next 1`이 있다.
- 정당한 `BattleSetup` 입력(게이지 = `GaugeMax`, 세라 + 유리탄 5장, `e_shelf`)으로 만든 독립 Core 전투에서 실제 명령을 실행한다: 필살기 → 다음 턴 예약 3장.
  - Core가 세 번째 예약을 받았는지 확인한다.
  - `TimelineCells`에 3장이 Order 순으로 모두 있는지 확인한다.
  - 다다음 턴 그룹이 `빈 칸 × AfterSlots, - × (AfterSlotsMax−AfterSlots)`인지 확인한다.
- 라비엔 `담보 계약`으로 실제 봉인을 만든 뒤 다음 턴 예약을 1장 한다.
  - Core가 다음 예약을 `SLOT_FULL`로 거절하는지 확인한다.
  - 칸이 `[카드, 봉인, -…]`인지 확인한다.
- 이 시험은 표시 투영의 증거다. UI 연습 흐름 자체를 검증한 것은 아니다.

**시험 8 `EnemyIntentForecastMatchesActualHitsAndStatusesAreVisible`**
- 정당한 `BattleSetup`(니아·하린·세라, 구조 분석·도발·정조준 포함 5장, `e_moth`)으로 독립 전투를 만든다.
- 실제로 카드 3장을 사용한 뒤 확인한다:
  - 취약·약화 값이 원본 카드 값과 같은지.
  - 상태칩 문자열과 파티 `충전` 표시.
  - '다음' 값이 `기본 공격 7 × 2`인지(원본 패턴 값).
- 4턴 동안 매 턴 공격 예상치가 **Core 실제 hit 이벤트 피해와 같은지** 확인한다.
  - 1턴은 약화가 반영된 값, 4턴은 나방이 원본 패턴으로 얻은 힘 +1이 반영된 값이다. 두 경우가 모두 실제로 나왔는지도 단언한다.
- 이어서 고정 UI 연습 흐름에서 실제 초기 손패의 `구조 분석`으로 카드 → 방식 → 대상 → 확정을 진행한다. 해당 적 패널에 상태칩 라벨과 의도 두 줄 라벨이 정확한 문자열로 보이는지, 잘리지 않았는지 확인한다.

**공통 사항**
- 실행 중인 전투의 상태를 주입하거나 되돌린 곳은 없다. 단언을 약화시킨 곳도 없다.
- 시험 2의 주석 행 번호(`cs:156`, `cs:127`)는 실제 행과 어긋나 있다. 표시상 문제일 뿐이고, 이번 범위에서는 고치지 않았다.

## 남은 보류 (변경 없음)
- **M2** 시작 비원자성
- **M7** 48dp·14sp·실기기
- **R1** Edit 모드 수명주기
- **L4–L12:** 무기 ID, 게이지 100 하드코딩, 기록 매핑·40줄, 효과 대상 후보 필터, EventSystem 재생성, 성능, 속성 아이콘, 공개 조회 API
- 접근성 전반·Run·저장·서버·아트

## Root 확인 요청
- 컴파일. C# 튜플 반환, iterator 안의 로컬 함수, nullable 경고 확인이 필요하다.
- 시험 8개 실행.
- 적 5체에서 상태칩과 의도 두 줄의 가독성 스크린샷.
- 봉인·5칸 시간표 시각 확인.
- 이 결과물은 자체 승인하지 않는다.
