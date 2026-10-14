| op | 의미 | 파라미터 |
|---|---|---|
| `dmg` | 피해 | v 즉시 / rv 다음 턴 예약 / ra 다다음 턴 예약 값, hits 타수, tgt(one·same·all·rand·fixed), brk 균열(기본 1, 약점일 때만), shock 충격(속성 무관), pierce 관통 bps, scale·hitsScale 카운터 비례, mult 조건부 배수 |
| `blk` | 방어 | v/rv/ra, scale, base0=true면 기본값 대신 0에서 스케일만 |
| `heal` | 파티 회복 | v/rv/ra, scale, overflowBlock 넘친 회복을 방어로 |
| `st` | 적 상태 부여 | s(vul·weak·mark·burn), n, rn 예약 시 수치, tgt |
| `buff` | 파티 버프 | s(charge·seraLock·healUp·lumiRush·overloadFree·counterDouble), n(bps), turns |
| `counter` | 반격 획득 | n |
| `draw` | 뽑기 | n, rn, scale |
| `discard` | 버리기 | n (플레이어가 고름) |
| `ap` | 행동력 즉시 +n | n (최대 9) |
| `apNext` | 다음 턴 행동력 +n | n |
| `gauge` | 필살기 게이지 +n | n, who(self·all) |
| `create` | 카드 생성 | card, n, zone(hand) |
| `pull` | 예약 1개 1턴 앞당김 | 픽 없으면 가장 먼저 건 다다음 예약 → 없으면 가장 먼저 건 예약 |
| `slot` | 이번 턴 예약 칸 추가 | next, after (최대 3/2) |
| `grow` | 이 카드 성장 +1 | 예약 해결일 때만, 카드당 전투 1회·파티 전투 상한 |
| `growPick` | 손의 성장 카드 1장 성장 +n | n |
| `growLowest` | 가장 낮은 성장 카드 n장 +1 | 전투 상한과 별도 |
| `growAll` | 모든 성장 카드 +1 | 공명 6 전용 |
| `copyPick` | 손의 성장 카드 사본(비용 0, 소멸) | filter |
| `loan` | 선불 계약 | 행동력 +n, 다음 턴 −n (부채). 부채 상한 4, 행동력 최대 9 초과 불가 |
| `defer` | 유예 | 다음 턴 부채 1개를 1턴 미루고 +1(재유예 불가) |
| `restraint` | 절제 계약 | 이번 턴 3장 이하 → 다음 턴 첫 예약 피해·방어 +30%, 4장째에 위약(다음 턴 드로우 −1) |
| `collateral` | 담보 계약 | 다음 턴 예약 칸 1개 봉인(다음 내 턴 종료까지) + 방어 v |
| `clearDebt` | 부채 모두 제거 | - |
| `selfdmg` | 과부하 | v: 파티 체력 손실(방어 무시). 쓰러지면 사용 불가 |
| `cleanse` | 해로운 효과 모두 제거 | 파티 약화 등 |
| `cleanseOne` | 해로운 효과 1종 제거 | s |
| `exhaustPick` | 소멸시키기 | from(hand·discard), n (플레이어가 고름) |
| `exhaustHand` | 손패 전부 소멸 | - |
| `tune` | 임시 부품 부착 | part, pick(one·same·all·random) |
| `costdown` | 비용 감소(이번 턴) | n, pick(one·all) |
| `recall` | 버린 카드 1장 손으로 | 플레이어가 고름 |
| `power` | 전투 지속 효과 | id(blueprint·greenhouse·bulwark·rebirth·oblivion·casefile·echochamber) + 수치 |
| `echo` | 잔향 +n | n |
| `nextBoost` | 다음 다른 캐릭터 카드 +bps | bps (즉시 사용 카드에만) |
| `freeNext` | 다음 n장 비용 0 | n |
| `breakBonus` | 균열 추가 감소 | n 또는 scale |
| `breakBonusAll` | 모든 적 균열 감소 | scale |
| `if` | 조건 분기 | cond, then[], else[] |

**조건(cond.c)**

| c | 참일 때 |
|---|---|
| `marked` | 대상에게 표식 |
| `burning` | 대상이 화상 |
| `broken` | 대상이 균열 상태 |
| `debt` | 부채가 있음 |
| `modded` | 이 카드에 부품(임시 포함) |
| `reserved` | 예약으로 해결 중 |
| `intentAttack` | 대상의 이번 의도가 공격 |
| `echo` | 잔향 ≥ n |
| `hpBelow` | 파티 체력 ≤ pct% |
| `targetHpBelow` | 대상 체력 ≤ pct% |
| `growthInHand` | 손의 성장 카드 ≥ n |
| `exhaustedAtLeast` | 소멸한 카드 ≥ n |
| `killedThis` | 이 카드로 처치 |

**카운터(scale.per)** — 값 = k × 카운터(최대 max)

| per | 카운터 |
|---|---|
| `echo` | 잔향 |
| `exhausted` | 이번 전투 소멸 수(+시작 보정) |
| `debt` | 부채 합 |
| `growthSum` | 덱의 성장치 합 |
| `growthCards` | 성장 카드 수 |
| `counter` | 현재 반격 |
| `missingHp10` | 잃은 체력 ÷10(내림) |
| `contracts` | 이번 턴 계약 수 |
| `tempParts` | 이번 전투 붙인 임시 부품 수 |
| `block` | 현재 방어 |
| `lastCount` | 직전 효과가 처리한 수 |
| `targetBurn` | 대상 화상 |
