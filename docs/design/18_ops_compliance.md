# 18. 운영·배포·정책 준수

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

## 18.4 Google Play·등급·법규 체크리스트 (출시 전 재확인 필수)

| 항목 | 기준 |
|---|---|
| 대상 API | 2026-08-31 이후 신규 앱·업데이트는 Android 16(API 36) 이상. 제출 시점 요구치 재확인 |
| 빌드 | AAB, Play 앱 서명, 16KB 페이지 크기 대응, 64비트 |
| 결제 | Google Play 결제 시스템, 서버 검증·acknowledge, 무효 구매 알림 처리, 테스트 구매는 라이선스 테스터 |
| 등급 | 한국 15세 이용가 목표(IARC/자체 등급 분류). 최종 아트·대사·상점 이미지로 신청. Play 콘텐츠 정책(성적 콘텐츠, 미성년자 성적 대상화 금지)은 등급과 별개로 준수 |
| 확률 표시 | 게임산업법 확률형 아이템 정보 공개(2024-03-22 시행): 모집 화면·웹·광고에 확률표, 서버 확률과 공개 표 일치(자동 비교 시험) |
| 개인정보 | 개인정보처리방침, 데이터 보안 섹션, 앱 안·웹 계정 삭제 경로, SDK 수집 범위 |
| 미성년 | 나이 구간 자기 신고, 미성년 결제 한도 설정, 야간 푸시 금지 |
| 권리 | 게임명·캐릭터 상표 검색, 폰트(OFL)·음악·음성 계약, AI 생성 자산 제공처 약관과 기록(§15.4) |

이 문서는 법률·세무 확인서가 아니다. 등급 결과·법령 해석·세금은 출시물과 사업자 상태를 기준으로 전문가 확인을 받는다.
