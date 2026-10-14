# <a id="ch6"></a>6. 카드·효과 DSL·개조

## 6.1 카드 데이터 형식

카드는 `content/helpers.mjs`의 `card(id, name, owner, opts)`로 작성하고 `data/game_data.json → cards[]`로 출력된다.

| 필드 | 타입 | 설명 |
|---|---|---|
| `id` | string | 전역 고유. 캐릭터 카드 `<char>_<name>`, 공용 `com_*`, 무기 카드 `wc_*`, 저주 소문자 단어 |
| `name` | string | 표시 이름(한국어). 현지화 키는 `card.<id>.name` |
| `owner` | string | 캐릭터 ID · `common` · `weapon` · `curse` |
| `rar` | B·A·S·T·C | 기본·고급·희귀·토큰·저주. 보상 확률과 기억 저장 비용을 정한다 |
| `cost` | int | 행동력 비용(0~3) |
| `type` | atk·skl | 공격/기술(첫 공격 판정, 충전 소비 대상) |
| `res` | y·n·o·after | 사용 방식(§5.4) |
| `kw` | string[] | `exhaust`(소멸), `contract`(계약) |
| `uses` | int | 전투당 사용 횟수(다 쓰면 소멸) |
| `eff` | op[] | 순서 있는 효과 목록(§6.3) |
| `plus` | {eff} | 강화(+) 전용 효과. 없으면 자동 강화 규칙(§6.2) |
| `growth` / `evo` | bool / id[2] | 성장 카드 여부 / 진화 분기 2개 |
| `costIf` | {cond, delta} | 조건부 비용 변경 |
| `attr` | 속성 | 생략 시 소유 캐릭터 속성, 무기 카드는 `wielder` |
| `start` | bool | 기본(시작) 기억 덱 포함 |
| `pool` | bool | false면 보상·상점에 나오지 않음(진화·토큰·저주) |
| `flavor` | string | 효과가 없는 카드의 설명문 |

카드 텍스트는 효과 목록에서 **자동 생성**한다(`cardText`). 수동 문구를 따로 두지 않는 이유는 미리보기·판정·표기가 어긋나지 않게 하기 위해서다. 같은 규칙을 C#·서버·클라이언트가 공유한다.

## 6.2 강화(+)

- 휴식에서 1장 강화(카드당 1회, 무기 카드 불가).
- 자동 규칙: 피해·방어·회복·반격·담보의 `v/rv/ra`(반격은 `n/rn`) × 1.3 **올림**, 상태 부여 `n/rn` +1. `plus.eff`가 있으면 그것을 쓴다.
- 강화는 기억 저장 비용 1(§8).

## 6.3 효과 DSL 참조

효과(op)는 JSON 객체이며 목록 순서대로 실행한다. 대상 `tgt`: `one`(지정, 기본), `same`(직전 대상), `all`, `rand`, `fixed`(표식 고정).

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
| `growAll` | 모든 성장 카드 +1 | 사용처: 유리 세계수 카드 및 공명6 효과. 성장 가능한 카드만 실제 증가 |
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

**새 op를 추가하는 절차**: ① `engine/rules-core.js`의 `execOp`·`opText`(텍스트)·필요하면 `validateOps`(사전 검사)에 추가 → ② C# 이식 → ③ `tools/gen_docs.mjs`의 OPS 표에 설명 추가(빠지면 문서 생성 실패) → ④ 단위 시험·교차 벡터 추가.

## 6.4 카드 수치 예산(작성 가이드)

현재 데이터의 행동력 1당 평균값(데이터에서 자동 집계):

| 구분 | 카드 수 | 지금 | 다음 턴 예약 | 다다음 턴 예약 | 표본 |
|---|---|---|---|---|---|
| 피해 · 기본(B) | 14 | 8.2 | 11.2 | 11.2 | `sera_round` `nia_wrench` `harin_bash` `aila_frostlight` `sion_cut` `sion_void` `kaya_bolt` `kaya_deduce` `lumi_jab` `lumi_spark` `com_slash` `com_overwatch` `wc_basic_ranged` `wc_basic_melee` |
| 피해 · 고급(A) | 7 | 8.1 | 11.0 | 11.6 | `sera_timed` `rien_crescendo` `wc_star_bow` `wc_frost_blade` `wc_hunter_rifle` `wc_mech_bow` `wc_guard_saber` |
| 피해 · 희귀(S) | 7 | 9.4 | 12.3 | 12.3 | `sera_phase` `sera_last` `sion_requiem` `sion_lastpage` `kaya_verdict` `lumi_lightning` `com_breaker` |
| 방어 · 기본(B) | 7 | 6.9 | 9.7 | 9.7 | `nia_barrier` `harin_shield` `sion_bind` `kaya_cover` `lumi_guard` `com_guard` `wc_basic_device` |
| 방어 · 고급(A) | 4 | 6.8 | 10.6 | 10.6 | `harin_frostwall` `harin_hold` `com_barrier` `wc_ledger_lock` |

집계 방법: 보상 풀 카드 중 효과가 정확히 1개(단일 대상 피해 또는 방어, 카운터 비례·광역·무작위 제외)인 카드, 값 × 타수 ÷ 행동력(비용 0은 0.5). 무기 카드 포함, 진화·토큰 제외. 예약 전용 카드의 "지금" 칸은 참고용 기본값 v.

작성 규칙:

1. **예약 가치 ≈ 지금 × 1.4~1.5**, 다다음 턴 전용·예약 전용 카드는 × 1.8~1.9까지(대신 즉시 사용 불가). 비용을 먼저 내는 위험과 칸 소비의 대가다.
2. 고급·희귀의 차이는 **수치보다 기능**(균열 수치, 다단, 조건 보너스, 덱 조작)으로 낸다. 희귀 카드도 순수 수치는 기본 대비 +15~20% 이내.
3. 균열 수치: 기본 1, 균열 특화 카드 2~3, 비용 3 이상이거나 다다음 턴 전용·소멸인 마무리 카드 4~5(`sera_last` 5, `kaya_closecase` 5, `sion_lastpage` 4), 필살기 1~4(광역·다단 필살기는 1). 충격(속성 무관)은 공용 카드 위주로 1, 균열 쐐기 3.
4. 비용 0 카드는 다음 중 하나 이상을 반드시 가진다: ① 전투당 횟수 제한 또는 소멸 ② 손패 대가(버리기·소멸, 또는 자신을 대체하는 뽑기) ③ 체력 대가(과부하) ④ 미래 비용(부채·봉인·위약) ⑤ 낮은 수치(피해·방어가 행동력 1 카드 평균의 50% 이하이거나 피해·방어가 없음) ⑥ 덱에 1장뿐인 무기 카드. 현재 비용 0 카드 25장 모두 이 중 하나 이상에 해당한다(무한 반복 방지).
5. 새 카드는 `node engine/tests/balance_probe.mjs`로 일반·엘리트·보스 평균 턴 수와 체력 손실이 §19.4 목표 범위에 드는지 확인한다.

## 6.5 카드 풀과 보상

- 캐릭터마다 고유 카드 12장(기본 B 4 · 고급 A 4 · 희귀 S 4, 그중 시작 카드 4장은 기본 3~4장 + 고급 0~1장) + 진화 카드(유리 10장) + 토큰(니아 드론 사격).
- 공용 카드 18장(B 8 · A 9 · S 1)과 저주 2장, 무기 카드 22장(무기마다 1장, 장착 시 덱에 1장).
- 전투 보상 카드 3택 1(§7.4). 상점 카드 3장(시작 카드 제외).

## 6.6 부품(카드 개조)

카드 한 장에 **시간 회로 1칸 + 보조 각인 1칸** 영구 부품을 붙일 수 있다(같은 칸에 다시 붙이면 교체). 임시 부품(증폭·과열)은 전투 중 카드 효과로만 붙고 전투가 끝나면 사라진다. 영구 부품은 공방 노드의 **변주**(무작위 부품 3개 중 1개, 이번 탐사 유지)나 보상·상점·사건으로 얻고, 기억 저장 때 비용을 내면 저장 덱에 남는다.

| ID | 이름 | 칸 | 등급 | 효과 | 기억 저장 비용 |
|---|---|---|---|---|---|
| `amp` | 증폭 | 임시(전투 한정) | - | 이번 전투 동안 피해·방어 +30%. | 저장 불가 |
| `overheat` | 과열 | 임시(전투 한정) | - | 이번 전투 동안 피해 +50%, 사용 후 소멸. | 저장 불가 |
| `c_delay` | 지연 증폭 | 시간 회로 | 일반 | 예약 해결 시 피해·방어 +40%, 즉시 사용 시 -10%. | 1 |
| `c_quick` | 즉발 회로 | 시간 회로 | 일반 | 즉시 사용 시 피해·방어 +20%. 예약 불가. | 1 |
| `c_precise` | 정밀 회로 | 시간 회로 | 일반 | 균열 수치 +1. | 1 |
| `c_echo` | 메아리 회로 | 시간 회로 | 희귀 | 예약으로 해결되면 다음 턴 손에 50% 위력의 소멸 사본(비용 0) 추가. | 2 |
| `c_far` | 원거리 전송 | 시간 회로 | 희귀 | 다다음 턴 예약 시 피해·방어 +70%. | 2 |
| `c_lite` | 경량 회로 | 시간 회로 | 희귀 | 비용 -1(최소 0), 피해·방어 -25%. | 2 |
| `e_pierce` | 관통 | 보조 각인 | 일반 | 피해의 50%가 방어를 무시. | 1 |
| `e_guard` | 보호 | 보조 각인 | 일반 | 해결 시 방어 4. | 1 |
| `e_recall` | 회수 | 보조 각인 | 일반 | 해결 시 카드 1장 뽑기(턴당 1회). | 1 |
| `e_mark` | 표식 | 보조 각인 | 일반 | 피해를 준 적에게 표식 1. | 1 |
| `e_burn` | 연소 | 보조 각인 | 일반 | 피해를 준 적에게 화상 2. | 1 |
| `e_chill` | 냉각 | 보조 각인 | 일반 | 피해를 준 적에게 약화 1. | 1 |
| `e_gauge` | 가속 | 보조 각인 | 희귀 | 해결 시 소유자 필살기 게이지 +10. | 2 |
| `e_sacrifice` | 소멸 대가 | 보조 각인 | 희귀 | 피해·방어 +50%. 사용 후 소멸. | 2 |

## 6.7 공용 카드와 저주

| ID | 이름 | 등급 | 비용 | 종류 | 사용 방식 | 효과 |
|---|---|---|---|---|---|---|
| `com_slash` | 기본 공격 | 기본 | 1 | 공격 | 지금·예약·다다음 | 피해 6 (예약 9), 충격 1. |
| `com_guard` | 기본 방어 | 기본 | 1 | 기술 | 지금·예약·다다음 | 방어 6 (예약 9). |
| `com_sort` | 정리 | 기본 | 0 | 기술 | 즉시 전용 | [즉시 전용·전투당 2회] 카드 1장 뽑기. 카드 1장 버리기. |
| `com_patch` | 응급 수리 | 기본 | 1 | 기술 | 지금·예약·다다음 | [소멸] 회복 6 (예약 8). |
| `com_capacitor` | 시간 축전기 | 고급 | 1 | 기술 | 예약 전용(다음·다다음) | [예약 전용] 행동력 +2. |
| `com_focus` | 집중 | 기본 | 0 | 기술 | 즉시 전용 | [즉시 전용·소멸] 다음 공격 카드 피해 +25%. |
| `com_shockwave` | 충격파 | 고급 | 2 | 공격 | 지금·예약·다다음 | 모든 적에게 피해 5 (예약 7), 충격 1. |
| `com_barrier` | 공용 차폐 | 고급 | 2 | 기술 | 지금·예약·다다음 | 방어 12 (예약 17). |
| `com_recall` | 회상 | 고급 | 1 | 기술 | 즉시 전용 | [즉시 전용] 버린 카드 1장을 손으로. |
| `com_flash` | 섬광탄 | 고급 | 0 | 기술 | 즉시 전용 | [즉시 전용·소멸] 모든 적에게 약화 1. |
| `com_pin` | 고정핀 | 기본 | 1 | 기술 | 지금·예약·다다음 | 취약 2 (예약 3). |
| `com_rally` | 사기 진작 | 고급 | 1 | 기술 | 즉시 전용 | [즉시 전용] 모든 캐릭터 필살기 게이지 +15. |
| `com_salvage` | 재활용 | 고급 | 0 | 기술 | 즉시 전용 | [즉시 전용] 손에서 카드 1장 소멸. 행동력 +1. |
| `com_overwatch` | 경계 사격 | 기본 | 1 | 공격 | 예약 전용(다음·다다음) | [예약 전용] 피해 11, 충격 1. |
| `com_bandage` | 붕대 | 기본 | 0 | 기술 | 즉시 전용 | [즉시 전용·소멸] 회복 3. |
| `com_tactics` | 전술 노트 | 고급 | 1 | 기술 | 즉시 전용 | [즉시 전용] 카드 2장 뽑기. |
| `com_anchor` | 시간 닻 | 고급 | 1 | 기술 | 즉시 전용 | [즉시 전용] 다다음 턴 예약 칸 +1. 카드 1장 뽑기. |
| `com_breaker` | 균열 쐐기 | 희귀 | 1 | 공격 | 지금·예약·다다음 | 피해 4 (예약 6), 충격 3. |
| `smudge` | 얼룩 | 저주 | 1 | 기술 | 즉시 전용 | [즉시 전용·소멸] 턴 종료 시 손에 있으면 파티 체력 2를 잃는다.. |
| `penalty_note` | 위약금 | 저주 | 2 | 기술 | 즉시 전용 | [즉시 전용·소멸] 사용하면 사라지는 빚 문서.. |

## 6.8 카드 ID 완전 목록

무기 카드는 기존 §10에 이름·효과가 있지만 ID가 일부 생략되어 있었다. 아래는 현재 데이터 173개 카드의 ID·이름·소유자 전체 목록이다. 고유 카드 상세는 §9.3, 공용 카드는 §6.7, 무기 효과는 §10.1에 있다.

| ID | 이름 | 소유자 |
|---|---|---|
| `sera_round` | 유리탄 | `sera` |
| `sera_cover` | 엄폐 사격 | `sera` |
| `sera_aim` | 정조준 | `sera` |
| `sera_reload` | 재장전 | `sera` |
| `sera_timed` | 시한 포격 | `sera` |
| `sera_calc` | 탄도 계산 | `sera` |
| `sera_pull` | 시간 당기기 | `sera` |
| `sera_double` | 연속 저격 | `sera` |
| `sera_phase` | 위상 저격 | `sera` |
| `sera_lock` | 미래 확정 | `sera` |
| `sera_flash` | 섬광 탄막 | `sera` |
| `sera_last` | 종언의 초침 | `sera` |
| `nia_wrench` | 스패너 타격 | `nia` |
| `nia_barrier` | 응급 차폐막 | `nia` |
| `nia_tune` | 미세 조정 | `nia` |
| `nia_scan` | 구조 분석 | `nia` |
| `nia_drone` | 보조 드론 | `nia` |
| `nia_overclock` | 과열 개조 | `nia` |
| `nia_rebuild` | 재조립 | `nia` |
| `nia_shock` | 공허 방전 | `nia` |
| `nia_blueprint` | 설계도 전개 | `nia` |
| `nia_assembly` | 즉석 조립 | `nia` |
| `nia_fieldlab` | 이동 공방 | `nia` |
| `nia_masterwork` | 걸작 | `nia` |
| `nia_drone_shot` | 드론 사격 | `nia` |
| `yuri_cocoon` | 유리 고치 | `yuri` |
| `yuri_sprout` | 새싹 화살 | `yuri` |
| `yuri_root` | 공생 뿌리 | `yuri` |
| `yuri_dew` | 이슬 맺기 | `yuri` |
| `yuri_greenhouse` | 온실 | `yuri` |
| `yuri_frostbloom` | 서리꽃 | `yuri` |
| `yuri_tend` | 돌봄 | `yuri` |
| `yuri_mutual` | 상리공생 | `yuri` |
| `yuri_metamorph` | 탈피 | `yuri` |
| `yuri_canopy` | 수관 | `yuri` |
| `yuri_seedvault` | 씨앗 금고 | `yuri` |
| `yuri_worldtree` | 세계수 | `yuri` |
| `yuri_cocoon_guard` | 유리 고치: 수호 | `yuri` |
| `yuri_cocoon_prey` | 유리 고치: 포식 | `yuri` |
| `yuri_sprout_thorn` | 새싹 화살: 가시덤불 | `yuri` |
| `yuri_sprout_pollen` | 새싹 화살: 꽃가루 | `yuri` |
| `yuri_root_net` | 공생 뿌리: 보호망 | `yuri` |
| `yuri_root_hunt` | 공생 뿌리: 사냥망 | `yuri` |
| `yuri_frostbloom_ice` | 서리꽃: 빙화 | `yuri` |
| `yuri_frostbloom_snow` | 서리꽃: 만년설 | `yuri` |
| `yuri_seedvault_plenty` | 씨앗 금고: 풍요 | `yuri` |
| `yuri_seedvault_fruit` | 씨앗 금고: 결실 | `yuri` |
| `ravien_advance` | 선불 계약 | `ravien` |
| `ravien_strike` | 인장 타격 | `ravien` |
| `ravien_restraint` | 절제 계약 | `ravien` |
| `ravien_collateral` | 담보 계약 | `ravien` |
| `ravien_defer` | 유예 협상 | `ravien` |
| `ravien_interest` | 이자 징수 | `ravien` |
| `ravien_seal` | 불꽃 인장 | `ravien` |
| `ravien_penalty` | 위약 조항 | `ravien` |
| `ravien_bigloan` | 대출 한도 | `ravien` |
| `ravien_auction` | 경매 | `ravien` |
| `ravien_goldscale` | 황금 저울질 | `ravien` |
| `ravien_settlement` | 최종 결산 | `ravien` |
| `harin_shield` | 경계 방패 | `harin` |
| `harin_bash` | 방패 강타 | `harin` |
| `harin_counter` | 반격 태세 | `harin` |
| `harin_taunt` | 도발 | `harin` |
| `harin_frostwall` | 서리 장벽 | `harin` |
| `harin_retaliate` | 보복 | `harin` |
| `harin_hold` | 버티기 | `harin` |
| `harin_iceguard` | 빙결 수호 | `harin` |
| `harin_bulwark` | 불굴 | `harin` |
| `harin_glacier` | 빙하 압박 | `harin` |
| `harin_oath` | 수호 맹세 | `harin` |
| `harin_avalanche` | 눈사태 | `harin` |
| `rien_note` | 첫 음 | `rien` |
| `rien_chord` | 화음 | `rien` |
| `rien_rest` | 쉼표 | `rien` |
| `rien_tempo` | 템포 업 | `rien` |
| `rien_crescendo` | 크레셴도 | `rien` |
| `rien_duet` | 이중주 | `rien` |
| `rien_feedback` | 피드백 | `rien` |
| `rien_harmony` | 하모니 | `rien` |
| `rien_solo` | 독주 | `rien` |
| `rien_echochamber` | 메아리 방 | `rien` |
| `rien_amplify` | 앰프 | `rien` |
| `rien_finale` | 피날레 | `rien` |
| `aila_bell` | 맑은 종 | `aila` |
| `aila_frostlight` | 서리빛 | `aila` |
| `aila_veil` | 장막 | `aila` |
| `aila_prayer` | 기도문 | `aila` |
| `aila_cleanse` | 정화 | `aila` |
| `aila_mend` | 봉합 | `aila` |
| `aila_snowfall` | 눈송이 | `aila` |
| `aila_sanctuary` | 성역 | `aila` |
| `aila_lullaby` | 자장가 | `aila` |
| `aila_rebirth` | 재생의 기록 | `aila` |
| `aila_overflow` | 넘치는 온기 | `aila` |
| `aila_dawnhymn` | 새벽 성가 | `aila` |
| `sion_cut` | 책갈피 베기 | `sion` |
| `sion_erase` | 지우개 | `sion` |
| `sion_bind` | 봉인 | `sion` |
| `sion_void` | 공허 파편 | `sion` |
| `sion_forget` | 망각 | `sion` |
| `sion_index` | 색인 | `sion` |
| `sion_pageblade` | 페이지 칼날 | `sion` |
| `sion_silence` | 침묵의 장 | `sion` |
| `sion_requiem` | 레퀴엠 | `sion` |
| `sion_oblivion` | 망각의 서 | `sion` |
| `sion_burnbook` | 금서 소각 | `sion` |
| `sion_lastpage` | 마지막 페이지 | `sion` |
| `kaya_bolt` | 화염 볼트 | `kaya` |
| `kaya_clue` | 단서 포착 | `kaya` |
| `kaya_deduce` | 추리 사격 | `kaya` |
| `kaya_cover` | 연막 | `kaya` |
| `kaya_tracking` | 추적 | `kaya` |
| `kaya_crossfire` | 교차 사격 | `kaya` |
| `kaya_interrogate` | 심문 | `kaya` |
| `kaya_ignite` | 점화 볼트 | `kaya` |
| `kaya_verdict` | 판결 | `kaya` |
| `kaya_casefile` | 사건 파일 | `kaya` |
| `kaya_flare` | 조명탄 | `kaya` |
| `kaya_closecase` | 사건 종결 | `kaya` |
| `lumi_jab` | 섬광 찌르기 | `lumi` |
| `lumi_dash` | 질주 | `lumi` |
| `lumi_guard` | 회피 기동 | `lumi` |
| `lumi_spark` | 방전 | `lumi` |
| `lumi_overdrive` | 오버드라이브 | `lumi` |
| `lumi_chain` | 연쇄 번개 | `lumi` |
| `lumi_adrenaline` | 아드레날린 | `lumi` |
| `lumi_thunderstep` | 천둥 걸음 | `lumi` |
| `lumi_limitbreak` | 한계 돌파 | `lumi` |
| `lumi_lightning` | 낙뢰 | `lumi` |
| `lumi_secondwind` | 재충전 | `lumi` |
| `lumi_storm` | 폭풍의 핵 | `lumi` |
| `com_slash` | 기본 공격 | `common` |
| `com_guard` | 기본 방어 | `common` |
| `com_sort` | 정리 | `common` |
| `com_patch` | 응급 수리 | `common` |
| `com_capacitor` | 시간 축전기 | `common` |
| `com_focus` | 집중 | `common` |
| `com_shockwave` | 충격파 | `common` |
| `com_barrier` | 공용 차폐 | `common` |
| `com_recall` | 회상 | `common` |
| `com_flash` | 섬광탄 | `common` |
| `com_pin` | 고정핀 | `common` |
| `com_rally` | 사기 진작 | `common` |
| `com_salvage` | 재활용 | `common` |
| `com_overwatch` | 경계 사격 | `common` |
| `com_bandage` | 붕대 | `common` |
| `com_tactics` | 전술 노트 | `common` |
| `com_anchor` | 시간 닻 | `common` |
| `com_breaker` | 균열 쐐기 | `common` |
| `smudge` | 얼룩 | `curse` |
| `penalty_note` | 위약금 | `curse` |
| `wc_sera` | 조준경 고정 | `weapon` |
| `wc_yuri` | 등불 빛 | `weapon` |
| `wc_ravien` | 저울질 | `weapon` |
| `wc_sion` | 책장 넘기기 | `weapon` |
| `wc_kaya` | 추적 화살 | `weapon` |
| `wc_lumi` | 섬전 | `weapon` |
| `wc_star_bow` | 별빛 화살 | `weapon` |
| `wc_frost_blade` | 결정 베기 | `weapon` |
| `wc_clock_core` | 태엽 감기 | `weapon` |
| `wc_silver_hymnal` | 성가 | `weapon` |
| `wc_hunter_rifle` | 조준 사격 | `weapon` |
| `wc_mech_bow` | 연발 화살 | `weapon` |
| `wc_guard_saber` | 세이버 일격 | `weapon` |
| `wc_iron_tonfa` | 톤파 연타 | `weapon` |
| `wc_pocket_engine` | 엔진 점화 | `weapon` |
| `wc_ledger_lock` | 잠금 | `weapon` |
| `wc_choir_bell` | 종소리 | `weapon` |
| `wc_glass_rod` | 굴절막 | `weapon` |
| `wc_basic_ranged` | 권총 사격 | `weapon` |
| `wc_basic_melee` | 단검 찌르기 | `weapon` |
| `wc_basic_device` | 공구 방벽 | `weapon` |
| `wc_basic_ritual` | 작은 기도 | `weapon` |
