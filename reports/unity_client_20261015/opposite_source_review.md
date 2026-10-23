# P1-UNITY-BATTLE-VIEW-REVIEW — 반대 작성자 검토 (Claude Opus, xhigh)

- 대상: `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeBattleClient.cs`(Codex 작성), `MemoryForge.Client.asmdef`, `Resources/MemoryForge/error-messages.json`
- 기준: design v0.8.1 §14.2·§14.3·§14.7, Core `Battle.cs`/`Battle.PlayerActions.cs`/`BattleState.cs` 현재 소스
- 검토 방식: 소스 정독만 했다. **빌드·Unity Play·스크린샷·시험은 실행하지 않았다(NOT_RUN).** 파일 SHA(16d5d82…)는 Bash가 없어 직접 계산하지 못했으므로 Root가 일치를 확인해야 한다.
- 범위: 프리셋 세라·하린·니아 전투 연습 화면. Run·편성·서사·보상·저장·서버·최종 아트는 범위 밖이며 축소로 판단하지 않았다.

## 판정

**조건부 통과.** 연습 화면으로 보여 주는 것은 가능하다. 다만 통합 전에 **H1(오버레이 클릭 관통)** 과 **M1(Render 예외 처리)** 은 고치기를 권한다. 둘 다 수정 범위가 작다.

확인된 정상 동작:
- 실제 Core 경로만 쓴다. `Battle.Create`→`Command("begin"/"play"/"ult"/"end")`(95–96)이고, 요청 버전은 `state.V`다.
- 실패한 명령은 상태를 바꾸지 않는다. Core가 사본을 실행한 뒤 성공했을 때만 반영하기 때문이다(`Battle.cs:126–145`). UI는 성공한 요청만 `history`에 넣는다.
- 가짜 피해·RNG·상태 변경이 없다. 미리보기는 독립 `Battle.Create`에 확정된 요청을 재생한 사본에서 계산하므로 실행 중 전투의 RNG를 소모하지 않는다(113–118).
- 삭제는 자기 자식 캔버스로 한정된다(`transform.Find`, 289). EventSystem도 자기 캔버스 자식으로만 만든다(168). 전역 삭제는 없다.
- 콘텐츠 정의는 복사해서 쓰고 변경하지 않는다(62, 72).
- 프리셋 구성은 원본 `Run.baseKit`과 기본 무기 카드에 맞는다. 공용 카드 `Assigned`, 무기 카드 `Wielder`, HP 243, Power4 160, Vit 81, 난이도 100/100 모두 일치한다.
- §14.2의 2.6초 거절 토스트(99/103)와 비활성 버튼 안 사유 문구(228)가 있다. 화면에 '전투 연습'이 표시된다(147).

## 결함 (심각도순)

| ID | 심각도 | 위치 | 내용 | 제안 |
|---|---|---|---|---|
| H1 | **High** (§14.7 의도치 않은 소비) | 165–166, 158–164, 253–277, `Box` 299 | 오버레이 패널(효과 대상·전투 기록·결과)은 `Box`가 `raycastTarget=false`로 만든다. 그래서 빈 영역을 누르면 아래에 깔린 버튼이 눌린다. 기록 패널(704–1568 × 190–674)은 사용 방식 버튼(y 620–672, x 816–1336)과 효과 대상 버튼을 덮지만 그 클릭을 막지 못한다. 결과적으로 기록을 보면서 탭하다가 카드가 바로 사용된다. 효과 대상 모달도 하단 빈 영역이 방식 버튼과 겹친다. | 오버레이 루트 이미지에 `raycastTarget=true`를 켜거나 전체 화면 차단 패널을 둔다. 그리고 오버레이가 열린 동안 아래 입력을 잠근다. |
| M1 | Medium | 89–104, 106–128 | `Render()`가 `Execute`의 try 안에서 호출된다(100). 미리보기의 `candidate.Command`가 `RuleException`을 던질 수 있다. 예를 들어 사전 검사(Precheck)는 통과했는데 해결 단계에서 `UNSUPPORTED_EFFECT`/`BATTLE_*_NOT_IMPLEMENTED`가 나는 경우다. 그러면 **이미 확정된 명령**이 `LastError`로 잘못 보고되고, catch 안의 `Render()`(103)가 다시 던져 화면이 절반만 만들어진 채 남는다. | `Preview`에서 `RuleException`을 잡아 사유 문구를 반환하고, `Render`를 try 밖으로 옮긴다. |
| M2 | Medium | 49–72 | `StartPracticeBattle`이 원자적이지 않다. `content`/`encounters`/`setup`을 먼저 교체하고 나서 `Battle.Create`를 호출한다. Create나 `begin`이 실패하면 이전 `battle`과 `history`가 새 `setup`과 섞인다. 그 결과 미리보기 재생이 어긋나고, 헤더에는 다른 조우가 표시된다. 예외가 버튼 핸들러 밖으로 그대로 전파된다. 현재 원본 1–2구역은 생성 가능해서 잠재 결함이다. | 지역 변수로 만들고 begin까지 성공한 뒤에 한 번에 교체한다. 실패하면 토스트를 띄운다. |
| M3 | Medium (정보 가림) | 206–214 | 시간표가 `SlotsBase`만큼만 칸을 그린다. 임시 칸(`SlotsTemp`, `slot` 효과)으로 생긴 예약은 화면에 나오지 않는다. 봉인 칸(`SealedUntil`)은 '빈 칸'으로 보인다. §14.7은 "예약 슬롯을 줄이지 않"도록 요구한다. | 칸 수를 Core 용량 규칙(`ReservationBook.CalculateCapacity`)과 같게 계산하고 봉인 칸을 따로 표시한다. 예약 목록 전체는 항상 표시한다. |
| M4 | Medium (정보 정확도) | 199, 328 | 적 의도가 원본 `d`만 보여 준다. 실제 피해는 난이도·누적 `Strength`·약화가 반영된다(`Battle.cs:610–611`). `def`+`str`나 `buff` 이후에는 표시 값이 실제보다 작다. | Core의 공개 계산을 쓰거나, 같은 규칙으로 표시하고 예상치임을 밝힌다. |
| M5 | Medium (명세 §14.7) | 113–118 | 미리보기가 실제 RNG 상태로 재생된다. 그래서 `tgt:"rand"`나 드로우에 의존하는 결과가 피해 합계로 드러난다. 명세는 "숨겨진 드로우·난수를 노출하지 않는다"고 한다. 소모는 하지 않는다. | 무작위·드로우가 들어간 카드는 범위나 '무작위'로 표시하거나 RNG를 가린 사본에서 계산한다. |
| M6 | Medium (명세 §14.3/§14.7) | 144, 220–231 | 대상이 첫 번째 살아 있는 적으로 자동 지정되고, 방식 버튼을 누르면 바로 확정된다. 선택→방식→대상/미리보기→확정이 분리되지 않았고, 적이 2체 이상일 때 "적을 눌러 대상을 고르세요" 안내가 없다. 첫 탭으로 소비되지는 않는다. | 확정 단계를 추가하고, 대상이 필요한 카드는 적이 1체가 아니면 대상을 직접 고르게 한다. |
| M7 | Medium (Android 수용 전 필수) | 139, 279–286 | 기준 해상도 1600×900을 화면에 맞추면 6.5″ 가로 폰에서 1단위가 약 0.48dp다. 버튼 44–52단위는 약 21–25dp(최소 48dp), 손패 높이 172단위는 약 83dp(최소 120dp), 본문 21–23단위는 약 10–11sp(최소 14sp)가 된다. Editor·PC에서는 문제없지만 §14.2를 만족하지 못한다. | 실기기 단계에서 기준 해상도나 레이아웃 밀도를 다시 잡는다. |
| R1 | 확인 필요 (Edit 모드 수명주기) | 13, 39, 136, 291–292 | `[ExecuteAlways]`라 Edit 모드에서도 씬 오브젝트를 만든다. `DontSaveInEditor`는 루트에만 걸려 있다. `OnDisable` 중에 자식에 `SetActive(false)`/`DestroyImmediate`를 호출한다. 호스트 GameObject를 끄거나 씬을 닫을 때 Unity 오류("…being activated or deactivated")가 나는지, 씬 저장 시 뷰가 직렬화되거나 씬이 dirty가 되는지 실제 Editor에서 확인해야 한다. Play 모드의 비활성/재활성은 Test 4가 확인한다. | Edit 모드 생성을 끄거나 자식 전체에 HideFlags를 건다. 제거는 다음 프레임으로 미룬다. |
| L1 | Low | 318, `error-messages.json` 15–58 | 내부 불변식 코드의 문구가 개발 메모("내부 불변식/데이터 오류(사용자에게는 \"문제가 생겼어요\"…")를 그대로 보여 준다. | 이 유형은 "문제가 생겼어요"와 보고 안내로 바꾼다. |
| L2 | Low | 219, 44 | 토스트가 만료된 뒤에도 `LastError`가 남아 기본 안내문이 위험색으로 표시된다. | 만료할 때 오류 표시 상태도 함께 지운다. |
| L3 | Low | 85–86 | `EffectiveCard`가 try 밖에 있어 알 수 없는 uid면 예외가 그대로 전파된다. 거절된 플레이에도 선택한 `picks`가 지워진다. | try 안으로 옮기고, 성공했을 때만 picks를 지운다. |
| L4 | Low | 66–70 | `BattleActorSetup.WeaponId`를 비워 둔다. 기준 프로브(`balance_probe.mjs:55`)와 Run은 기본 무기 ID를 넣는다. 정련 1의 `w_none` 값과 대체값은 0이라 수치는 같지만 상태(`actors[].weapon`)가 다르다. 무기 선택도 ID 접두어(`w_basic_`)에 의존한다. | `WeaponId = weapon.Id, Refine = 1`로 설정한다. |
| L5 | Low | 181–182, 180 | 게이지 최대값을 100으로 하드코딩했다. | `Rules.GaugeMax`를 쓴다. |
| L6 | Low (명세) | 246–249, 276, 342–358 | 모든 방식이 검사에 실패한 손패 카드가 흐리게 표시되지 않는다. 기록은 12줄뿐이다(명세는 40줄). 주요 이벤트(`dmg`, `repay`, `resolved`, `status`)가 '전투 효과 적용'으로만 나오고, `hit`(적이 파티를 공격)은 방향이 없다. | §14.3 상태표대로 보완한다. |
| L7 | Low (명세) | 253–271 | 효과 대상 모달이 효과와 무관하게 손패·예약·버림 전체를 후보로 보여 준다. 필요할 때 자동으로 열리지 않고, 필살기 선택값은 전달하지 않는다. | 효과의 대상 영역에 맞춰 후보를 거르고 자동으로 띄운다. |
| L8 | Low | 167–168 | 매 `Render`마다 EventSystem을 지웠다 새로 만든다. 씬의 EventSystem이 늦게 켜지면 두 개가 공존할 수 있다. | 한 번만 만들어 캔버스 밖 소유 자식으로 유지한다. |
| L9 | Low (성능) | 113–114, 130–170 | 매 입력마다 UI 전체를 다시 만들고 미리보기 때문에 기록 전체를 다시 재생한다. 긴 전투에서는 비용이 커진다. | 버전별로 결과를 캐시한다. |
| L10 | Low | 57, 322 | 구역 정렬이 문자열 기준이라 "10"이 "2"보다 앞에 온다. 속성 이름을 하드코딩했고 색·아이콘이 없다(§14.2 색 의존 금지). | 원본 순서를 유지하고, 속성 데이터는 별도로 내보낸다. |
| L11 | Low | `MemoryForge.Client.asmdef` | `"Newtonsoft.Json"`을 asmdef 이름 참조로 적었다. 실제로는 미리 컴파일된 DLL이 자동 참조되어 동작한 것으로 보인다. | 필요하면 `precompiledReferences`로 명시한다. 폰트 OFL 사본이 함께 들어가는지(§14.2)도 확인한다. |
| L12 | Low | 16–19 | 대상·검사 결과를 읽을 공개 조회 API가 없다. 그래서 시험은 리플렉션으로 내부 `battle`을 **읽기 전용**으로 사용했다. | 읽기 전용 위임(`Check`/`AllowedModes`/`CostOf`/`Target`)을 추가한다. `Battle` 자체는 노출하지 않는다. |

모든 결함이 기존 범위 안에서 다음 반복으로 처리할 수 있는 항목이다. 전체를 재작성할 필요는 없다.

## 시험 (`client/MemoryForgeUnity/Assets/Tests/PlayMode/BattleClientPlayModeTests.cs`)

namespace는 `MemoryForge.Client.Tests`다. Root가 asmdef에 `MemoryForge.Client`, `MemoryForge.Core`, `UnityEngine.UI`와 Newtonsoft 참조를 연결해야 한다. **전부 NOT_RUN이다.**

1. `ExportedContentLoadsEveryOriginalDefinitionAndRoundTripsExactly`
   - 원본 개수 10/173/22/16/32/20/2와 `SourceCounts`를 확인한다.
   - 원시 엔벨로프(JToken, 중복 키는 오류)와 Newtonsoft 역직렬화 결과를 다시 직렬화한 것을 전체 비교한다. 비교 대상은 공개 필드·null·존재 플래그·배열 순서·사전 키 순서다.
   - Editor에서는 원본 `data/game_data.json`의 SHA, 버전, 정의 ID·이름·핵심 값과 순서, 시작 공용 카드·무기 타입·조우를 대조한다.
2. `DefaultPracticeOpensTheReferenceBattleWithReadableCriticalLabels`
   - 기본 `OnEnable`로 시작한 뒤 1프레임 후 상태를 확인한다: PLAYER, V1, 1턴, HP 243, AP 4(=규칙), 손패 5(=규칙), 세라·하린·니아, 첫 원본 조우, 기본 키트 수와 출처.
   - `ForceUpdateCanvases` 뒤 제목·연습 헤더·HP·턴·AP·캐릭터·적·손패·턴 종료 라벨이 정점을 만들고 잘리지 않았는지 확인한다(`cachedTextGenerator`).
3. `PreviewLeavesTheBattleUntouchedAndPlayCommitsThroughTheCore`
   - 실제 초기 손패에서 dmg/blk만 가진 즉시 사용 가능 카드를 고른다(고정 시드 목록, 비활성 호스트에서 시드를 설정한 뒤 활성화).
   - 미리보기 전후로 상태·RNG·log·ev 지문이 같은지 확인한다.
   - `PlayCard` 후 V+1, 손패에서 빠짐, AP=이전-비용, `Used`+1, play 이벤트, 피해/방어 증가와 미리보기 수치가 일치하는지 확인한다.
   - 이미 사용한 카드를 다시 내면 `NOT_IN_HAND`(또는 종료 시 `WRONG_PHASE`), 필살기는 `ULT_NOT_READY`가 나고 지문이 변하지 않는지, 한국어 토스트가 보이는지 확인한다.
4. `ReservationResolvesOnTheNextTurnAndResetRebuildsOneOwnedView`
   - 실제 초기 손패의 예약 가능 카드로 `next`를 예약한다. AP, 영역, 예약 행, 미해결 상태, 시간표 라벨을 확인한다.
   - 턴 종료 후 다음 턴에서 `resolved(reserved=next)`, `Used`+1, 예약 제거를 확인한다.
   - 다시 시작하면 1프레임 뒤 활성 소유 캔버스가 1개, 활성 EventSystem이 1개(자기 소유)이고 시작 지문이 처음과 같은지 확인한다.
   - 호스트를 비활성/재활성했을 때 소유 뷰가 제거되고 다시 1개가 되며 같은 시작 상태인지 확인한다.

### 시험의 알려진 한계
- 리플렉션으로 private `battle` 필드를 읽기 전용으로만 사용한다(L12).
- 라벨을 찾는 기준은 현재 표시 형식(`"행동력  {AP}"`, `"다음  {카드}"` 등)이다. 문구를 바꾸면 시험도 함께 바꿔야 한다.
- `characterCountVisible`이 공백을 뺀 글자 수 이상이면 잘림이 없다고 판정한다. 시각적 겹침과 화면 밖 위치는 Root의 스크린샷 수용에서 판정한다.
- 시드 탐색 목록(1234, 1–48)에 해당 카드가 없으면 실패로 보고한다. 어느 시드가 채택됐는지는 실제 실행 결과로 확인해야 한다.
- Test 4는 깨끗한 PlayMode 테스트 씬을 전제로 한다. 다른 EventSystem이 있으면 소유 조건이 실패한다.
- H1(클릭 관통), M3(임시 칸), R1(Edit 모드), M7(dp)은 자동 시험에 넣지 않았다. 실제 포인터 레이캐스트와 Editor·실기기에서 확인해야 한다.
