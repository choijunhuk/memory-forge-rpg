# P0-SAVE-V2 — 2026-10-15 개발·검증 기록

첫 개발 묶음은 보존형 저장 v2의 JS Run/HTML 연결이다. 루트는 `/Users/choi/Documents/Memory_forge`, 입력 기준 커밋은 `f4077c8`, 작업 브랜치는 `codex/storage-v2`다. 전체 입력을 먼저 보존했으며 GitHub 원격은 `https://github.com/choijunhuk/memory-forge-rpg` 비공개다.

## 구현과 보존

- `proposals/memory_v2.cjs`: 기존 1변화 `recover` 시험을 유지하고, 안정 키·실제 변화 ID·전체 결과 비용·정상 다중 선택·결정적 이관/격리를 같은 순수 함수로 처리한다.
- `engine/rules-core.js`: `memoryPolicy:'v2'` opt-in, 출발/종료/기본 스냅샷, 다음 탐사로 키 계승, 이력·버전·멱등 영수증. 기본 덱 사용 자체는 기존 진화/강화/부품을 되돌리지 않는다. 저장 종료 후 부착한 부품도 반영한다. 기존 legacy 경로와 시험은 유지했다.
- `prototype/src/ui.js`: 출발/후보 덱 비교·전체 용량·패배 0~1개, 기존 덱 유지, 원본 바이트 백업, 손상/초과 기록 차단, 체크포인트, 저장 실패 표시와 같은 요청 재시도. 메모리와 Run의 실시간 상태는 보관 성공 후 바꾼다.
- `tools/build_prototype.mjs`와 `prototype/index.html`: 동일 저장 모듈을 브라우저에 포함한다. v2 실제 클릭 시험 후 미연결 경고를 현행 개발 상태로 바꿨다.
- 신규 시험은 `engine/tests/memory_v2_integration.test.mjs`, `engine/tests/memory_v2_edges.test.mjs`, `prototype/tests/memory_v2.mjs`. 실행 진입점은 `npm run test:memory-ui`, CI에도 추가했다. 오류 문서 생성기의 검사 범위는 v2 모듈까지 확장했다.

설계 원본 24개 파일, 게임 데이터·콘텐츠·아트 브리프/50개 프롬프트는 체크섬이 그대로다. 기존 파일 누락은 없다. 새 매니페스트를 만들어 입력 검사를 덮지 않았다. `preservation.json`은 입력 423개 파일의 변경/보존 대조다. PDF는 원문을 축약하지 않고 본문 154쪽 + 안내 5쪽을 다시 생성했다. 과거 배포 기록은 원래 위치에 보존했다.

## 실제 Claude 협업

공식 CLI 2.1.285, claude.ai Max 인증과 계정 Usage credits OFF 확인 후 호출했다. Codex도 ChatGPT 인증·포함 사용량·자동 충전 OFF를 확인했고 후속 확인에서 구매 크레딧 잔액은 같았다. API 키·구독 토큰을 추출하지 않았고 API/추가 크레딧/유료 대체 도구를 사용하지 않았다. 그림 생성·내용 수정 담당은 Codex이며 이번에는 생성하지 않았다.

Claude 전용 managed worktree `claude-memory-v2/Memory_forge`, 브랜치 `claude/storage-v2-ui`. 읽기 시험과 UI 작업은 각각 실제 종료 0과 구조화 결과를 회수했다. 요청·session_id·반환 결과는 `ops/ai/tasks/P0-SAVE-V2*`에 있다. 원시 CLI/계정 증거는 Git 제외 경로 `ops/ai/local/`에 둔다. `ops/ai/policy.json`의 false 표시는 입력 패키지 제작 당시 상태이며 현행 계정 증거를 공개 저장하는 용도로 바꾸지 않았다.

`--restricted`, 명시적 Read/Glob/Grep/Edit 도구, UI 한 파일의 편집 허용, 빈 strict MCP, `dontAsk`를 사용했다. `--bare`나 무제한 권한은 쓰지 않았다. Claude 작성 커밋을 Codex가 문법 확인 후 가져와 실제 엔진/브라우저 시험과 독립 검토를 거쳐 보완했다. CLI 성공과 제품 전체 완료는 구분한다.

## 검증

최신 상태는 `verification.json`. JS 73, 독립 저장 14, Python 23, 문서 회귀 9, PDF 인계 13, 브라우저 13개가 통과했다. 기존 UI 자동 클릭 2판도 723클릭·15화면에서 통과했다. 실제 HTML 탐사 패배/보존/재로딩 경로와, 고정 종료 스냅샷으로 분리한 변화 선택·실패 사례를 구분해 시험했다. 화면은 `screens/`, 시각 판정은 `visual_verdict.json`이다.

```sh
npm test
npm run test:proposals
python3 -m unittest discover -s tests -v
npm run test:docs
npm run test:handoff
npm run test:memory-ui
```

로컬 검증은 기존 선언된 Python 의존성을 프로젝트 밖 가상환경에 설치하고, 이미 번들된 Playwright와 설치된 Chromium을 사용했다. 관련 환경 경로는 실행 로그/작업 문맥에 남겼으며 프로젝트 의존성을 새로 추가하지 않았다. `npm run docs`의 초기 실패는 macOS 네이티브 Pango 검색 경로와 원본에 쓰인 대체 글꼴 부재 때문이었다. 필요한 로컬 빌드 라이브러리를 보완하고 원본 PDF의 내장 대체 글꼴을 프로젝트 밖에서 재사용해 154+5쪽을 맞췄다. 글꼴 바이너리는 저장소에 넣지 않았다. 실패 시도와 성공 로그를 모두 유지했다.

이 Mac에서 실제 성공한 PDF 실행은 다음과 같다. `BASH_ENV` 파일은 Homebrew 네이티브 라이브러리 검색 경로와 프로젝트 밖 fontconfig를 자식 셸에 설정한다. 계정 인증·전역 AI 설정은 바꾸지 않는다.

```sh
BASH_ENV=/Users/choi/.codex/tmp/memory-forge-doc-env.sh \
PATH="/Users/choi/.codex/tmp/memory-forge-python-20261015/bin:$PATH" \
npm --script-shell=/bin/bash run docs
```

## 경계와 다음 작업

로컬 저장 버전 검사는 서버/DB의 잠금·병렬 저장·인증·원자적 CAS를 증명하지 않는다. C# Battle/Run, 서버 v2, Unity, Android 실기기, 실아트·결제·운영 배포는 NOT_RUN/미구현이다. 기존 C#의 RNG/가챠/Stats 검사는 전체 이식 완료와 다르다.

다음 순서는 후반 적 행동과 같은 조건의 밸런스 → C# Battle/Run/저장 동치 → 서버·DB → Unity/Android다. 무료 용량 변경·본편 인장·고난도 목표, 업적 상세·패스 50레벨 배정·무기 전승의 미결정 항목도 계속 유지한다. 가격·확률·재화·나이·상용 배포 정책은 이번에 변경하지 않았다.

## GitHub CI 인수

첫 실행 `38610287262`는 시험 전에 `tools/run_all_checks.sh: Permission denied`(126)로 멈췄다. ZIP 복원 파일에 실행 비트가 없음을 로컬에서도 확인했고, 동일 검사 스크립트를 명시적 Bash로 실행하도록 CI 호출 한 줄을 고쳤다. 시험 삭제·skip·기준 완화는 없다. 최종 원격 결과는 비공개 PR #1의 해당 head 검사와 Actions 로그로 확인한다. 로컬 결과와 원격 C#/스키마 검사는 서로 대체하지 않는다.
