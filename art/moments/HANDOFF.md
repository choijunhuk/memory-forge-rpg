# HANDOFF — style-A 원본 보정 완료 / Unity 반영 전

2026-10-18 / Codex 창작 원본 / **전부 `draft`**. 스타일 A 승인은 개별 그림·새 원본의 Unity 수용 승인이 아니다.

[style-A 시각 리뷰](../requests/moments-visual-review-20261018-styleA.md)에서 Codex에게 배정한 원본 지적에 대응했다. **새 선택 PNG 25개: 에일라 18개 렌더링 보정 + 루미 B 전용 연기 7개.** 현행 전체는 6명·18장면·215박자, **고유 PNG 135개 / 이미지 매핑 137개**다(전신 91·배경 8·표정 36). 미선택 후보는 이력으로 남겼다.

현행 선택의 기준은 [selected-assets.json](production-source-polish-20261018/selected-assets.json), [accepted-assets.json](production-source-polish-20261018/accepted-assets.json), 6개 캐릭터 `moments.json`이다. 이전 전체 재제작의 선택 인덱스는 당시 이력이다. [이번 변경 목록](production-source-polish-20261018/changes.json) / [이전 인계·6 JSON·검증 스냅샷](production-source-polish-20261018/before/) / [전후 비교](production-source-polish-20261018/before-after-summary.png).

## 지적별 결과

| 지적 | 원본에서 한 일 | 수용 범위 |
|---|---|---|
| `design_mismatch` 루미 B 마지막 헬멧 수 | 새 `fig_bond_end`에 별개 외곽·파란 바이저를 가진 **헬멧 두 개를 같은 팔로 지지**하고 반대 손바닥은 비워 내민다. B 21–22박자에만 선택했다. 로비의 한 헬멧 `fig_soft`는 그대로다. | 자체 원본 검수 PASS. 새 Unity 캡처는 NOT_RUN. |
| `optional_opinion` 에일라의 장식/하이라이트 밀도 | A/B/C의 선택 전신 18개를 함께 보정했다. 땋은 머리의 큰 명암, 낮춘 자수 대비, 절제한 반복 반짝임, 더 단순한 가구 명암으로 얼굴·손을 먼저 읽게 했다. 의상 재단·열린 등/어깨·트임·성인 실루엣과 장식의 정체성은 유지한다. | 자체 미술 판단으로 개선. 취향 문제의 보편적 해결·사용자 개별 승인을 주장하지 않는다. |
| `optional_opinion` 반복되는 포즈와 연기 | 루미 B에 장비 정리 중 멈춤, 앞서 걷다 돌아봄, 보폭 맞추기, 함께 웃음, 바람에 머리 누르기, 발끝을 드는 도착, 두 헬멧의 귀로 초대를 각각 배정했다. | PNG 키포즈 7개 / figure 선택 11박자 변경. 실제 보행 애니메이션 완성이 아니다. |

[지적별 기록](production-source-polish-20261018/findings-resolution.json) / [루미 연기 합성](production-source-polish-20261018/lumi-acting-final-review.png) / [도착 수정·마지막 합성](production-source-polish-20261018/lumi-arrival-ending-review.png) / [에일라 페이지 흐름](production-source-polish-20261018/aila-bond-final-review.png) / [보관 포즈 최종 알파 수정](production-source-polish-20261018/aila-store-fix-review.png).

루미의 새 figure 선택은 **B 9·11·13–17·19–22박자**다. 도착은 `fig_bond_arrive` **v02**, 마지막은 `fig_bond_end` **v01**. 이전에는 루미에게 한 개, 화면 밖 동행자에게 한 개가 있으며, 돌려받은 뒤에만 루미 그림에 두 개가 보인다. 새 얼굴/몸/손 앵커 7세트를 원본에서 측정했다. [STYLE-GUIDE.md](STYLE-GUIDE.md)에 이 소품 연속성과 에일라의 강조 순서를 덧붙였다.

에일라는 쓰기 **v03**, 보관 **v03**, 나머지 전신은 **v02**다. C의 기존 wait→turn 별칭을 유지했다. 페이지 가져오기→펜 대기→펜촉/종이 접촉→작성한 페이지 제시→반환→보관, 탁자 위 은색 등불·찻잔·가구 상태를 보존했다. 6개 독립 표정은 이미 절제된 셀 명암으로 읽혀 검토 후 유지했다. 다른 4명, 기존 배경 8개와 표정 36개는 수정하지 않았다.

## 검증 — 원본 범위 PASS

[VALIDATION.json](VALIDATION.json) / [재현 가능한 검사](production-source-polish-20261018/verify.py) / [이번 전체 결과](production-source-polish-20261018/verification.json).

- 6 JSON·18장면·215박자, 137개 이미지 매핑·135개 고유 PNG의 경로/디코드/해시/참조 검사. 선택 경로와 수용 인덱스가 정확히 일치하며 이미지 미선택·누락 0.
- 새 선택 25개 전부 **1024×1536 RGBA**, 네 정확한 빈 모서리 alpha=0. 윗 48×48 모서리 패치 최대 alpha≤1/255, 밝은/어두운 바탕 시각 검수. RGBA 형식만으로 PASS를 주지 않았다.
- **대사·화자·tone·gaze·hold·camera·input·trigger·stage·why·intent·모션 구조 불변.** 루미 B의 figure 선택과 자산 목록, 새 앵커, 에일라 이미지 경로만 변경했다. 로비/승리의 루미 장면 데이터와 한 헬멧 공용 그림은 불변이다.
- 작업 전 존재한 PNG/APNG **353개 SHA256 불변**. 이전 그림을 덮어쓰거나 삭제하지 않았다. 허용한 6개 기존 원본/문서 외에 다른 기존 소스 파일 변경 0.
- 자체 visual-verdict: 에일라 92/100, 루미 94/100. 자동 품질 측정·외부 리뷰·사용자 승인 점수가 아니다.
- 첫 검사에서 도구 훅이 갱신하는 `.omc/.../pre-tool-advisory-throttle.json`을 발견했다. [초기 실패](production-source-polish-20261018/verification-initial.json)를 보존하고 정확히 그 훅 상태 파일만 별도 보고했다. 소스/이미지 검사는 유지했다.
- 배포 매니페스트는 시작 때부터 **423항목 중 기존 불일치 32개로 FAIL**이었다. [사전 기록](production-source-polish-20261018/manifest-precheck.json). 배포 매니페스트를 재생성하지 않았다.

## 제작 기록과 미채택

**내장 구독 `image_gen`만 사용**했다. [28개 생성 / 25개 선택 / 3개 미채택](production-source-polish-20261018/generation-records.json), 전체 프롬프트·입력·캐시 경로·선택 상태는 [records/](production-source-polish-20261018/records/)에 있다. 백엔드 모델·seed는 도구에 노출되지 않아 추정하지 않았다. 이미지 내용 수정/알파 재시도도 같은 도구로 했다. Swift 합성은 검수용이며 원본 픽셀을 편집하지 않는다.

미채택: 에일라 쓰기 v02는 강조 정리가 부족해 v03으로, 루미 도착 v01은 발끝 동작이 약해 v02로, 에일라 보관 v02는 빈 모서리 alpha 1/255로 v03으로 교체했다. 미채택 파일도 보존했으며 현행 JSON은 가리키지 않는다. 검사를 느슨하게 바꾸어 통과시키지 않았다.

## 남은 수용과 다음 단계

**NOT_RUN:** 새 원본의 Unity 바이트 보존 임포트·실제 재생/캡처·전환 중 가림·앵커 카메라·입력/idle 복귀·다른 화면비·실기기·음성·리깅·중간 동작·AnimationClip/Timeline·Claude 독립 리뷰·게임 코드 시험/lint/typecheck/build.

기존 리뷰의 106개 캡처와 134개 source/import 일치는 **이전 원본에 대한 증거**다. 이번 새 25 PNG와 변경 JSON의 임포트·재생 증거로 재사용하지 않는다. 소스 원본만 확인했으므로 새 구현 verdict를 발행하지 않는다. 장면 전체의 환경 경쟁과 “AI 일러스트 느낌”이 전부 해결됐다고도 선언하지 않는다.

다음 구현 담당 작업은 변경된 **에일라/루미 JSON 2개와 선택 PNG 25개**를 내용 변경 없이 임포트하고, 루미 B 21–22의 두 헬멧·초대 손, 새 B 연기, 에일라 쓰기/페이지/보관/마지막 초대를 재캡처하는 것이다. 선택 인덱스의 해시로 대조한 뒤 새 장면을 다시 시각 검수한다. 이번 세션에서는 Claude를 호출하지 않았다.

**프로젝트 변경은 `art/moments/` 안에만 있다. 게임 코드·Unity·커밋·push·Claude 호출·API/별도 유료 서비스 사용은 없다.** 바뀐 기존 파일: `aila/moments.json`, `lumi/moments.json`, `STYLE-GUIDE.md`, `README.md`, `HANDOFF.md`, `VALIDATION.json`. 새 PNG·기록·검증은 이 폴더 안에 있으며 기존 전체 재제작 인계는 `before/HANDOFF.md`에 보존했다.
