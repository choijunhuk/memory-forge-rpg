# Claude 독립 검토 — P1-JSON-CI-SUBSCRIPTION-RESUME-REVIEW

- 날짜: 2026-10-16
- 검토자: 공식 Claude Code CLI 세션. 모델 ID는 환경에 `claude-opus-5-5`로 표시되며, 요청된 노력 수준은 high다. 모델 자신의 진술은 인증·과금 증거가 아니다. 구독 경로, 노력 수준 적용 여부, session_id는 Root가 CLI 호출 인자와 세션 메타데이터로 기록해야 한다.
- 기준: Root `f673b780317f305d74c17c96958a630a7cebb268`. 비교 대상은 `review_inputs/json-ci-original/<path>` 대비 현재 제안 파일 3개다. worktree의 git diff는 사용하지 않았다.
- 편집 파일: 이 보고서만 편집했다.

## 판정: **APPROVE**

차단 결함은 없다. 아래 비차단 메모는 이번 패치에서 고치지 말고 Root 확인 항목으로 둔다.

## 정확한 변경 범위 (원본 대비 줄 단위 비교)

| 파일 | 줄 | 변경 |
|---|---|---|
| `client/MemoryForgeUnity/Packages/manifest.json` | 14–15 | 14행 끝에 `,`를 추가하고, 15행에 `"com.unity.nuget.newtonsoft-json": "3.2.1"`을 추가했다. 그 밖의 패키지와 버전은 동일하다. |
| `client/MemoryForgeUnity/Packages/packages-lock.json` | 211 | `com.unity.nuget.newtonsoft-json`의 `"depth": 1`을 `0`으로 바꿨다. version·source·url·dependencies와 나머지 435줄은 동일하다. |
| `.github/workflows/ci.yml` | 1 | 헤더 `Proposed CI gate (NOT run on GitHub in this delivery)`를 `Development CI gate; local Unity/device validation is recorded separately`로 바꿨다. `Pin reviewed action commits…` 문구는 유지된다. |
| `.github/workflows/ci.yml` | 23 | pip 명령에 `-r tools/ai/requirements.txt`를 추가했다. |
| `.github/workflows/ci.yml` | 35 | 마지막 단계로 `- run: npm run test:handoff`를 추가했다. |

버전 업그레이드, 새 패키지 공급자나 scopedRegistries, 규칙·데이터 변경, 시험 약화, 모델 설정 변경, 과금 설정 변경은 없다.

## 근거

### 1. 패키지 해석 의미 (manifest.json:15, packages-lock.json:211)
- 직접 의존이 필요하다. 런타임 `Assembly-CSharp`가 Newtonsoft를 직접 사용한다(`Assets/Scripts/ClientRunContent.cs:11-12`, `ClientBattleContent.cs:8`, `MemoryForgeExpeditionClient.cs:7`, `MemoryForgeBattleClient.cs:56`). PlayMode 시험 다수도 같은 패키지를 사용한다. 원본에서는 이 패키지가 개발 도구인 `com.coplaydev.unity-mcp`(lock 15행, `3.0.2` 요구)를 통한 전이 의존으로만 들어왔다. 그래서 MCP를 제거하거나 버전을 바꾸면 런타임 컴파일이 깨질 수 있었다. 직접 승격하면 이 결합이 끊긴다.
- 해석 결과는 바뀌지 않는다. 원본 lock에서도 이미 `3.2.1`이 선택돼 있었다. MCP가 요구하는 `3.0.2`보다 높은 버전이 채택된 상태였다. 프로젝트 manifest의 직접 버전 `3.2.1`은 이 해석 결과와 같으므로 설치 버전과 다른 패키지의 해석은 그대로다. 직접 의존의 lock depth는 0이므로 211행 변경은 정확하다.
- 출처도 그대로다. `source: registry`, `url: https://packages.unity.com`이 유지된다.
- asmdef 참조: `Assets/Tests/{Editor,PlayMode}/*.asmdef`에는 `overrideReferences`나 `precompiledReferences`가 없다. 따라서 자동 참조되는 `Newtonsoft.Json.dll`이 계속 포함된다. asmdef는 수정할 필요가 없다.

### 2. 기존 PDF 시험의 의존성 (ci.yml:23)
- `tools/ai/test_handoff.py:36,41,50`은 `fitz`(PyMuPDF)와 `pypdf`를 import한다. 원본 CI의 pip 명령(`openapi-spec-validator`, `requirements-docs.txt` = markdown-it-py·beautifulsoup4·weasyprint)으로는 이 두 패키지가 설치되지 않는다. 따라서 `tools/ai/requirements.txt`(reportlab<5, pypdf<7, PyMuPDF<2)를 추가해야 하고, 그것으로 충분하다. 해당 파일은 기존에 있던 파일이며 AI API 의존성이 없다(1행 주석).
- weasyprint 68.0과의 명백한 버전 충돌은 보이지 않는다. 실제 해결 여부는 CI에서 확인해야 한다(NOT_RUN).
- PDF 대상의 결정성: CI의 `run_all_checks.sh` `gen_docs` 단계는 `build_integrated_design.py`를 `--pdf` 없이 실행한다(25행, `build_integrated_design.py:107`). 그래서 PDF를 다시 만들지 않고, `test:handoff`는 커밋된 `docs/Memory_Forge_v0.8.1_Restored_Design.pdf`와 `Memory_Forge_Complete_Design.pdf`를 비교한다. 결과는 결정적이며, 커밋된 PDF가 서로 어긋나면 CI가 잡아낸다.

### 3. CI 권한과 환경 (ci.yml 전체)
- `permissions: contents: read`는 그대로다. secrets, env, 토큰, 모델 호출 단계를 추가하지 않았다.
- `test_handoff.py`의 docstring은 "no model calls, credentials, cloud access, or payments"라고 밝힌다. 실제 코드도 로컬 파일과 `git ls-files`만 읽는다(`tools/ai/delivery_files.py:26`).
- 마지막 단계로 둔 것은 적절하다. 앞 단계에서 생기는 파일은 `reports/`, `prototype/tests/shots/`(ignore), `__pycache__/`(ignore)다. `fonts-nanum`은 저장소 밖에 설치되므로 `test_no_font_binaries_or_credentials_shipped`를 오탐하게 할 경로가 보이지 않는다.
- 1행 헤더: 새 문구는 "GitHub에서 통과했다"고 주장하지 않는다. Unity·실기기 검증을 별도 기록으로 구분하므로 자동 시험과 수용 증거의 구분 원칙에 맞다. 다만 GitHub Actions에서 실제로 실행·통과한 기록이 생기기 전에는 보고서에 CI PASS라고 적지 않아야 한다.

### 4. 과금·담당자 정책 시험 유지
현재 worktree의 `tools/ai/test_handoff.py`를 기준으로 확인했다. 다음 시험이 그대로 남아 있으며, 이 패치로 CI에서 처음 강제된다.
- `test_shared_policy`(14–17행): image owner Codex, fallbacks 없음, api/extra/purchased 모두 false, `actual_cli_call_verified` false
- `test_active_entrypoints_contain_billing_and_owner`(18–20행)
- `test_auth_precedes_model_call`(21–24행)

이 패치는 정책 시험을 약화하지 않고 오히려 강화한다. 단, Root에 동기화된 것은 검토 입력 3개뿐이다. Root의 `test_handoff.py`와 `ops/ai/policy.json`이 이 worktree 사본과 같은지는 Root가 확인해야 한다.

## 비차단 메모 (이번 패치 범위 밖, 변경 요구 아님)
1. `manifest.json:15` — 키가 알파벳 순서가 아니다. 원본 14행의 MCP 항목도 이미 순서 밖이었다. Unity가 나중에 manifest를 다시 쓰면 순서가 바뀔 수 있지만 해석에는 영향이 없다. Root가 Editor resolve를 마친 뒤 manifest와 lock에 추가 diff가 없는지 확인하길 권한다.
2. 원본 lock에도 manifest와 다른 값이 이미 있었다. `com.unity.test-framework` 1.5.1과 `render-pipelines.universal` 17.0.4는 Unity 6 builtin이라 editor에 고정된 버전이다. 이번 패치와 무관하므로 고치지 않는다.
3. `tools/ai/requirements.txt`는 범위 지정이고, `requirements-docs.txt`는 정확한 버전 고정이다. 재현성을 높이려면 이후 별도 작업으로 다루는 편이 낫다. 이번에 고정을 추가하면 범위가 넓어진다.
4. `test_shared_policy`는 `actual_cli_call_verified == false`를 검사한다. 이번 구독 협업 재개 호출이 확인되더라도 시험을 통과시키려고 policy를 그대로 두거나, 반대로 policy만 true로 바꾸지 말아야 한다. 실행 증거는 `docs/AI_ENVIRONMENT.md` 같은 곳에 기록하고, 플래그 의미를 바꾸려면 policy와 시험을 함께 별도 검토해야 한다.
5. 커밋 전에 Root가 `git ls-files`로 두 PDF와 `START_HERE.txt`가 추적되는지 확인해야 한다. 이 세션에서는 Bash를 쓸 수 없어 worktree에 파일이 있는 것만 확인했다.

## 실행 상태
| 항목 | 상태 |
|---|---|
| 원본 대비 3개 파일 줄 단위 비교, 관련 소스·스크립트 정적 확인 | 수행 (Read/Grep/Glob만 사용) |
| Unity Editor package resolve, 컴파일, Edit/Play 시험 | NOT_RUN (Root 담당) |
| Android/네이티브 빌드 | NOT_RUN (Root 담당) |
| `pip install …`(CI 23행) 의존성 해결 | NOT_RUN |
| `npm run test:handoff`, `npm run test:docs`, `npm run docs`, `tools/run_all_checks.sh` | NOT_RUN (Bash 금지) |
| GitHub Actions 실제 실행 | NOT_RUN |
| `git ls-files`로 PDF 추적 여부 확인 | NOT_RUN |
| 구독 인증·과금 설정 확인 | NOT_RUN. 이 세션의 권한 밖이다. 모델의 자기 진술은 증거가 아니다. |

에이전트 호출, 이미지 작업, MCP, API·크레딧 경로, 자격 증명 읽기는 모두 사용하지 않았다.
