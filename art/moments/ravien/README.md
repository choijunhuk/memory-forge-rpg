> 이전 제작 이력: 현재 A 스타일 원본·준비 상태는 [HANDOFF](../HANDOFF.md)와 [STYLE-GUIDE](../STYLE-GUIDE.md)를 따른다. 아래의 과거 파일·수량·검증은 당시 기록이다.

# 라비엔 캐릭터 모먼트 창작 원본 — draft

## 최신 — round 3 원본 제작 (2026-10-18)

A/B/C 이미지 누락 0, 독립 표정 6/6(512×512 RGBA). 이미지 매핑 23개 / 고유 파일 22개. **draft 유지, Unity 신규 수용 NOT_RUN.**

A 5·6박자는 손을 내린 `fig_lobby_wait`, 7박자와 idle은 두 손이 비고 닫힌 서류철을 보조 탁자에 둔 `fig_lobby_end`다. 같은 빈 의자를 유지한다. 4·5·7박자는 full/face, 6박자는 기존 bust/face다. B 끝의 `fig_bond_resolve`는 `fig_lobby_end`의 실제 PNG를 재사용해 손이 다시 서류철을 쥐지 않는다. C의 `fig_resolve`만 서류철을 옆구리에 낀다.

전체 대사·연기 톤·hold·모션·gaze·input·intent는 baseline 그대로다. 표정은 아직 별도 대화 portrait 슬롯에 연결하지 않았다. 현재 파일 목록·해시는 [선택 자산](../production-round3-20261018/selected-assets.json), 검사/한계는 [HANDOFF](../HANDOFF.md)를 따른다. 아래 최초·2차 패스의 제작 이력과 관절/음성 미완료 설명은 유지하되 현재 ready 수량은 이 절이 우선한다.

## Unity 시각 리뷰 보정 — 2026-10-18 (finding 4)

A/B/C 세 장면의 표시 내레이션을 문장별로 점검했다. `speaker: null`의 `line.text`도 플레이어에게 표시되므로 행동·감정의 산문만 둔다. 아래는 본문에서 옮긴 **비표시 제작 지시**이며 박자 번호는 1부터 센다. 발화 대사·나이·의상·장면 의미·`hold`·입력·모션·`line.tone`은 바꾸지 않았다. 기존 `intent`와 이 문서로 박자별 의도를 보존하므로 새 형식 필드는 필요 없다.

| 장면 / 박자 | 비표시 제작 지시 |
|---|---|
| `ravien_lobby_01` / 1 | 몸 전체는 거의 움직이지 않고 손과 눈의 순서로 업무를 끝낸다. |
| `ravien_lobby_01` / 3 | 고개가 종이보다 반 박자 늦게 돌아오도록 한다. |
| `ravien_lobby_01` / 5 | 대답을 재촉하지 않고 자기 자리는 그대로 둔다. 4·5박자는 얼굴·손·빈 의자 좌판이 함께 보이는 full/face/none을 유지한다. |
| `ravien_lobby_01` / 7 | 응답을 받아도 업무 동작으로 되돌리지 않는다. |
| `ravien_bond_01` / 17 | 카메라는 얼굴과 손을 함께 두고, 그녀가 말을 고르는 시간을 자르지 않는다. |
| `ravien_bond_01` / 33 | 나란한 두 사람이 문으로 향하는 데서 장면이 닫힌다. |
| `ravien_victory_01` / 5 | 기다리는 눈웃음으로 끝내며 윙크하거나 카메라에 입술을 들이밀지 않는다. |

새 Unity 재생은 NOT_RUN이다. 비표시 지시의 문서화는 해당 동작의 리그·중간 프레임·음성이 구현되었다는 뜻이 아니다.

**의자 권유 구도 — round 2 이후:** 4박자 fig_offer, 5박자 fig_lobby_wait는 모두 full/face/none/0이다. 6박자는 기다리는 fig_lobby_wait의 bust/face, 7박자는 빈 손과 의자·놓인 서류철을 함께 보이는 fig_lobby_end의 full/face/pull_out/0.6이다.

## 이미지 준비 상태 — 3차 패스 갱신 — 2026-10-18

A/B/C가 선언한 그림과 배경이 모두 존재한다. `idle_figure: fig_lobby_end`. 현재 이미지 매핑 23개, 고유 PNG 22개(512×512 독립 표정 6개 포함)가 실제 파일을 가리킨다. 모두 창작 draft이며 Unity 신규 재생 수용·사용자 승인은 별도다.

Codex 내장 구독 이미지 도구로 기준 전신과 행동별 투명 PNG를 제작했다. 공방 배경은 기존 프로젝트 `bg_zone1_event_workshop_v02.png`의 바이트 복사본이다. 얼굴·손 앵커는 선택 그림을 보고 정규화 좌표를 보정했다. 좌표는 자체 시각 측정이며 Unity 카메라 수용 증거가 아니다.

B/C를 포함한 모든 장면 이미지가 준비되었다. 대사·hold·입력·의도는 그대로다. 포즈 사이 소품·가구·장식의 미세한 생성 변동과 연속 동작의 빈칸은 남아 있다. 현재 파일/ready 검증은 상위 VALIDATION.json의 round3, 프롬프트·참조·해시는 ../production-round3-20261018/records/를 따른다.


요구 기준: `art/requests/character-moments-20261018.md`, `docs/MOMENTS_FORMAT.md`, `docs/AI_WORKFLOW.md` §18, `OWNERSHIP.md`, `docs/design_v0.8.1.md` §9.3·§15.3, `content/character_notes.mjs`, `art/briefs/characters_v0.8.json`·`prompts_v0.8.1.json`.

현재 28세 고정. **사용자 승인 전 창작 초안**이며 게임 코드·Unity·정사·기존 대사를 직접 교체하지 않는다. 이번 담당 파일은 이 폴더의 `moments.json`, `README.md`뿐이다.

175cm 성인 체형, 와인빛 긴 웨이브 머리·초록 눈. 깊게 파인 검은 베스트, 트임 펜슬 스커트, 가터 스타킹, 느슨한 붉은 넥타이, 긴 검은 장갑, 황금 저울 펜던트. 외형 대안·나이 대안을 채택하지 않는다.

## 최초 패스 기록 / 2차 패스로 갱신된 항목은 위 상태 우선

A/B/C 세 장면의 대사·지문·시선·카메라·음성 디렉션과 슬롯 키프레임 4개를 유지했다. 이번 2차 패스는 A의 기준 전신·핵심 손동작·표정과 배경을 실제 PNG로 채웠다. `ready` 모션은 슬롯 이동 데이터이며 관절 리그 완성을 뜻하지 않는다. 외부 API·Claude 호출 없이 내장 구독 이미지 도구만 사용했다.

현재 B는 `line.text`의 Python `len()` 합 **2,673자**, **33박자**다. 공백·문장부호를 포함하고 title·tone·JSON 구문은 제외한다. 기존 제목 아래 시작·전개·관계 변화·마무리가 있는 대본 전체이며 시놉시스가 아니다. 플레이어 답변은 speaker=null 지문으로만 다룬다. 비용·보상·친밀도 수치·새 해금 규칙은 없다.

## 장면 지시

- **A — 닫힌 서류철, 비워 둔 옆자리**: 책상 옆 선 자세 → ① 도장을 정확히 한 번 누르기 → ② 종이 끝을 손끝으로 돌리고 눈을 공방장에게 옮기기 → ③ 옆 의자를 가리키고 손을 내린 채 대답 기다리기 → 서류철을 닫은 선 자세. 업무가 끝난 뒤에도 곁에 남아 달라는 사적인 초대를 말한다.
- **B — 서명 없는 약속**: 기존 에피소드 4 「서명 없는 약속」의 완결된 저녁 장면. 빈 종이 앞에 앉기 → 산책을 부탁하면서 조건을 붙이는 버릇을 알아차리기 → 종이를 놓고 같이 있고 싶은 뜻을 직접 말하기 → 문 앞에서 나란히 다음 걸음을 고르기. 과거의 미상환 계약 내용·채권·설정 비밀을 새로 확정하지 않는다.
- **C — 정산 뒤에 남은 시선**: 기본복 승리 연출. 서 있는 계약서·불꽃 봉인 자세 → 인장을 회수하고 기존 승리 대사 “정산 완료.” → 옆자리를 열고 상대를 보려고 돌아봤다고 개인적으로 말하기 → 서류철을 옆구리에 둔 정면 자세. 별도 스킨·보상·능력 효과를 만들지 않는다.

시선·표정: A: 도장 아래 → 종이 → 플레이어 → 옆 의자 → 플레이어. B: 빈 종이와 눈맞춤 사이의 망설임 → 솔직한 대답 뒤 잠깐 아래 → 문 앞에서 나란한 눈맞춤. C: 봉인 → 정면 확인 → 사적인 눈웃음.

음성: 낮고 느긋한 존댓말. A의 마지막 질문과 B의 직접적인 고백은 계산된 미소를 덜어 낸다. 숨소리를 과장하거나 모든 문장 끝에 유혹의 웃음을 붙이지 않는다. C는 일 처리의 자신감에서 사적인 시선으로 넘어간다. 각 `line.tone`을 함께 따른다. `voice_*` asset key는 녹음 제작 묶음 식별자이며 실제 오디오 경로가 아니다. 녹음 파일은 없으므로 beat.voice를 쓰지 않았다. `speaker:null`은 동작/상황 지문이며 플레이어 배역을 임의로 추가하지 않는다.

카메라: A는 full 시작 → 업무 마감 medium → 종이·눈맞춤 bust → 의자 권유 4·5박자 full/face → 손을 내린 기대의 6박자 bust/face → 7박자 medium 마무리. 의자 권유의 full은 얼굴·가리키는 손·빈 좌판을 함께 담으며 권유 내레이션이 끝날 때까지 유지한다. B는 medium으로 손·종이를 연결하고 중요한 응답에서 얼굴 bust로 천천히 접근한다. 시작과 끝의 full은 공간 관계를 보여 준다. C는 방패/봉인 포함 full → 얼굴과 손 medium/bust → 다시 full/medium. 모든 카메라 move/duration은 JSON에 있다. 얼굴·손 anchor는 **최초 패스의 제작용 목표 위치**이며 실제 PNG를 보고 확인한 좌표가 아니다. 제작 시 전신 캔버스의 얼굴 `(0.50,0.16)`, 손 `(0.49,0.51)` 기준으로 포즈를 정렬하고 자산이 이 위치를 못 지키면 실제 anchor를 재측정한다. 가슴·트임·허리만 분리 확대하지 않고 기본복 실루엣을 그대로 보존한다.

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
| `fig_default` | `moments/ravien/images/char_ravien_stand_neutral_v01.png` | ready | 서류철을 든 기본복 전신, 평정 |
| `fig_stamp` | `moments/ravien/images/char_ravien_stamp_focused_v01.png` | ready | 상체를 숙이지 않고 책상 도장을 누르는 핵심 포즈, 결의 |
| `fig_turn` | `moments/ravien/images/char_ravien_paper_turn_playful_v01.png` | ready | 손끝으로 종이 귀를 돌리는 핵심 포즈, 눈웃음 |
| `fig_offer` | `moments/ravien/images/char_ravien_chair_offer_happy_v01.png` | ready | 검은 장갑 손끝이 옆 의자를 가리키는 핵심 포즈, 즐거움 |
| `fig_lobby_soft` | `moments/ravien/images/char_ravien_stand_soft_v01.png` | ready | 옆 의자를 가리킨 뒤 손을 내리고 선 포즈, 기대 |
| `fig_bond_turn` | `moments/ravien/images/char_ravien_seated_playful_v01.png` | ready | 앉은 채 종이 귀를 돌리고 공방장을 보는 포즈, 장난 |
| `fig_waver` | `moments/ravien/images/char_ravien_seated_surprised_v01.png` | ready | 펜을 멈추고 눈썹이 잠깐 올라간 앉은 포즈, 놀람 |
| `fig_soft` | `moments/ravien/images/char_ravien_seated_worried_v01.png` | ready | 종이에서 손을 떼고 질문을 기다리는 포즈, 걱정과 솔직함 |
| `fig_resolve` | `moments/ravien/images/char_ravien_stand_determined_v01.png` | ready | 닫힌 서류철을 옆구리에 두고 나란히 선 포즈, 결의 |
| `fig_annoyed` | `moments/ravien/images/char_ravien_stand_annoyed_v01.png` | ready | 눈썹만 좁히고 입을 누른 기본복 전신, 짜증 |
| `fig_seal` | `moments/ravien/images/char_ravien_battle_seal_determined_v01.png` | ready | 세운 계약서 위 불꽃 봉인에서 인장을 회수하는 선 전투 포즈, 결의 |
| `fig_victory` | `moments/ravien/images/char_ravien_seal_return_relaxed_v01.png` | ready | 인장을 회수하고 공방장 쪽으로 돌아선 승리 포즈, 여유 |
| `bg_room` | `moments/ravien/images/char_ravien_background_workshop_evening_v01.png` | ready | 책상·옆 의자·문이 함께 읽히는 공방 저녁 배경 |
| `bg_victory` | `moments/ravien/images/char_ravien_background_quiet_afterbattle_v01.png` | ready | 적이나 보상을 표시하지 않는 전투 후 무대 배경 |

§15.3의 독립 512×512 표정 6개는 expr_neutral/expr_happy/expr_annoyed/expr_surprised/expr_worried/expr_determined로 제작·등록했다. 전신 포즈 변형과 별개의 PNG다. 로비 3동작의 연속 관절/프레임·부위분리 PSD·리그는 아직 미완성이다. 소품 단독 레이어도 제작하지 않았다.

## 입력과 보존할 의도

모든 장면은 탭 진행·건너뛰기·다시 보기 가능, cooldown_s=0 초안이다. 빠른 탭은 현재 글을 완성한 뒤 다음 박자로 진행하며 장면을 중첩 시작하지 않는다. B는 기존 제목 선택 이벤트, C는 기존 승리 이벤트에 연결할 표현 데이터로만 두고 해금/보상 조건을 정의하지 않는다. 반복 시작 시 첫 figure·camera·stage로 재설정하고, 생략한 실제 음성/이미지를 재생 성공으로 세지 않는다. UI로 얼굴과 손이 가리면 UI 배치를 조정하며 대사·프레임·기본복을 축약/덮기 하지 않는다.

28세 라비엔의 영리한 여유와 존댓말 속 장난을 유지한다. 사람을 거래 대상으로 만들지 않고, 능숙한 말투가 솔직한 개인적 초대로 바뀌는 틈이 매력이다. 깊은 네크라인의 검은 베스트·트임 펜슬 스커트·가터 스타킹·느슨한 붉은 넥타이·긴 검은 장갑과 와인빛 긴 머리·초록 눈·175cm 성인 실루엣을 보존한다. 카메라는 얼굴과 장갑 손짓을 중심으로 하고 가슴·다리만 따로 크롭하지 않는다.

## 검증과 미완료 — 3차 패스

A의 선언 이미지·박자·배경 참조, idle 파일, PNG 디코드·투명 알파, 모션 내부 참조와 JSON 형식을 검사했다. 전체 대사·타이밍·입력·의도는 변경 전 바이트 재구성 해시로 보존을 확인했다. 자체 시각 검수는 상위 `production-20261018/visual-verdict-*.json`, 현재 검사 결과는 상위 `VALIDATION.json`을 따른다.

미완료: 관절 리그·연속 프레임·녹음·PSD·AnimationClip·Timeline·VFX. NOT_RUN: 이번 신규 그림의 Unity 임포트/재생·기기·스킵/연타/대기 복귀·접근성 수용, Claude 리뷰, 게임 코드 시험·빌드. 포즈 컷의 소품 출입·장식 위치 변동과 카메라 앵커는 실제 화면에서 추가 검수가 필요하다.
