# P1-CARD-FLOW-TURN-DIGEST — 작성자 보고

- 작성: Claude Opus 5.5(`claude-opus-5-5`), 요청 노력 xhigh, 2026-10-16. 실제 적용 노력과 session_id는 이 세션에서 조회할 수 없어 Codex 회수 메타데이터를 따른다.
- 변경 파일(허용된 3개만):
  - `client/MemoryForgeUnity/Assets/Scripts/BattleTurnDigest.cs`
  - `client/MemoryForgeUnity/Assets/Tests/PlayMode/BattleTurnDigestPlayModeTests.cs`
  - 이 보고서.
  - Main 클라이언트·씬·설정·Core·참고 자료·독립 검토서는 건드리지 않았다.
- 검증: **컴파일·Unity Edit/PlayMode·기존 시험 모두 NOT_RUN**(셸·Editor 없음). 수행한 것은 정적 대조뿐이다. 모든 기대 문자열을 구현의 분기와 한 줄씩 손으로 맞췄고, 이벤트 이름·필드·Source 값은 현재 Core의 `Emit` 지점(Battle.cs, Battle.PlayerActions.cs, Battle.Effects.cs)과 JS `this.ev` 목록으로 확인했다. `.meta`는 이 worktree의 기존 스크립트에도 없어서 Root의 Editor 임포트에 맡긴다.

## API (namespace `MemoryForge.Client`, 의존성은 MemoryForge.Core 타입뿐)

`BattleTurnDigest.Build(BattleDigestSnapshot? before, IEnumerable<BattleEvent?>? events, BattleDigestSnapshot? after) → IReadOnlyList<BattleDigestEntry>`

- 입력 DTO(공개 필드, null=투영 안 함):
  - `BattleDigestSnapshot{Turn, Hp, Block, Ap, Phase, Enemies, Reservations, CardNames}`
  - `BattleDigestEnemy{Uid, Name, Hp, Block, Alive, IntentText}`
  - `BattleDigestReservation{Uid, Name, Kind, Due}`
  - `CardNames`는 uid→표시 이름이다. 뽑을 더미 순서·RNG·비공개 카드 필드는 없다.
- 출력:
  - 매 호출 새 `ReadOnlyCollection`이다. `IList` 변경 시도는 `NotSupportedException`.
  - `BattleDigestEntry{Kind, Text, Provenance, IsReservedResolution}`는 생성자 internal인 불변 객체다.
  - 입력 객체·맵·이벤트 목록을 보관하지 않고, 입력을 바꾸지 않는다.
  - Run/Battle 호출, 타이머, 정적 상태가 없다.
- 행 순서(규칙 순서를 재배열하지 않음):
  1. `Summary` 2행: ① `턴 결과 · 3턴 → 4턴`(+`· 전투 승리/패배`, after.Phase 기준) ② `파티 체력 a → b · 방어 … · 행동력 … · 시간 예약 n장 → m장`. 양쪽이 모두 투영된 값만 쓰고, 하나도 없으면 `파티 상태 정보 없음`.
  2. `PreviousIntent`: before에서 살아 있는 적마다 `{적} · 직전 예고 · {IntentText}`(없으면 `… · 직전 예고 정보 없음`). **예고는 실행으로 표시하지 않는다.**
  3. 이벤트마다 1행. Core가 낸 순서 그대로다.
  4. `EnemyState`: 양쪽 적 목록이 있을 때만, 바뀐 적만 `결과 · {적} · 체력 a → b · 방어 … · 처치됨`, 새 적은 `결과 · {적} · 새로 등장 · 체력 n`.
- `IsReservedResolution`은 `resolved`에 Reserved가 있는 행에서만 참이다. 원문 §14.3 0.3초/장을 적용할 행이다. 예제의 250/180ms·K2는 들여오지 않았다.

### 이벤트 → 행 (Kind · 문구 · Provenance)

| Core 이벤트 | Kind | 문구 예 | Provenance |
|---|---|---|---|
| `play` Mode now/기타 | CardPlayed | `카드 사용 · 정조준 → 톱니 병정 · 행동력 1` | — |
| `play` Mode next/after | ReservationPlayed | `예약 사용 · 유리탄 → 기억 나방 · 다음 턴 · 행동력 2` | — |
| `reserve` | ReservationRegistered | `시간 예약 등록 · 유리탄 · 다음 턴 예약 · 4턴 시작에 해결` | — |
| `resolved` Reserved 없음 | CardResolved | `효과 처리 끝 · 정조준`(+`· 불발`) | — |
| `resolved` Reserved 있음 | ReservationResolved | `예약 해결 · 시한 포격 · 다음 턴 예약`(+`· 불발`) | — |
| `fizzle` / `echoCopy` / `exhaust` | Fizzle / Copy / Exhaust | `불발 · X · 표식 대상 없음` / `메아리 사본 · X` / `소멸 · X` | — |
| `dmg` Source card·ult·counter·relic·burn·기타 | CardDamage·UltimateDamage·CounterDamage·RelicDamage·BurnDamage·EffectDamage | `톱니 병정에게 피해 22 (방어 3 막힘) · 카드 「시한 포격」` | 카드·필살기·반격·유물·화상·기타 효과 |
| `hit` | EnemyAttack | `기억 나방의 공격 · 피해 12 (방어 6 막음)` | 적 공격 |
| `hpLoss` enemy·reflect·overload·relic·smudge·기타 | PartyHpLoss | `파티 체력 -5 · 반사` | 적 공격·반사·과부하·유물·얼룩·기타 |
| `block` card/collateral·relic/moss/rod·weapon·part·yuri·warmth·greenhouse/oblivion·기타 | PartyBlock | `방어 +8 · 유물` | 카드·유물·무기·부품·패시브·넘친 회복·지속 효과·기타 |
| `heal` / `status` / `break` / `kill` | PartyHeal / EnemyStatus / Break / Kill | `파티 체력 +5` / `톱니 병정 · 표식 +1` / `톱니 병정 · 균열!` / `기억 나방 처치` | — |
| `stunSkip` `charge` `expose` `enemyHeal` `insert` `partyDebuff` `recover` `phase` | EnemyAction | `톱니 병정 · 균열로 행동하지 못함` 등 | — |
| `repay` / `turn` / `end` | Repay / TurnStart / BattleEnd | `부채 상환 · 행동력 -1` / `4턴 시작 · 행동력 3` / `전투 종료 · 승리` | — |
| `envelope` | Other | `유물 효과 · 체력 1로 버팀` | 유물 |
| `reshuffle` `burnCard` `grow` `tune` `pull` `loan` `defer` `power` `restraintKept/Broken` `ult` | Other | 짧은 고정 문구 | — |
| null·빈 이름·미지(A의 `apDrain/sealSlot/delayRes/silence/eatName` 포함) | Other | `기타 전투 효과` | — |

필드가 없으면 숫자·이름을 생략한다. 이름의 기본값은 카드는 `카드`, 적은 `적`이다. 반사·과부하는 적에 대한 카드 피해가 아니라 파티 체력 손실 출처다.

## 한계 (의도한 범위)

- **카드 정의 이름:** 같은 명령의 `play/resolved/echoCopy` 이벤트가 이름 있는 uid와 그 정의 id를 함께 말할 때만 알 수 있다. 그래서 `dmg.Card`나 `insert.Card`는 그런 근거가 없으면 이름 없이 표시된다. 유물 이름은 지어내지 않는다.
- **이벤트가 없는 적 행동:** Core는 적 `def/buff/reflect/blockAll/summon`과 atkdebuff 약화에 이벤트를 내지 않는다. 이것들은 직전 예고와 최종 공개 값으로만 드러난다. 드로우도 이벤트가 없어 `reshuffle`만 보인다. 전체 애니메이션 대본이 아니다.
- **문구 다듬기 미완:** A의 후반 적 이벤트는 C# 필드가 확정된 뒤 별도로 문구를 추가할 대상이며, 지금은 일반 행이다. `phase`는 uid만 쓴다. 한국어 문구는 UX 검토 전 초안이다.
- **Main 미구현:** 입력 게이트·타이머·오버레이·로그 40줄 연결은 이 단위에 없다(Main 미변경).

## 시험 (NUnit `[Test]` 8개, PlayMode 어셈블리, NOT_RUN)

모두 명시 DTO와 손으로 쓴 이벤트만 쓴다. Run/Battle/콘텐츠/라이브 상태 주입은 없다.

1. 즉시 사용·즉시 해결 vs 예약 사용·시간 예약 등록 vs 예약 해결(다음/다다음 턴, 불발). `IsReservedResolution`은 예약 해결 2행만 참.
2. 메아리 사본은 Copy 행이며 사용 행이 아님. 이름은 이벤트가 뒷받침하는 범위만.
3. 적 공격·반격·유물·화상·반사·과부하·유물 체력 손실·방어 출처·필살기·이름 없는 카드 피해의 문구와 Provenance. 반사·과부하는 PartyHpLoss만.
4. null 입력·null 요소·빈/미지 이벤트·필드 누락이 예외 없이 일반 행이 됨.
5. 직전 예고는 예고로만 표시되고 죽은 적은 제외. `방어 6` 예고는 실행 행이 아니라 최종 `방어 0 → 6`으로만 보임.
6. 요약·적 최종 변화(처치·새 등장), 승리·패배 때 투영된 값만 사용.
7. 턴 종료형 이벤트 15개의 Core 순서 보존.
8. 읽기 전용, 호출마다 새 목록, 호출자 DTO·맵·이벤트를 나중에 바꿔도 행 불변, Build가 입력 이벤트를 바꾸지 않음(CanonicalState 직렬화 비교).

## Main 통합 단계 (B 완료 후 Root의 writer)

1. **`end` 직전 `before` 투영**(`run.State.Battle`):
   - `Turn/Phase/Hp/Block/Ap`.
   - 적마다 `Uid/Name/Hp/Block/Alive`와 `IntentText`. IntentText는 화면의 "이번" 줄과 같은 `MemoryForgeBattleClient.IntentText(enemy.Intent, enemy.Status, b.DifficultyDamage, rules.WeakBps, true)`.
   - 예약마다 `Uid/Due/Kind`와 `run.BattleEffectiveCard(uid).Name`.
   - 손패·예약 uid의 `CardNames`.
   - `Zones.Draw`와 `Rng`는 읽지 않는다.
2. **응답 보관:** `run.BattleCommand("end")`의 반환 `BattleResponse`를 받는다. 현재 `SendBattle`은 이 값을 버린다.
3. **이벤트 카드 이름:** 이벤트 uid는 이벤트의 `Id`로 정적 카드 목록(`cardDefs[e.Id].Name`)에서 이름을 붙인다. 전투가 살아 있으면 공개 구역(손·버림·소멸·예약)에 있는 uid만 `BattleEffectiveCard`로 붙인다. 뽑을 더미 uid에는 이름을 붙이지 않는다.
4. **`after` 투영:**
   - `run.State.Battle`이 있으면 1과 같은 투영.
   - 전투가 끝나 사라졌으면 `{Phase = response.Phase, Hp = run.State.Hp}`만 쓴다. 다른 값은 지어내지 않는다.
5. **행 보관과 표시:** `Build` 결과를 화면 전용 상태로 보관하고, 이미 확정된 화면 위에 막는 오버레이로 그린다.
   - `IsReservedResolution` 행은 0.3초/장으로 단계 표시한다.
   - 다른 행의 간격은 미승인 시험값이다.
   - 단계 진행·건너뛰기·계속은 라벨만 바꾸고 Run을 호출하지 않는다.
   - '서버 확정'으로 표기하지 않는다.
6. **입력 게이트:** Root 결정대로 요약이 열린 동안 **모든 공개 명령 진입점**(EnterNode/ConfirmCard/PlayCard/PickCard/AutoPick/EndTurn/UseUltimate/보상·서비스·저장 명령 등)이 Run을 호출하지 않고 거절해야 한다. 요약을 닫고 대신 실행하지 않는다. 해제는 계속/건너뛰기와 Root가 정한 수명 경계(Depart/StartPractice/OnDisable 등)로만 한다.
7. **Main 수용 시험:** 독립 검토서 S1의 인수 시험(실제 클릭 2회=명령 1회, 승리 후 지도로 겹침, 시간 조건별 지문 동일, 눌린 버튼 유지 등)을 Main 단계에서 추가한다.
