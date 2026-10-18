> 이전 제작 이력: 현재 A 스타일 원본·준비 상태는 [HANDOFF](../HANDOFF.md)와 [STYLE-GUIDE](../STYLE-GUIDE.md)를 따른다. 아래의 과거 파일·수량·검증은 당시 기록이다.

# 하린 캐릭터 모먼트 창작 원본 — draft

## 최신 — round 3 원본 제작 (2026-10-18)

A/B/C 이미지 누락 0, 독립 표정 6/6(512×512 RGBA). 이미지 매핑 21개 / 고유 파일 21개. **draft 유지, Unity 신규 수용 NOT_RUN.**

B의 고양이는 봉제 인형이다. 살아 있는 새끼 고양이처럼 읽힌 걱정 v01은 미채택으로 남기고, 봉제 인형 기준의 걱정 v02를 선택했다. 끝은 맨손으로 벗어 둔 두 장갑을 무릎 위에 포갠 실제 `fig_rest`다. C는 전방 방패→옆길 열기와 열린 손을 구별한다.

전체 대사·연기 톤·hold·모션·gaze·input·intent는 baseline 그대로다. 표정은 아직 별도 대화 portrait 슬롯에 연결하지 않았다. 현재 파일 목록·해시는 [선택 자산](../production-round3-20261018/selected-assets.json), 검사/한계는 [HANDOFF](../HANDOFF.md)를 따른다. 아래 최초·2차 패스의 제작 이력과 관절/음성 미완료 설명은 유지하되 현재 ready 수량은 이 절이 우선한다.

## Unity 시각 리뷰 보정 — 2026-10-18 (finding 4)

A/B/C 세 장면의 표시 내레이션을 문장별로 점검했다. `speaker: null`의 `line.text`도 플레이어에게 표시되므로 행동·감정의 산문만 둔다. 아래는 본문에서 옮긴 **비표시 제작 지시**이며 박자 번호는 1부터 센다. 발화 대사·나이·의상·장면 의미·`hold`·입력·모션·`line.tone`은 바꾸지 않았다. 기존 `intent`와 이 문서로 박자별 의도를 보존하므로 새 형식 필드는 필요 없다.

| 장면 / 박자 | 비표시 제작 지시 |
|---|---|
| `harin_lobby_01` / 3 | 인형을 손으로 덮었다가 급히 넣는 핵심 포즈로 전환한다. 눈은 공방장을 피하고 손을 뗀 뒤 낮게 헛기침한다. |
| `harin_lobby_01` / 5 | 몸을 숙여 어려 보이게 하지 않는다. |
| `harin_bond_01` / 33 | 문 옆 방패와 테이블 위 고양이가 같은 화면에 들어온다. |
| `harin_victory_01` / 1 | 방패로 전방을 가린 자세에서 시작한다. 마지막 충격 뒤 발을 고정하고 어깨의 힘을 먼저 푼다. |
| `harin_victory_01` / 3 | 열린 손은 잡아끄는 자세가 아니라, 필요하면 잡을 수 있도록 허리 높이에서 멈춘다. |
| `harin_victory_01` / 5 | 어깨와 허리가 드러나는 기본 갑주 실루엣, 탄탄한 팔과 긴 망토가 전신 구도에서 함께 읽힌다. |

새 Unity 재생은 NOT_RUN이다. 비표시 지시의 문서화는 해당 동작의 리그·중간 프레임·음성이 구현되었다는 뜻이 아니다.

## 이미지 준비 상태 — 3차 패스 갱신 — 2026-10-18

A/B/C가 선언한 그림과 배경이 모두 존재한다. `idle_figure: fig_lobby_soft`. 현재 이미지 매핑 21개, 고유 PNG 21개(512×512 독립 표정 6개 포함)가 실제 파일을 가리킨다. 모두 창작 draft이며 Unity 신규 재생 수용·사용자 승인은 별도다.

Codex 내장 구독 이미지 도구로 기준 전신과 행동별 투명 PNG를 제작했다. 공방 배경은 기존 프로젝트 `bg_zone1_event_workshop_v02.png`의 바이트 복사본이다. 얼굴·손 앵커는 선택 그림을 보고 정규화 좌표를 보정했다. 좌표는 자체 시각 측정이며 Unity 카메라 수용 증거가 아니다.

B/C를 포함한 모든 장면 이미지가 준비되었다. 대사·hold·입력·의도는 그대로다. 포즈 사이 소품·가구·장식의 미세한 생성 변동과 연속 동작의 빈칸은 남아 있다. 현재 파일/ready 검증은 상위 VALIDATION.json의 round3, 프롬프트·참조·해시는 ../production-round3-20261018/records/를 따른다.


요구 기준: `art/requests/character-moments-20261018.md`, `docs/MOMENTS_FORMAT.md`, `docs/AI_WORKFLOW.md` §18, `OWNERSHIP.md`, `docs/design_v0.8.1.md` §9.3·§15.3, `content/character_notes.mjs`, `art/briefs/characters_v0.8.json`·`prompts_v0.8.1.json`.

현재 26세 고정. **사용자 승인 전 창작 초안**이며 게임 코드·Unity·정사·기존 대사를 직접 교체하지 않는다. 이번 담당 파일은 이 폴더의 `moments.json`, `README.md`뿐이다.

180cm 탄탄한 성인 체형, 짧은 짙은 남색 머리·회청색 눈·왼쪽 눈썹 위 흉터. 어깨와 허리선이 드러나는 서리빛 경량 갑주, 한쪽 긴 망토, 하이웨이스트 전투 바지, 전개형 대형 방패. 몸집을 줄이거나 귀여운 취향을 유아화로 바꾸지 않는다.

## 최초 패스 기록 / 2차 패스로 갱신된 항목은 위 상태 우선

A/B/C 세 장면의 대사·지문·시선·카메라·음성 디렉션과 슬롯 키프레임 4개를 유지했다. 이번 2차 패스는 A의 기준 전신·핵심 손동작·표정과 배경을 실제 PNG로 채웠다. `ready` 모션은 슬롯 이동 데이터이며 관절 리그 완성을 뜻하지 않는다. 외부 API·Claude 호출 없이 내장 구독 이미지 도구만 사용했다.

현재 B는 `line.text`의 Python `len()` 합 **2,603자**, **33박자**다. 공백·문장부호를 포함하고 title·tone·JSON 구문은 제외한다. 기존 제목 아래 시작·전개·관계 변화·마무리가 있는 대본 전체이며 시놉시스가 아니다. 플레이어 답변은 speaker=null 지문으로만 다룬다. 비용·보상·친밀도 수치·새 해금 규칙은 없다.

## 장면 지시

- **A — 확인해도 되는 주머니**: 방패를 세운 선 자세 → ① 장갑 손목끈 정리 → ② 주머니 인형 귀를 숨기기 → ③ 헛기침 뒤 숨긴 행동을 취소하고 귀를 다시 내보이기 → 주머니를 가리지 않는 선 자세. 보아도 좋은 상대를 스스로 고른다는 친밀감이다.
- **B — 귀여운 것에 약한 이유**: 기존 에피소드 2 「귀여운 것에 약한 이유」의 완결된 일상 장면. 돌아와 인형을 감추기 → 의자에 앉아 천을 깔고 접힌 귀를 펴기 → 자기 취향과 웃는 얼굴을 보여 주어도 괜찮다는 경험 → 인형을 테이블에 두고 두 장갑을 벗은 편한 자세. 인형의 구입 경위·어린 시절·과거 경계선 사건은 새 정사로 만들지 않는다.
- **C — 방패가 열어 주는 옆자리**: 기본복 승리 연출. 전방을 방패로 막은 자세 → 기존 승리 대사 “경계 유지.” → 방패를 바깥쪽으로 치워 열린 손과 옆길을 보여 주기 → 끌어당기지 않고 옆에 서 달라고 말하기 → 방패를 낮춘 전신 자세. 전투 결과·보상과 분리된 연출이다.

시선·표정: A: 장갑 아래 → 들킨 주머니 옆 → 공방장을 피함 → 자발적 눈맞춤. B: 인형·문 옆 방패 → 칭찬에 아래 → 얼굴을 보고 먼저 피하지 않음. C: 전방 → 동료 안전 확인 → 옆에 서기를 바라는 정면 눈맞춤.

음성: 낮고 단단한 반말. 보고는 짧고 정확하게, 사적인 말은 중간 숨과 짧은 정적으로 망설임을 낸다. B의 칭찬 뒤 음높이를 유아적으로 올리지 않는다. C의 “옆으로”는 명령의 강세를 낮춰 바람으로 들리게 한다. 각 `line.tone`을 함께 따른다. `voice_*` asset key는 녹음 제작 묶음 식별자이며 실제 오디오 경로가 아니다. 녹음 파일은 없으므로 beat.voice를 쓰지 않았다. `speaker:null`은 동작/상황 지문이며 플레이어 배역을 임의로 추가하지 않는다.

카메라: A는 full 시작 → 손을 포함하는 medium → 눈맞춤 bust → medium/full 마무리. B는 medium으로 손·종이를 연결하고 중요한 응답에서 얼굴 bust로 천천히 접근한다. 시작과 끝의 full은 공간 관계를 보여 준다. C는 방패/봉인 포함 full → 얼굴과 손 medium/bust → 다시 full/medium. 모든 카메라 move/duration은 JSON에 있다. 얼굴·손 anchor는 **최초 패스의 제작용 목표 위치**이며 실제 PNG를 보고 확인한 좌표가 아니다. 제작 시 전신 캔버스의 얼굴 `(0.50,0.16)`, 손 `(0.49,0.51)` 기준으로 포즈를 정렬하고 자산이 이 위치를 못 지키면 실제 anchor를 재측정한다. 가슴·트임·허리만 분리 확대하지 않고 기본복 실루엣을 그대로 보존한다.

## 모션 데이터와 세 동작의 차이

- `settle`: 0→0.35→0.8초. figure 전체가 위 5px·회전 −0.35도로 잠깐 반응하고 0으로 복귀한다. 무게를 가라앉히는 무대 보조다.
- `turn_to_player`: 0→0.65→1.25초. figure 전체를 x −20px·회전 −0.7도·scale 1.015까지 옮기고 기본값으로 복귀한다. 실제 목 돌리기는 대응 포즈 그림 전환으로 표현해야 한다.
- `pause_near`: 0→0.55→1.55→2.1초. x −14px·scale 1.02에서 1초 멈추고 복귀한다. 상대 응답을 기다리는 무대 거리다.
- `step_clear`: 0→0.65→1.1→1.6초. x +38px로 옆자리를 열었다가 복귀한다. 발 디딤·옷·머리카락 움직임은 아직 없다.

기준 1920×1080 px, 비반복 `inOut`. 모든 모션은 시작·끝에 x/y/rot=0, scale/alpha=1로 복귀한다. 키프레임은 관절 리그가 아니며 실제 핵심 행동을 대신하지 않는다. A의 세 미세행동은 위 장면 지시와 이미지 표의 핵심 포즈를 각각 따로 그려야 한다. 정지 그림을 이동시킨 결과를 손·팔 동작 완성으로 표기하지 않는다.

## 예정 이미지 전체

아래는 art/ 기준 상대 경로다. 상태는 3차 패스의 실제 파일 기준이다. 임시 기본 시트나 다른 캐릭터를 대신 표시하지 않는다. 현행 기본 시트와 동일한 얼굴·의상을 유지하고 전신은 같은 피벗/투명 여백으로 그린다. 배경과 소품 위치를 포함한 전체 구도는 기본 1920×1080 무대에서 검수한다.

| 키 | 예정 경로 | 상태 | 그림 지시 |
|---|---|---|---|
| `fig_default` | `moments/harin/images/char_harin_stand_neutral_v01.png` | ready | 방패를 문 옆에 세운 기본복 전신, 무표정 |
| `fig_glove` | `moments/harin/images/char_harin_glove_determined_v01.png` | ready | 장갑 손목끈을 정리하는 핵심 포즈, 결의 |
| `fig_lobby_cat` | `moments/harin/images/char_harin_pocket_reveal_soft_v01.png` | ready | 장갑을 낀 채 주머니에서 고양이 귀를 다시 보여 주는 선 포즈, 조심스러운 호의 |
| `fig_guard` | `moments/harin/images/char_harin_shield_guard_determined_v01.png` | ready | 전방을 방패로 막고 발을 고정한 승리 직전 포즈, 결의 |
| `fig_hide` | `moments/harin/images/char_harin_pocket_surprised_v01.png` | ready | 주머니 인형 귀를 손으로 덮는 핵심 포즈, 놀람 |
| `fig_cough` | `moments/harin/images/char_harin_cough_annoyed_v01.png` | ready | 주머니에서 손을 떼고 헛기침하는 핵심 포즈, 짜증 |
| `fig_worry` | `moments/harin/images/char_harin_seated_worried_v02.png` | ready | 주머니 위에서 손을 멈추고 앉은 포즈, 걱정 |
| `fig_cat` | `moments/harin/images/char_harin_cat_offer_happy_v01.png` | ready | 접힌 천 위에 고양이를 앉히는 핵심 포즈, 희미한 즐거움 |
| `fig_soft` | `moments/harin/images/char_harin_seated_happy_v01.png` | ready | 장갑 낀 손을 무릎 위에 두고 눈맞춤하는 앉은 포즈, 서툰 미소 |
| `fig_lobby_soft` | `moments/harin/images/char_harin_stand_soft_v01.png` | ready | 장갑을 낀 채 공방장에게 눈을 돌리는 선 포즈, 서툰 미소 |
| `fig_rest` | `moments/harin/images/char_harin_seated_unguarded_v01.png` | ready | 두 장갑을 벗어 무릎 위에 포갠 앉은 포즈, 편한 미소 |
| `fig_resolve` | `moments/harin/images/char_harin_stand_determined_v01.png` | ready | 다시 곧게 서지만 어깨를 푼 포즈, 결의 |
| `fig_victory` | `moments/harin/images/char_harin_shield_open_relief_v01.png` | ready | 큰 방패를 바깥쪽으로 치우고 손을 내미는 승리 포즈, 안도 |
| `bg_room` | `moments/harin/images/char_harin_background_workshop_evening_v01.png` | ready | 문 옆 방패와 테이블의 고양이를 함께 읽을 수 있는 공방 배경 |
| `bg_victory` | `moments/harin/images/char_harin_background_quiet_afterbattle_v01.png` | ready | 적이나 보상을 표시하지 않는 전투 후 무대 배경 |

§15.3의 독립 512×512 표정 6개는 expr_neutral/expr_happy/expr_annoyed/expr_surprised/expr_worried/expr_determined로 제작·등록했다. 전신 포즈 변형과 별개의 PNG다. 로비 3동작의 연속 관절/프레임·부위분리 PSD·리그는 아직 미완성이다. 소품 단독 레이어도 제작하지 않았다.

## 입력과 보존할 의도

모든 장면은 탭 진행·건너뛰기·다시 보기 가능, cooldown_s=0 초안이다. 빠른 탭은 현재 글을 완성한 뒤 다음 박자로 진행하며 장면을 중첩 시작하지 않는다. B는 기존 제목 선택 이벤트, C는 기존 승리 이벤트에 연결할 표현 데이터로만 두고 해금/보상 조건을 정의하지 않는다. 반복 시작 시 첫 figure·camera·stage로 재설정하고, 생략한 실제 음성/이미지를 재생 성공으로 세지 않는다. UI로 얼굴과 손이 가리면 UI 배치를 조정하며 대사·프레임·기본복을 축약/덮기 하지 않는다.

26세 하린의 강인한 전위 실루엣과 무뚝뚝한 반말, 칭찬과 사적인 초대 앞에서만 망설이는 배려를 보존한다. 180cm 탄탄한 체형·짧은 남색 머리·회청색 눈·왼쪽 눈썹 위 흉터·어깨와 허리선이 드러나는 서리빛 경량 갑주·한쪽 긴 망토·하이웨이스트 전투 바지·대형 방패를 바꾸지 않는다. 귀여운 취향은 유아화 장치가 아니며 상사처럼 명령하거나 일방적인 보호로 관계를 끝내지 않는다. 얼굴·손·방패를 연결하고 노출 부위만 잘라 보여 주지 않는다.

## 검증과 미완료 — 3차 패스

A의 선언 이미지·박자·배경 참조, idle 파일, PNG 디코드·투명 알파, 모션 내부 참조와 JSON 형식을 검사했다. 전체 대사·타이밍·입력·의도는 변경 전 바이트 재구성 해시로 보존을 확인했다. 자체 시각 검수는 상위 `production-20261018/visual-verdict-*.json`, 현재 검사 결과는 상위 `VALIDATION.json`을 따른다.

미완료: 관절 리그·연속 프레임·녹음·PSD·AnimationClip·Timeline·VFX. NOT_RUN: 이번 신규 그림의 Unity 임포트/재생·기기·스킵/연타/대기 복귀·접근성 수용, Claude 리뷰, 게임 코드 시험·빌드. 포즈 컷의 소품 출입·장식 위치 변동과 카메라 앵커는 실제 화면에서 추가 검수가 필요하다.
