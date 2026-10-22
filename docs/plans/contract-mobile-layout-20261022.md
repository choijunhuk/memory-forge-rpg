# 실제 폰 레이아웃 보정 — 2026-10-22

사용자 피드백: 글자/배치가 불편하고 좌우가 잘려 보이며 가장자리에 정렬되지 않는다. 이전 에이전트의 화면 PASS를 사용자 수용으로 세지 않는다.

## 게임 역할과 범위

이미 승인된 보유 동료·모집·성장·장비·지원 동작과 실제 계정352건 검증, 정확한 미확정 요청 보존은 유지한다. 새 가격/확률/경제/서버/Core/아트 원본 변경은 없다. 기존1600x900 인물·Moment 무대의 비율/포즈/의도를 보존하면서 HUD만 실제 safe area에 맞춰 좌우/하단을 정렬한다.

## 완료 조건

- 2340x1080 실제폰에서 Home·동료/상세·탐사준비·성장/전투·모집의 좌우 padding과 grid가 safe area에 정렬된다. 비대칭 노치와 좌우반전,16:9PC/4:3태블릿도 검증한다.
- 본문/버튼 글자는 읽기 가능한 물리크기이며 BestFit의 조용한 축소로 통과하지 않는다. 행높이/간격/스크롤을 함께 조정하고 전체 글자 표시를 검사한다.
- 초상/배경은 의도한비율을 유지한다. fullbleed배경과 고정내부배경의 경계가 화면을 자른 것처럼 보이지 않게 한다. 모달 shield/veil/overlay는 확장된 영역 전체를 덮어 뒤 입력을 차단한다.
- UnityScreen metrics와Android density/scaledDensity를 구분해 교정한다. Development-only 수치 진단에 계정/키/본문/경로/예외문자열을 넣지 않는다.
- 캡처 시험에서 frame을 강제로 scale1/center로 보정하지 않고 생산 fitting을 통과한다. 순수 viewport계산+화면 픽셀경계/전체 glyph/터치크기 회귀검사, 실제원본해상도 캡처/visual-verdict90+를 통과한다.
- 실제폰 재설치/로그인/읽기 전용 재개와 PC를 확인하고 전체관련Unity시험·공식반대AI검토·최신head CI를 완료한다. 추가 모집/재화 변경은 필요하지 않다.

## 담당

별도 mobile-safearea worktree의 UI production C#와 FUP의 UI 회귀 시험은 Codex가 담당한다. 공식 Claude Opus 구현 호출은 구독 세션 한도로 파일 수정 전에 종료되었으며, 한도 초기화 뒤 읽기 전용 독립 검토를 맡긴다. 추가 과금이나 다른 모델로 전환하지 않는다. Root는 Unity 단일 writer/실기기, 통합/CI/기록을 맡는다. 기존 원본 root의 8개 변경 파일과 모든 창작 원본을 보존한다. 기존 패턴을 재사용하고 dependency를 추가하지 않는다.

## 단위 확인 보완

Unity6 문서상 Android의 Screen.dpi는 논리 densityDpi이며 물리 패널 PPI가 아니다. Android34+ 글자 확대는 비선형이므로 각 목표12/14/18sp를 TypedValue.applyDimension(COMPLEX_UNIT_SP, sp, Activity DisplayMetrics)으로 변환하고, scaledDensity 또는1sp 비율을 모든 크기에 곱하지 않는다. 실제폰 font_scale1.0/wm density450은 읽기 전용 설정값이며 Unity 값은 새 개발 로그에서 별도로 측정한다. 근거: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Screen-dpi.html , https://developer.android.com/reference/android/util/DisplayMetrics , https://developer.android.com/about/versions/14/features#non-linear-font-scaling .

## 좌표와 재생 호환 경계

탐사 화면의 내부 Box/Label·모션·스크롤로 옮기는 행은 기존 top-left 절대 좌표 계약을 유지한다. 안전 영역을 채우는 바깥 HUD와 손패/적 영역에 반응형 anchor를 명시적으로 적용한다. 모든 내부 사각형에 추론형 anchor를 적용하면 기존 sizeDelta·anchoredPosition 대입과 충돌하므로 같은 방식으로 합치지 않는다. stretch된 스크롤 content는 세로 크기만 갱신하고, 화면/이펙트 좌표 변환은 실제 frame.rect를 사용한다.

실제 배경 그림은 원본 비율을 유지하는 cover 자식에서 넘치는 부분만 crop한다. UI용 sliced frame의 의도된 stretch와 구분한다. 배경·어둡게 하는 면·모달 차단면은 같은 화면 범위를 덮는다.

실행 중 density·fontScale·픽셀 배율 변경은 UI만 다시 배치하며 계정 명령·미확정 요청·출격 길이·재생 중인 beat를 바꾸지 않는다. 대사는 탭으로 읽기 완료한 원문 prefix와 타이핑 진행을 보존하고, 남은 원문을 새 글자 크기에 맞춰 페이지로 나눈다. 새 페이지 계산으로 원문·공백·줄바꿈을 잃거나 beat/음성을 다시 실행하지 않는다.
