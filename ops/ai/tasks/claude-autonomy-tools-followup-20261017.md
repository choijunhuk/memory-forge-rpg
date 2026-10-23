# 최신 사용자 지시 — Claude 자율성·의견·실행 권한 보완

이전 MCP 구현의 후속이다. Opus5.5·구독 포함 사용량·고정 계정 확인·no fallback·재귀 AI 호출 금지 유지. 다른 Claude가 서버 UI 및 아트 도구를 담당하므로 그 파일을 수정하지 않는다. 책임은 기존 MCP 작업과 같은 실행기/시험/공통 정책 파일이다. 현재 작업물은 보존한다. 대규모 재설계나 전체 문서 재탐색 없이 아래 차이를 구현한다. 검증은 실제 실행하지 않았으면 NOT_RUN이다.

## 사용자 최신 지시를 정책에 반영

Claude의 활동량을 높이고 서로 독립인 구현을 별도 branch/worktree에서 병렬 진행한다. Codex는 작업 경계·지시·계획·독립 검증·통합의 품질을 유지하며 이미지 생성/내용 수정을 전담한다. 캐릭터 외형의 최종 디자인은 Codex가 맡되, Claude는 캐릭터를 포함한 모든 분야에 자유롭게 의견을 낼 수 있다. 코드·게임 시스템·UI/UX·스토리·성능·테스트·아키텍처는 승인된 설계 범위에서 자율적으로 판단하고 구현한다. 의견 때문에 매번 승인받거나 같은 내용을 반복 검토하지 않는다. 이미 승인된 비성적 캐릭터의 나이/존재/외형/서브컬처 표현을 포괄적인 약관 추정만으로 삭제·축소·변경하지 않는다. 구체적 구현 결함·설계 불일치·실제 제약과 선택적 의견을 구분해 Codex가 채택 여부를 판단한다. 실제 안전·권한·과금 경계를 우회하라는 뜻은 아니다. 가격·확률·재화·영구 자산·나이의 정책 변경과 운영 배포의 승인 경계는 그대로다.

일반 개발 작업의 도구 권한도 불필요하게 읽기/편집만으로 제한하지 않는다. 필요한 로컬 시험 명령을 Claude가 직접 수행할 수 있는 최소한의 명시적 opt-in 경로를 실행기에 추가한다. 승인된 작업에 지정된 테스트 명령만 허용하고, 모델/API·구독 우회·비밀 조회·배포·Git 파괴 명령은 허용하지 않는다. CLI 권한은 OS sandbox가 아님을 정직하게 기록하고, 명령 프로필을 범용 실행 엔진으로 키우지 않는다. 고정 모델/과금 guard와 파일 소유권은 유지한다. 필요한 단위 시험은 기존 `tools/ai/test_handoff.py`에 묶는다.

## 실제 설치 버전 조회가 확인한 MCP 교정

Codex가 무료 npm `@steipete/peekaboo@4.9.5 mcp`를 provider 환경변수 없이 로컬 stdio로 부팅해 initialize/tools-list만 실행했다(추론/화면 조작은 아직 없음). 실제 serverInfo는 peekaboo-mcp4.9.5다. 실제 도구 목록:
`menu, analyze, app, permissions, select_text, dock, sleep, image, click, type, press, window, agent, clipboard, paste, browser, dialog, verify_state, drag, scroll, inspect_ui, action, space, set_value, see, capture`.
`list`와 `hotkey` 도구는 없다. app(action=list), window(action=list)가 발견 경로다. 기존 초안 목록을 실제 이름에 맞춘다. `image`는 screenshot-only이며 AI 분석 입력을 받지 않고, `see`의 OCR은 로컬 Apple Vision이다. analyze/agent는 AI 공급자를 호출하므로 계속 금지한다. 이름에 쓰인 기본 provider 설명은 실제 호출 증거가 아니다. 앱·창·메뉴·의미 기반 조작 등 무료 native 조작도 해당 작업의 대상 앱에 한해 활용할 수 있게 하고, 무의미하게 전부 막지 않는다. 계정·청구·비밀·터미널을 GUI로 우회 조작하는 것은 금지한다.

Unity의 무료 `execute_code`는 실제 Editor의 C# method-body 도구이며 설치/검수/실행 확인에 쓰인다. write 소유권이 있는 명시 프로필에서 사용할 수 있게 한다. 임의 코드 실행이므로 파일 scope를 OS 수준으로 강제한다고 주장하지 않는다. 기존 Editor·파일 보존, 유료 asset/AI 생성·다른 모델 호출 금지 경계는 유지한다. 실제 Unity tools/list와 CLI 노출/도구 호출을 다음 실험에서 검증한다. 설정만으로 연결됐다고 쓰지 않는다.

최신 ImagineArt 무료 크레딧 전용 예외·Codex 이미지 담당도 이전 작업 범위를 유지한다. 기존 공통 지침을 삭제/축약하지 말고 최신 우선 절과 runtime-policy에 일관되게 반영한다. 결과는 변경 파일·새 실행 인자·실제 확인 못 한 점 위주로 짧게 보고한다.
