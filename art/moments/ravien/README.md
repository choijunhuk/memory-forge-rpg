# 라비엔 캐릭터 모먼트 창작 원본 — draft

요구 기준: `art/requests/character-moments-20261018.md`, `docs/MOMENTS_FORMAT.md`, `docs/AI_WORKFLOW.md` §18, `OWNERSHIP.md`, `docs/design_v0.8.1.md` §9.3·§15.3, `content/character_notes.mjs`, `art/briefs/characters_v0.8.json`·`prompts_v0.8.1.json`.

현재 28세 고정. **사용자 승인 전 창작 초안**이며 게임 코드·Unity·정사·기존 대사를 직접 교체하지 않는다. 이번 담당 파일은 이 폴더의 `moments.json`, `README.md`뿐이다.

175cm 성인 체형, 와인빛 긴 웨이브 머리·초록 눈. 깊게 파인 검은 베스트, 트임 펜슬 스커트, 가터 스타킹, 느슨한 붉은 넥타이, 긴 검은 장갑, 황금 저울 펜던트. 외형 대안·나이 대안을 채택하지 않는다.

## 만들어진 것 / 아직 없는 것

실제 생성물은 JSON 원본과 이 제작안뿐이다. A 로비·B 인연·C 승리 3개 장면, 대사·지문·표정/시선·카메라/음성 지시, 슬롯 키프레임 4개를 작성했다. `status: ready`인 모션은 **JSON 슬롯 이동 데이터가 작성됐다는 뜻**이다. PNG, 핵심 동작 그림, 손가락/팔 관절 리그, 프레임 애니메이션, Unity AnimationClip/Timeline, VFX, 녹음, 음악은 만들지 않았다. 일러스트가 없는 지금은 대사·타이밍만 재생할 수 있으며 캐릭터 시각 동작 완성으로 보고하면 안 된다. 이미지 생성 도구·API·Claude를 호출하지 않았다.

현재 B는 `line.text`의 Python `len()` 합 **2,689자**, **33박자**다. 공백·문장부호를 포함하고 title·tone·JSON 구문은 제외한다. 기존 제목 아래 시작·전개·관계 변화·마무리가 있는 대본 전체이며 시놉시스가 아니다. 플레이어 답변은 speaker=null 지문으로만 다룬다. 비용·보상·친밀도 수치·새 해금 규칙은 없다.

## 장면 지시

- **A — 닫힌 서류철, 비워 둔 옆자리**: 책상 옆 선 자세 → ① 도장을 정확히 한 번 누르기 → ② 종이 끝을 손끝으로 돌리고 눈을 공방장에게 옮기기 → ③ 옆 의자를 가리키고 손을 내린 채 대답 기다리기 → 서류철을 닫은 선 자세. 업무가 끝난 뒤에도 곁에 남아 달라는 사적인 초대를 말한다.
- **B — 서명 없는 약속**: 기존 에피소드 4 「서명 없는 약속」의 완결된 저녁 장면. 빈 종이 앞에 앉기 → 산책을 부탁하면서 조건을 붙이는 버릇을 알아차리기 → 종이를 놓고 같이 있고 싶은 뜻을 직접 말하기 → 문 앞에서 나란히 다음 걸음을 고르기. 과거의 미상환 계약 내용·채권·설정 비밀을 새로 확정하지 않는다.
- **C — 정산 뒤에 남은 시선**: 기본복 승리 연출. 서 있는 계약서·불꽃 봉인 자세 → 인장을 회수하고 기존 승리 대사 “정산 완료.” → 옆자리를 열고 상대를 보려고 돌아봤다고 개인적으로 말하기 → 서류철을 옆구리에 둔 정면 자세. 별도 스킨·보상·능력 효과를 만들지 않는다.

시선·표정: A: 도장 아래 → 종이 → 플레이어 → 옆 의자 → 플레이어. B: 빈 종이와 눈맞춤 사이의 망설임 → 솔직한 대답 뒤 잠깐 아래 → 문 앞에서 나란한 눈맞춤. C: 봉인 → 정면 확인 → 사적인 눈웃음.

음성: 낮고 느긋한 존댓말. A의 마지막 질문과 B의 직접적인 고백은 계산된 미소를 덜어 낸다. 숨소리를 과장하거나 모든 문장 끝에 유혹의 웃음을 붙이지 않는다. C는 일 처리의 자신감에서 사적인 시선으로 넘어간다. 각 `line.tone`을 함께 따른다. `voice_*` asset key는 녹음 제작 묶음 식별자이며 실제 오디오 경로가 아니다. 녹음 파일은 없으므로 beat.voice를 쓰지 않았다. `speaker:null`은 동작/상황 지문이며 플레이어 배역을 임의로 추가하지 않는다.

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
| `fig_default` | `moments/ravien/images/char_ravien_stand_neutral_v01.png` | todo | 서류철을 든 기본복 전신, 평정 |
| `fig_stamp` | `moments/ravien/images/char_ravien_stamp_focused_v01.png` | todo | 상체를 숙이지 않고 책상 도장을 누르는 핵심 포즈, 결의 |
| `fig_turn` | `moments/ravien/images/char_ravien_paper_turn_playful_v01.png` | todo | 손끝으로 종이 귀를 돌리는 핵심 포즈, 눈웃음 |
| `fig_offer` | `moments/ravien/images/char_ravien_chair_offer_happy_v01.png` | todo | 검은 장갑 손끝이 옆 의자를 가리키는 핵심 포즈, 즐거움 |
| `fig_lobby_soft` | `moments/ravien/images/char_ravien_stand_soft_v01.png` | todo | 옆 의자를 가리킨 뒤 손을 내리고 선 포즈, 기대 |
| `fig_bond_turn` | `moments/ravien/images/char_ravien_seated_playful_v01.png` | todo | 앉은 채 종이 귀를 돌리고 공방장을 보는 포즈, 장난 |
| `fig_waver` | `moments/ravien/images/char_ravien_seated_surprised_v01.png` | todo | 펜을 멈추고 눈썹이 잠깐 올라간 앉은 포즈, 놀람 |
| `fig_soft` | `moments/ravien/images/char_ravien_seated_worried_v01.png` | todo | 종이에서 손을 떼고 질문을 기다리는 포즈, 걱정과 솔직함 |
| `fig_resolve` | `moments/ravien/images/char_ravien_stand_determined_v01.png` | todo | 닫힌 서류철을 옆구리에 두고 나란히 선 포즈, 결의 |
| `fig_annoyed` | `moments/ravien/images/char_ravien_stand_annoyed_v01.png` | todo | 눈썹만 좁히고 입을 누른 기본복 전신, 짜증 |
| `fig_seal` | `moments/ravien/images/char_ravien_battle_seal_determined_v01.png` | todo | 세운 계약서 위 불꽃 봉인에서 인장을 회수하는 선 전투 포즈, 결의 |
| `fig_victory` | `moments/ravien/images/char_ravien_seal_return_relaxed_v01.png` | todo | 인장을 회수하고 공방장 쪽으로 돌아선 승리 포즈, 여유 |
| `bg_room` | `moments/ravien/images/char_ravien_background_workshop_evening_v01.png` | todo | 책상·옆 의자·문이 함께 읽히는 공방 저녁 배경 |
| `bg_victory` | `moments/ravien/images/char_ravien_background_quiet_afterbattle_v01.png` | todo | 적이나 보상을 표시하지 않는 전투 후 무대 배경 |

표정 6종 향후 커버리지: 평정(fig_default), 즐거움(fig_offer), 짜증(fig_annoyed), 놀람(fig_waver), 걱정(fig_soft), 결의(fig_resolve). 짜증은 이번 본문에 억지로 삽입하지 않은 향후 표정 커버리지다. §15.3의 512×512 독립 표정과 로비 3동작·부위분리 원본/리그는 **아직 미완성**이다. 이 목록은 그 제작을 위한 대응표이며 완료 수량이 아니다. 소품은 해당 전신 포즈/배경에 그려 넣는 계획이므로 독립 prop 슬롯 그림을 만들어 놓았다고 주장하지 않는다.

## 입력과 보존할 의도

모든 장면은 탭 진행·건너뛰기·다시 보기 가능, cooldown_s=0 초안이다. 빠른 탭은 현재 글을 완성한 뒤 다음 박자로 진행하며 장면을 중첩 시작하지 않는다. B는 기존 제목 선택 이벤트, C는 기존 승리 이벤트에 연결할 표현 데이터로만 두고 해금/보상 조건을 정의하지 않는다. 반복 시작 시 첫 figure·camera·stage로 재설정하고, 생략한 실제 음성/이미지를 재생 성공으로 세지 않는다. UI로 얼굴과 손이 가리면 UI 배치를 조정하며 대사·프레임·기본복을 축약/덮기 하지 않는다.

28세 라비엔의 영리한 여유와 존댓말 속 장난을 유지한다. 사람을 거래 대상으로 만들지 않고, 능숙한 말투가 솔직한 개인적 초대로 바뀌는 틈이 매력이다. 깊은 네크라인의 검은 베스트·트임 펜슬 스커트·가터 스타킹·느슨한 붉은 넥타이·긴 검은 장갑과 와인빛 긴 머리·초록 눈·175cm 성인 실루엣을 보존한다. 카메라는 얼굴과 장갑 손짓을 중심으로 하고 가슴·다리만 따로 크롭하지 않는다.

## 검증 범위

JSON 파싱, 장면 3종/필수 필드, B 글자 수(2,000–3,000), speaker, 이미지·모션 참조, art 상대 경로/파일명, 이미지 todo·모션 ready, 모션 종료값 복귀를 정적 검사한다. 이미지 미생성으로 육안·스크린샷 대조 `$visual-verdict`는 NOT_RUN이며, 비교할 산출물/참조쌍이 없으므로 점수를 만들지 않는다. Unity 임포트·실제 화면·음성·접근성·스킵/반복 작동·사용자 수용은 NOT_RUN이다.
