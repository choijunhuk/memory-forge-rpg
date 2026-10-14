# <a id="ch18"></a>18. 운영·배포·정책 준수

## 18.1 환경

| 환경 | DB·키·저장소 | 대상 | 결제 |
|---|---|---|---|
| local | 개발자 합성 DB(`tools/check_schema.sh`와 같은 임시 클러스터) | 단위·참조 시험 | mock만 |
| dev | 독립 프로젝트 + 합성 계정 | 팀 개발·AI 에이전트 작업 | mock 네임스페이스 |
| staging | 독립 DB·발급자·저장소 | 통합·출시 후보(RC) | Play 라이선스 테스터, 실제 검증 API |
| production | 운영 전용 | 실사용자 | 실상품, **사람 승인으로만 배포** |

- Play 트랙(내부·비공개·프로덕션)과 서버 환경을 같은 것으로 취급하지 않는다. 클라이언트 빌드는 패키지명·서버 주소·토큰 발급자·상품 목록·콘텐츠 해시 조합을 시작 시 검사한다.
- AI 에이전트에게 운영 권한·운영 데이터·비밀 키를 주지 않는다. 운영 데이터가 필요하면 비식별 추출본을 별도 승인으로 만든다.

## 18.2 CI/CD

`.github/workflows/ci.yml`(제안, 이 패키지에서 GitHub에서 실행하지 않음):

1. `node content/build.mjs data` → 데이터가 커밋본과 같은지(`git diff --exit-code data/`).
2. `node --test engine/tests/*.test.mjs`, `node engine/tests/fuzz_runs.mjs 100 short`, `node engine/tests/fuzz_runs.mjs 40 full`.
3. `node tools/gen_vectors.mjs` → 벡터 변경 없음 확인. `dotnet run --project core/MemoryForge.CoreChecks -- .`
4. `python3 -m unittest discover -s tests`, `python3 tools/verify_economy.py`.
5. `tools/check_schema.sh`(PostgreSQL 16 서비스 컨테이너), OpenAPI 검증.
6. `node tools/build_prototype.mjs --no-data` → `node prototype/tests/smoke.mjs 2`(Playwright).
7. `node tools/gen_docs.mjs` → 문서 표 변경 없음 확인.

배포: staging 자동, production은 수동 승인 + 점검 공지 + DB 마이그레이션 사전 리허설 + 롤백 계획(이전 서버 이미지·이전 콘텐츠 해시 즉시 복귀, DB는 앞으로만 가는 마이그레이션 + 보정 스크립트).

## 18.3 백업·장애

- PostgreSQL: 연속 아카이빙(PITR) 7일 + 일일 스냅샷 30일. 분기마다 복구 리허설.
- 장애 등급: S1 결제·모집 오류 / S2 로그인·전투 불가 / S3 일부 기능. S1은 모집·상점 즉시 차단 스위치(서버 설정)로 막고 공지.
- 모집·재화 오류 보상은 원장 기반으로 계정별 정확히 계산해 우편 지급(일괄 지급 금지).

## 18.4 출시 요건 확인 — 미검증 체크리스트

원본의 API36·16KB·등급·환불 관련 문장은 당시 작성자의 제출 조건 가정이었다. 이번 작업은 파일·내용·참조 코드 재검증이며 최신 법령·스토어 심사 확인서는 아니다. 실제 제출 시 Play Console과 아래 공식 출처에서 해당 날짜·계정·국가에 적용되는 조건을 다시 확인한다. 출처 URL은 §23.5에 보존한다.

| 영역 | 확인할 것 |
|---|---|
| Android | 제출일 대상 API, AAB·서명, 64비트·16KB 환경과 포함 SDK 호환성 |
| 결제 | 실제 Play 검증 API, acknowledge/consume, 재시도·환불·무효 구매 처리, 국가별 조건 |
| 등급 | 최종 아트·대사·스토어 이미지 기준 등급 분류; 플랫폼 콘텐츠 정책과 별도 검토 |
| 확률 | 기본·회차별·종합·개별 확률 및 천장 공개, 공개 표와 서버 설정 동일성 |
| 개인정보 | 처리방침·Data safety·수집 SDK·앱/웹 삭제 요청·보관 기간 |
| 결제 보호 | 나이 구간·보호자 승인·한도 정책은 국내 법적 기준과 구현 요건을 별도 확인 |
| 권리 | 게임명·상표·아트·음성·폰트 라이선스 및 제작 기록. 폰트 원본 파일을 본 ZIP에 배포하지 않음 |

