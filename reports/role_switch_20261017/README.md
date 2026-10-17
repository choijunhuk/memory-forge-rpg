# 역할 전환 수용 기록 — Claude 루트, Codex는 이미지·캐릭터 외형·읽기 전용 리뷰 (2026-10-17)

## 요구와 결정

- 사용자 지시: 개발은 Claude가 맡고, Codex는 이미지 생성·내용 수정, 캐릭터 외형 최종 디자인, 읽기 전용 리뷰만 맡는다. Codex의 SAVE-PARTS(`0623d61`) 커밋 뒤에 적용한다. Codex의 마지막 루트 단위 BATTLE-CONTROLS는 `d50e0ec`로 끝났다.
- 루트 세션 잠금 방식은 사용자가 (가)를 골랐다. Desktop 루트 세션을 유지하고, 세션 JSONL 감사로 커밋·수용을 막는다.
- 기준 문서는 `docs/AI_WORKFLOW.md` §17이다. 기계 판독용 정책은 `ops/ai/runtime-policy.json`의 `work_routing`이다.

## 변경 파일

- `docs/AI_WORKFLOW.md`: §17 신설(역할, 호출 방향, 리뷰 계약, 루트 세션 운영 1–6).
- `AGENTS.md`, `CLAUDE.md`, `docs/START_CLAUDE.md`, `README.md`: §17이 우선한다는 절. 예전 서술은 이력으로 보존했다.
- `ops/ai/runtime-policy.json`:
  - `work_routing` 갱신: 루트, Codex 3개 전담과 금지, 호출 방향, 민감 경로 리뷰, `root_denied_tools`, `root_allowed_mcp_servers`, `root_session_controls`, `root_session_audit`.
  - 실행기 문구 2곳을 루트 기준으로 고쳤다.
  - `claude_autonomy`의 `codex_keeps`와 `adoption_decision`을 바꿨다.
- `tools/ai/audit_root_session.py` (신규): `--start-check`과 세션 JSONL 감사.
- `tools/ai/test_handoff.py`: 역할 배분 테스트, 감사 테스트(재현 사례마다 서브테스트) 추가.
- `ops/ai/tasks/ROLE-SWITCH-CODEX-REVIEW.md`, `.result.json`: Codex 호출과 회차별 판정.
- 로컬 전용 `.claude/settings.local.json`(Git 제외): 생성 도구 7개 deny, `mcp__unityMCP` allow.

## 검증

| 항목 | 결과 |
|---|---|
| `tools/ai/test_handoff.py` | 33/33 OK. 실행 환경은 scratch venv이고 `tools/ai/requirements.txt`와 `tools/audit/requirements-docs.txt`를 설치했다 |
| `npm run test:docs` | OK |
| `audit_root_session.py --start-check` | pass. 인증 claude.ai/firstParty, 공급자 환경 깨끗함, 설정 고정, deny 적용, Codex ChatGPT 로그인 |
| 루트 세션 감사 | [root_session_audit.json](root_session_audit.json): pass. 위반 0, 예외 33, Codex 호출 6건, 권한 모드 `bypassPermissions` |
| deny 실효 | 적용 직후 생성 도구 6개가 실제 세션의 도구 목록에서 빠진 것을 관찰했다 |
| Unity | MCP 읽기(editor state, Console 오류 0)를 확인했다. computer-use로 Unity 탭 클릭이 됨을 확인했고 원래 상태로 되돌렸다 |

## 예외 33건의 사유

모두 감사 규칙이 생기기 전이거나 더 엄격해지기 전의 이력이며, 각 사유는 감사 보고서에 그대로 있다.

- **세션 시작 때 deny 대상 생성 도구 6개 노출:** deny 파일이 생기기 전이다. deny를 적용하자 빠졌고, 한 번도 호출하지 않았다.
- **Codex 읽기 전용 리뷰 6건이 시작 확인 전으로 판정됨:**
  - 3건은 시작 확인 명령이 생기기 전이다. Codex 인증은 별도 명령으로 확인했다.
  - 3건은 복합 명령으로 돌린 시작 확인(결과는 pass) 뒤에 실행했다. "단독 명령만 인정" 규칙은 그 뒤에 추가됐다.
- **잠금 보조 함수 자체 시험 1건:** 소유자 `claude-root-locktest`로 한 명령 안에서 획득, 재획득 거부, 해제를 했다. Editor 쓰기는 없었다.
- **Unity 클릭 시연 1건:** Project 탭을 눌렀다가 Console 탭으로 되돌렸다. 프로젝트 변경은 없다.
- **MCP 점검 뒤 추가한 deny 대상의 이전 노출 16건:**
  - 7개(원격 유료 기기, 외부 리뷰 실행, `blender_bridge`, `delete_repository`)는 deny 적용 뒤 세션에서 빠졌다.
  - claude.ai 커넥터 Manyfast의 생성 도구 9개는 deny를 넣어도 세션에서 빠지는 것이 관찰되지 않았다. 그 서버는 허용 목록 밖이라 호출하면 감사 위반이 된다. 완전히 없애려면 사용자가 claude.ai 커넥터 설정에서 끈다.
  - 모두 호출한 적은 없다.
- **deny 확인 1건:** 해롭지 않은 변수로 `printenv SHELL`을 시도했다. 실행 전에 permissions.deny로 거부됐고 아무것도 출력되지 않았다.

## Codex 독립 리뷰 (읽기 전용, gpt-6.1-sol, high)

역할 문서는 3회차에서 4건이 해결되고 1건이 일부만 해결됐다. 남은 1건은 루트 세션 잠금 방식이었고, 사용자 결정 (가)로 정리했다.

감사 스크립트는 3회차에서 최종 판정이 났다.
- **해결:** 자격 증명 경로 정규화, 인증 종료 코드 확인, 감사 경계 문서화.
- **한계로 인정(real_constraint):** 인터프리터 코드의 의미 분석, 의도적 회피.
- **최종 판정 뒤 수정했지만 Codex 재리뷰는 받지 않음** (§10 보류 규칙). 각 항목에 회귀 서브테스트를 붙였다.
  - 잠금 해제 순서
  - `env`·`sudo` 래퍼
  - `bash -lc`와 리다이렉션이 붙은 셸 heredoc
  - 인증 파일·공급자 변수 변경 시 시작 확인 무효화
  - 깨진 중첩 레코드
  - 작은따옴표 안 치환과 Grep 검색어 오탐 2건

thread ID와 사용량은 `ops/ai/tasks/ROLE-SWITCH-CODEX-REVIEW.result.json`에 있다.

## 보안 리뷰 후속 (자동 보안 리뷰 지적)

- **지적:** `bypassPermissions` 루트 세션에서는 Bash가 실행 전에 제한되지 않고, 감사는 사후 확인뿐이다.
- **보강:** `root_denied_permissions`를 `.claude/settings.local.json`의 deny에 추가했다. 대상은 자격 증명 파일 읽기, `security find-*-password`·`dump-keychain`, `printenv`, `claude --bare`이며, 시작 확인이 적용 여부를 검사한다.
- **실측:** bypass 모드에서도 `printenv SHELL`이 실행 전에 거부됐다.
- **2차 보강 (같은 리뷰의 후속 지적):** `env`와 `/usr/bin/` 전체 경로 형태를 deny에 추가했다. 정책 키 `root_denied_permissions`가 없으면 시작 확인이 통과하지 않고 오류로 실패한다.
- **남는 위험:** 접두어 deny는 복합·간접 형태로 우회될 수 있다. 권한 모드는 사용자가 정한다. `default`·`acceptEdits` 모드나 OS 샌드박스가 더 강하다는 점을 §17과 `permission_mode_tradeoff`에 적었다. 시작 확인은 모드 때문에 실패시키지 않는다.

## 감사 미검증 (수용을 막지 않음)

- 첫 도구 목록
- MCP 하위 프로세스 환경
- 실제 추론 환경과 확인한 셸 환경의 일치
- 인터프리터 코드의 의미
- 계정 화면의 Usage credits/Extra usage OFF

## 사용자 판정·후속

- computer-use의 앱 허용 창은 세션마다 뜨고, 이 앱 설정으로는 상시 허용으로 바꿀 수 없다. Unity 작업은 Unity MCP 위주로 한다.
- **MCP 점검·추가 설치** (별도 세션, [보고서](../mcp_setup_20261017/README.md)):
  - `serena` 1.7.0(C# 기호 탐색·진단)과 `context7` 4.1.1(최신 라이브러리 문서)을 local 범위에 설치했다. 루트 세션이 `claude mcp list`에서 둘 다 Connected임을 다시 확인했다.
  - 이 프로젝트에서 고장 난 플러그인 serena와 desktop-commander를 껐다.
  - 허용 서버·deny 목록을 반영했다. 추가된 deny는 원격 유료 기기, 외부 리뷰 실행, 생성형 도구, `delete_repository`다.
  - `.serena/`를 `.gitignore`에 넣었다.
  - 새 서버는 다음에 여는 세션부터 보인다.
- 머지는 하지 않았다. 브랜치는 Codex 시기의 PR #2(`codex/late-actions-balance`)와 같다.
