# 실제 무료 native 화면 MCP 시험 — Unity 창만

Opus5.5 High·구독 포함 사용량만 사용한다. Peekaboo4.9.5의 analyze/agent/browser/clipboard/paste는 제공하지 않는다. 이미지 생성/AI 분석/다른 모델/API 호출·셸·파일 편집·과금/권한 우회는 하지 않는다. 설치/도구 목록을 연결·조작 성공으로 세지 않는다. Codex가 이 작업 동안 Unity Editor/화면 제어를 넘기며 같은 Editor를 쓰지 않는다. 다른 작업자가 있으므로 Scene/Prefab·프로젝트 파일은 수정/저장하지 않는다.

아래 좁은 시험을 실제 도구로 수행한다. 파일/문서 전체를 다시 읽거나 긴 계획을 쓰지 않는다.
1. permissions로 실제 macOS 권한을 읽는다.
2. app(action=list)와 필요하면 window(action=list)로 실행 중인 **Unity Editor의 MemoryForgeUnity 프로젝트 창**을 정확히 찾는다. Codex/Claude/계정/청구/터미널/시스템 설정 창은 캡처하거나 조작하지 않는다.
3. 그 exact window/PID에만 see(또는 image)를 호출해 실제 화면·AX를 받는다. 화면은 로컬 OCR/캡처만 사용한다. 전체 모니터/알 수 없는 전면 창을 찍지 않는다. 가능하면 캡처를 `/Users/choi/Documents/Memory_forge/ops/ai/local/claude-native-unity-probe-20261017.png`에 저장한다.
4. 실제 AX 정보에 **비어 있는 Unity Hierarchy/Project 검색 필드**가 확실하게 식별되고 입력 도구가 지원되면, set_value로 `MemoryForgeMcpProbe`를 넣고 관찰한 뒤 원래 빈 값으로 되돌려 실제 조작을 확인한다. 불명확한 대상·GameObject 이름·파일·씬을 수정하지 않는다. 필드가 없거나 권한/배경 조작 정책이 거부하면 해당 조작만 NOT_RUN/거부로 기록하고 억지로 우회하지 않는다.

결과는 실제 연결, 캡처, 조작을 각각 구분하고 권한/플랫폼 한계와 원상복구 여부만 짧게 보고한다. `mcp.connection_observed`는 전송/도구 응답 증거일 뿐 GUI 조작 성공과 동일하지 않다. 새 유료 서비스나 크레딧을 쓰지 않는다.
