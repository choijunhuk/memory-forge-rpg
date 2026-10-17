# 텔레메트리 이벤트 사전 — §16.6 명세 (2026-10-17)

상태: **SPEC_ONLY.** 원문 [§16.6](../../docs/source_v0.8.1/16_chapter.md)의 이벤트 사전을 기계 판독 가능한 JSON Schema로 옮겼다. 클라이언트·서버의 수집, 전송, 저장, SDK 연결 코드는 **없다**. 수집과 전송은 개인정보 승인 경계라서 나중 단계에서 따로 승인받는다.

| 파일 | 내용 |
|---|---|
| [events.schema.json](events.schema.json) | JSON Schema 2020-12. 이벤트 1건 = JSON 객체 1개. 최상위 `oneOf`가 9개 이벤트 중 정확히 하나를 고른다. |
| [test_events_schema.py](test_events_schema.py) | 스키마 자체 검사. `python3 -m unittest discover -s ops/telemetry -v` |

시험은 원문 표를 직접 읽어 스키마와 대조한다. 그래서 원문이나 스키마 중 한쪽만 바뀌면 실패한다. 검사 항목은 다음과 같다.

- JSON 키 중복(이벤트 이름 포함)이 없다.
- 이벤트 이름 집합이 원문과 같다.
- 이벤트별 필드가 원문과 같고 모두 필수다. 배열 안의 필드도 같다.
- 표본율과 보존 기간이 원문과 같다.
- 금지된 개인정보·숨김 정보 필드 이름이 없다.
- 모든 객체가 닫혀 있다(`additionalProperties: false`).
- 자유 문자열 필드가 없다.
- 열거값이 `data/game_data.json` 안에 있다.
- 정상 표본 9개는 통과한다. 이메일·원시 계정 ID·필드 누락·형식 오류 등 잘못된 표본 99개는 거부된다.

시험 안의 작은 검증기는 스키마가 쓰는 키워드만 구현한다. 스키마에 그 밖의 키워드가 들어오면 시험이 실패한다. `jsonschema`가 있는 환경에서는 공식 검증기와의 일치도 확인한다. 이 로컬 기기에는 `jsonschema`가 없어 그 한 시험은 SKIP이다. 새 의존성은 설치하지 않았다. **현재 CI는 이 시험 묶음을 실행하지 않는다.** `tools/run_all_checks.sh`가 `tests/`만 찾기 때문이다. CI 연결(그리고 SKIP 시험이 실제로 실행됐는지 확인)은 루트에 넘긴 제안이다.

## 공통 봉투

| 필드 | 형식 | 근거 |
|---|---|---|
| `event` | 이벤트 이름 상수 | 원문 표의 이벤트 열 |
| `accountHash` | 선택. 소문자 16진수 64자 | 원문 "계정 ID는 해시". 원시 계정 ID는 금지 필드다 |

발생 시각, 앱·콘텐츠 버전, 플랫폼, 세션 같은 공통 필드는 원문에 없다. 그래서 넣지 않고 결정 대기로 둔다(아래 D-3).

## 이벤트

형식 약어: `id` = 영숫자와 `_ . : -`만 쓰는 64자 이하 식별자(공백·`@` 불가). `n` = 0 이상 정수.

| 이벤트 | 원문 필드 | 스키마 필드 | 해석 |
|---|---|---|---|
| `run_start` | runId, mode, chapter, party[charId,res,level,weaponId], difficulty, result, floorsCleared, turns, durationSec | `runId` id, `mode` id, `chapter` n, `party[]`, `difficulty` id | 원문은 `run_start`/`run_end`를 한 행에 적었다. 결과 4개 필드는 시작 때 값이 없으므로 `run_end`에만 둔다(스키마 `x-omittedSourceFields`에 사유를 적고 시험이 확인한다). |
| `run_end` | 위와 같음 | 위 5개 + `result` id, `floorsCleared` n, `turns` n, `durationSec` n(초) | — |
| `party[]` 항목 | charId, res, level, weaponId | `charId` id, `res` 0~6, `level` 1 이상, `weaponId` id 또는 null | 1~6명. 2파티 연전(§16.2 P2-3)이 있어 최대 6으로 둔다. 공명 상한 6은 §19.4. |
| `battle_end` | battleId/runId, encounter, result, turns, hpLost, breaks, reservesMade, ultsUsed | `battleId` id, `runId` id, `encounter` id, `result` id, 나머지 n | `battleId/runId`는 두 필드로 본다. `hpLost`의 단위(HP 값 또는 %)는 D-9. |
| `card_play` | cardId, mode, cost, apLeft, target tier, previewDmg, actualDmg | `cardId` id, `mode` now/next/after, `cost` n, `apLeft` 정수, `targetTier` normal/elite/boss/minion/null, `previewDmg` n, `actualDmg` n | 표본 10%(`x-sampleRate: 0.1`). `apLeft`는 계약 부채로 음수가 될 수 있다. 방식 값은 서버 명령의 `mode`, 등급 값은 데이터 `enemies[].tier`와 같다. 대상이 없으면 null. |
| `memory_commit` | charId, capacity, used, changes[type,cost], defeat | `charId` id, `capacity` n, `used` n, `changes[]` {`type` id, `cost` n}, `defeat` 참/거짓 | — |
| `gacha_pull` | bannerId, count, results[rarity,id,featured,lost], pityBefore, currency mix | `bannerId` id, `count` 1 또는 10, `results[]` {`rarity` R/SR/SSR, `id` id, `featured`, `lost`}, `pityBefore` n, `currencyMix[]` {`currency` id, `amount` n} | `count`는 모집 1회/10회(§17). `lost`는 픽업 실패. `currency mix`는 재화별 사용량 목록으로 본다. 재화 종류·비율은 정하지 않는다. `results` 길이와 `count`가 같은지는 JSON Schema로 표현하지 않았다(수집 구현 시험에서 확인). |
| `purchase` | productId, priceKrw, firstBonus, result | `productId` id, `priceKrw` n, `firstBonus` n, `result` id | `firstBonus`는 실제 지급된 첫 구매 보너스 수량이다. 없으면 0이다(데이터 `products.firstBonus`와 같은 단위). 가격 값은 정하지 않는다. |
| `tutorial_step` | step, durationSec, skipped | `step` id, `durationSec` n, `skipped` 참/거짓 | — |
| `error_shown` | code, screen | `code` 대문자 코드(예: `STALE_VERSION`), `screen` id | — |

`result`·`mode`·`difficulty`·`step`·`screen`·`currency`는 형식만 고정했다. 값 목록은 D-8이다. 참고로 현행 JS의 탐사 결과값은 `clear`·`defeat`·`return`이다(`engine/rules-core.js`).

## 개인정보 최소화 규칙 (원문 범위)

원문이 정한 것은 세 가지다. 개인 식별 정보 금지, 계정 ID는 해시, 보존 13개월. 아래 규칙은 이 세 문장을 스키마로 강제하는 방법이다. 새 정책을 만들지 않는다.

1. **허용 목록만 받는다.** 모든 객체가 닫혀 있어 원문 필드 밖의 값은 스키마를 통과하지 못한다. 필드를 더하려면 스키마와 시험을 함께 바꿔야 하고 개인정보 검토(D-1)를 거친다.
2. **개인 식별 정보 이름을 금지한다.** 이메일·전화·이름/닉네임·원시 계정/사용자/플레이어 ID·기기/광고 ID·IP·위치·생년월일·결제 토큰/주문 ID·비밀번호/자격 증명·세션·연락처·메시지 같은 이름은 시험이 거부한다(`FORBIDDEN_EXACT`, `FORBIDDEN_PARTS`).
3. **계정은 해시로만 연결한다.** `accountHash` 하나뿐이고 64자 16진수만 받는다.
4. **자유 문자열이 없다.** 모든 문자열 필드는 열거값 또는 식별자 패턴이다. 이메일·공백·65자 이상 값은 들어갈 수 없다.
5. **숨김 정보를 내보내지 않는다.** §16.3에 따라 시드·RNG·뽑을 더미 순서·비공개 상태 이름도 금지한다.
6. **보존 13개월, `card_play` 표본 10%**는 스키마 주석값(`x-retentionMonths`, `x-sampleRate`)으로 적고 원문과 대조한다. 실제 삭제·표본 추출 구현은 없다.

## 결정 대기 (DECISION_PENDING)

원문에 없거나 승인 경계에 걸리는 항목이다. 여기서 정하지 않는다.

| ID | 항목 | 근거 | 막히는 일 |
|---|---|---|---|
| D-1 | 수집 동의·옵트아웃·연령 구간별 수집 여부 | 원문 §16.6에 없음. §18.4 개인정보·결제 보호가 별도 확인 대상 | 수집 코드 연결 |
| D-2 | 계정 해시 방식(비밀 키 HMAC인지 단순 해시인지)·키 보관·교체, `accountHash` 필수 여부 | 원문은 "해시"만 적음. 키 없는 해시는 계정 ID로 되돌릴 수 있어 가명 처리 수준이 달라진다 | 서버 측 해시 구현 |
| D-3 | 공통 봉투 추가 필드(발생 시각, 앱·콘텐츠 버전, 플랫폼, 세션) | 원문에 없음. 시각이 없으면 13개월 보존 기준은 수신 시각이 된다 | 집계·보존 구현 |
| D-4 | 전송 경로·저장소·처리자(SDK 선택 포함) | 유료 서비스·외부 인프라 금지, 원문에 없음 | 전송 구현 |
| D-5 | 13개월 경과 삭제 방법과 증명 | 원문은 기간만 정함 | 보존 구현 |
| D-6 | 계정 삭제 요청 때의 텔레메트리 처리 | §14 설정: 데이터 삭제는 2단계 확인 + 30일 유예. 텔레메트리 적용 범위는 원문에 없음 | 삭제 흐름 |
| D-7 | Play Data safety 신고 항목과의 대응 | §18.4 개인정보. `purchase`는 구매 내역, `accountHash`는 식별자 분류 검토가 필요하다 | 스토어 제출 |
| D-8 | `mode`·`difficulty`·`result`·`step`·`screen`·`currency` 값 목록 | 원문은 필드 이름만 정함 | 대시보드·집계 |
| D-9 | `hpLost` 단위, 10% 표본의 추출 단위(카드 사용마다/전투마다/계정마다) | 원문에 없음 | 집계 정확도 |

## 다른 기록과의 관계

- 텔레메트리 `gacha_pull`·`purchase`는 분석용 사본이다. 판정·재화의 권위 기록은 서버 DB의 `gacha_pull`·`purchase`·`ledger` 테이블이다(§17.1, `server/schema.sql`). 이 둘은 다르다. 보상 계산이나 고객 문의 판정에 텔레메트리를 쓰지 않는다(§18.3).
- `ops/ai/runtime-policy.json`의 `telemetry: false`는 AI 도구의 사용 통계 설정이다. 게임 텔레메트리와 관계없다.
