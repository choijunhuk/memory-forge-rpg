# 카드를 누르고 있으면 카드 주인이 뒤에서 보인다 — 2026-10-19

사용자 요청: "카드 쓸려고 누르고 있으면 카드 주인인 캐릭터가 카제나처럼 뒤태가 보였으면 좋겠어. 뒤가 보이는 모션."

결과:
- **동작(런타임): 완료.** Codex 리뷰 7차에서 APPROVE를 받았고, PlayMode 전체를 통과했다.
- **뒷모습 그림: 아직 없음.** Codex 창작 호출이 이 세션에서 막혔다(§4). 그림이 생길 때까지는 주인의 SD를 실루엣으로 대신 보여 준다.

## 1. 동작

`MemoryForgeExpeditionClient.cs`의 「the card's owner from behind」 절에 있다.

| 입력 | 화면 |
|---|---|
| 손패 카드를 0.2초 누르고 있음 | 카드 주인이 무대 왼쪽 아래에 뒤에서 본 모습으로 0.18초 동안 올라온다. 누르는 동안 1.6초 주기로 숨 쉬듯 움직인다 |
| 카드를 위로 끌어 조준 | 바로 나타난다 |
| 손을 뗌 / 손패 위에 놓음(사용 안 됨) | 0.15초 동안 아래로 내려가며 사라진다 |
| 적·시간표에 놓아 실제 사용 | 앞으로 살짝 밀린 뒤(0.12초) 내려가며 사라진다(0.2초). `_back_act` 그림이 있으면 그 순간 바꿔 보여 준다 |
| 짧은 탭 | 아무것도 나오지 않는다(0.2초 전에 뗌). 카드 선택은 그대로다 |
| 동작 줄이기(`Calm`) | 움직임 없이 바로 나타나고 바로 사라진다 |

화면 위치와 입력 처리:
- **겹침 순서:** 무대(파티·적) 위, 카드 줄·손패·시트 아래에 그린다. 손패 글씨를 가리지 않는다.
- **터치:** 그림은 터치를 받지 않는다(`raycastTarget`·`blocksRaycasts` 끔). 놓을 자리 판정에도 영향이 없다.
- **다시 그리기:** 사용이나 클릭으로 화면을 다시 그려도 그림은 상태(`backWho`·`backLeaving`)로 남는다. 다시 그린 화면에서 이어서 사라진다. 기존 `flight`와 같은 방식이다.
- **두 손가락:**
  - 다른 카드를 조준하면 그 카드의 주인으로 바뀐다.
  - 먼저 누른 손가락을 늦게 떼도 새 카드의 그림은 남는다.
  - 오른쪽 버튼이나 다른 손가락은 잡은 카드를 놓지 않는다.
- **앱을 떠남:** 포커스를 잃거나 일시 정지하면 누르고 있던 카드를 놓은 것으로 처리한다.
- **미구현:** 고르기 시트를 거치는 사용은 취소처럼 사라진다. 시트에서 확정할 때 그림이 다시 나오지는 않는다.

그림 파일:
- `Resources/Art/Characters/char_<id>_back_ready`(`_back_act`)가 있으면 그대로 쓴다.
- 없으면 `char_<id>_sd`를 짙은 실루엣과 청록 테두리로 대신 보여 준다. 코드에 `ponytail:`로 표시했으며, 10명의 그림이 생기면 지운다.

## 2. 시험 (batchmode, `271cb45` 위)

| 시험 | 결과 |
|---|---|
| `HoldingOrAimingAHandCardShowsItsOwnerFromBehind`: 탭·이전 타이머 무효·떼기+클릭·누른 채 다시 그리기·조준 후 취소·실제 사용·동작 줄이기 | PASS |
| `ASecondFingersAimShowsThatCardsOwner`: 두 손가락 조준, 누름 겹침, 조준 취소 후 다른 카드 누름 유지 | PASS |
| `TheHeldOwnerStaysUnderTheHandIgnoresOtherButtonsAndLetsGoOnFocusLoss`: 겹침 순서, 오른쪽 버튼 떼기·끌기, 포커스 상실 | PASS |
| 기존 `DraggingAHandCardOntoAnEnemyPlaysItOnThatEnemy` | PASS |
| PlayMode 전체 | **PASS** 348개 중 344 통과, 실패 0, 건너뜀 4 |
| `CaptureCardHoldBackView`(Explicit, `MF_EXPEDITION_SHOTS`) | PASS. `01_hold_silhouette_fallback.png` |

- 실기기 터치 확인은 NOT_RUN이다. S24 Ultra 연결이 끊겨 있었다.
- 화면 확인은 2340×1080 캡처로 했다(`01_hold_silhouette_fallback.png`). 세라·하린·니아 중 첫 카드(경계 방패)의 주인 실루엣이 카드 줄 아래, 파티 위에 서 있다.

## 3. Codex 읽기 전용 리뷰 (`gpt-6.1-sol`, high)

| 차수 | 세션 | 판정 | 지적과 처리 |
|---|---|---|---|
| 1 | `01a151b1-1f3f-7252-bf48-51b4df0c0fd6` | REQUEST_CHANGES | 사용 후 다시 그리기에서 그림이 사라짐, 동작 줄이기 무시, 오래된 누름 타이머, 시험 부족 → 상태 기반 다시 그리기, `Calm`/`Still`, 타이머 번호, 시험 추가 |
| 2 | `01a151bd-a2a3-7e32-b08c-17dfb568249d` | REQUEST_CHANGES | 두 번째 손가락 조준 때 앞 카드의 주인이 남음 → `backUid`로 그림 주인을 기록하고 바꿈 |
| 3 | `01a151c3-e67a-7fb3-bb79-c4cf835a22ef` | REQUEST_CHANGES | 누름이 겹치면 앞 그림이 남음 → 떼면 항상 자기 그림을 치움, 사용 중 다시 그리기는 `playingUid`로 보호 |
| 4 | `01a151c8-0a92-71c1-96a6-85c562ffa748` | REQUEST_CHANGES | 조준 취소가 다른 카드의 누름을 지움 → 자기 누름만 지움 |
| 5 | `01a151cb-272d-7d90-978f-66dd87c623cd` | REQUEST_CHANGES | 손패 글씨를 가림, 포커스 상실, 오른쪽 버튼 떼기 → 겹침 순서(`stageEnd`), `Leave()`, 포인터 소유 |
| 6 | `01a151d2-b5b0-7941-92dc-4f242c2aaf0a` | REQUEST_CHANGES | 포커스 상실 때 겹친 누름, 오른쪽 버튼 끌기 → 대기 누름과 그림을 따로 정리, 시작·이동·끝 모두 포인터 확인 |
| 7 | `01a151d7-a336-7b31-aedb-ef902bcb8924` | **APPROVE** | 막는 결함 없음 |

- 7차의 선택 의견(같은 카드를 두 번째 손가락이 누르면 첫 손가락 소유가 바뀜)은 채택하지 않았다. 첫 소유를 고정하면, 떼기 신호를 하나 잃었을 때 그 카드가 이후 누름을 무시하게 된다.
- Codex 셸은 겹친 샌드박스 때문에 시작하지 못했다. 시험 실행은 이 세션에서 했고, Codex는 파일을 직접 읽어 검토했다.

## 4. 뒷모습 그림 (Codex 담당)

- 요청서: `art/requests/card-hold-backview-20261019.md`. 세라·하린·니아의 `_back_ready`(선택 `_back_act`), style A, 1024×1536 투명 PNG를 요청했다.
- 모든 캐릭터는 전투 동작 중심, 비성적 연출이다. 니아(17)는 평소 의상, 어깨 높이 카메라다.
- 이 세션에서 `command codex exec -s workspace-write -C art/candidates ...`를 실행했다(세션 `01a151a0-6099-74a1-85e3-48acc613e9a7`). Codex의 셸과 쓰기가 `sandbox_apply: Operation not permitted`(exit 71)로 실패했다.
- 원인: 이 세션의 프로세스는 이미 샌드박스 안에서 돈다. `sandbox-exec`로 직접 시험해도 같은 오류가 났으므로 샌드박스를 겹쳐 걸 수 없다.
- 루트의 메인 checkout에서 요청서 §6의 명령으로 실행하면 된다. 그림이 생기면 Claude가 바이트 그대로 `Resources/Art/Characters/char_<id>_back_ready.png`에 복사한다. 코드는 바꿀 필요가 없다.
