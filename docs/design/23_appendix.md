# 23. 부록

## 23.1 용어

| 용어 | 뜻 |
|---|---|
| 공방장 | 플레이어. 얼굴 없음, 성별 미정, 비전투 |
| 기억 카드 / 기억 덱 | 카드 / 캐릭터별 저장 덱 |
| 기억 용량 | 기억 덱에 남길 수 있는 변경 비용 한도(6~9) |
| 탐사 | 로그라이트 런(지도 7층 + 보스) |
| 예약 · 다음 턴 · 다다음 턴 | 비용을 지금 내고 효과를 현재 턴 + 1 / + 2 시작에 해결 |
| 칸 · 봉인 · 앞당김 | 만기 턴별 예약 수 한도 · 담보 계약으로 칸 1개 막기 · 예약을 1턴 당기기 |
| 균열 · 약점 · 충격 | 게이지 0 상태(행동 취소·+25%·행동력 +1) · 균열을 깎는 속성 · 속성 무관 균열 감소 |
| 의도 | 적의 이번·다음 행동 |
| 잔향 · 과부하 · 성장/진화 · 계약 · 반격 · 표식 · 소멸 | 캐릭터 고유 규칙(§5.11) |
| 변주 | 공방 노드에서 카드에 영구 부품 3택 1 |
| 부품(회로·각인·임시) | 카드 개조 |
| 공명 / 정련 | 캐릭터 중복 단계(1~6) / 무기 중복 단계(R1~R5) |
| 반천장 · 풀천장 · 픽뚫 | SSR 확정(80) · 픽업 확정(160) · 50:50 실패 |
| 기억 결정체 · 잔광 | 중복 보상 재화 · R 결과 재화 |
| 집중 | 스태미나 |
| 기준 전투 | 전투력 40·체력 81·스킬 Lv.1·배율 100% |
| 스테이지 레벨 | 적 배율을 정하는 콘텐츠 레벨 |

## 23.2 오류 코드와 사용자 문구

<!-- include: gen/errors.md -->

## 23.3 레벨 표 (1~60)

<!-- include: gen/level_table.md -->

## 23.4 저장소 지도

| 경로 | 내용 |
|---|---|
| `content/` | 작성 원본: `helpers.mjs`(효과 DSL 함수), `characters.mjs`, `character_notes.mjs`, `world.mjs`(공용 카드·무기·부품·유물·적·사건), `gacha.mjs`, `progression.mjs`, `build.mjs` |
| `data/game_data.json` | 생성된 단일 데이터 |
| `engine/rules-core.js` | JS 규칙 참조(전투·탐사·기억·모집·스탯) |
| `engine/tests/` | 단위 시험, 퍼즈, 밸런스 측정, 교차 벡터 |
| `core/` | C# 코어(netstandard2.1) + 교차 검사(net10) |
| `server/` | `schema.sql`, `openapi.yaml`, `tests/schema_checks.sql` |
| `prototype/` | 브라우저 프로토타입(`index.html` 빌드 결과, `src/`, `tests/smoke.mjs`) |
| `tools/` | `build_prototype.mjs`, `gen_vectors.mjs`, `gen_docs.mjs`, `build_docs.mjs`, `verify_economy.py`, `check_schema.sh`, `run_all_checks.sh` |
| `tests/` | Python 경제 시험 |
| `docs/` | 이 문서(원본 `docs/design/`), `decisions.md`, 시작 안내, PDF, v0.6 보관본 |
| `reports/` | 실행 결과 기록, `delivery_status.json` |

## 23.5 참고 자료

- Google Play Console 도움말 — 대상 API 수준 요구사항(2026-08-31 이후 Android 16 / API 36): https://support.google.com/googleplay/android-developer/answer/11926878
- Android Developers — Target API level requirements for Google Play apps: https://developer.android.com/google/play/requirements/target-sdk
- Android Developers — Support 16 KB page sizes: https://developer.android.com/guide/practices/page-sizes
- Google Play Billing — 서버 측 구매 확인·acknowledge: https://developer.android.com/google/play/billing/security
- Google Play 정책 — 부적절한 콘텐츠(성적 콘텐츠): https://support.google.com/googleplay/android-developer/answer/9878810
- 대한민국 정책브리핑 — 확률형 아이템 정보 표시(2024-03-22 시행): https://www.korea.kr/multi/visualNewsView.do?newsId=148924522
- Google Play 정책 — 결제: https://support.google.com/googleplay/android-developer/answer/9858738
- Google Play 정책 — 사용자 데이터: https://support.google.com/googleplay/android-developer/answer/10144311
- v0.6 문서의 출처 목록(S01~S28, R01~R11)은 `docs/archive/sources_v0.6.md`에 보관.

외부 게임(카오스 제로 나이트메어, Slay the Spire, 원신, 붕괴: 스타레일, 니케, 명조 등)은 장르 관례 비교용이며, 이 문서의 수치는 그 게임들의 실제 수치가 아니다.
