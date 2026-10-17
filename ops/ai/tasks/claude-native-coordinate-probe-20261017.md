# 화면 조작 범위 보완 — 정확한 Unity 창의 시각 좌표 사용 허용

이전 native probe는 연결/권한/Unity 창 캡처에 성공했다. 입력을 하지 않은 것은 이전 작업 지시가 AX 필드만 허용했기 때문이며, Unity의 AX 정보가 적다는 이유로 모든 시각 조작이 불가능하다고 결론내릴 필요는 없다. 사용자는 실제 화면 조작과 더 자율적인 개발을 이미 허용했다. 이번에는 **fresh exact-window screenshot에 근거한 좌표 클릭과 키 입력**도 이 아래의 좁은 가역 시험에 허용한다. 일반적인 좌표 입력 자체를 우회라고 취급하지 않는다. 도구 자체가 명확하게 거절하면 회피·보호 해제는 하지 않는다.

Opus5.5 High, 기존 구독 포함 사용량만. 다른 모델/AI 분석/이미지 생성·셸·파일/씬/프리팹 저장·청구/비밀 조작 금지. Codex는 이 작업 중 Editor/화면을 조작하지 않는다.

1. 실행 중인 Unity MemoryForgeUnity exact 창을 새 see로 관찰한다(이전 관측 PID97768/window32587은 현재 목록과 대조한다). 현재 검색 필드가 비었는지 화면으로 확인한다.
2. **Hierarchy 또는 Console의 비어 있는 검색 필드 하나만** 화면에서 확실하게 찾는다. `click`의 fresh `snapshot`/`coordinate_reference`와 `coordinate_space=image_pixels` 또는 normalized를 사용해 그 필드를 클릭한다. 전역 pointer/다른 앱 창/맥 메뉴/장면 Play 버튼은 이 시험에서 건드리지 않는다. 실제 설치 click schema가 이 방식을 지원한다.
3. 명확한 도구/상태 관측이 허용하면, 같은 exact-window fresh snapshot을 써서 `MemoryForgeMcpProbe`를 입력하고 새 화면에서 확인한다. 그 뒤 같은 필드를 비우고 새 화면으로 원래 빈 상태를 확인한다. 초기 값이 불명확하거나 비어 있지 않으면 다른 빈 검색 필드를 쓰고, 없으면 입력만 NOT_RUN으로 보고한다. 기존 필터나 유저 내용을 잃지 않게 한다.
4. 도구 결과의 confirmed/unverified/denied를 구분한다. 키 주입 접수만으로 성공이라고 하지 않는다. 새 이미지 관측에서 실제 텍스트 등장과 제거를 봐야 GUI 조작 PASS다. 가능하면 입력/복구 캡처를 `/Users/choi/Documents/Memory_forge/ops/ai/local/claude-native-coordinate-input-20261017.png`, `claude-native-coordinate-restored-20261017.png`로 저장한다.

결과는 연결·입력·복구 각각의 실제 결과와 남은 한계를 짧게 적는다. 개발 도구를 시험하는 것이며 실제 게임 클릭/Android/전체 Unity 입력 호환성을 주장하지 않는다.
