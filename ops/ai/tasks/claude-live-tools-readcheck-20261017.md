# 실제 지정 시험·Unity MCP 읽기 호출

새 실행기/공통 지침은 Codex 독립 검토 후 Python57/57, handoff31/31을 통과했다. 이 작업은 설정 완료를 주장하기 위한 실제 도구 시험이다. Opus5.5·기존 구독만, 이미지/다른 AI/과금·비밀 접근 금지. 코드를 편집하지 않는다. 전체 문서 재분석이나 장문 계획 없이 아래 호출을 실제 실행하고 결과를 짧게 보고한다.

1. 허용된 `npm run test:handoff`를 Bash로 정확히 한 번 실행한다. Codex가 PDF venv PATH를 제공했다. 명령을 감싸거나 다른 셸 명령/리다이렉트/추가 옵션을 붙이지 않는다. 실제 완료 여부와 통과/실패 수를 기록한다.
2. 연결된 unityMCP 리소스에서 `mcpforunity://editor/state`를 읽어 실제 Unity 버전/인스턴스/non-batch·Play 상태를 확인한다. 리소스 도구가 없으면 이 항목만 NOT_RUN이라 한다.
3. `mcp__unityMCP__read_console`을 action=get, types=[error], count=5로 한 번 호출한다. Console을 지우거나 Editor·씬을 바꾸지 않는다. 현재 Editor 쓰기는 Codex가 가지고 있다. 단순 포트/설정 확인을 연결 성공으로 세지 말고 실제 MCP 결과를 근거로 보고한다.

필요한 MCP/셸 도구가 없거나 권한이 거부되면 회피 도구·다른 모델·API·재귀 CLI를 쓰지 말고 정확한 거부를 보고한다. 독립적인 1–3항은 계속 수행한다. 결과는 실행한 도구·판정·한계만 짧게 적는다.
