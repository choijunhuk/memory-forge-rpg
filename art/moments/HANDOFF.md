# HANDOFF — A(선명한 셀) 성인 6명 전체 재제작 완료

2026-10-18 / Codex 창작 원본 / **자산은 모두 `draft`** — 사용자 승인은 그림체 A에 대한 결정이며 개별 자산·Unity 수용 승인을 뜻하지 않는다.

**6명·18장면·215박자의 참조 그림 전체를 A로 다시 제작하고 연결했다. 고유 PNG 128개 / 이미지 매핑 130개 / 미제작·미선택 이미지 0개다.** 구성은 전신 포즈 84개, 불투명 장면 배경 8개, 독립 512×512 RGBA 표정 36개다. 원래 참조 114개를 모두 교체하고 연속성을 위한 B 전용 포즈 14개를 추가했다. A 전원 → B/C → 표정 순으로 제작했다.

[STYLE-GUIDE.md](STYLE-GUIDE.md)가 이후 제작 기준이다. [선택 파일·해시·생성 기록 인덱스](production-style-a-20261018/selected-assets.json)와 [경로 목록](production-style-a-20261018/accepted-assets.json)이 현재 선택을 정의한다. 이전 PNG/APNG와 미채택 A 후보는 이력으로 보존하며 현행 6 JSON·모션 프레임에서 과거 PNG 참조는 0개다. 이전 인계·JSON·README·검증은 [before/](production-style-a-20261018/before/)에 있다. 캐릭터별 예전 README도 이력으로 표시했다.

| 인물 / 유지 나이 | 매핑 / 고유 PNG | A/B/C 원본 | 표정 512² |
|---|---:|---|---:|
| 세라 / 24 | 15 / 15 | 완료 | 6/6 |
| 라비엔 / 28 | 24 / 23 | 완료 | 6/6 |
| 하린 / 26 | 22 / 22 | 완료 | 6/6 |
| 에일라 / 23 | 26 / 25 | 완료 | 6/6 |
| 시온 / 27 | 24 / 24 | 완료 | 6/6 |
| 루미 / 22 | 19 / 19 | 완료 | 6/6 |

라비엔의 기존 `fig_bond_resolve` 별칭은 A 마지막 빈손 그림을 유지하되 B 끝은 새 `fig_bond_end`로 분리했다. 에일라 C 마지막 wait는 원래처럼 새 돌아봄 그림을 정지 재사용한다. 중복 복사로 수량을 부풀리지 않았다. 표정은 neutral/happy/annoyed/surprised/worried/determined이며 등록된 이미지/앵커를 교체했다. 새 portrait overlay나 게임 UI는 추가하지 않았다.

## Round-3 원본 지적 해결

- **하린 B 공개 시점:** 6–10박자는 빈 천만 보이는 `fig_worry`다. 11–14박자에 봉제 고양이를 손에 들어 공개하고, 15–20박자는 새 `fig_cat_placed`로 천 위에 내려놓는다. 21–32박자와 마지막 맨손/벗은 장갑 상태에도 같은 인형이 남는다. 기존 둥근 남회색/흰색 봉제 고양이의 파란 구슬 눈·분홍 발바닥·금색 별 목걸이·접힌 귀를 소품 전용 참조로 고정했다. 방패는 화면 왼쪽, 같은 의자·직사각 탁자·천·물잔·도구를 유지한다. [연속 합성](production-style-a-20261018/harin-bond-corrected.png), [최종 알파 수정본 합성](production-style-a-20261018/scene-final-corrections-preview.png).
- **에일라 B 쓰기/페이지/등불:** 3박자부터 같은 은색 등불을 탁자 위 고정 위치에 둔다. 빈 종이 가져오기(9–10) → 펜 대기(13–14) → 실제 펜촉/종이 접촉(15–16) → 작성한 페이지 제시(17–18) → 반환받은 페이지(19–20) → 서랍 보관(21)을 전용 그림으로 배정했다. 컵이나 등불을 들고 쓰는 모순을 제거했고 대사는 보존했다. [쓰기/제시 검수](production-style-a-20261018/aila-writing-verdict.json), [가구/보관 합성](production-style-a-20261018/aila-bond-review.png).
- **라비엔 B 가구:** 같은 전경 타원형 책상·붉은 의자·서류철·종이·펜을 유지한다. 마지막은 같은 가구 옆에 빈손으로 서며 폴더는 책상에 남는다. A 마지막의 빈 의자·빈손·옆 탁자 폴더도 유지했다.
- **시온 B/C:** B의 감정/손동작 8포즈에 같은 독서대를 유지한다. 책으로 입가를 가리는 포즈의 중복 책을 제거했다. C에는 책 없이 장검을 유지하고, 마지막은 새 안경 포즈에서 빈손만 내려 검 폭을 보존했다. [B 연속 합성](production-style-a-20261018/sion-bond-review.png)의 hidden v01은 중복 책 발견용 이력이며, 선택한 v02는 [수정 합성](production-style-a-20261018/bond-other-corrections.png)에 있다.
- **투명도 잔여물:** 새 하린 B 걱정과 시온 C 마지막을 포함한 선택 전신 84개는 네 빈 모서리 alpha가 모두 0이다. 밝은/어두운 바탕에서도 검수했다. 표정은 기존 의상 하단 크롭을 보존하며 실제 빈 윗모서리 alpha=0, RGBA 여백을 확인했다. 배경 8개는 장면 공간을 유지하는 불투명 그림이다.

## 검증 결과 — 원본 범위 PASS

[VALIDATION.json](VALIDATION.json) / [최종 정적 검사](production-style-a-20261018/verification-final.json) / [독립 데이터 일관성 검사](production-style-a-20261018/consistency-final.json) / [장면 시각 판정](production-style-a-20261018/all-scenes-visual-verdict.json) / [표정 시각 판정](production-style-a-20261018/expression-final-verdict.json).

- 6 JSON 파싱·18장면·215박자·130개 매핑·고유 PNG 128개의 경로/디코드/규격/알파/SHA256, beat·stage·asset·anchor·motion-frame 참조 검사. 모든 장면 image 자산 121항목이 실재 선택 파일로 `ready`, 이미지 todo 0. 음성 18항목은 원래 `todo` 유지.
- 대사·화자·tone·gaze·hold·input·trigger·stage·why·intent·모션 구조 불변. 세라 `check_time`의 5프레임을 같은 순서·반복·fps로 새 파일에 연결했다. 6명 idle도 새 A 파일이다.
- 기존 PNG/APNG **124개 SHA256 불변**. 입력 당시 존재했던 그림을 삭제하거나 덮어쓰지 않았다.
- 표정 36개 전부 정확히 512×512 RGBA, 인물별 6개의 해시가 각각 다르다. [세라](production-style-a-20261018/expressions-review-sera.png) · [라비엔](production-style-a-20261018/expressions-review-ravien.png) · [하린](production-style-a-20261018/expressions-review-harin.png) · [에일라](production-style-a-20261018/expressions-review-aila.png) · [시온](production-style-a-20261018/expressions-review-sion.png) · [루미](production-style-a-20261018/expressions-review-lumi.png).
- 구도가 달라진 B 얼굴/손과 표정 얼굴 앵커는 새 원본을 보고 정규화 좌표로 보정했다. [anchor-updates.json](production-style-a-20261018/anchor-updates.json). 실제 재생 카메라 수용과 구별한다.
- 자체 시각 점수는 장면/표정 각각 94/100이며 자동 측정·외부 리뷰·사용자 개별 승인 점수가 아니다. 미채택 원인과 교정은 회차별 verdict에 보존했다.

## 제작 경로와 남은 수용 범위

생성/내용 수정은 **내장 구독 `image_gen`만** 사용했다. API·별도 유료 서비스·ComfyUI·ImagineArt·Claude 호출은 없었다. 전체 프롬프트·입력 참조·원본 캐시·선택 경로·최종 선택 상태는 [records/](production-style-a-20261018/records/). 도구가 공개하지 않는 백엔드 모델·seed는 추정하지 않았다. 표정은 [고해상 원본](production-style-a-20261018/expression-hires/)을 `sips -z 512 512`로 비생성형 축소했다. Swift 합성판은 검수용이며 PNG 내용을 다시 그리지 않는다.

에일라 반환 페이지의 첫 생성 1건은 출력 안전 검사에서 거절됐다. 기존 페이지 제시 그림에서 손·종이만 편집한 재시도는 성공했고, 의상·나이·설계를 바꾸거나 다른 공급자로 우회하지 않았다. [도구 제약 기록](production-style-a-20261018/tool-constraints.json)에 미해결 항목은 없다.

**이번 요청의 미제작 PNG: 0개.** 전신은 1024×1536, 배경은 1672×941, 표정은 512×512다. 배경은 정식 2560×1440 레이어 마스터보다 낮다. 작은 장식/자수/기계 내부 선의 픽셀 동일성, 레이어 PSD·리그·중간 동작·AnimationClip·Timeline·음성은 이번 PNG 완료의 보장 범위가 아니다.

**NOT_RUN:** Unity 임포트·실제 재생·전환 중 가림·입력/idle 복귀·다른 화면비·기기 수용·Claude 독립 리뷰·게임 코드 시험/lint/typecheck/build. 실제 라이브 수용은 새 원본의 바이트 보존 임포트와 재생 캡처로 별도 확인해야 한다. 프로젝트 수정 범위는 **art/moments/**이며 게임 코드·Unity·커밋·push·Claude 호출은 없다. 작업 전 배포 매니페스트는 423항목 중 기존 변경 32개로 FAIL이었고 재생성하지 않았다.
