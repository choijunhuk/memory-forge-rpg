# <a id="ch17"></a>17. 서버: 데이터베이스·트랜잭션·API

파일: `server/schema.sql`(PostgreSQL 16, `tools/check_schema.sh`로 실제 적용·제약 시험), `server/openapi.yaml`(OpenAPI 3.1, 27개 경로, 검증 통과), `server/tests/schema_checks.sql`.

## 17.1 테이블

| 테이블 | 용도 | 핵심 제약 |
|---|---|---|
| `content_version` | 규칙 버전·데이터 해시 | PK rules_version |
| `account`, `auth_identity`, `account_progress` | 계정·인증·계정 레벨·집중·스토리 진행 | 레벨 1~60, 집중 0~999, 나이 구간 자기 신고(생년월일 저장 안 함) |
| `wallet` | 재화 잔액(유료·무료 결정 분리) | 잔액 ≥ 0, 허용 재화 목록 |
| `inventory` | 재료·아이템 | 수량 ≥ 0 |
| `ledger` | 모든 재화·아이템 변경 원장(추가 전용) | `(account, request_id, asset, reason)` 유일 → 이중 지급 방지 |
| `command_receipt` | 멱등 영수증 | `(account, request_id)` PK, 요청 해시 저장 |
| `character_owned` | 보유 캐릭터·레벨·승급·스킬·장비·인연 | 레벨 ≤ 승급 상한(20/30/40/50/60), 스킬 1~10, 장비 0~15 |
| `weapon_instance` | 무기 | 모집 무기는 계정·무기당 1행(중복은 copies), 한 캐릭터 무기 1개, 장착 캐릭터는 보유 캐릭터여야 함 |
| `memory_deck`, `memory_deck_history` | 기억 덱 현재·이력 | 용량 사용 0~9 |
| `banner`, `gacha_pity`, `gacha_pull` | 배너·천장 상태(풀별)·모든 모집 결과 | 천장 n 범위(캐릭터<80, 무기<70, 초심자<50), 결과 `(account, request, idx)` 유일 |
| `run`, `battle` | 탐사·단일 전투(비공개 상태 포함) | 계정당 진행 중 탐사 1개 |
| `weekly_claim_counter`, `rift_progress`, `dungeon_clear`, `mission_progress`, `pass_state`, `monthly_card` | 주기 콘텐츠 | 주간 보스 0~3, 패스 플러스 ⇒ 프리미엄, 월정액 180일 상한 |
| `purchase`, `mail` | 결제·우편 | 구매 토큰 다이제스트 PK, 검증 모드(운영은 play만) |

## 17.2 핵심 트랜잭션

모든 쓰기 요청은 같은 골격을 따른다.

```text
BEGIN
  1. command_receipt 조회: 같은 request_id가 있으면 → 요청 해시 같음: 저장된 응답 반환 / 다름: 409 IDEMPOTENCY_CONFLICT
  2. 잠금(항상 같은 순서): account_progress → wallet(재화 이름순) → inventory(아이템 이름순) → 대상 행(gacha_pity / character_owned / run …) FOR UPDATE
  3. 규칙 실행(C# 코어). 실패 → ROLLBACK + 422 {code}
  4. 상태 행 갱신(version + 1), ledger 행 추가, 결과 행 추가
  5. command_receipt 저장(응답 본문 포함)
COMMIT → 응답
```

| 트랜잭션 | 단계 4의 내용 |
|---|---|
| 모집 1/10회 | 모집권·결정 차감(원장), 10회 결과 `gacha_pull` 10행(roll_bps 포함), 보유 캐릭터·무기 copies 증가 또는 신규 행, 기억 결정체·잔광 지급(원장), `gacha_pity` 갱신 |
| 레벨업 | 아이템 차감(최소 조합), 크레딧 차감, 레벨·경험치 갱신(상한에서 정지) |
| 승급·스킬·장비 | 재료·크레딧 차감, 단계 +1(계정 레벨 조건 검사) |
| 탐사 명령 | `run.private_state` 갱신, state_version + 1, 이벤트 반환. 탐사 안의 동전 등은 원장 밖(탐사 상태) |
| 기억 저장 | `Run.commitMemory` 실행 → 캐릭터별 `memory_deck` 교체 + 이전 판 `memory_deck_history`, run.status = saved |
| 주간 보스 보상 | 승리한 battle 확인 → `weekly_claim_counter` +1(최대 3, 초과 시 422 WEEKLY_LIMIT) → 승급 핵·크레딧·토큰 지급 |
| 구매 | Play Developer API 검증 → `purchase` 삽입(토큰 다이제스트 유일) → 지급(원장) → 커밋 후 acknowledge/consume. 지급 전 실패는 다음 로그인 때 재시도 |
| 우편 수령 | 만료 확인 → 첨부 지급(원장) → claimed_at |

## 17.3 API 요약

| 메서드·경로 | 용도 |
|---|---|
| `POST /auth/session` | 로그인(Play Games·게스트) |
| `GET /account` · `GET /content/version` | 계정 스냅샷 · 규칙 버전 |
| `GET /gacha/banners` · `GET /gacha/banners/{id}/rates` · `POST /gacha/pull` · `POST /gacha/selector` · `GET /gacha/history` | 모집 |
| `GET /characters` · `POST /characters/{id}/{action}` (action = level-up · ascend · skill-up · gear-craft · gear-enhance · gift) | 캐릭터 성장 |
| `GET /weapons` · `POST /weapons/{instanceId}/{action}` (action = level-up · ascend · equip · unequip) | 무기 |
| `GET /memory-decks` | 기억 덱 |
| `POST /runs` · `POST /runs/{id}/commands` · `POST /runs/{id}/memory-commit` | 탐사 |
| `POST /battles` · `POST /battles/{id}/commands` · `POST /dungeons/{id}/sweep` | 단일 전투·소탕 |
| `GET /missions` · `POST /missions/{id}/claim` · `GET /pass` · `GET /mail` · `POST /mail/{id}/claim` | 운영 |
| `GET /shop` · `POST /purchases/verify` · `POST /shop/exchange` | 상점 |

상세 스키마는 `server/openapi.yaml`. 오류 응답은 `{code, message, detail}`이며 코드는 엔진 `RuleError`와 서버 코드(`IDEMPOTENCY_CONFLICT`, `STALE_VERSION`, `CONTENT_OUTDATED`, `NOT_ENOUGH_ITEMS`, `LOCKED_BY_ACCOUNT_LEVEL`, `WEEKLY_LIMIT`, `PURCHASE_INVALID`, `MOCK_FORBIDDEN` 등). 엔진 코드와 사용자 문구는 부록 §23.2.

## 17.4 공개 상태 DTO (전투)

서버 어댑터가 엔진 `publicView()`(뽑을 더미는 장수만, RNG 없음)를 아래 DTO로 변환한다. 카드별 `cost`·`modes`는 서버가 `costOf`·`allowedModes`로 계산해 넣는다(클라이언트가 규칙을 다시 계산하지 않아도 되게).

```json
{
  "version": 12, "phase": "PLAYER", "turn": 3, "hp": 201, "maxHp": 243, "block": 6, "counter": 0, "ap": 4, "apNext": 0,
  "hand": [{"uid": "c14", "id": "sera_timed", "plus": false, "mods": {"c": "c_delay", "e": null}, "temp": [], "growth": 0, "cost": 2, "modes": ["next", "after"]}],
  "piles": {"draw": 9, "discard": 4, "exhaust": 1},
  "reservations": [{"uid": "c9", "id": "sera_round", "due": 4, "kind": "next", "target": "e2"}],
  "slots": {"next": {"cap": 2, "used": 1, "sealed": false}, "after": {"cap": 1, "used": 0}},
  "actors": [{"id": "sera", "gauge": 40, "res": 1}],
  "enemies": [{"uid": "e2", "id": "e_cog", "hp": 14, "maxHp": 24, "block": 0, "brk": 2, "brkMax": 4, "brokenUntil": 0,
               "weak": ["volt", "void"], "st": {"vul": 0, "weak": 0, "mark": 1, "burn": 0, "str": 0, "reflect": 0},
               "intent": {"t": "atk", "d": 18}, "next": {"t": "atk", "d": 14}}],
  "status": {"debts": [{"n": 2, "due": 4}], "echo": 1, "restraint": false, "buffs": [{"s": "charge", "n": 4000, "until": 4}]}
}
```

이벤트(`ev`)는 연출용 순서 목록이다: `play`, `reserve`, `dmg`, `block`, `break`, `kill`, `hit`, `hpLoss`, `status`, `grow`, `exhaust`, `repay`, `turn`, `stunSkip`, `ult`, `fizzle`, `end` 등.

## 17.5 저장 v2와 기존 API·DB의 접합 작업

현재 스키마·OpenAPI는 legacy 기준 계약이다. `selectedChangeIds`, `expectedMemoryVersion`, stable key, before/after 스냅샷을 §8.11 요청과 일치시키고, 기존 memory_deck_history에 이관 이력·콘텐츠 해시를 남겨야 한다. 독립 저장 모듈14개 시험은 DB 인증·잠금·재전송·결제 원장 시험을 대체하지 않는다.
