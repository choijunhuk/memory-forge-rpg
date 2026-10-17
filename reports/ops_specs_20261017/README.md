# 운영 명세 단위 — §16.6 텔레메트리, §18.3 백업·장애, §18.4 출시 점검, §19.3 인수 시험 (2026-10-17~18)

브랜치 `claude/ops-specs-b89143`(worktree `ops-specs-b89143`). Claude(Opus 5.5) 루트 하위 세션 작업이다. 설계서의 미구현 운영 항목을 **명세·스키마·점검 도구**로 만들었다. 수집 코드·운영 인프라·유료 서비스는 만들지 않았고 서버 코드는 읽기만 했다.

## 산출물

| 원문 | 산출물 | 상태 |
|---|---|---|
| §16.6 텔레메트리 | [이벤트 사전 문서](../../ops/telemetry/EVENTS.md), [JSON Schema](../../ops/telemetry/events.schema.json), [스키마 시험](../../ops/telemetry/test_events_schema.py) | 명세 완료. 수집·전송 없음(승인 경계) |
| §18.3 백업·장애 | [백업·장애 명세](../../docs/ops/backup_recovery.md), [파일 저장소 복원 리허설 도구](../../docs/ops/rehearse_file_store_restore.py), [리허설 결과](file_restore_rehearsal.json) | 개발 서버 절차 + 파일 저장소 리허설 1회 PASS. 운영 PITR·스냅샷은 미구현 |
| §18.4 출시 요건 | [출시 점검표](../../docs/ops/launch_checklist.md) | 항목별 상태·근거·확인 방법 |
| §19.3 제품 인수 | [인수 시험 현황](../../docs/ops/product_acceptance.md) | 시험별 상태·증거·빈 곳 |
| 장부 | `docs/IMPLEMENTATION_STATUS.md`의 §16.6·§18.3·§18.4·§19.3 행 | 갱신 |

## 시험과 검증

| 검사 | 결과 | 증거 |
|---|---|---|
| `python3 -m unittest discover -s ops/telemetry -v` | **PASS 8, SKIP 1**(공식 `jsonschema` 검증기 일치 시험: 로컬에 패키지 없음, 새 의존성 미설치) | [telemetry_tests.txt](telemetry_tests.txt) |
| 스키마 변이 시험(잘못된 스키마 15종 + 원문 행 추가 1종 + JSON 키 중복) | **17/17 실패로 잡힘** | [schema_mutation_check.py](schema_mutation_check.py), [schema_mutation_check.txt](schema_mutation_check.txt) |
| 파일 저장소 백업→복원 리허설(실제 host 프로세스, 죽은 포트의 `http_proxy` 설정 상태) | **PASS**(게이트 14항목 모두 일치, 종료 코드 0). 리뷰 전 버전으로도 2회 통과했지만 그때 게이트는 약했다(리뷰 #2) | [file_restore_rehearsal.json](file_restore_rehearsal.json) |
| 리허설 `--work` 거부(현재 worktree, 메인 checkout, `.git` 안) | 3/3 거부, 디렉터리 생성 없음 | 이 세션 기록 |
| 새 문서·장부 행의 상대 링크 | 깨진 링크 0 | 이 세션 기록 |
| 루트 세션 감사 | 아래 "감사" | [root_session_audit.json](root_session_audit.json) |

스키마 시험은 원문 `16_chapter.md`의 표를 직접 읽어 비교한다. 확인하는 것은 다음과 같다.

- 이벤트 이름 9개가 원문과 같고 중복이 없다.
- 이벤트별 필드·배열 안 필드가 원문과 같고 모두 필수다.
- `card_play` 표본 10%, 보존 13개월, "계정 ID는 해시" 문장이 원문에 있다.
- 금지된 개인정보·숨김 정보 필드 이름이 없다.
- 모든 객체가 닫혀 있고 자유 문자열이 없다.
- 열거값이 게임 데이터 안에 있다.
- 정상 표본 9개는 통과하고 잘못된 표본 99개는 거부된다.

리허설은 실제 개발 host를 임시 fixture·저장소로 띄워 다음을 확인했다(1회).

- 복원 바이트가 백업과 같다. 디렉터리 0700, 봉투 0600이다.
- 계정·run 화면이 백업 시점과 같다.
- 백업 전 요청의 같은 requestId 재시도가 같은 응답 바이트를 돌려준다.
- 백업 이후 `enter` 명령은 복원본에서 재실행되며 원래와 같은 응답이 나왔다(결정적 재생).
- 바이트 하나를 바꾼 복원본은 `500 STORE_CORRUPT`로 거부되고 그대로 남았다.

## 오늘 확인한 출시 관련 사실

- 실기기 실행: 확인됨(dev APK, SM-S928N Android 16).
- macOS 빌드: 확인됨.
- **target API:** dev APK의 관찰값은 35다. 설치된 SDK Platform은 33/34/35뿐이다. 원문은 2026-08-31 이후 API 36 이상을 **가정**하며 §18.4는 제출일에 다시 확인하라고 한다. 그래서 상태는 "미확인(적용 요건)"이고, 가정이 맞으면 출시 차단이다.
- **16KB:** `lib_burst_generated.so`만 4KB 정렬이다. 별도 세션(android-16kb)이 수정 중이다.
- **64비트 설정 위험:** 프로젝트 설정 `AndroidTargetArchitectures: 1`(ARMv7)이다. ARM64는 dev 빌드 스크립트가 메모리 안에서만 지정한다.

## 결정 대기 (근거는 각 문서에 있다)

- 텔레메트리 D-1~D-9: 수집 동의·연령, 계정 해시 방식·키, 공통 봉투 필드, 전송·처리자, 13개월 삭제 방법, 계정 삭제 연동, Data safety 대응, 값 목록, `hpLost` 단위·표본 단위.
- 백업·장애 B-1~B-4: 운영 보관소 제공자·비용, 차단 스위치 범위·권한자, 보상 우편 조건, 개발 저장소 가지치기.
- 출시: 국가별 결제 조건, 결제 보호(나이 구간·보호자·한도), 텔레메트리 외 보관 기간.

가격·확률·재화·개인정보 정책은 새로 정하지 않았다.

## 루트에 넘기는 제안 (이 세션 파일 범위 밖)

1. CI 연결: `tools/run_all_checks.sh`와 `.github/workflows/ci.yml`은 `tests/`만 찾는다. `python3 -m unittest discover -s ops/telemetry -v` 한 단계를 더하면 CI의 `jsonschema`로 SKIP 시험도 실행된다.
2. 서버 제안(읽기만 했고 고치지 않음, Codex 리뷰 대상): 모집·상점 API가 생길 때 차단 스위치를 함께 넣기, 파일 저장소 rename 뒤 디렉터리 fsync, 리허설 시나리오를 `MemoryForge.ServerChecks`/CI로 옮기기, PG 덤프 왕복 시험 추가.
3. 빌드 환경: 시스템 .NET SDK는 9라서 net10 서버 빌드가 `NETSDK1045`로 실패한다. `DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`가 필요하다. 출력에 파이프를 걸면 실패가 종료 코드 0으로 가려질 수 있다(이번에 한 번 겪음).
4. 리허설 도구 위치: 파일 범위 때문에 `docs/ops/`에 두었다. `tools/ops/`로 옮기는 것은 루트 판단이다.

## 하지 않은 것

- 텔레메트리 수집·전송·저장 코드, SDK 연결.
- PostgreSQL 덤프·복원 왕복(이 기기에 PostgreSQL 클라이언트 도구가 없다), PITR·스냅샷·운영 인프라.
- 서버·Unity·CI 코드 수정, `docs/CURRENT.md` 수정, MANIFEST 대상 파일 수정. run-create 고정 자료 입력은 바뀌지 않았다.

파일 범위 밖 쓰기 한 건: §17 "증거" 규칙이 Codex 호출 기록을 `ops/ai/tasks/`에 두라고 해서 새 파일 `OPS-SPECS-20261017-CODEX-REVIEW.result.json` 하나만 만들었다. 기존 파일은 바꾸지 않았다. 기존 Python 시험 `tests/` 57/57 PASS(이번 실행).

## 독립 리뷰 (Codex, 읽기 전용)

AI_WORKFLOW §17 리뷰 계약에 따라 공식 Codex CLI를 `command codex exec -s read-only -m gpt-6.1-sol -c model_reasoning_effort=high`로 두 번 호출했다. Codex는 파일을 바꾸지 않았다. 기록: [`ops/ai/tasks/OPS-SPECS-20261017-CODEX-REVIEW.result.json`](../../ops/ai/tasks/OPS-SPECS-20261017-CODEX-REVIEW.result.json).

| 회차 | thread | 판정 | 내용 |
|---|---|---|---|
| 1 | `01a14a64-d1e3-7b72-8c10-e41fec710a19` | BLOCKING | implementation_defect 8개, design_mismatch 2개 |
| 2 | `01a14a6b-3eab-78c2-b646-f2d14723e30d` | NO_BLOCKING_DEFECT | 10개 모두 RESOLVED. 새 LOW 1개(이 README의 변이 시험 개수 15→17)는 반영했다 |

1회차 지적과 조치:

| # | 지적 | 조치 |
|---|---|---|
| 1 | 리허설 HTTP가 프록시 설정을 따라 Bearer 비밀이 프록시로 갈 수 있음 | `ProxyHandler({})` 전용 opener. 죽은 포트 프록시 설정으로 다시 실행해 PASS |
| 2 | 성공 게이트가 봉투 권한·재실행 상태/바이트를 보지 않고 손상 거부를 HTTP≥400으로만 판정 | 14항목 `GATE`를 값 그대로 비교. 실패 항목을 결과 파일에 기록 |
| 3 | host 종료 보장 없음, 시작 대기가 마감 전에 막힘 | 출력 읽기 스레드 + 마감 있는 큐 대기, `with` 문으로 항상 종료·회수 |
| 4 | `--work`가 다른 checkout 안이어도 통과 | 가장 가까운 기존 상위 경로에서 `git rev-parse`로 work tree·Git 디렉터리 거부 |
| 5 | 로컬 검증기가 `oneOf` 옆 키워드를 무시 | 옆 키워드도 평가. 변이 시험에 회귀 사례 추가 |
| 6 | 원문 파서가 역따옴표 없는 행을 건너뜀 | 표의 모든 데이터 행을 읽고 모르는 형식은 실패. 회귀 사례 추가 |
| 7 | EVENTS.md가 CI 실행을 주장 | CI가 실행하지 않는다고 고치고 연결은 루트 제안으로 둠 |
| 8 | 차단 스위치 범위(모집·상점)를 결정 대기로 다시 열어 둠 | 원문대로 둘 다 필수. 권한자·추가 범위만 결정 대기 |
| 9 | target API 35를 확인된 미충족으로 표시(원문은 API 36이 미검증 가정이라 함) | "미확인(적용 요건), 관찰값 35"로 바꿈. 장부 행도 같이 |
| 10 | 상점 경로가 없다고 과하게 적음 | 원정 안 `buy`는 있고 운영 모집·유료 상점 API와 스위치가 없다고 정확히 적음 |

## 감사

- 시작 확인: `python3 tools/ai/audit_root_session.py --start-check` → `ROOT_START_CHECK pass: true`.
- 커밋 전 감사: [root_session_audit.json](root_session_audit.json) → **pass: true**, 위반 0, 예외(waive) 0, 증거 누락 0.
  - 세션 `f572607c-c7e6-48db-9a00-a3088e0bcfb6`, 모델 `claude-opus-5-5`만 사용.
  - Codex 호출 2회, 모두 규칙 형식이다.
  - Unity 잠금 없음(Unity Editor 미사용).
- 경고: 허용 목록 밖 MCP 서버 16개가 **노출만** 됐다. 호출은 없다.
- 감사 미검증(보고서 `unverified` 그대로): 첫 도구 목록, MCP 하위 프로세스 환경, 실제 추론 환경과 셸 환경의 일치, 인터프리터 코드 의미, 계정 화면의 Usage credits/Extra usage OFF.
