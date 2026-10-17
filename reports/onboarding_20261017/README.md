# 첫 실행 안내(온보딩) — Unity 로컬 연습 탐사 (2026-10-17)

## 목적

처음 하는 사람이 안내만 보고 지도 → 카드 선택 → 즉시/예약 방식 → 적 대상 → 턴 종료 → 보상 → 기억 저장을 이해하게 한다. 원문은 [§14.5 온보딩 순서](../../docs/source_v0.8.1/14_chapter.md)와 [§3.4 첫 7일](../../docs/source_v0.8.1/03_chapter.md)이다. 대상은 오프라인 연습판 `Assets/Scenes/MemoryForgeExpedition.unity`의 `MemoryForgeExpeditionClient`다.

## 바꾼 것

새 파일만 추가했다. 기존 클라이언트·씬·프리팹·수치·버튼 동작은 바꾸지 않았다.

| 파일 | 역할 |
|---|---|
| `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeOnboardingSteps.cs` | 안내 순서·문구·표시/완료 조건(데이터)과 읽기 전용 상태 창구 |
| `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeOnboarding.cs` | 안내 오버레이, PlayerPrefs 기록, 배포 씬 클라이언트에만 붙이는 설치 |
| `client/MemoryForgeUnity/Assets/Tests/PlayMode/OnboardingPlayModeTests.cs` | 새 PlayMode 시험 5개와 보고서 캡처용 명시 실행 시험 1개 |

### 동작 방식

- **붙는 곳:** `RuntimeInitializeOnLoadMethod`와 `SceneManager.sceneLoaded`로 `MemoryForgeExpedition` 씬의 클라이언트에만 컴포넌트를 붙인다. 씬 파일은 수정하지 않는다. 다른 시험들이 만드는 일반 클라이언트에는 붙지 않는다.
- **보이는 것:**
  - 머리줄의 제목·위치 글자 자리(프레임 x 20–920, y 2–74)에 안내 패널 하나를 띄운다.
  - 눌러야 할 조작 주위에 황동색 테두리를 그린다.
  - 패널·테두리·글자는 레이캐스트를 받지 않는다. 받는 것은 안내 자체의 `안내 건너뛰기`·`알겠어요` 버튼뿐이고, 이 버튼들은 머리줄 글자 위에만 있다.
  - 덱·기록·카드 설명·카드 고르기·턴 결과·마지막 행동 결과 같은 덮개 창이 열려 있으면 안내를 숨긴다. 단, 턴 결과 단계는 예외로 그 창 위에 표시한다.
- **넘어가는 기준:** 단계는 그 조작이 실제로 그려져 있고 쓸 수 있을 때만 보인다. 연습판의 공개 상태가 실제로 바뀌었을 때 끝난다.
  - 예약은 시간표에 새 예약 uid가 생겨야 끝난다.
  - 카드 사용은 그 카드가 손패를 떠나고 행동 결과 표시가 열려야 끝난다.
  - 저장은 기억 버전이 올라가야 끝난다.
  - 설명만 있는 단계는 `알겠어요`로 끝난다.
- **기록:** `PlayerPrefs`에 `MemoryForge.Onboarding.v1.done`(끝난 단계 id)과 `.skipped`를 둔다. 계정·서버 연결은 없다. 끝난 단계는 다시 보이지 않는다.
  - 건너뛰기를 누르면 기억 저장 안내를 뺀 모든 단계가 이후 실행에서도 숨는다.
- **레슨 간격:** 레슨 2·3·4는 앞 레슨을 끝낸 전투가 아닌 **다음 전투**에서 시작한다(§14.5의 전투당 한 레슨). 보스 레슨은 레슨 3까지 끝나야 열린다.

## 원문 대비

| §14.5 | 연습판 구현 | 상태 |
|---|---|---|
| ① 프롤로그 1전투: 지금 사용만(행동력·손패) | 첫 전투: `map`(되돌릴 수 없음 1회 안내, §14.4) → `battle.hand` → `battle.mode`(즉시) → `battle.target`(대상이 필요할 때) → `battle.confirm` → `battle.endturn` → `battle.summary` | 구현 |
| ② 2전투: 다음 턴 예약 + 적 의도 2개 | 다음 전투: `battle.intent`(이번/다음) → `reserve.next` → `reserve.confirm`(실제 예약) → `reserve.timeline`(턴 종료로 해결) | 구현 |
| ③ 3전투: 약점·균열 + 필살기 | 그다음 전투: `battle.break`(약점 속성·균열 0이면 다음 행동 취소·받는 피해 +25%, §5.6) → `battle.ult`(게이지가 찼을 때) | 구현(필살기는 게이지가 찰 때까지 대기) |
| ④ 보스: 다다음 턴 예약 + 균열 타이밍 | 보스전: `boss.timing`(균열 다음 턴 시작에 해결되게, §5.6 설계 의도) → `boss.after`(실제 다다음 턴 예약) | 구현. **"실패해도 승리 가능한 수치"는 만들지 않음**(연습판 조우·수치 그대로) |
| ⑤ 첫 기억 저장(용량 6 중 3 안내), 니아·하린·리엔 지급 | `save`: 남길 변화·캐릭터별 결과 용량·저장 버튼 안내, **건너뛰기 버튼 없음**. 실제 용량 숫자는 저장 화면의 각 열(`결과 용량 a/b`)이 그대로 보여 준다 | 안내 구현. 캐릭터 지급은 미구현(승인 경계·계정 없음) |
| ⑥ 초심자 모집 10회(모집권 지급) → 2장 공방(변주)·계약, 3장 전 주간 보스 | `forge`: 첫 공방에서 변주·제거·떠나기 안내(연습판은 1구역에 공방이 있음) | 공방 소개만. 모집·모집권·계약·주간 보스는 연습판에 없음 → 미구현 |
| 각 단계 "설명 1문장 + 직접 해 보기", 3줄 이하, 건너뛰기 허용, 저장 안내는 건너뛸 수 없음 | 모든 문구를 실제 글꼴(IBM Plex Sans KR 17)·패널 폭으로 3줄 이하 시험. 저장 단계만 `Required` | 구현 |

§3.4 1일 행의 "튜토리얼 전투 3회 + 보스, 첫 기억 저장"이 위 ①–⑤에 대응한다. 지급(결정 1,600·상시권·니아/하린/리엔), 초심자 모집 10회×5, 2~7일 해금·계정 레벨, D1/D7·튜토리얼 완료율 측정은 이번 범위 밖이다.

### 설계 판단

- **고정 튜토리얼 조우를 만들지 않았다.** 원문은 프롤로그 전용 전투를 가정하지만, 연습판은 실제 생성 지도·조우를 쓴다. 조우·수치를 바꾸면 "수치는 바꾸지 않는다"를 어긴다. 그래서 같은 순서의 레슨을 연습판의 1·2·3번째 전투와 보스에 붙였다.
- **"저장하지 않고 닫기"는 그대로 둔다.** 기존 버튼 의미를 바꾸지 않는다. 저장 안내에는 건너뛰기 버튼이 없다. 안내 문구는 닫으면 이번 변화가 남지 않는다고 알린다. 실제 저장으로 끝날 때까지 다음 저장 화면마다 다시 나온다. 강제 저장으로 해석하지 않았다.
- **이미 한 조작은 인정한다.** 안내가 뜨기 전에 플레이어가 그 조작을 이미 했으면(예: 의도 설명을 읽는 동안 `다음 턴` 선택), 그 단계는 보이지 않고 끝난 것으로 기록된다.

## 시험 (이 worktree의 별도 batchmode Unity 6000.0.46f1)

메인 Unity Editor(`/Users/choi/Documents/Memory_forge/client/MemoryForgeUnity`)는 열지도, 쓰지도 않았다. Editor 잠금도 잡지 않았다.

| 실행 | 결과 |
|---|---|
| 새 시험 `OnboardingPlayModeTests` 5개 | PASS 5/5 (명시 실행 캡처 시험 1개는 일반 실행에서 Skipped) |
| 전체 PlayMode | 322개 중 PASS 320, Skipped 1(명시 실행 캡처), **FAIL 1(기존, 아래)** |
| 전체 EditMode | PASS 3/3 |
| `node tools/gen_run_create_vectors.mjs --check` | PASS (fixture drift 없음) |

새 시험 내용. 모든 누름은 실제 EventSystem 레이캐스트 클릭이고, 그 지점의 맨 위 대상이 해당 버튼인지 확인한다. 즉 안내가 버튼을 가리지 않는지도 함께 본다.

1. `FirstRunCoachLeadsTheFirstBattleThroughTheRealButtonsAndOpensLessonTwoInTheNextBattle`
   - 첫 실행 지도 단계가 갈 수 있는 칸만 테두리로 표시하는지 본다.
   - 안내가 가리키는 버튼만 눌러 레슨 1을 원문 순서로 마친다.
   - 그 뒤 배운 반복으로 첫 전투를 **승리**하고, 보상 단계까지 간다.
   - 기록 저장을 확인한다.
   - 다음 전투에서 레슨 2가 열리는지 본다.
   - 예약 확정 단계에서 일부러 턴을 끝내면 그 단계가 끝나지 않고 다음 턴에 다시 안내되는지 본다.
   - 안내대로 실제 예약 → 턴 종료 해결까지 레슨 2를 끝낸다.
2. `SkipHidesTheGuideForGoodButTheMemorySaveGuideStillShowsAndCannotBeSkipped`
   - 건너뛰기 뒤에는 아무 단계도 보이지 않는다.
   - 패배 저장 화면에서는 저장 안내만 건너뛰기 버튼 없이 나온다.
   - `저장하지 않고 닫기` 뒤에는 미완료로 남고, 다음 저장 화면에서 다시 나온다.
   - 실제 저장(버전 0→1)으로 끝난다.
3. `CompletedStepsStayHiddenOnTheNextLaunchAndANewRecordStartsOver`
   - 같은 기록으로 다시 켜면 끝난 지도 단계는 숨고, 끝나지 않은 단계는 보인다.
   - 비활성화/재활성화 뒤에도 같은 단계가 다시 그려진다.
   - 새 기록은 처음부터 시작한다.
4. `EveryStepTextFitsThreeLinesOfTheCoachAsPlainSentences`
   - 모든 문구와 대체 문구가 실제 글꼴·폭에서 3줄 이하인지 본다.
   - 저장 단계만 건너뛸 수 없는지, 선행 단계 순서가 맞는지 본다.
5. `OnlyTheShippedSceneClientGetsTheCoach`
   - 일반 클라이언트에는 붙지 않는다.
   - 배포 씬을 additive로 불러오면 그 씬의 클라이언트에 하나만 붙는다.

### 새 시험과 무관한 기존 실패 1건

`PracticeServerSessionPlayModeTests.CardDetailsShowTheCurrentServerTextWhollyScrollableReadOnlyAndCloseWhenStale`가 batchmode에서 `the label is as tall as its wrapped text — Expected ≥ 1508.05, But was 1482.0`으로 실패한다. 새 파일 3개를 빼고 같은 시험만 돌린 **기준 실행에서도 같은 값으로 실패**했다. 이번 변경과 무관한 batchmode 환경(화면 크기·글자 배치) 차이로 보고, 고치지 않고 루트에 넘긴다.

## 독립 리뷰 (Codex, 읽기 전용)

`command codex exec -s read-only -m gpt-6.1-sol -c model_reasoning_effort=high`. Codex CLI 0.162.1, ChatGPT 로그인을 확인했다. 요청서와 2차 변경 요약은 [`ops/ai/tasks/claude-onboarding-review-20261017.md`](../../ops/ai/tasks/claude-onboarding-review-20261017.md)에 있다.

- **1차** — thread `01a14a69-23d8-7422-a519-0470e4d2c1af`, 종료 0, **CHANGES_REQUESTED**.
  - P1: 예약 확정이 턴 종료만으로 끝남.
  - P2: 먼저 한 조작 때문에 다음 단계가 사라짐.
  - P2: 두 번째 필살기 사용을 인식하지 못함.
  - P2: 저장 안내를 닫기로 우회할 수 있음.
  - P2: 보스 레슨이 레슨 2·3 전에 열리고, 방식 선택만으로 끝남.
  - P2: 시험 실패 시 additive 씬이 남음.
  - 확인된 것: 레이캐스트·패널 겹침 없음, 규칙 문구가 원문과 일치, 새 지급·수치 없음.
  - 6건 모두 수정했다(위 "동작 방식"·"설계 판단"과 시험 1·2·3).
- **2차** — thread `01a14a74-1d7b-7192-9a0c-aa04ef18ff6a`, 종료 0, **CHANGES_REQUESTED**(P2 3건): 확인 단계에서 카드를 바꾸면 사용을 인정하지 않음, 다음 턴 레슨이 다다음 예약으로 끝남, 필살기가 레슨 3 경계에 없음. 세 건 모두 수정했다(새 행동 표시 인스턴스로 판정, `next` 예약만 인정, `battle.ult`를 Core로, 보스 레슨은 보스전 안에서 레슨 3 뒤에 이어질 수 있음).
- **3차 리뷰는 아직 받지 않았다.**

## 현재 상태 (WIP — 사용 한도로 중단)

- 2차 수정 뒤 시험 1을 탐사 끝(레슨 3·4·저장)까지 늘렸다. 원래 수치에서 레슨 3(균열·필살기)과 첫 저장까지 실제 버튼으로 도달함을 확인했다(1회 실행).
- 마지막 실행에서 시험 1이 보스 구간 어딘가의 `Click`에서 "레이캐스트 결과 없음"으로 **FAIL**이다. 나머지 새 시험 4개는 PASS다. 원인은 아직 보지 못했다(드라이버 클릭 시점 또는 보스 단계 강조 대상 추정).
- 위 "시험" 표의 전체 PlayMode 320/322·EditMode 3/3은 2차 수정 **전** 실행 결과다.

## 스크린샷 (1600×900, 시험 안에서 카메라로 그린 실제 화면)

| 파일 | 단계 |
|---|---|
| `01_map.png` | 지도: 갈 수 있는 칸 테두리 |
| `02_battle.hand.png` | 손패·행동력 |
| `03_battle.mode.png` | 즉시 방식 |
| `04_battle.confirm.png` | 사용 확정 |
| `05_battle.endturn.png` | 턴 종료 |
| `06_battle.summary.png` | 턴 결과 위 안내 |
| `07_battle.target.png` | 적 대상(두 번째 턴, 대상이 필요한 카드) |
| `08_reward.png` | 보상 |
| `09_battle.intent.png` | 두 번째 전투: 이번/다음 의도(알겠어요 버튼) |
| `10_save.png` | 기억 저장 안내(건너뛰기 버튼 없음, 용량 열 테두리) |

캡처는 `MF_ONBOARDING_SHOTS`를 지정하고 `CaptureCoachScreens`를 이름으로 실행해 다시 만든다. 이 캡처는 batchmode 렌더이고, macOS 빌드·실기기 화면 확인을 대신하지 않는다.

## 남은 범위

- 사람이 처음부터 직접 해 보는 첫 플레이 시험. 이번 시험은 안내를 따르는 자동 드라이버다.
- 실기기(Android)에서 48dp 터치와 안내 패널 가독성 확인.
- 고정 프롤로그 조우와 "실패해도 승리 가능한 수치". 새 수치라 설계 승인이 필요하다.
- 캐릭터·모집권·결정 지급, 초심자 모집, 계약·주간 보스 소개. 승인 경계이고 연습판에는 없는 기능이다.
- 안내 기록의 계정·서버 저장. 지금은 기기 로컬 PlayerPrefs다.
- 레슨 3·4(균열·필살기·보스)는 실제 전투 경로에서 자동 시험하지 않았다. 문구·순서·완료 조건은 정적 시험과 리뷰로만 확인했다.
- 기존 batchmode 실패 1건(위).

## 실행 증거

- 세션: Claude Opus 5.5(`claude-opus-5-5`). 루트 세션이 연 하위 세션이고, worktree는 `.claude/worktrees/onboarding-c82499`, 브랜치는 `claude/onboarding-c82499`, session_id는 `9bebcfcd-9239-45f2-ad7e-854deb8b63f9`다.
- `python3 tools/ai/audit_root_session.py --start-check`: PASS.
- 커밋 전 감사: [`root_session_audit.json`](root_session_audit.json).
- batchmode 실행 때 Unity가 `ProjectSettings/ProjectSettings.asset`·`ShaderGraphSettings.asset`를 다시 직렬화했다. 이 차이는 되돌렸고 커밋하지 않았다.
