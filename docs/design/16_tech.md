# 16. 기술 설계

## 16.1 구성

```wire
content/*.mjs ──(node content/build.mjs)──▶ data/game_data.json  ◀── 단일 데이터 원본(규칙 버전 + 해시)
      │                                          │
      │                     ┌────────────────────┼─────────────────────────┐
      ▼                     ▼                    ▼                         ▼
tools/gen_docs.mjs   engine/rules-core.js   core/MemoryForge.Core(C#)   server(ASP.NET Core)
(문서 표 생성)        JS 규칙 참조·프로토타입    netstandard2.1 · C# 8         .NET LTS + PostgreSQL 16
                     ·봇·벡터 생성             Unity 클라이언트 + 서버 공유    권위 판정·원장·모집
                            │                    │  ▲                          │
                            └── 교차 벡터 ───────┘  └──── 같은 코어 DLL ───────┘
                                                       Unity 6 클라이언트(연출·입력·캐시)
```

- **권위**: 전투·탐사·모집·재화는 서버가 판정한다. 클라이언트는 같은 C# 코어로 **미리보기**(버튼의 예상 피해)와 연출만 계산한다. 서버 응답의 공개 상태가 항상 우선한다.
- **데이터 한 벌**: 클라이언트·서버·문서·프로토타입이 같은 `game_data.json`을 쓴다. 클라이언트는 시작 시 `/content/version`으로 규칙 버전·해시를 확인하고 CDN에서 해시 주소의 데이터를 받는다. 버전이 다른 명령은 서버가 `409 CONTENT_OUTDATED`로 거절한다.

## 16.2 규칙 코어 이식 (JS → C#)

| 단계 | 내용 | 확인 |
|---|---|---|
| 완료 | `Rng`, `Gacha`(상태 기계), `Stats`, `ReservationBook` | `CoreChecks` 202/202 |
| P1 | `Battle` 전체(효과 op 39종, 적 행동 12종, 유물 32·부품 16·무기 패시브 17종), `Run`, 기억 저장 | 아래 교차 시험 |
| P2-3 | JS·C# 동시: 전투 수정자(기록 균열 규칙 6종), 연전 진행기(층 3연전·2파티·파티별 체력 이월), 웨이브 진행(성장 던전 1~3웨이브) | 단위 시험 + 트레이스 |

교차 시험 방식: JS가 `(시드, 데이터 해시, 명령 목록)` 시나리오 1,000개를 무작위 봇으로 만들고 각 명령 뒤의 상태 해시(정렬된 JSON의 SHA-256)를 저장한다(`tools/gen_traces.mjs`, P1 작성). C#은 같은 명령을 재생해 모든 해시가 같아야 통과. 규칙: 부동소수점 금지(정수·BigInteger), 모든 반올림은 명세 그대로(내림·JS `Math.round` 동치), 컬렉션 순회 순서 고정(등장 순서).

## 16.3 결정론과 숨겨진 정보

- 전투 RNG 상태와 뽑을 더미 순서는 서버 비공개 상태(`run.private_state`, `battle.private_state`)에만 있다. 공개 상태는 `publicView`: 뽑을 더미 장수, 손패, 버린·소멸 더미, 예약, 적의 현재·다음 의도.
- 같은 초기 상태 + 같은 명령열 = 같은 결과(재현). 고객 문의·치트 의심 시 서버에서 재생해 검증한다.
- 모집 RNG는 CSPRNG(`RandomNumberGenerator`)로 매 회 0~9999 정수(roll_bps)와 풀 선택 인덱스를 뽑아 기록한다. 시드 RNG를 모집에 쓰지 않는다.

## 16.4 Unity 클라이언트

| 항목 | 결정 |
|---|---|
| 버전 | Unity 6 LTS 계열, IL2CPP, ARM64, URP 2D |
| 구조 | 화면 = View(프리팹) + Presenter, 상태는 서버 스냅샷 + 이벤트 큐. 전투 연출은 서버 이벤트(`ev`)를 순서대로 재생 |
| 리소스 | Addressables(캐릭터별 번들), Spine 런타임 또는 Live2D, TextMeshPro 한국어 폰트 아틀라스(동적 + 자주 쓰는 2,350자 정적) |
| 입력 | 카드 탭 선택 → 방식 버튼(드래그 사용은 선택 사항). 모든 버튼 48dp 이상 |
| 오프라인 | 진행 저장 없음. 네트워크가 끊기면 마지막 명령을 같은 요청 ID로 재전송(멱등) |
| 성능 목표 | 중급기(스냅드래곤 7 계열·RAM 6GB)에서 60fps, 메모리 1.5GB 이하, 첫 설치 AAB 150MB 이하 + 추가 다운로드 |
| Android | 2026-08-31 이후 신규 앱·업데이트는 **target API 36 이상**(출시 시점 요구치 재확인), 16KB 페이지 크기 지원 빌드 |

## 16.5 서버

| 항목 | 결정 |
|---|---|
| 런타임 | ASP.NET Core(.NET LTS), 상태 없는 API 서버 다중화 |
| DB | PostgreSQL 16(`server/schema.sql`), 계정 단위 행 잠금, 모든 재화 변경 = 원장 행 |
| 캐시 | 선택: Redis(배너·콘텐츠 버전·속도 제한). 판정 상태는 DB에만 |
| 인증 | Play Games Services 서버 인증 코드 → 세션 토큰(JWT, 1시간) + 갱신 토큰 |
| 무결성 | Play Integrity API 판정을 로그인·결제·모집 요청에 첨부(차단보다 위험 점수로 사용) |
| 명령 처리 | 요청 = `{requestId, expectedVersion, kind, args}`. 계정 잠금 → 영수증 확인(멱등) → 엔진 실행 → 상태·원장·영수증 저장 → 커밋 → 응답 |
| 속도 제한 | 계정당 명령 20/초, 모집 2/초, 구매 검증 5/분 |

## 16.6 텔레메트리(이벤트 사전)

| 이벤트 | 필드 |
|---|---|
| `run_start` / `run_end` | runId, mode, chapter, party[charId,res,level,weaponId], difficulty, result, floorsCleared, turns, durationSec |
| `battle_end` | battleId/runId, encounter, result, turns, hpLost, breaks, reservesMade, ultsUsed |
| `card_play` (표본 10%) | cardId, mode, cost, apLeft, target tier, previewDmg, actualDmg |
| `memory_commit` | charId, capacity, used, changes[type,cost], defeat |
| `gacha_pull` | bannerId, count, results[rarity,id,featured,lost], pityBefore, currency mix |
| `purchase` | productId, priceKrw, firstBonus, result |
| `tutorial_step` | step, durationSec, skipped |
| `error_shown` | code, screen |

개인 식별 정보는 넣지 않는다(계정 ID는 해시). 보존 기간 13개월.
