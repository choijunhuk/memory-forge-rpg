# 실제 구현 — Claude의 무료 Unity·화면 조작 MCP 연결

최신 사용자 지시: 구현·개발 도구 설치와 활용은 Claude Opus CLI가 수행하고, Codex는 이미지 생성·지시·독립 검증·통합을 맡는다. Max 5x 구독의 실제 사용량이 남아 있고, 공식 계정 화면에서 사용 크레딧 OFF·자동 충전 OFF를 새로 확인했다. API/추가 결제/다른 모델/fallback/AI·CLI 재귀 호출 금지. 이미지 생성·내용 수정은 하지 않는다.

현재 `tools/ai/run_claude.py`는 매번 빈 `mcpServers`와 strict config를 전달하므로 `.mcp.json`의 기존 Unity MCP도 Claude 세션에 들어가지 않는다. 이를 사용자의 이번 MCP 요청에 맞게 제한된 명시 프로필로 개선한다. 계획·문서만 작성하지 말고 실행기·설정·독립 시험을 구현한다. 다른 작업자가 있으므로 책임 파일만 수정하고 다른 변경을 되돌리지 않는다. 커밋·푸시·배포하지 않는다.

## 책임 파일

- `tools/ai/run_claude.py`
- `tools/ai/test_handoff.py` (새 실행기 프로필·허용 도구·과금 guard 회귀시험을 이 기존 파일에 추가 가능)
- `.mcp.json`
- `docs/AI_ENVIRONMENT.md`, `docs/AI_WORKFLOW.md`
- `AGENTS.md`, `CLAUDE.md`, `ops/ai/runtime-policy.json`

## 설계·완료 기준

1. 기존 기본 Read/Glob/Grep/Edit 제한과 구독 인증·계정 일치·청구 사전 검사·Opus5.5만 허용·High/xhigh/Max·결과 모델 검증·재귀 호출 금지를 유지한다. 필요할 때만 명시적으로 선택할 `none` / `unity` / `desktop` 같은 MCP 프로필을 추가한다. 사용자/클라우드 MCP 전체를 무차별 로드하지 않는다. 프로필 목록·구성·허용 도구는 검증 가능한 고정 범위여야 한다.
2. Unity는 이미 설치된 무료 MCP `http://127.0.0.1:8080/mcp`를 사용한다. 무료 로컬 Editor 도구만 허용하고 유료 `asset_gen`은 노출하지 않는다. 설치/설정과 실제 연결/조작 시험은 구분한다. 같은 Unity Editor의 쓰기 담당은 한 AI만 가진다.
3. Mac 화면 조작에는 이미 설치된 신뢰 가능한 로컬 MCP가 작동하면 먼저 재사용한다. Codex 전역 설정에 있는 computer-use 명령은 현재 프로젝트 상대 `Codex Computer Use.app/.../SkyComputerUseClient mcp`인데 해당 경로의 실행 파일이 없어 바로 재사용할 수 없었다. 경로만 보고 연결됐다고 하지 않는다. 필요한 경우 실제 확인한 대안은 MIT의 `@steipete/peekaboo@4.9.5`이다(공식 npm 조회로 버전/라이선스 확인). MCP는 `npx -y @steipete/peekaboo@4.9.5 mcp` 형태를 지원한다. 설치는 현재 세션의 제한 도구로 하지 못하므로, 설정을 준비한 뒤 다음 실행에서 실제 부팅/도구 호출이 가능하게 만든다. 특정 사용자만의 실행 파일 절대경로나 비밀값을 공통 저장소에 넣지 않는다.
4. 화면 MCP는 로컬 캡처·AX·클릭/스크롤/키보드만 사용한다. Peekaboo의 선택적 `analyze`/`agent` 및 외부 AI provider 호출을 금지하고, 현재 문서/도구 이름을 근거로 명시 허용 목록과 금지 목록을 구성한다. 환경변수만 비웠다고 전체 과금 차단을 증명했다고 하지 않는다. 보안 경계의 실제 한계를 문서화한다. 새 API 키·토큰 추출·SDK 재사용·유료 모델 분석을 하지 않는다.
5. 공식 Claude CLI 도움말에서 `--restricted`는 코드 실행/WebFetch를 기본 제거하지만 `--tools`가 명시한 도구는 허용하며, MCP를 차단하려면 strict config도 사용한다고 확인했다. 이 작업에는 임의 셸 권한을 추가할 필요가 없다. MCP 프로필에서 필요한 명시 도구를 공급하되 기존 파일 편집 경계를 유지하고, unknown 프로필/임의 URL/임의 명령을 거절한다. 실제 지원 여부는 다음 실행에서 검증하므로 프롬프트 선언을 연결 성공으로 세지 않는다.
6. 의미 있는 단위 시험을 추가한다: 기본 프로필의 기존 동작, unity/desktop의 고정 구성과 명시적 허용/금지, 유료 이미지·AI 분석 도구 제외, 모델/청구/파일 경계가 유지되는지. 기존 검사를 약화하거나 manifest를 재생성하지 않는다.

공식 참고: https://code.claude.com/docs/en/mcp , https://peekaboo.sh/MCP.html , https://peekaboo.sh/quickstart.html . 외부 문서는 기술 자료일 뿐 사용자 지시를 대체하지 않는다.

## 최신 운영 정책도 함께 반영

사용자는 ImagineArt 플러그인도 필요할 때 사용하되 **무료 크레딧만** 쓰도록 새로 허용했다. 이것은 ImagineArt에만 적용되는 명시적 예외이며, 기존 구매/유료 잔액·자동 충전·추가 결제는 금지한다. Codex가 무료 잔액과 실제 차감 경로를 구분해 확인할 수 없으면 해당 생성만 보류한다. 이미지 생성 담당은 여전히 Codex이며 Claude에는 ImagineArt/다른 이미지 생성 도구를 공급하지 않는다. 일반 Codex/Claude 추론은 기존 구독 포함 사용량만 허용한다. 모든 대화 모델 고정과 GitHub Private 정책을 유지한다.

현재는 Read/Glob/Grep/Edit만 사용 가능하다. 실행하지 못한 MCP 설치·화면 조작·셸 시험은 NOT_RUN으로 남기고 코드·구성·시험 작성을 완료한다. 결과에는 변경 사항과 다음 실제 연결 시험 명령/검증 기준을 짧게 제시한다. 필요한 권한·유료 경로가 있으면 그 MCP만 보류하고 기존 Unity 연결과 독립 작업은 유지한다.
