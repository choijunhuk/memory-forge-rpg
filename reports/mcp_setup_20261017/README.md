# Claude 루트 세션 MCP 점검·설치 보고 (2026-10-17)

- 작성: Claude Opus 5.5 side session (Desktop, 구독 로그인). 측정 시각 2026-10-17 18:58–19:07 KST.
- 범위: Memory Forge 루트 세션이 쓰는 MCP 서버의 설치 상태와 실제 호출 결과를 확인했다. 빈칸을 분석하고, 무료 서버 2개를 local 범위에 설치·검증했다.
- 이 저장소에서 쓴 파일은 이 보고서 하나다. 프로젝트 `.mcp.json`, `.claude/*`, runtime-policy, 문서는 수정하지 않았다. 커밋은 하지 않았다.
- Unity Editor에는 읽기 호출만 했다. Play·씬·프리팹 변경, `execute_code`, `manage_tools`는 호출하지 않았다.
- "설치됨"은 `claude mcp list`(새 프로세스)의 상태다. "호출"은 이 Desktop 세션에서 실제 도구를 부른 결과다.

## 1. 현재 상태 (설치 전 실측)

| 서버 (도구 접두사 `mcp__<server>__`) | 출처·버전 | 범위 | `claude mcp list` (새 CLI 프로세스) | 실제 호출 (이 Desktop 세션) | 판정 |
|---|---|---|---|---|---|
| `unityMCP` | 프로젝트 `.mcp.json` HTTP `127.0.0.1:8080/mcp`. Unity 패키지 `com.coplaydev.unity-mcp` git `#aa5fc638…`. 서버 `mcpforunityserver 10.3.0`(uvx) | project | ⏸ `Pending approval (run claude to approve)` | ✅ `mcpforunity://editor/state`: `MemoryForgeUnity@4034a4e2fe236d6e`, 6000.0.46f1, scene `MemoryForgeServerPractice`, not playing, not compiling, `ready_for_tools: true`. ✅ `mcpforunity://instances`: 1개. ✅ `read_console get`(error) 0건. ✅ `unity_reflect` `UnityWebRequest.SendWebRequest` 시그니처. ✅ `validate_script` `PracticeServerSession.cs` errors 0 / warnings 0 | 동작. 대화형 CLI 승인만 남음(§4) |
| `computer-use` | Claude Desktop 내장 | — | (목록에 없음, 앱 내장) | ✅ `list_granted_applications`: `allowedApps: []`, clipboard read/write false. ✅ `list_apps`: Unity(pid 97768)·Unity Hub 실행 중 | 준비됨. 클릭은 미시험: 앱별 `request_access`에 사용자 승인이 필요하다 |
| `mobile` | `@mobilenext/mobile-mcp@1.0.6` (2026-09-30) | user | ✔ Connected | 처음에는 기기 0대(`adb devices` 비어 있음, AVD 3개). `Small_Phone`을 `-read-only -no-window`로 부팅했다(21초). ✅ `mobile_list_available_devices`: `Small_Phone` android 16 online. ✅ `mobile_get_screen_size`: 720x1280. ✅ `mobile_list_elements_on_screen`: 홈 화면 요소 48개. 확인 후 `adb emu kill`로 종료했다 | 에뮬레이터 인식 확인. 실기기는 미시험 |
| `plugin_serena_serena` | plugin `serena@claude-plugins-official`, `uvx --from git+https://github.com/oraios/serena`(**버전 미고정**, git HEAD = 2.0.1.dev0) | plugin | ✔ Connected | ❌ C# LS 기동 실패: `Required .NET runtime version 10.0 not found (installed versions: [9.0.3])`. PATH의 `dotnet`은 9.0.201/9.0.3이다 | **C# 동작 안 함** → §2에서 대체 설치 |
| (Claude Code 내장 LSP) | — | — | — | ❌ `No LSP server available for file type: .cs` | C# LSP 없음 |
| `chrome-devtools` | `chrome-devtools-mcp@1.10.1 --isolated=true` (2026-09-23) | user | ✔ Connected | ✅ `list_pages`: `about:blank` 1개 | 동작 |
| `plugin_playwright_playwright` | plugin, `npx @playwright/mcp@latest`(**미고정**, 현재 0.0.83, 2026-09-28) | plugin | ✔ Connected | ✅ `browser_tabs list` | 동작. 버전 미고정 |
| `github` | 원격 `api.githubcopilot.com/mcp/`, `headersHelper` | user | ✔ Connected | ✅ `get_me`: login `choijunhuk` | 동작. `delete_repository`·`merge_pull_request`·`push_files` 같은 쓰기 도구를 포함한다 |
| `plugin_desktop-commander_desktop-commander` | plugin, `npx -y @wonderwhy-er/desktop-commander@latest` → 0.3.0 (2026-10-13, **4일 경과**) | plugin | ✔ Connected | ✅ `get_config`: `telemetryEnabled: true`, `allowedDirectories: []`(전체 접근), 셸 실행 도구 포함 | 동작. 2주 규칙·텔레메트리·범위 면에서 부적합(§3·§5) |
| `plugin_context7_context7` | plugin, 원격 `mcp.context7.com` | plugin | ! Needs authentication | 호출 불가 | 인증 전 사용 불가 → §2 local 설치로 대체 |
| `plugin_pdf-viewer_pdf` | plugin, `npx -y @modelcontextprotocol/server-pdf`(미고정, 현재 2.0.3, 2026-09-25) | plugin | ✔ Connected | ✅ `list_pdfs`: 저장소 PDF 16개. 허용 디렉터리는 저장소와 notes 폴더뿐이다 | 동작 |
| 기타 노출 서버 | `plugin_greptile_greptile`(외부 유료 리뷰 SaaS) ✔, `lazyweb` ✔, `mobbin`·`nicelydone`·`refero`(디자인 SaaS) 인증 필요, claude.ai 커넥터(Claude Docs·Notion·Google Drive·manyfast), bio-research·sales 등 업무 플러그인 다수 | user/plugin | 혼재 | 호출하지 않음 | 이 프로젝트 개발에 불필요 |

참고: `unityMCP`의 `Pending approval`은 터미널 `claude` 대화형 세션에만 해당한다. Desktop 세션에서는 위와 같이 연결돼 있다. runner의 `--strict-mcp-config --mcp-config`(unity-read/unity-write)는 명시 설정이므로 이 승인과 무관하다.

## 2. 설치한 서버

두 서버 모두 `claude mcp add -s local`로 설치했다. 설정은 `~/.claude.json`의 `projects["/Users/choi/Documents/Memory_forge"].mcpServers`에 있고 이 프로젝트에만 적용된다. 이미 열려 있는 세션에는 붙지 않는다. **다음에 새로 여는 세션부터** 도구가 보인다.

| 이름 (접두사) | 정확한 버전 (게시일) | 범위 | 출처 | 라이선스 | 검증 결과 |
|---|---|---|---|---|---|
| `serena` (`mcp__serena__`) | PyPI `serena-agent==1.7.0` (2026-08-09, 69일). 실행: `/opt/homebrew/bin/uvx --from serena-agent==1.7.0 serena start-mcp-server --context claude-code --enable-web-dashboard false --open-web-dashboard false`. env: `PATH=/Users/choi/.dotnet:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin`, `DOTNET_ROOT=/Users/choi/.dotnet` | local | oraios/serena (공식 배포자, PyPI) | GPL-3.0-or-later (SolidLSP 부분은 MIT). 로컬 사용만 하고 배포하지 않는다 | 새 프로세스 `claude mcp list`: **✔ Connected** (19:07 KST). stdio 직접 호출로 Unity 소스 사본을 검증했다(아래) |
| `context7` (`mcp__context7__`) | npm `@upstash/context7-mcp@4.1.1` (2026-09-14, 33일). 실행: `npx -y @upstash/context7-mcp@4.1.1` | local | upstash/context7 (공식 배포자) | MIT | 새 프로세스 `claude mcp list`: **✔ Connected** (19:03, 19:07 KST). stdio 호출: `serverInfo Context7 4.1.1`, 도구 `resolve-library-id`·`query-docs`. `resolve-library-id("Npgsql")` → `/websites/npgsql_doc` 등을 반환했고 `isError` 없음. **API 키 없이 동작** |

### serena 검증 상세

- C# LS가 요구하는 .NET 10 런타임은 사용자 범위 `~/.dotnet`에 이미 있다(SDK 10.0.401 / runtime 10.0.12, 2026-09-08 릴리스). 이 경로는 PATH에 없다. 서버 env에만 이 PATH를 넣었으므로 시스템·셸 설정과 전역 `dotnet`(9.0.201)은 바꾸지 않았다.
- 저장소에 `.serena/`가 생기지 않도록 Unity 프로젝트의 `Assets`·`.sln`·`.csproj`를 scratch로 복사했다. `Library`·`Packages`는 심볼릭 링크로 두고 이 사본에서 시험했다.
  - `get_symbols_overview Assets/Scripts/PracticeServerSession.cs` → `{"Namespace": ["MemoryForge.Client"]}` (8.9초, 첫 기동 포함)
  - `find_symbol PracticeServerSession depth=1` → Class 191–801행과 상수·필드 목록
  - `find_referencing_symbols MemoryForge.Client/PracticeServerSession` → `MemoryForgeServerPracticeClient.Connect`·`OpenAndRefresh` 등 다른 파일의 참조
  - `get_diagnostics_for_file … min_severity=1` → `{}`. 오류 0건이므로 UnityEngine 참조 해석이 정상이다
- 시험 뒤 serena가 `~/.serena/serena_config.yml`의 `projects`에 추가한 scratch 경로 1줄은 제거해 원상 복구했다.
- 플러그인 serena와 같은 저장소 계열이다. 플러그인은 git HEAD 미고정이고 PATH의 .NET 9 때문에 C#이 안 된다. local `serena`가 이를 대체한다(§4-2).

### 공통 한계

- 고정한 것은 최상위 버전뿐이다. `npx`·`uvx`는 하위 의존성을 범위 지정자로 해석하므로 전이 의존성까지 고정되지는 않는다.
- context7은 질의 문자열(라이브러리 이름·질문)을 context7.com에 보낸다. 무료 익명 한도(속도 제한)로 쓰고 키는 넣지 않았다. **질의에 코드·비밀값·내부 설계 원문을 넣지 않는다.**

## 3. 빈칸 분석과 거절한 후보

| 작업 | 현재 수단 | 빈칸 | 결과 |
|---|---|---|---|
| Unity C# 탐색·참조·리팩터 | 없음(플러그인 serena 실패, 내장 LSP 없음) | 기호 단위 탐색·진단 | **serena 1.7.0 local 설치** |
| Unity API 검증 | `unityMCP` `unity_reflect`·`unity_docs`·`validate_script` | 없음 | 기존 유지 |
| .NET 서버(net10.0, Npgsql 10.0.3)·JS 라이브러리 문서 | 학습 데이터 | 최신 API 문서 | **context7 4.1.1 local 설치** |
| PostgreSQL 조회 | `ServerChecks` + psql wrapper, 저장소 밖 owner-only 연결 파일 | DB MCP | 거절(아래) |
| JS 엔진·HTML | `node --test`, playwright, chrome-devtools(`lighthouse_audit` 포함), 내장 브라우저 | 없음 | 기존 유지 |
| Android 출시·시험 | `mobile`(에뮬레이터 검증), `unityMCP manage_build`, adb | 실기기 연결 | 사용자 조치(§4) |
| PDF 문서 | pdf-viewer, `npm run docs` | 없음 | 기존 유지 |
| GitHub·CI | `github` MCP(`actions_*`, `get_job_logs`) | 없음 | 기존 유지 |

거절한 후보:

| 후보 | 이유 |
|---|---|
| PostgreSQL MCP (`postgres-mcp`, `@modelcontextprotocol/server-postgres` 등) | 서버 설계는 연결 정보를 저장소 밖 `chmod 600` 파일에만 둔다. DB MCP는 DSN을 `~/.claude.json` env에 평문으로 저장하므로 이 설계를 깬다. Memory Forge용 개발 DB 컨테이너도 지금 없다(실행 중인 postgres는 다른 프로젝트 것). 공식 `server-postgres`는 보관(archived) 상태이고 읽기 전용 우회 취약점 이력이 있다 |
| Microsoft Learn MCP (`learn.microsoft.com/api/mcp`) | 공식·무료지만 원격 서비스라 **버전 고정이 불가능**해 기준에 어긋난다. context7과 겹친다 |
| NuGet MCP (Microsoft `NuGet.Mcp.Server`) | `dnx` 실행에 .NET 10 SDK가 PATH에 있어야 한다. 필요해지면 serena와 같은 env 방식으로 재검토한다 |
| `csharp-ls` / Claude Code `csharp-lsp` 플러그인 | MCP가 아니다. `csharp-ls` 0.28.0은 `tools/net10.0`이고 PATH 변경이 필요하다. serena와 겹친다 |
| serena 2.0.0 / plugin git HEAD | 2.0.0은 2026-10-13 게시(2주 미만)이고, HEAD는 미고정이다 |
| Unity 공식 AI(Assistant/Gateway) | Unity AI 포인트 과금 |
| 다른 Unity MCP(IvanMurzak 등) | `unityMCP`와 중복되고 같은 Editor를 이중으로 쓸 위험이 있다 |
| `unityMCP` asset_gen 그룹 | 생성형(BYOK)이라 이미지·모델은 Codex 전담이다 |
| mobile-mcp 원격 기기 | 서버 지침에 "Remote devices cost money"라고 명시돼 있다 → deny 제안(§6) |
| mobile-mcp 1.0.8 업그레이드 | 현재 1.0.6이 정상 동작하므로 바꿀 이유가 없다 |
| git·filesystem·sequential-thinking·memory MCP | Bash·Read·claude-mem과 중복 |
| desktop-commander 유지·고정 | Bash·Read·Write와 중복. 텔레메트리 켜짐, 전체 디렉터리 접근, `@latest` 0.3.0(4일)이다. 루트 허용에서 빼는 것을 제안한다 |

## 4. 사용자·루트 세션 조치

### 사용자 조치

1. **새 세션 열기**: local 서버 `serena`·`context7`은 지금 열린 세션에는 없다. 새 Desktop 세션이나 `claude`를 열면 붙는다.
2. **플러그인 중복 정리(권장)**: 플러그인 `serena`(C# 불가·미고정)와 `desktop-commander`(텔레메트리·전체 접근·4일 된 버전)를 끈다. 플러그인 `context7`은 OAuth가 필요 없다(local `context7`로 대체).
   - 이 프로젝트에서만 끄려면 아래 명령을 쓴다. `-s local`은 `.claude/settings.local.json`의 `enabledPlugins`를 바꾼다. 이 파일은 루트 세션이 편집 중이므로 **루트 세션과 순서를 맞춰** 실행한다.

     ```bash
     claude plugin disable serena@claude-plugins-official -s local
     ```

     ```bash
     claude plugin disable desktop-commander@synced -s local
     ```

   - desktop-commander를 계속 쓴다면, 사용자가 텔레메트리를 끄고 허용 디렉터리를 제한한다. 이 작업은 사용자가 직접 승인한다.
3. **unityMCP CLI 승인(터미널 사용 시만)**: 저장소 루트에서 대화형 `claude`를 열면 `.mcp.json`의 `unityMCP` 승인 프롬프트가 뜬다. 거기서 승인한다. Desktop 세션과 runner에는 필요 없다.
4. **Android 실기기 시험**: 기기에서 개발자 옵션 → USB 디버깅을 켜고 USB로 연결한다. 기기에 뜨는 RSA 지문을 허용한다. 그다음 `adb devices`에 `device`가 표시되는지 확인한다.
5. **computer-use 클릭**: 첫 사용 때 뜨는 앱 접근 대화상자에서 Unity를 허용한다(세션별). macOS 손쉬운 사용·화면 기록 권한이 Claude 앱에 있어야 한다. 이는 시스템 설정이라 사용자만 바꿀 수 있다.
6. **(선택) 노출 축소**: 이 프로젝트에 쓰지 않는 업무 플러그인(bio-research, sales, marketing, legal, finance, zoom 등)과 claude.ai 커넥터를 끈다. 그러면 감사 스크립트의 "노출됐지만 허용 목록 밖" 경고가 줄어든다.

### 루트 세션 조치

- serena를 처음 쓸 때는 `activate_project`에 `/Users/choi/Documents/Memory_forge`(또는 `client/MemoryForgeUnity`)를 넘긴다. 이때 해당 폴더에 `.serena/project.yml`과 캐시가 생기며, 이 경로는 `.gitignore`에 없다. **활성화 전에 `.serena/`를 `.git/info/exclude`(또는 `.gitignore`)에 넣을지 결정한다.**
  - 저장소 루트로 활성화하면 server·core·tools·client의 `.csproj` 여러 개를 한 번에 읽는다. 첫 인덱싱은 수십 초 걸릴 수 있다.
- context7에는 공개 라이브러리 질의만 넣는다.

## 5. 제안 1 — `work_routing.root_allowed_mcp_servers`

현재 초안(runtime-policy 작업본) 대비 변경:

- 추가: `serena`, `context7`, `plugin_pdf-viewer_pdf`
- 제외: `plugin_desktop-commander_desktop-commander`(§3), `plugin_serena_serena`(local `serena`로 대체. 플러그인을 끄기 전까지는 감사에 "노출됨" 경고로 남는 것이 의도된 신호다)

제안 값:

```json
["ccd_turn", "ccd_session", "ccd_session_mgmt", "ccd_view", "ccd_pr", "ccd_host", "ccd_settings", "ccd_sidebar", "ccd_window", "ccd_directory", "ccd_connectors",
 "unityMCP", "computer-use", "Claude_Browser", "claude-in-chrome", "chrome-devtools", "plugin_playwright_playwright",
 "github", "serena", "context7", "mobile", "terminal", "visualize", "plugin_pdf-viewer_pdf",
 "plugin_claude-mem_mcp-search", "plugin_oh-my-claudecode_t"]
```

- `claude-in-chrome`은 사용자의 실제 Chrome이다. 사용자가 요청할 때만 쓰는 조건으로 유지를 제안한다.
- 허용 목록 밖 노출 서버(참고): `plugin_greptile_greptile`, `lazyweb`, `mobbin`, `nicelydone`, `refero`, `plugin_context7_context7`, `plugin_desktop-commander_desktop-commander`, `plugin_serena_serena`, claude.ai 커넥터 UUID 접두사 4개, `scheduled-tasks`, `mcp-registry`, `Claude_Code_iOS_Simulator`, `plugin_bio-research_*` 등.
  - claude.ai 커넥터: `1a59c906-…`=Claude Docs, `235d7bf9-…`=Notion, `90598b53-…`=manyfast, `b3252c20-…`=Google Drive.

## 6. 제안 2 — `root_denied_tools`에 더할 생성형·유료 도구

```json
["mcp__unityMCP__blender_bridge",
 "mcp__mobile__mobile_allocate_remote_device", "mcp__mobile__mobile_list_remote_devices",
 "mcp__mobile__mobile_release_remote_device", "mcp__mobile__mobile_login_to_cloud_provider",
 "mcp__plugin_greptile_greptile__trigger_code_review",
 "mcp__90598b53-3e39-4750-a9e5-938a578721de__generate_wireframe",
 "mcp__90598b53-3e39-4750-a9e5-938a578721de__generate_user_flow",
 "mcp__90598b53-3e39-4750-a9e5-938a578721de__regenerate_wireframe",
 "mcp__90598b53-3e39-4750-a9e5-938a578721de__regenerate_wireframe_page",
 "mcp__90598b53-3e39-4750-a9e5-938a578721de__regenerate_user_flow",
 "mcp__90598b53-3e39-4750-a9e5-938a578721de__regenerate_meta",
 "mcp__90598b53-3e39-4750-a9e5-938a578721de__append_wireframe_pages",
 "mcp__90598b53-3e39-4750-a9e5-938a578721de__run_chat_turn",
 "mcp__90598b53-3e39-4750-a9e5-938a578721de__continue_chat_thread"]
```

- `blender_bridge`: `mcpforunity://tool-groups`의 asset_gen 그룹 6개 도구 중 현재 deny 목록에 빠진 하나다.
- 현재 목록의 `mcp__unityMCP__asset_gen`은 실제 도구 이름이 아니라 그룹 이름이다. 그래서 효과가 없지만 해롭지도 않다. 그룹 활성화(`manage_tools activate asset_gen`)를 해도 개별 도구 deny가 막는다.
- mobile 원격 기기 4개: 서버 지침 "Remote devices cost money"에 근거한다.
- greptile 리뷰 실행: 외부 유료 SaaS 리뷰 작업이다. 독립 리뷰는 Codex 담당이다.
- manyfast(`90598b53-…`): AI 생성·대화 도구다. 같은 서버에 `read_credit_balance`가 있어 **크레딧 소모로 추정**한다. 서버 자체가 허용 목록 밖이므로 이중 방어용이다.
- 범위 밖 참고(생성형·유료는 아님): `mcp__github__delete_repository`는 되돌릴 수 없다. 별도 deny를 검토할 수 있다.

## 7. 이 작업이 바꾼 것

| 대상 | 변경 | 되돌리기 |
|---|---|---|
| `~/.claude.json` (이 프로젝트 local) | `mcpServers.serena`, `mcpServers.context7` 추가 | `claude mcp remove serena -s local` / `claude mcp remove context7 -s local` |
| `~/.serena/serena_config.yml` | 시험 때 추가된 scratch 경로 1줄을 제거해 원상 복구 | — |
| uv·npm 캐시 | `serena-agent 1.7.0`, `@upstash/context7-mcp 4.1.1` 내려받음 | 캐시 |
| Android AVD `Small_Phone` | `-read-only -no-snapshot-save`로 부팅 후 종료. AVD는 변경 없음 | — |
| Unity Editor | 읽기 호출만 했다 | — |
| 저장소 | `reports/mcp_setup_20261017/README.md`만 추가 | — |
