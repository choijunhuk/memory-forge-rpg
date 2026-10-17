# 하린 캐릭터 모먼트 창작 원본 — draft

요구 기준: `art/requests/character-moments-20261018.md`, `docs/MOMENTS_FORMAT.md`, `docs/AI_WORKFLOW.md` §18, `OWNERSHIP.md`, `docs/design_v0.8.1.md` §9.3·§15.3, `content/character_notes.mjs`, `art/briefs/characters_v0.8.json`·`prompts_v0.8.1.json`.

현재 26세 고정. **사용자 승인 전 창작 초안**이며 게임 코드·Unity·정사·기존 대사를 직접 교체하지 않는다. 이번 담당 파일은 이 폴더의 `moments.json`, `README.md`뿐이다.

180cm 탄탄한 성인 체형, 짧은 짙은 남색 머리·회청색 눈·왼쪽 눈썹 위 흉터. 어깨와 허리선이 드러나는 서리빛 경량 갑주, 한쪽 긴 망토, 하이웨이스트 전투 바지, 전개형 대형 방패. 몸집을 줄이거나 귀여운 취향을 유아화로 바꾸지 않는다.

## 만들어진 것 / 아직 없는 것

실제 생성물은 JSON 원본과 이 제작안뿐이다. A 로비·B 인연·C 승리 3개 장면, 대사·지문·표정/시선·카메라/음성 지시, 슬롯 키프레임 4개를 작성했다. `status: ready`인 모션은 **JSON 슬롯 이동 데이터가 작성됐다는 뜻**이다. PNG, 핵심 동작 그림, 손가락/팔 관절 리그, 프레임 애니메이션, Unity AnimationClip/Timeline, VFX, 녹음, 음악은 만들지 않았다. 일러스트가 없는 지금은 대사·타이밍만 재생할 수 있으며 캐릭터 시각 동작 완성으로 보고하면 안 된다. 이미지 생성 도구·API·Claude를 호출하지 않았다.

현재 B는 `line.text`의 Python `len()` 합 **2,604자**, **33박자**다. 공백·문장부호를 포함하고 title·tone·JSON 구문은 제외한다. 기존 제목 아래 시작·전개·관계 변화·마무리가 있는 대본 전체이며 시놉시스가 아니다. 플레이어 답변은 speaker=null 지문으로만 다룬다. 비용·보상·친밀도 수치·새 해금 규칙은 없다.

## 장면 지시

- **A — 확인해도 되는 주머니**: 방패를 세운 선 자세 → ① 장갑 손목끈 정리 → ② 주머니 인형 귀를 숨기기 → ③ 헛기침 뒤 숨긴 행동을 취소하고 귀를 다시 내보이기 → 주머니를 가리지 않는 선 자세. 보아도 좋은 상대를 스스로 고른다는 친밀감이다.
- **B — 귀여운 것에 약한 이유**: 기존 에피소드 2 「귀여운 것에 약한 이유」의 완결된 일상 장면. 돌아와 인형을 감추기 → 의자에 앉아 천을 깔고 접힌 귀를 펴기 → 자기 취향과 웃는 얼굴을 보여 주어도 괜찮다는 경험 → 인형을 테이블에 두고 두 장갑을 벗은 편한 자세. 인형의 구입 경위·어린 시절·과거 경계선 사건은 새 정사로 만들지 않는다.
- **C — 방패가 열어 주는 옆자리**: 기본복 승리 연출. 전방을 방패로 막은 자세 → 기존 승리 대사 “경계 유지.” → 방패를 바깥쪽으로 치워 열린 손과 옆길을 보여 주기 → 끌어당기지 않고 옆에 서 달라고 말하기 → 방패를 낮춘 전신 자세. 전투 결과·보상과 분리된 연출이다.

시선·표정: A: 장갑 아래 → 들킨 주머니 옆 → 공방장을 피함 → 자발적 눈맞춤. B: 인형·문 옆 방패 → 칭찬에 아래 → 얼굴을 보고 먼저 피하지 않음. C: 전방 → 동료 안전 확인 → 옆에 서기를 바라는 정면 눈맞춤.

음성: 낮고 단단한 반말. 보고는 짧고 정확하게, 사적인 말은 중간 숨과 짧은 정적으로 망설임을 낸다. B의 칭찬 뒤 음높이를 유아적으로 올리지 않는다. C의 “옆으로”는 명령의 강세를 낮춰 바람으로 들리게 한다. 각 `line.tone`을 함께 따른다. `voice_*` asset key는 녹음 제작 묶음 식별자이며 실제 오디오 경로가 아니다. 녹음 파일은 없으므로 beat.voice를 쓰지 않았다. `speaker:null`은 동작/상황 지문이며 플레이어 배역을 임의로 추가하지 않는다.

카메라: A는 full 시작 → 손을 포함하는 medium → 눈맞춤 bust → medium/full 마무리. B는 medium으로 손·종이를 연결하고 중요한 응답에서 얼굴 bust로 천천히 접근한다. 시작과 끝의 full은 공간 관계를 보여 준다. C는 방패/봉인 포함 full → 얼굴과 손 medium/bust → 다시 full/medium. 모든 카메라 move/duration은 JSON에 있다. 얼굴·손 anchor는 **미완성 그림을 위한 목표 위치**이며 실제 PNG를 보고 확인한 좌표가 아니다. 제작 시 전신 캔버스의 얼굴 `(0.50,0.16)`, 손 `(0.49,0.51)` 기준으로 포즈를 정렬하고 자산이 이 위치를 못 지키면 실제 anchor를 재측정한다. 가슴·트임·허리만 분리 확대하지 않고 기본복 실루엣을 그대로 보존한다.

## 모션 데이터와 세 동작의 차이

- `settle`: 0→0.35→0.8초. figure 전체가 위 5px·회전 −0.35도로 잠깐 반응하고 0으로 복귀한다. 무게를 가라앉히는 무대 보조다.
- `turn_to_player`: 0→0.65→1.25초. figure 전체를 x −20px·회전 −0.7도·scale 1.015까지 옮기고 기본값으로 복귀한다. 실제 목 돌리기는 대응 포즈 그림 전환으로 표현해야 한다.
- `pause_near`: 0→0.55→1.55→2.1초. x −14px·scale 1.02에서 1초 멈추고 복귀한다. 상대 응답을 기다리는 무대 거리다.
- `step_clear`: 0→0.65→1.1→1.6초. x +38px로 옆자리를 열었다가 복귀한다. 발 디딤·옷·머리카락 움직임은 아직 없다.

기준 1920×1080 px, 비반복 `inOut`. 모든 모션은 시작·끝에 x/y/rot=0, scale/alpha=1로 복귀한다. 키프레임은 관절 리그가 아니며 실제 핵심 행동을 대신하지 않는다. A의 세 미세행동은 위 장면 지시와 이미지 표의 핵심 포즈를 각각 따로 그려야 한다. 정지 그림을 이동시킨 결과를 손·팔 동작 완성으로 표기하지 않는다.

## 예정 이미지 전체

아래는 모두 art/ 기준 상대 경로, **todo / 파일 없음**이다. 임시 기본 시트나 다른 캐릭터를 대신 표시하지 않는다. 현행 기본 시트와 동일한 얼굴·의상을 유지하고 전신은 같은 피벗/투명 여백으로 그린다. 배경과 소품 위치를 포함한 전체 구도는 기본 1920×1080 무대에서 검수한다.

| 키 | 예정 경로 | 상태 | 그림 지시 |
|---|---|---|---|
| `fig_default` | `moments/harin/images/char_harin_stand_neutral_v01.png` | todo | 방패를 문 옆에 세운 기본복 전신, 무표정 |
| `fig_glove` | `moments/harin/images/char_harin_glove_determined_v01.png` | todo | 장갑 손목끈을 정리하는 핵심 포즈, 결의 |
| `fig_lobby_cat` | `moments/harin/images/char_harin_pocket_reveal_soft_v01.png` | todo | 장갑을 낀 채 주머니에서 고양이 귀를 다시 보여 주는 선 포즈, 조심스러운 호의 |
| `fig_guard` | `moments/harin/images/char_harin_shield_guard_determined_v01.png` | todo | 전방을 방패로 막고 발을 고정한 승리 직전 포즈, 결의 |
| `fig_hide` | `moments/harin/images/char_harin_pocket_surprised_v01.png` | todo | 주머니 인형 귀를 손으로 덮는 핵심 포즈, 놀람 |
| `fig_cough` | `moments/harin/images/char_harin_cough_annoyed_v01.png` | todo | 주머니에서 손을 떼고 헛기침하는 핵심 포즈, 짜증 |
| `fig_worry` | `moments/harin/images/char_harin_seated_worried_v01.png` | todo | 주머니 위에서 손을 멈추고 앉은 포즈, 걱정 |
| `fig_cat` | `moments/harin/images/char_harin_cat_offer_happy_v01.png` | todo | 접힌 천 위에 고양이를 앉히는 핵심 포즈, 희미한 즐거움 |
| `fig_soft` | `moments/harin/images/char_harin_seated_happy_v01.png` | todo | 장갑 낀 손을 무릎 위에 두고 눈맞춤하는 앉은 포즈, 서툰 미소 |
| `fig_lobby_soft` | `moments/harin/images/char_harin_stand_soft_v01.png` | todo | 장갑을 낀 채 공방장에게 눈을 돌리는 선 포즈, 서툰 미소 |
| `fig_rest` | `moments/harin/images/char_harin_seated_unguarded_v01.png` | todo | 두 장갑을 벗어 무릎 위에 포갠 앉은 포즈, 편한 미소 |
| `fig_resolve` | `moments/harin/images/char_harin_stand_determined_v01.png` | todo | 다시 곧게 서지만 어깨를 푼 포즈, 결의 |
| `fig_victory` | `moments/harin/images/char_harin_shield_open_relief_v01.png` | todo | 큰 방패를 바깥쪽으로 치우고 손을 내미는 승리 포즈, 안도 |
| `bg_room` | `moments/harin/images/char_harin_background_workshop_evening_v01.png` | todo | 문 옆 방패와 테이블의 고양이를 함께 읽을 수 있는 공방 배경 |
| `bg_victory` | `moments/harin/images/char_harin_background_quiet_afterbattle_v01.png` | todo | 적이나 보상을 표시하지 않는 전투 후 무대 배경 |

표정 6종 향후 커버리지: 무표정(fig_default), 즐거움(fig_cat/fig_soft), 짜증(fig_cough), 놀람(fig_hide), 걱정(fig_worry), 결의(fig_glove/fig_resolve). 표정은 포즈와 함께 그릴 전신 변형이며 512×512 독립 표정 6장 제작은 향후다. §15.3의 512×512 독립 표정과 로비 3동작·부위분리 원본/리그는 **아직 미완성**이다. 이 목록은 그 제작을 위한 대응표이며 완료 수량이 아니다. 소품은 해당 전신 포즈/배경에 그려 넣는 계획이므로 독립 prop 슬롯 그림을 만들어 놓았다고 주장하지 않는다.

## 입력과 보존할 의도

모든 장면은 탭 진행·건너뛰기·다시 보기 가능, cooldown_s=0 초안이다. 빠른 탭은 현재 글을 완성한 뒤 다음 박자로 진행하며 장면을 중첩 시작하지 않는다. B는 기존 제목 선택 이벤트, C는 기존 승리 이벤트에 연결할 표현 데이터로만 두고 해금/보상 조건을 정의하지 않는다. 반복 시작 시 첫 figure·camera·stage로 재설정하고, 생략한 실제 음성/이미지를 재생 성공으로 세지 않는다. UI로 얼굴과 손이 가리면 UI 배치를 조정하며 대사·프레임·기본복을 축약/덮기 하지 않는다.

26세 하린의 강인한 전위 실루엣과 무뚝뚝한 반말, 칭찬과 사적인 초대 앞에서만 망설이는 배려를 보존한다. 180cm 탄탄한 체형·짧은 남색 머리·회청색 눈·왼쪽 눈썹 위 흉터·어깨와 허리선이 드러나는 서리빛 경량 갑주·한쪽 긴 망토·하이웨이스트 전투 바지·대형 방패를 바꾸지 않는다. 귀여운 취향은 유아화 장치가 아니며 상사처럼 명령하거나 일방적인 보호로 관계를 끝내지 않는다. 얼굴·손·방패를 연결하고 노출 부위만 잘라 보여 주지 않는다.

## 검증 범위

JSON 파싱, 장면 3종/필수 필드, B 글자 수(2,000–3,000), speaker, 이미지·모션 참조, art 상대 경로/파일명, 이미지 todo·모션 ready, 모션 종료값 복귀를 정적 검사한다. 이미지 미생성으로 육안·스크린샷 대조 `$visual-verdict`는 NOT_RUN이며, 비교할 산출물/참조쌍이 없으므로 점수를 만들지 않는다. Unity 임포트·실제 화면·음성·접근성·스킵/반복 작동·사용자 수용은 NOT_RUN이다.
