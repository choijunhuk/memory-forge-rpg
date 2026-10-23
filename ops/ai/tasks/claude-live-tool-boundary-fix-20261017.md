# 실제 MCP 노출 목록에 따른 한 번의 경계 보강

실행기는 실제 CLI에서 handoff31/31 직접 Bash 실행과 Unity state/read_console 연결에 성공했다. 세션 ab266f26-8e4e-49d9-9745-cc4047c562de, Opus5.5 High, apiKeySource none, permission denial0, read_console 호출만 있었다. 별도 AI/이미지/과금 호출은 없었다. 하지만 실제 init에서 허용 목록 밖 도구가 문맥에 남았다. 설치된 tool 이름을 확인해 경계를 보완한다. 기존 변경을 보존하고 불필요한 새 프레임워크나 전체 재분석을 하지 않는다.

책임은 `tools/ai/run_claude.py`, `tools/ai/test_handoff.py`, `ops/ai/runtime-policy.json`, `docs/AI_ENVIRONMENT.md`, `docs/AI_WORKFLOW.md`다. 구독/고정모델/자율성/이미지Codex/병렬/기존 파일 지침은 그대로다. 코드는 Codex가 독립 검토하고 재시험한다. 이번 작업은 이미지 생성이나 Editor 쓰기를 하지 않는다.

실제 unity-read의 exposed_not_allowed 17개:
`blender_bridge, debug_request_context, find_in_file, generate_audio, generate_image, generate_model, import_model, import_model_file, manage_camera, manage_graphics, manage_physics, manage_probuilder, manage_profiler, manage_script_capabilities, manage_tools, unity_docs, unity_reflect`.

1. `asset_gen`만 차단하면 설치본의 `generate_image/generate_model/generate_audio/import_model` 등이 문맥에서 빠지지 않는다. 이미지 생성 담당·유료 공급자 경계에 따라 실제 이름을 명시 deny한다. 위 나머지 미배정 도구도 현재 두 Unity 프로필에서는 정확한 deny로 제외한다. 이 작업은 새 도구 기능을 분석/확장하는 범위가 아니며 실제 기능이 필요해지면 무료 여부와 쓰기 성격에 따라 따로 배정하면 된다. CLI 규칙은 와일드카드 전체 허용 없이 정확한 이름을 사용한다.
2. `mcp_evidence`는 같은 서버 prefix의 미허용 도구도 노출되면 명확한 실패로 기록한다(`exposed_not_allowed`를 기록만 하고 통과시키지 않는다). 전송 연결 관찰과 Editor/화면 기능 성공은 구분한다. 현재 probe 결과의 노출 증거를 지우거나 성공 기록을 새 이름으로 바꾸지 않는다.
3. 기존 profile/policy 동기화 시험에 실제 이름 누락 및 같은 prefix unknown 노출 거절의 의미 있는 회귀 검사를 추가한다. 기존 billing/model/effort/lock/default동작 검사를 약화하지 않는다.
4. 문서에 최초 실제 read/Bash 성공과 위 보강 전 노출 한계를 역사로 짧게 기록한다. 이후 write/native probe는 아직 NOT_RUN이다. 결과는 변경·검증 안 한 점 위주로 짧게 보고한다.

이 CLI 호출에는 명시적으로 handoff 및 Python 시험을 허용할 예정이다. 허용 도구가 보이면 정확한 지정 시험을 직접 수행한다. 없거나 거부되면 NOT_RUN으로 정직하게 남기고 구현은 완료한다. 도구 제약 때문에 API·다른 모델·재귀 CLI를 쓰지 않는다.
