# 백업·장애 대응 — §18.3 명세와 현재 개발 서버 절차 (2026-10-18)

원문: [§18.3](../source_v0.8.1/18_chapter.md). 기준 코드: [`server/MemoryForge.Server`](../../server/MemoryForge.Server/README.md). 이 코드는 Development 전용 로컬 서버이며 파일 저장과 PostgreSQL 저장을 갖는다. 운영 DB·운영 인프라·유료 서비스는 만들지 않았다. 이 문서는 **원문 목표**와 **현재 개발 서버에서 실제로 하는 절차**를 나눠 적는다. 서버 코드는 바꾸지 않았다.

## 1. 원문 요구 대비 상태

| 원문 요구 | 현재 상태 | 근거 |
|---|---|---|
| PostgreSQL 연속 아카이빙(PITR) 7일 | **미구현.** 운영 DB가 없다. 목표 절차만 §4에 적었다. | — |
| 일일 스냅샷 30일 | **미구현.** 보관소·일정이 없다. | — |
| 분기마다 복구 리허설 | **부분.** 파일 저장소 리허설 도구를 만들고 1회 통과했다(2026-10-18). PostgreSQL 리허설과 정기 일정은 없다. | [리허설 결과](../../reports/ops_specs_20261017/file_restore_rehearsal.json), [도구](rehearse_file_store_restore.py) |
| 장애 등급 S1 결제·모집 / S2 로그인·전투 / S3 일부 기능 | **명세만.** 등급별 순서를 §5에 적었다. 개발 서버 오류 코드와 등급의 대응은 제안이다. | — |
| S1은 모집·상점 즉시 차단 스위치(서버 설정)로 막고 공지 | **미구현.** 개발 서버에는 운영 모집 API와 유료 상점·결제 API가 없고 차단 스위치도 없다. 있는 것은 원정 안 상점 명령 `buy`(코인 사용, `PracticeRunStore.cs`)뿐이다. 지금 막는 방법은 host 중지뿐이다. | README "What it does"·명령 목록 |
| 모집·재화 오류 보상은 원장 기반 계정별 계산 후 우편(일괄 지급 금지) | **미구현.** 개발 서버에 원장·우편이 없다. 운영 스키마에는 `ledger`·`mail` 테이블이 있다. | [`server/schema.sql`](../../server/schema.sql) |

## 2. 이미 검증된 것과 아닌 것

날짜가 다른 보고서는 각 실행의 증거다. 이번 작업에서 다시 실행한 것은 리허설 하나다.

| 장애·복구 상황 | 상태 | 근거 |
|---|---|---|
| host 프로세스 SIGKILL 뒤 재시작, 영수증·저장 카드 보존(파일·PG) | 확인됨(개발, 2026-10-16) | [파일 수용](../../reports/server_persistence_20261016/root_acceptance.md), [PG 수용](../../reports/postgres_persistence_20261016/root_acceptance.md) |
| 두 host가 한 저장소/한 DB에 쓸 때 중복·오래된 버전 경쟁 | 확인됨(개발) | 같은 보고서 |
| 쓰기 실패·Core 부분 변경 실패의 롤백(새 이익·ID·버전 없음) | 확인됨(개발) | 같은 보고서 |
| 변조된 저장소·행 거부, 바이트 그대로 보존 | 확인됨(개발) + 이번 리허설 | 같은 보고서, 리허설 `damaged_restore_*` |
| DB 중지·시작(같은 볼륨), 실행 중 UPDATE 권한 회수·복구, 스키마 마커 없음 거부 | 확인됨(PG 장애 시험 27개) | [PG 수용](../../reports/postgres_persistence_20261016/root_acceptance.md) |
| COMMIT 성공 응답 유실 → `STORE_COMMIT_UNKNOWN`, 같은 requestId 재시도가 저장된 응답 반환 | 확인됨(PG 장애 시험) | 같은 보고서 |
| 파일 저장소 백업 → 새 디렉터리 복원 → 같은 화면·같은 영수증 | **확인됨(이번, 1회)** | 리허설 결과 |
| 백업 이후 명령의 손실과 같은 requestId 재실행 | **확인됨(이번, 1회, `enter` 명령)** | 리허설 `post_backup_command_*` |
| PostgreSQL `pg_dump`/`pg_restore` 왕복 | 미실행. 이 기기에 PostgreSQL 클라이언트 도구가 없다. | — |
| 전원·저장 장치 장애 | **보장 안 함.** 파일 저장은 rename 뒤 디렉터리 `fsync`가 없고 macOS `F_FULLFSYNC`를 쓰지 않는다. | README "Known limits" |
| PITR·스냅샷·차단 스위치·원장 보정·우편 | 미구현 | — |
| 운영 규모 부하·다기기·실계정 | 미구현 | — |

## 3. 현재 개발 서버의 백업·복원 절차

공통 원칙(서버 README의 계약을 그대로 따른다):

- 손상·불일치 저장소를 **고치거나 지우거나 초기화하지 않는다.** 서버도 거부만 하고 파일·행은 그대로 둔다.
- 저장소 JSON을 손으로 편집하지 않는다. 편집하면 `500 STORE_CORRUPT`가 된다.
- 저장소는 저장소 밖 owner-only 경로에 둔다(디렉터리 `0700`, 파일 `0600`). 비밀(fixture·자격 증명·PG 연결 파일)은 백업과 따로 둔다.
- 저장소는 Core 빌드·`data/game_data.json`·fixture에 결합돼 있다. 결합이 다르면 `409 STORE_*_MISMATCH`로 거부한다. 다른 빌드로 억지로 읽지 않는다.

### 3.1 파일 저장소 (`--MemoryForge:PracticeStore`)

계정마다 `account-<alias>.json`(봉투)과 `account-<alias>.lock`(영구 잠금 파일)이 있다. 봉투 하나는 임시 파일 → flush → 같은 디렉터리 rename으로 바뀌므로 언제 읽어도 이전 또는 새 완전한 봉투다. 다만 여러 계정을 같은 시점으로 맞춰 주지는 않는다. 그래서 host를 멈춘 뒤 백업한다.

백업:

1. host를 멈춘다(SIGTERM).
2. 저장소 디렉터리 전체를 묶는다: `tar -cf <backup>.tar -C <store> .`
3. 각 파일의 SHA-256을 기록한다. 백업은 owner-only 위치에 둔다.

복원:

1. host를 멈춘다.
2. 새 빈 디렉터리를 `0700`으로 만들고 압축을 푼다. 원래 디렉터리는 조사용으로 보존한다.
3. 복원한 파일의 SHA-256이 백업 기록과 같은지 확인한다.
4. 백업 때와 **같은** Core 빌드·`data/game_data.json`·fixture로 host를 시작한다.
5. `GET /dev/practice/account`와 진행 중 run 화면을 확인한다.
6. 백업 이후 요청은 잃는다. 클라이언트가 같은 requestId로 다시 보내면 서버는 복원된 상태에서 다시 실행한다.
   - 결정적 명령(`enter` 등)은 같은 응답이 나왔다(리허설 `post_backup_command_same_bytes_as_original: true`).
   - run 생성은 서버 CSPRNG가 새 seed/runId를 고르므로 결과가 달라진다(README 한계). 백업 시점 이후의 run 생성 영수증을 잃으면 다른 원정이 된다.
   - 운영에서 같은 상황이 모집·재화 쪽에 생기면 원장 보정 대상이다(§5.6).

리허설 도구: `python3 docs/ops/rehearse_file_store_restore.py --server-dll <MemoryForge.Server.dll> --work <저장소 밖 빈 디렉터리> --out <결과.json>`. 임시 fixture·비밀·저장소를 `--work` 안에서만 만들고 비밀은 출력하지 않는다. 확인 항목은 다음과 같다.

- 복원 바이트가 백업과 같다.
- 복원 디렉터리가 `0700`, 봉투가 `0600`이다.
- 계정·run 화면이 백업 시점과 같다.
- 백업 전 영수증을 같은 requestId로 재시도하면 같은 바이트가 나온다.
- 백업 이후 명령이 재실행된다.
- 바이트 하나를 바꾼 복원본은 `500 STORE_CORRUPT`로 거부되고 그대로 남는다.

빌드는 .NET 10 SDK로 한다(`DOTNET_ROOT=$HOME/.dotnet`). 이 기기의 시스템 SDK 9는 net10 대상을 빌드하지 못한다.

### 3.2 PostgreSQL 저장소 (`--MemoryForge:PracticeBackend=postgres`, 미실행 절차)

`dev_practice.account_state(account_alias, envelope bytea)` 한 행이 파일 봉투와 같은 바이트를 담는다. 원래 `server/schema.sql`의 UUID 테이블은 비어 있다.

백업(미실행):

1. host를 멈추거나 쓰기가 없는 시점을 고른다. `pg_dump`는 한 트랜잭션 스냅샷이라 행 사이 일관성은 DB가 보장한다.
2. `pg_dump -Fc -n dev_practice -d <전용 시험 DB> -f <backup>.dump`. 연결 정보는 owner-only 연결 파일이나 `PGPASSFILE`로 주고 명령줄에 비밀을 넣지 않는다.

복원(미실행):

1. 새 전용 시험 DB에 `server/schema.sql`을 적용한다.
2. `pg_restore -d <새 DB> --no-owner <backup>.dump`로 `dev_practice` 스키마와 마커를 복원한다.
3. 앱 역할에 README가 정한 최소 권한을 다시 준다. 대상 테이블에 `SELECT, INSERT, UPDATE`, 마커에 `SELECT`, 스키마에 `USAGE`다.
4. host를 시작한다. 시작 검사가 PostgreSQL 16, `fsync`·`full_page_writes` 켜짐, `synchronous_commit`이 off가 아님, 마커와 권한을 확인한다.
5. 파일 저장소 §3.1의 4~6과 같은 확인을 한다.

## 4. 운영 목표 절차 (원문, 인프라 미생성)

여기는 원문 목표를 절차로 풀어 쓴 것이다. 실행 증거가 아니다. 운영 인프라·보관소 선택은 비용과 운영 승인 경계라 정하지 않는다(결정 대기 B-1).

- **PITR 7일:** WAL 연속 아카이빙(`archive_mode=on`, 보관소로 WAL 복사)과 기준 백업을 둔다. 복구는 새 클러스터에 기준 백업을 놓고 `recovery_target_time`까지 WAL을 재생한다. 운영 DB에 직접 덮어쓰지 않는다.
- **일일 스냅샷 30일:** 매일 기준 백업 또는 스냅샷을 만들고 30일 보관한다. 각 백업은 체크섬과 복원 가능 여부를 기록한다.
- **분기 복구 리허설:** staging에 최신 스냅샷 + PITR로 특정 시각을 복원한다. 걸린 시간, 손실 구간, 영수증 재생 결과, 원장 합계 일치를 기록한다. 리허설 기록이 없으면 그 분기의 백업은 "미검증"이다.
- **배포 롤백(§18.2와 연결):** 이전 서버 이미지·이전 콘텐츠 해시로 즉시 돌아간다. DB는 앞으로만 가는 마이그레이션과 보정 스크립트를 쓴다.

## 5. 장애 대응 순서

1. **감지·기록:** 시각, 영향 범위(계정 수·기능), 오류 코드를 적는다. 개발 서버 로그에는 예외 유형·SQLSTATE만 남으므로 응답 코드가 1차 근거다.
2. **등급:** S1 결제·모집 오류 / S2 로그인·전투 불가 / S3 일부 기능(원문).
3. **격리:**
   - S1이면 모집과 상점을 모두 차단 스위치로 막고 공지한다(원문). 스위치는 아직 없다. 지금은 해당 host를 멈추는 것만 가능하다.
   - S2·S3은 영향 기능만 막는다.
4. **증거 보존:** 저장소 디렉터리 또는 DB 덤프의 사본을 먼저 뜬다. 손상본을 고치지 않는다.
5. **원인 확인:** 같은 초기 상태 + 같은 명령열 = 같은 결과다(§16.3). 서버에서 journal을 재생해 확인한다.
6. **복구와 보상:**
   - 재시도·재시작·복원(§3) 중 가장 작은 조치를 고른다.
   - 모집·재화 오류는 원장 기준으로 계정별 정확한 보상을 계산하고 우편으로 지급한다. 일괄 지급은 하지 않는다(원문).
   - 텔레메트리는 보상 계산 근거가 아니다(`ops/telemetry/EVENTS.md`).
7. **공지·사후 기록:** 원인, 손실 구간, 보상 내역, 재발 방지를 남긴다.

### 5.1 개발 서버 응답 코드별 대응

코드와 동작은 서버 README 그대로다. 등급 열은 **제안**이며 운영 API에서 다시 정한다.

| 응답 | 의미 | 대응 | 등급 제안 |
|---|---|---|---|
| `503 ACCOUNT_BUSY` | 계정 잠금 대기 초과(파일 10초, PG 5초) | 같은 requestId로 재시도 | 개별 S3, 광범위하면 S2 |
| `503 STORE_UNAVAILABLE` | COMMIT 전 DB 실패 | DB 상태 확인 뒤 같은 requestId로 재시도. DB 재시작 직후 첫 요청이 503일 수 있다(PG 장애 시험) | S2 |
| `503 STORE_COMMIT_UNKNOWN` | COMMIT 중 실패, 반영됐을 수 있음 | **반드시 같은 requestId로** 재시도한다. 새 requestId를 쓰면 두 번 실행될 수 있다 | S2(모집·결제면 S1) |
| `500 STORE_CORRUPT` / `500 STORE_REPLAY_MISMATCH` | 봉투를 읽을 수 없거나 재생 결과가 다름 | 그 계정을 멈추고 사본을 보존한다. 마지막 정상 백업 복원을 검토하고 원인을 조사한다 | S2 |
| `409 STORE_CONTENT_MISMATCH` / `STORE_IMPLEMENTATION_MISMATCH` / `STORE_FIXTURE_MISMATCH` / `STORE_UNSUPPORTED` | 데이터·Core 빌드·fixture·형식 결합이 다름 | 배포 결합을 되돌리거나 새 저장소를 쓴다. 이관은 별도 검토 작업이다 | 배포 실수면 S2 |
| `507 STORE_TOO_LARGE` | 봉투 512 MiB 경계 | 가지치기 정책이 없어 지우지 않는다. 새 저장소를 쓴다 | S3 |
| host 시작 거부(종료 코드 2) | 설정·권한·PG 설정 위반 | 메시지대로 설정을 고친다. 검사를 끄지 않는다 | — |

## 6. 결정 대기와 서버 쪽 제안

결정 대기(정하지 않음):

| ID | 항목 | 근거 |
|---|---|---|
| B-1 | 운영 DB·WAL 보관소·스냅샷 보관소의 제공자와 비용 | 유료 서비스·외부 인프라는 승인 경계다. 원문에 제공자가 없다 |
| B-2 | 차단 스위치를 켜는 권한자, 그리고 모집·상점 밖으로 범위를 넓힐지(예: 결제 검증) | 원문은 모집과 상점을 **둘 다** 막으라고 정했다. 이 둘은 필수이며 결정 대기가 아니다. 권한자와 추가 범위만 원문에 없다 |
| B-3 | 보상 우편의 만료 기간·문구·공지 채널 | 원문에 없다. 재화 지급은 경제 승인 경계다 |
| B-4 | 개발 저장소의 가지치기·압축 | README: 승인된 보존·이관 정책이 필요하다 |

서버 코드 제안(이번에 고치지 않음, 각각 Codex 리뷰 대상):

1. 운영 API에 모집·유료 상점 경로가 생길 때 둘 다 막는 설정 차단 스위치를 같이 넣는다. 꺼진 경로는 고정 코드로 거절하고 원장을 쓰지 않는다.
2. 파일 저장소의 rename 뒤 디렉터리 `fsync`(macOS는 `F_FULLFSYNC`). 전원 장애 주장을 하려면 필요하다.
3. 이번 리허설 시나리오를 `MemoryForge.ServerChecks` 또는 CI에 넣는다. 지금은 수동 도구다.
4. PostgreSQL 백업·복원 왕복을 ServerChecks의 PG 모드 옆에 추가한다(§3.2).
