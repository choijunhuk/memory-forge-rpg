# 카드를 누르고 있을 때 카드 주인의 뒷모습 — Codex 창작 요청 (2026-10-19)

요청: Claude 루트. 제작·결정: Codex. 기준: 사용자 지시 2026-10-19, `OWNERSHIP.md`, `docs/AI_WORKFLOW.md` §18.

## 1. 사용자 요청

> 카드 쓰려고 누르고 있으면 카드 주인인 캐릭터가 카제나처럼 뒤태가 보였으면 좋겠어. 뒤가 보이는 모션.

해석:
- 전투에서 손패 카드를 누르거나 위로 끌어 조준하는 동안, 그 카드의 주인 캐릭터가 화면 앞쪽에 **등 뒤에서 본 모습**으로 나타난다. 카메라는 어깨 너머이고, 캐릭터는 화면 안쪽의 적을 향한다.
- 손을 떼서 카드를 쓰면 짧은 동작이 나오고 물러난다.
- 카오스 제로 나이트메어(카제나)는 **연출 방식의 참고**일 뿐이다. 그쪽 그림·구도·의상을 베끼지 않는다.

## 2. 지금 화면 (Claude 조사, `271cb45` 기준)

- 전투 화면은 무대 우선 배치이고, 카드를 위로 끌면 조준했다가 놓을 때 사용한다(`MemoryForgeExpeditionClient.cs`의 `CardDrag`, `BeginCardDrag`·`CardDragTo`·`EndCardDrag`).
- 카드마다 주인(`def.Owner`)이 있고, 지금은 카드 띠에 `char_<id>_cutin` 또는 `char_<id>_sd`만 작게 나온다.
- 연습 전투 파티는 세라(24, 성인)·하린(26, 성인)·니아(17, 청소년)이다.
- 뒷모습 그림은 아직 없다.

## 3. 이번에 만들 것 (최소 범위)

세 명(세라·하린·니아) 각각에 대해 다음을 만든다.

| 파일 (제안 이름) | 내용 |
|---|---|
| `char_<id>_back_ready_v01.png` | 등 뒤 또는 3/4 뒤에서 본 모습. 어깨 너머 구도이고 화면 안쪽의 적을 향한다. 무기·도구는 준비 자세다. 허리 위부터 무릎 위 사이. 투명 배경 RGBA, 기존 style A 전신과 같은 1024×1536 |
| `char_<id>_back_act_v01.png` (선택) | 같은 구도로 카드를 쓰는 순간의 동작 자세. 없으면 ready 한 장으로 재생한다 |

- 얼굴·머리·의상·무기는 현재 승인된 style A(`art/moments/<id>/images/`, 니아는 `art/generated/starter-20261017/`)와 같게 한다.
- **연출 기준(모든 캐릭터 공통):**
  - 전투 동작 중심의 뒷모습이다.
  - 몸매를 강조하는 각도나 의상 변형은 쓰지 않는다.
  - 니아(17)는 청소년이므로 평소 의상 그대로, 어깨 높이 카메라로 그린다.
- 글자·로고·UI는 넣지 않는다.
- 왼손·오른손잡이, 무기 쥐는 손 등 시트에 정해진 설정을 지킨다.

## 4. 모션 (Claude가 재생기로 구현할 기본안, Codex가 바꿀 수 있음)

| 단계 | 기본안 |
|---|---|
| 누름·조준 시작 | 화면 왼쪽 아래에서 위로 0.18초 ease-out으로 올라온다. 약간 커지며 불투명해진다 |
| 누르고 있는 동안 | 1.6초 주기의 작은 호흡(상하 4px 안팎), 조준 방향으로 아주 약하게 기울어진다 |
| 놓아서 사용 | act 그림이 있으면 0.12초 바꿔 보여 준다. 앞으로 짧게 밀린 뒤 0.2초에 걸쳐 내려가며 사라진다 |
| 취소(아래로 놓기 등) | 사용 동작 없이 0.15초에 걸쳐 내려가며 사라진다 |

- Codex가 타이밍·자세 의도를 바꾸려면 산출 폴더의 `README.md`에 적는다. Claude는 그 값대로 재생한다.
- 프레임 애니메이션이 필요하면 `_back_ready_f01..fNN.png`처럼 번호를 붙인다.

## 5. 산출 위치와 기록

- 그림과 제작 기록: `art/candidates/20261019/backview/`
  - `README.md`: 도구, 프롬프트, 참조한 원본 경로, 채택본
  - 해시 목록
- 채택본은 Claude가 바이트 그대로 `client/MemoryForgeUnity/Assets/Resources/Art/Characters/char_<id>_back_ready.png`(와 `_back_act.png`)로 복사해 연결한다. `tools/check_art_copies.py`로 원본 해시와 대조한다.
- 나머지 일곱 명은 이 세 명의 결과를 본 뒤 같은 형식으로 요청한다.

## 6. 진행 상태 (2026-10-19)

- **런타임: 완료.** Claude가 구현했다(`MemoryForgeExpeditionClient.cs`의 「the card's owner from behind」 절과 PlayMode 시험 `HoldingOrAimingAHandCardShowsItsOwnerFromBehind`).
  - 위 파일 이름의 그림이 `Resources/Art/Characters/`에 있으면 그대로 쓴다.
  - 그림이 없으면 그동안 주인의 SD를 어두운 실루엣으로 대신 보여 준다.
- **그림: 미착수.** Codex 호출이 막혔다.
  - 이 요청을 쓴 Claude 세션(Android 16KB worktree에서 파생)에서 `codex exec -s workspace-write -C art/candidates`를 실행했다. Codex 세션은 `01a151a0-6099-74a1-85e3-48acc613e9a7`이다.
  - Codex의 셸·쓰기가 모두 `sandbox_apply: Operation not permitted`(exit 71)로 시작 전에 실패했다. 파일은 하나도 만들어지지 않았다.
  - 원인은 이 세션의 프로세스가 이미 다른 샌드박스 안에서 돌고 있어서, 샌드박스를 겹쳐 걸 수 없다는 점이다. `sandbox-exec`로 직접 시험해도 같은 오류가 났다.
- **루트가 할 일:** 이전 moments 작업처럼 메인 checkout의 루트 세션에서 같은 호출을 실행하면 된다.
  - 산출 폴더를 `art/candidates/20261019/backview`로 미리 만든다.
  - `command codex exec -s workspace-write -C art/candidates/20261019/backview -m gpt-6.1-sol -c model_reasoning_effort=high "<이 요청서 경로를 읽고 따르라>"`
