# P1-CARD-FLOW-TURN-BARRIER — 작성자 보고 (source only)

- 작성: Claude Opus 5.5(`claude-opus-5-5`), 요청 노력 xhigh, 2026-10-16. 실제 적용 노력과 session_id는 Codex 회수 메타데이터를 따른다.
- 변경 파일(허용 4개만):
  - `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs`
  - `client/MemoryForgeUnity/Assets/Tests/PlayMode/ExpeditionClientPlayModeTests.cs`
  - `client/MemoryForgeUnity/Assets/Tests/PlayMode/ExpeditionTurnFlowPlayModeTests.cs`
  - 이 보고서.
  - Digest helper·Core·콘텐츠·씬·설정·참고 자료는 바꾸지 않았다.
- **검증: 컴파일·Unity Edit/PlayMode·스크린샷·빌드 모두 NOT_RUN**(셸·Editor 없음). 정적 대조만 했다.
  - Core `Emit` 지점과 `BattleTurnDigest` 문구, 기존 시험 helper, uGUI 좌표를 대조했다.
  - 화면 좌표: 턴 종료(1360,696,208×180), 지도로(1288,822,280×56), 저장하지 않고 닫기(1310,816,258×62), 기억 저장(978,816,316×62).

## 공개 API (MemoryForgeExpeditionClient)

- `bool HasTurnSummary`: 실제 `end` 성공 뒤 요약이 열려 있으면 참.
- `bool TurnSummaryFullyShown`, `IReadOnlyList<BattleDigestEntry> TurnSummaryRows`: 열린 요약의 불변 행. 닫혀 있으면 빈 목록.
- `IReadOnlyList<string> BattleLog`: 최근 전투 사건 행(최대 40개)의 사본.
- `bool SkipTurnSummary()`: 남은 행을 모두 표시한다. 라벨만 제자리 갱신하고 Run을 호출하지 않는다.
- `bool ContinueTurnSummary()`: 요약을 닫고 다시 그린다. Run 호출 없음.
- 거절 코드 `TURN_SUMMARY_OPEN`, 문구 "턴 결과를 확인하는 중이에요. '계속'을 누르면 다음 행동을 할 수 있어요."

## 동작

- **요약 생성:** `SendBattle`이 명령 전 `run.State.Battle`에서 공개 필드만 투영하고, `Run.BattleCommand` 반환값(`BattleResponse`)을 보관한 뒤 명령 후 투영으로 `BattleTurnDigest.Build`를 부른다.
  - 투영 필드: 턴·phase·HP·방어·AP, 적 uid·이름·HP·방어·생존과 화면 "이번" 줄과 같은 `IntentText`, 시간 예약 uid·이름·due·kind, 손·예약 카드 이름.
  - `kind=="end"`면 요약을 연다. 승리·패배로 끝나는 `end`도 포함한다.
  - 전투가 Run에서 사라지면 after는 `{Phase=response.Phase, Hp=Run.Hp}`뿐이다. 턴·방어·AP·적은 null로 둬 지어내지 않는다.
  - 뽑을 더미 순서·RNG는 읽지 않는다.
- **사건 uid 이름:** `play/resolved/echoCopy`는 그 이벤트의 `Id`로 정적 카드 목록에서 이름을 붙인다. 그 밖의 uid는 살아 있는 전투의 공개 구역(손·버림·소멸·예약)에 있을 때만 붙이고, 아니면 "카드"로 둔다.
- **표시:** 전면 차단막 위에 패널(160,60,1280×830)을 띄운다.
  - 내용: 제목 "턴 결과", 연습 세션 확정 결과·임시 텍스트 표시 안내, 스크롤 본문, 상태 줄, 버튼 2개.
  - 본문은 사건 행 앞에 "· "를 붙이고 요약·직전 예고·결과 행은 그대로 둔다.
  - 버튼은 `SkipTurnSummary` "남은 결과 모두 보기"(프레임 200–540×820–880)와 `ContinueTurnSummary` "계속"(560–940×820–880)이다. 턴 종료·지도로·저장 버튼과 겹치지 않는 위치를 골랐다.
- **간격:** 원문 §14.3 승인값만 쓴다. `IsReservedResolution` 행마다 직전 표시 뒤 0.3초를 기다리고, 다른 행은 바로 보인다(Update에서 라벨만 제자리 갱신).
  - 예제의 250/180ms·K2·탭 쿨다운은 넣지 않았다.
  - 상태 줄: 진행 중 "예약 해결을 차례로 보여 주는 중이에요 (n/m) …", 완료 "모두 표시했어요. '계속'을 누르면 다음 행동을 할 수 있어요."

## 입력 게이트 범위

요약이 열린 동안 아래 공개 진입점은 Run에 닿기 전에 거절된다. `LastError=TURN_SUMMARY_OPEN`이고 다시 그리지 않으므로 눌린 버튼은 유지된다.

- 지도·전투: `EnterNode`, `SelectCard/SelectMode/SelectEnemy`, `ConfirmCard`, `PlayCard`, `EndTurn`, `UseUltimate`, `PickCard`, `AutoPick`, `CancelPick`.
- 보상·서비스(공통 `Do` 게이트): `TakeCard/SkipCard/TakeRelic/TakePart/Evolve/Proceed/Rest*/Forge*/Buy*/ChooseEvent/Archive/ApplyFoundPart`.
- 저장·출발: `Assign`, `Abandon`, `ToggleChange`, `CommitMemory`(요청 번호 증가 전), `CloseWithoutSave`, `Depart`, `StartPractice`.
- 요약을 닫고 명령을 대신 실행하는 경로는 없다(Root 결정).
- 계속 허용: 요약 자체의 Skip/Continue와 표시 전용 읽기(`Show*Info/CloseCardInfo/MemoryChanges/PreviewSelection/PlayForecast/Forecast*`).
- **공통 버튼 콜백:** `IsActive() && IsInteractable()`일 때만 실행한다. 이전 Render의 비활성 버튼과 직접 `onClick.Invoke`를 차단한다. 알림 만료가 눌린 버튼을 유지하는 기존 수정은 그대로다.
- **Picker:** 전투 명령(play/ult/end)이 성공하면 명령 전에 준비한 picker를 지운다. Core의 pick fallback은 바꾸지 않았다.

## 수명·복구

- `OnDisable`: 화면과 요약·타이머만 지운다. `OnEnable`은 실제 Run을 그대로 다시 그린다.
- `OnApplicationPause(false)`/`OnApplicationFocus(true)` → `ResumeView`:
  - 카드나 PLAYER phase가 사라진 picker만 지운다.
  - 그 경우나 화면이 없을 때만 다시 그린다.
  - 열린 요약은 오래된 상태가 아니므로 유지한다.
  - Run 명령·재시작·서버 조회는 없다.
- S2 계산 오류 fail-closed는 이번 단위에 넣지 않았다.

## 전투 기록

- **채우는 방식:** 성공한 모든 전투 명령의 digest에서 **사건 행만** `battleLog`에 넣는다. 요약·직전 예고·결과 행은 넣지 않는다. 최근 40개만 남긴다.
  - `EnterNode`로 전투에 들어가면 그 전투의 begin 이벤트도 넣는다.
- **수명:** 전투가 끝난 보상·저장·결과 화면에서도 유지하고, `Depart`(새 연습 포함)에서만 비운다.
- **기록 시트:** 스크롤로 바꿨다. 순서는 "탐사 진행"(Run 로그 14줄)과 "전투 기록 · 최근 n개 (최대 40개)"다. `CloseLog` 이름은 유지했다. 쓰이지 않게 된 `EventText`는 제거했다.

## 기존 시험 수정 (ExpeditionClientPlayModeTests)

- `BattleStep`의 실제 `end`가 성공하면 새 `DismissTurnSummary`가 요약을 명시적으로 닫는다. 순서: Skip → `TurnSummaryFullyShown` → Continue, 그리고 Run 지문 불변 확인.
- 이를 거치는 시험: `ActualCommandsReach…`, `DrawCardForecast…`, `PickSheetForecasts…`, `RewardShopAndDeck…`(DriveUntil/Step).
- 기존 상태·문구·raycast·지문 판정은 하나도 바꾸지 않았다.

## 새 시험 (ExpeditionTurnFlowPlayModeTests, 9개, NOT_RUN)

모든 시험은 Open/mirror/실제 raycast/지문을 복사한 자체 helper만 쓴다. replay probe는 다음 실제 명령을 고르는 데만 쓴다.

1. 턴 종료 같은 좌표 2회 탭 → `end` 1회. 두 번째 탭은 차단막에 걸린다. 요약 행이 사건 수·직전 예고 수·새 턴 행과 일치한다. 실제 Skip/Continue 클릭 뒤 새 `end`가 받아들여진다.
2. 카드 없이 턴 종료 반복(하단 좌표 2회 탭) → 패배 후 저장 화면 유지. 요약 행:
   - 첫 행 `… · 전투 패배`
   - 다음 행 `파티 체력 X → 0`
   - 적 결과 행 없음, `전투 종료 · 패배` 1회.
   양성 대조로 그 좌표가 `CloseWithoutSave`임을 확인한다. 150회 안에 패배하지 않으면 Inconclusive.
3. 적이 1체·HP 40 이하일 때 replay probe로 승리하는 `end`(단독 또는 다음 턴 예약 뒤)를 찾아 하단 좌표를 2회 탭한다. 보상 pending 해시가 그대로이고, 양성 대조로 그 좌표가 `Proceed`임을 확인한다. 1500명령 안에 못 찾으면 Inconclusive.
4. 요약 중 공개 명령 29개와 선택 5개 → 모두 거절되고 Run·선택·LastRequestId·기억 버전·picker가 그대로다. 턴 종료/손패/적/확정 좌표 탭은 차단막에 걸린다. Continue 뒤 선택은 다시 받아들여진다.
5. 예약 해결 행:
   - 0.3초 단계 표시: 경과 ≥0.3×n, 진행 중 Run 불변, 같은 화면 객체.
   - Skip, disable/enable, focus/pause resume 각 경우에 같은 확정 지문이 나오고 고착 잠금이 없다.
6. 예약 단계 표시 중 계속 버튼을 누르고 있어도 같은 버튼·캔버스·EventSystem이 유지되고, 떼면 정확히 1회 닫힌다.
7. 요약 아래 시간표는 Core `TimelineCells`의 표시와 같다. 다음 턴 카드는 해결되고 다다음 턴 카드만 남으며, 요약 행은 예약을 만들지 않는다.
8. pick sheet를 연 채 실제 `end` → picker가 사라지고 요약 아래에 `BattlePicks`가 없다.
9. 첫 전투 종료 후 기록 = min(40, 실제 이벤트 수). 꾸밈 행이 없고 `전투 종료 · …`가 유지된다. 기록 시트 표시를 확인하고, Abandon/CloseWithoutSave 뒤 유지, Depart 뒤 비움.

## 알려진 공백

- **승리로 끝나는 `play/ult`의 두 번째 탭:** 요약이 없어 보호하지 않는다. 확정·필살기 좌표가 보상 버튼과 겹칠 수 있다. 같은 프레임 멀티터치도 마찬가지다.
- **지도·보상 재배치 연타:** 범위 밖이다. Root가 탭 쿨다운 상수를 금지했다.
- **시험 3·2 도달 여부:** 기준 파티로 실제 도달하는지 확인하지 못했다. 미도달이면 Inconclusive이며 성공으로 세지 않는다.
- **이벤트 범위:** 적 def/buff/summon 등은 Core 이벤트가 없어 직전 예고와 최종 공개 값으로만 보인다. A의 후반 적 이벤트는 digest에서 일반 행이다.
- **표현:** 텍스트 임시 표현이다. 피해 팝업·카드 비행·동작 줄이기 설정 UI가 없다.
- **resume 시험:** `SendMessage`로만 확인한다. 실제 OS pause, Android 실기기, 물리 입력, 새 빌드 실행은 NOT_RUN이다.
- **서버·네트워크 재접속:** 미구현.

## Root 검증

1. **Editor:** Test Runner PlayMode `MemoryForge.Client.Tests.ExpeditionTurnFlowPlayModeTests`, `ExpeditionClientPlayModeTests`, `BattleTurnDigestPlayModeTests`, 그다음 전체 PlayMode/EditMode.
2. **CLI 예:**
   ```
   Unity -batchmode -projectPath client/MemoryForgeUnity -runTests -testPlatform PlayMode -testFilter "MemoryForge.Client.Tests.ExpeditionTurnFlowPlayModeTests" -testResults <path>
   ```
3. **시험 외 확인:**
   - Console 오류·경고.
   - Inconclusive 시험의 사유.
   - 1600×900과 640×360·글자 130%의 요약·기록 시트 스크린샷.
   - Skip/Continue 위치와 대비.
   - 새 macOS 빌드 실행·물리 입력은 별도.
