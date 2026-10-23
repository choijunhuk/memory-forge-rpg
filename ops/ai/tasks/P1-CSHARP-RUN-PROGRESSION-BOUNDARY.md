# P1-CSHARP-RUN-PROGRESSION-BOUNDARY — R1 실제 탐사 진행 경계

> 2026-10-16 Root 소스 갱신: 아래는 작성 시점의 정적 분석 이력이다. Q1 AP 재환급·Q2 무료 두 번째 제거·Q3 완료 이벤트 반복과 시작 즉시 패배의 탐사 미종료는 실제 원문/공개 명령으로 입증해 JS에서 수정했다. 이 결함을 그대로 C#에 이식하지 않는다. 정확한 현재 엔진은 dae99192bfa137762c1c863efda8207b3851e3a6e7ecf2ff029bed3585a33ab4이며 `enter`의 원래 로그 뒤 이미 끝난 전투만 기존 finalizer로 넘긴다. 검증·독립 리뷰·양쪽 저장 수용은 reports/run_node_contracts_20261016을 따른다. Q4/Q5와 복원 정책은 임의로 변경하지 않았다. 현재 R0 데이터는 RunData.Memory.Battle에 전체 BattleData를 이미 보유하므로 새 JSON/데이터 계층 없이 재사용한다. 전체 C# Run 진행 명령은 여전히0이다.

- 상태: **소스 분석·인계만**. 구현·빌드·시험·해시·MCP 실행 없음. 아래 C# 이름은 **신규 제안**이며 존재하거나 승인된 API가 아니다.
- 작업: `P1-CSHARP-RUN-PROGRESSION-CLAUDE-ANALYSIS`, 공식 Claude Code Opus 5.5, 요청 effort xhigh. 실제 세션·모델·effort 증거는 부모 Codex가 보관한 CLI 메타데이터를 따른다. 편집 범위는 이 파일 1개다.
- 기준: worktree `/Users/choi/.codex/worktrees/csharp-battle-opening/Memory_forge`, 브랜치 `codex/csharp-battle-opening`, HEAD `2ffc29e` + 미커밋 R0/M0/Battle 작업 트리. 줄 번호는 2026-10-15 이 작업 트리 기준이다. `rules-core.js` Run 구간(1518-2081)은 이전 문서 `P1-CSHARP-RUN-MEMORY-BOUNDARY.md`(3d33c4c)와 줄이 같다.

## 0. 요구사항·게임 역할·수용 조건 (R1 첫 구현 단위 전에 기록)

| 항목 | 내용 |
|---|---|
| 원천 | §7.1-7.7(`07_chapter.md:5-134`), §8.1-8.7 legacy와 §8.8-8.14 v2(`08_chapter.md:5-120`), §16.2(`16_chapter.md:21-29`), §17.2/§17.5(`17_chapter.md:23-44,86-88`), §20.9 P1-1(`20_chapter.md:76-87`), SAVE-081-11/12와 TRACE-081-25(`decisions_v0.8.1.md:18-19,38`). 실행 원천은 `engine/rules-core.js:1518-2081`과 `tools/gen_traces.mjs`. |
| 게임 역할 | 서버가 판정하는 탐사(`16_chapter.md:18`): 지도 진입 → 전투 → 보상 → 노드 서비스 → 구역 전환 → 종료 → 기억 저장을 JS와 같은 수치·RNG 순서로 C#에서 판정한다. 클라이언트는 같은 코어로 미리보기만 계산한다. |
| R1 수용 | ① 실제 `Run.Create` 뒤 공개 명령만으로 진행한다. 기대 상태 주입, afterBattle 스텁, fixture에서 가져온 보상은 쓰지 않는다. ② 기존 legacy 1000 trace의 모든 시도에서 outcome/errorCode/stateHash가 같고, DONE과 finalStateHash까지 같다. ③ 별도 v2 1000 corpus도 같다. ④ 보완 oracle(§10)의 반환값·전체 상태·log·RNG가 같고, 거절하면 Run/store가 바뀌지 않는다. ⑤ Unity PlayMode는 Editor 실행 증거로만 센다. |
| 비범위 | 수치·확률·가격·용량·영구 저장 의미 변경, 서버 DB/인증/잠금, 실계정 이관, 신뢰 snapshot 복원 정책(§5). |

## 1. 현재 사실 — 재사용할 수락된 API와 빈칸

| 대상 | 현재 사실 | R1 영향 |
|---|---|---|
| R0 `Run` | public은 `Create/State/NodeById/Available/BaseKit/DefaultWeapon`이다. `NextCardKey/DeckMemorySnapshot/GenMap/Round`는 private static이다(`Run.cs:17-200`). 인스턴스는 `RunState`만 들고 **RunData·Battle 참조가 없다**(`:13-14`). 클래스는 `sealed`이고 partial이 아니다. | `Run(RunData, RunState)` 생성자, `Battle? battle` 필드, `partial` 추가가 필요하다. R0 통합 후 편집한다. |
| R0 `RunState` | `Pending/Result`는 `object?` 그래프, `Battle`은 `BattleState?`이다. `battleType/memoryAfter/savedMemory` 필드가 없다(`RunState.cs:37-88`). | 필드와 typed pending을 추가한다. |
| Battle | public은 `Create/Command/EffectiveCard/AllowedModes/CostOf/NeedsTarget/PublicView/State`(`Battle.cs:25-63,117-201,716-724`)와 `Precheck`(`Battle.PlayerActions.cs:53`)다. `Command`는 후보 실행과 rid 영수증을 쓰며 JS `command`(`rules-core.js:453-476`)와 같은 원자·멱등 계약이다. `IsOver()`는 private(`Battle.PlayerActions.cs:26`)이고, `BattleCardState.Key`와 `BattleState.CardOrder`는 internal(`BattleState.cs:93,253`)이다. `BattleCardSpec`에는 key가 없다(`BattleDefinitions.cs:329-336`). | Run 연결용 internal 표면은 **새 partial 파일**에 둔다. 기존 Battle 파일은 편집하지 않는다. |
| 라이브 전투 범위 | 구역 1/2 조우(`game_data.json:6831-6924`)가 쓰는 적 행동 12종(atk·atkdebuff·def·buff·debuff·charge·summon·heal·insert·reflect·expose·blockAll)을 C# `EnemyMove`가 모두 지원한다(`Battle.cs:523-581`). 적 패시브는 seedHeal뿐이다(`game_data.json:6471,6765`). 후반 행동은 후보 overlay에만 있다(CANDIDATE-081-16). | 정적 조사로는 라이브 Run을 막는 Battle 기능이 없다. 실제 증명은 R1 재생이다. |
| M0 `MemoryV2` | `Inspect/Baseline/ListChanges/Cost/ApplyChanges/Apply(+options)/Recover/Migrate/CapacityOf`(`MemoryV2.cs:119-360`). store는 `MemoryMigrationResult.ToJsonGraph()`의 version 0 형태뿐이다. **version/history/receipts를 다루는 store 타입이 없다**(`:105-115`). | R1d에서 store 래퍼를 새로 만든다. MemoryV2.cs는 편집하지 않는다. |
| Rng | C# `Pick`은 빈 목록에 `EMPTY_POOL`을 던진다(`Rng.cs:81-85`). JS `pick`은 `below(0)`이 되어 `RNG_BOUND`를 던진다(`rules-core.js:43-50`). | Run 안에서는 R0 GenMap처럼 `list[Below(list.Count)]`를 써서 오류 코드를 맞춘다. |
| Host | `RunDataLoader.Setup(JsonElement)`와 `BattleDataLoader.Request(JsonElement)`가 public이다(`RunDataLoader.cs:48`, `BattleDataLoader.cs:250-254`). `TraceHashChecks`는 snapshot만 검사하고 `BattleRunCommandReplay: NOT_IMPLEMENTED`를 출력한다(`TraceHashChecks.cs:28-52`). | 재생은 기존 로더를 재사용한다. 무결성·strict JSON은 같은 실행의 TraceHashChecks 통과를 전제로 한다. |
| Corpus | legacy는 700 short + 300 full이다(`gen_traces.mjs:67-74`). 소스 5개의 SHA-256이 고정되어 있다(`:20-23`). 273,748명령은 `CURRENT.md:41`의 이력 수치이며 이번에 재확인하지 않았다. **v2 corpus 파일은 없다**(vectors에는 `traces.jsonl.gz`만 있다). | v2는 기존 generator를 수정하지 않고 `--memory-policy v2`로 별도 출력한다. |

## 2. 실제 Run 명령 전체 지도

Run에는 rid/v 셸이 없고 메서드를 직접 호출한다. trace는 `{scope:'run',kind,args[]}` 16종만 허용한다(`gen_traces.mjs:40-41,86-87`). "사전 검사"는 **변형 전에** 던지는 코드를 순서대로 적은 것이다. "corpus"는 기존 legacy 정책(`gen_traces.mjs:188-239`)이 그 명령을 실제로 호출하는지를 뜻한다.

| 군 | 메서드(JS 줄) | 사전 검사(순서) | 성공 시 변형·RNG 순서와 데이터 필드 | corpus |
|---|---|---|---|---|
| 진입 | `enter(nodeId)` 1637-1650 | `available()`에 없으면 NODE_UNREACHABLE | pos/floor 설정 후 종류별 분기. battle/elite/boss는 startBattle. rest/forge는 uses=forge_ember면 2 아니면 1(forge는 `offers:null`). shop은 makeShop. event는 `pick(events)`. 끝으로 log `${NODE_NAME} 진입`(1516) | 도달 가능 id만 |
| 전투 시작 | 내부 `startBattle` 1660-1672, `encounterFor` 1653-1659 | 없음. Battle.create 오류(PARTY_SIZE 등)는 JS에서 pos/floor/RNG가 **바뀐 뒤** 발생 | deck 사본 {id,plus,mods,growth(없으면 0),wielder,assigned,from,key} → `Rng.next`로 전투 seed → 조우(고정 보스면 RNG 없음, 아니면 normal/elite/boss pool에서 pick) → Battle.create(runFlags 얕은 복사, nextApBonus) → Run `runFlags.nextApBonus=0` → cards 생성 순서대로 key 부여 → `do('begin')` → phase BATTLE, battleType 설정 | 예 |
| 전투 명령 | `battleCmd(kind,args)` 1673-1679 | NO_BATTLE. Battle 거절은 Battle이 원자 처리 | `battle.do`(rid=`r${v}-${kind}`, 478) → s.battle 교체 → 끝났으면 afterBattle. Battle 응답을 반환 | play/ult/end만 |
| 전투 종료 | 내부 `afterBattle` 1680-1699 | 없음 | hp, `runFlags={...run,...battle}`, key가 있는 인스턴스마다 `deck.growth=c.growth`(값이 0이어도 필드 생성). **패배**: result{defeat,floor}, pending save defeat:true, captureMemory, battle null. 통계와 coinsBonus는 갱신하지 않는다. **승리**: battles+1, kills+=죽은 적(소환 포함), elite면 elites+1 → 동전(boss 100, elite 50+below(16), battle 22+below(11)) + lucky_coin 10 + 전투 coinsBonus → Run coinsBonus=0 → cardChoices → pendingEvolutions(RNG 없음) → elite/boss면 relicChoices → battle이면 below(4)==0일 때 partChoices(false,1)[0] | 예 |
| 보상 생성 | `rarityRoll` 1703-1708, `cardChoices` 1709-1732, `relicChoices` 1734-1742, `partChoices` 1743-1749 | 없음 | rarityRoll은 **boss도 below(100)을 소비**한다. cardChoices: n은 chrono_crown이면 2 아니면 3. 공정성 need는 제시 횟수 차가 2를 넘을 때 첫 칸에 적용하며 RNG를 쓰지 않는다. 공용 판정 below(3)은 마지막 칸만. 캐릭터 칸은 pick(party), 희귀도 pool이 비면 그 캐릭터 전체로 대체. 중복을 빼고 비면 continue(pick 없음). offered는 캐릭터 카드만 +1. relic/part는 splice로 중복 없이 뽑는다. 데이터: cards(owner·rar·pool!==false·start), relics(rarity), parts(slot!='temp'·rarity), 원본 순서 | 예 |
| 보상 행동 | `takeCard` 1751, `skipCard` 1758, `takeRelic` 1767, `takePart` 1772, `evolve` 1773 | BAD_REWARD(pending·목록 검사). evolve는 BAD_REWARD → BAD_EVOLUTION | takeCard: `{id,key,owner,assigned(공용이면 party[0] 아니면 null),gained:true}` 추가 후 cards=[]. skipCard: cards가 있을 때만 +15, virtue_of_refusal이면 refusals<4일 때 maxHp/hp+2. takeRelic: push 후 relic=null. takePart: partsFound에 push 후 part=null. evolve: from=id, id=branch, evolved=true, 목록에서 제거 | 예(빈 목록 skip·거절은 없음) |
| 진행 | `proceed` 1783-1793 | REWARD/REST/FORGE/SHOP/EVENT가 아니면 WRONG_PHASE | pending=null. 보스 노드면 남은 구역이 있을 때 ARCHIVE, 없으면 VICTORY·result{clear,floor 7}·save defeat:false·captureMemory. 그 외는 MAP | 예 |
| 구역 | `archive(choice)` 1794-1800 | ARCHIVE가 아니면 WRONG_PHASE | `'return'`: VICTORY·result{return}·save·capture. **그 외 모든 값**: hp=min(maxHp, hp+floor(maxHp·0.15)), zoneIdx+1, zone, pos null, floor 0, genMap(RNG), MAP | return/continue만 |
| 휴식 | `rest(choice,key?)` 1809-1822 | WRONG_PHASE(phase, uses≤0) → heal이면 CANNOT_HEAL(memory_anvil), upgrade면 BAD_UPGRADE(없음·plus·무기), 그 외 BAD_CHOICE | heal: floor(maxHp·(30+old_compass면 10)/100). upgrade: plus=true. 끝으로 uses-1 | 1회만 |
| 공방 | `forgeOffer(key)` 1824, `forgeApply(partId)` 1831, `forgeRemove(key)` 1839 | offer/remove는 WRONG_PHASE → BAD_CARD(없음·무기). apply는 BAD_PART뿐이며 **phase를 검사하지 않음** | offer: `offers={key,parts:partChoices(false,3)}`. 재호출하면 덮어쓰고 RNG를 다시 소비. offers를 반환. apply: mods `{...mods,[circuit면 'c' 아니면 'e']:id}`, offers null, uses-1. remove: splice, uses-1. 사건 변주로 연 forge는 `fromEvent:true` | 1회만 |
| 상점 | `makeShop` 1855-1863, `buy(kind,id?,key?)` 1864-1879 | WRONG_PHASE → remove는 COINS_LOW → BAD_CARD, 그 외는 NOT_FOR_SALE(목록 없음·sold) → COINS_LOW | makeShop: 3회(pick(party), pick(owner·pool·!start)), 가격 B45/A70/S110/기타60 → 부품 2개(rare 90, 일반 60) → 유물 shop 2개(rare 180, 일반 120) → removeCost=50+25×removals. buy: 차감, sold=true. cards면 push(공용은 party[0] 배정, gained), parts면 partsFound, relics면 relics. remove: 차감, splice, removals+1, removeCost=null | remove는 removeCost가 있을 때만 |
| 사건 | `eventChoose(idx,key?)` 1881-1915 | WRONG_PHASE → BAD_CHOICE → COINS_LOW(need) → WOULD_KILL(hp+hpk(fx.hp)<1) | fx 적용 순서 고정: coins(최소 0) → hp(최소 1) → heal(maxHp 상한) → maxHp(+hpk, hp 상한) → maxHpPct(floor, 최소 1) → nextApBonus 누적 → curse `{id,key,owner:'curse',gained}` → relic(common/elite 후보 첫째) → card(party 소유·rar·pool에서 pick, **assigned 필드 없음**) → part(common 후보 n개 중 첫째) → partRare 1 → **variation이면 FORGE fromEvent로 즉시 return** → growth(성장<3 후보 pick, +1) → cardCopy(keys[0] 또는 첫 비무기 카드 사본 + 새 key) → cardRemove(keys[k] → 첫 curse → 첫 비무기) → done/choice. hpk(v)=Math.round(v·hpRef/(81·n)) | 단일 key만 |
| 부품·배정 | `applyFoundPart(partId,key)` 1845, `assign(key,charId)` 1801 | apply: NO_PART → BAD_CARD → (v2) INVALID_PART. assign: BAD_ASSIGN → (v2, 저장된 key) REASSIGNMENT_REQUIRES_SEPARATE_FLOW. **둘 다 phase를 검사하지 않음** | apply: mods 병합, partsFound에서 첫 일치 제거. 둘 다 pending이 save면 captureMemory | apply만(REWARD) |
| 저장 | `memoryChanges` 1930-1974, `commitMemory` 1976-2016(legacy)/2055-2081(v2), `previewMemory` 2048-2054, `captureMemory/memorySnapshot/deckMemorySnapshot` 2018-2047 | legacy: WRONG_PHASE(pending≠save) → BAD_CHOICE(형식·cid·중복·범위) → DEFEAT_KEEP_ONE → OVER_CAPACITY{charId,cost,capacity}. v2 순서는 이전 문서 §4 | legacy는 base kit에 선택한 변경을 **변경 목록 순서**로 다시 적용한다. add의 keepBits는 `{plus:!!plus,mods,from}`이라 plus:false가 남는다. 공용 카드는 assigned를 붙인다. 이후 savedMemory, pending done, DONE, 메모리 반환 | legacy commit만 |
| 부재 | **포기(§7.6, `07_chapter.md:69`)·중간 저장·복원 명령은 Run 메서드에 없다.** constructor `new Run(data,state)`(1519)는 검증 없이 Battle을 빈 영수증으로 다시 만든다(247). | 없음 |

의존 관계: Battle은 enter(전투 노드)·battleCmd·afterBattle에만 필요하다. M0는 Create·captureMemory·v2 memoryChanges·previewMemory·commitMemoryV2·assign에 쓴다. R0 `NextCardKey`는 카드를 추가하는 5곳(1755·1876·1895·1897·1905)에서 쓰고, `GenMap`은 archive continue, `DeckMemorySnapshot`은 capture, `BaseKit`은 legacy memoryChanges/commit에서 쓴다. 상태를 바꾸지 않는 질의(명령 아님)는 `available/nodeById/memoryChanges/previewMemory`다.

## 3. Battle 연결의 정확한 계약 (R1a)

- **Actor**: `BattleActorSetup{CharId, WeaponId(그대로; null이면 actor.weapon=null인데 deck에는 기본 무기 카드가 있음), Res=res(없으면 0), Refine=refine(없으면 0, Battle이 1~5로 제한), Gauge=Fields["gauge"](없으면 0), Power4/Hp/GearBps/SkillLv/UltLv=R0 파생값}`(`rules-core.js:268-277`, `Battle.cs:38-51`).
- **Deck**: `BattleCardSpec{Id, Plus=truthy, Circuit=mods.c(없으면 null), Edge=mods.e(없으면 null), Growth(없으면 0), Wielder, Assigned, From}`를 s.deck 순서대로 만들고, 같은 순서의 key 목록을 준비한다(`rules-core.js:239-241,1662`).
- **seed/조우/난이도**: seed는 `(uint)` Run rng의 `Next()`이고, 조우는 그 뒤에 뽑는다. DifficultyHp/Damage는 difficulty.hp/dmg(없으면 0)이며, Battle의 `Or(…,100)`이 JS `||100`과 같다. RunFlags는 Run runFlags의 얕은 사본이고, NextApBonus=runFlags.nextApBonus(없으면 0)이다.
- **고정 보스**: R0의 opaque `bosses`가 배열이면 `[zoneIdx]`, 객체면 문자열 키 `"zoneIdx"` 멤버를 읽는다. 값이 truthy면 `[value]`(RNG 없음), 아니면 pool에서 pick한다.
- **key**: `CardOrder[i]`(i < deck 수)에 key를 부여한다. eye_of_rift의 smudge 토큰은 deck 뒤에 생성되므로 key가 없다(`rules-core.js:290`). begin은 `new BattleRequest("r0-begin", 0, "begin", null)`이다. JS `do('begin')`의 args `{}`와 C# null은 영수증 식별이 같다(`BattleState.cs:376`).
- **battleCmd**: rid/v는 Run이 현재 battle V로 만든다. trace replay는 기록된 rid/v가 그 값과 같은지만 확인한다(`gen_traces.mjs:79-83`). afterBattle의 성장 반영은 CardOrder 순서로 돈다(JS Object.values 삽입 순서와 같음).
- **nextApBonus 수명(정적 확정)**: rules-core.js에서 nextApBonus가 나오는 곳은 249·256·1532·1665·1666·1894뿐이고, Battle은 `runFlags.nextApBonus`를 쓰지 않는다. startBattle은 Run 쪽만 0으로 만들고, afterBattle 병합(1682)이 전투 사본에 남은 이전 값을 되돌린다. 그래서 ev_rift(`game_data.json:7082-7087`) 이후 **모든 후속 전투가 첫 턴 AP+2를 받고, 다음 ev_rift는 이를 누적한다.** legacy corpus 해시에 고정된 현행 동작이므로 C#은 그대로 이식한다(Q1). R1b 집중 사례로 두 전투 이상 실제로 고정한다.

## 4. 오류·원자성·재생 계약

- **JS**: Run 메서드는 제자리 변형이다. trace의 합법 거절(rest/buy/eventChoose 코드)을 포함해 §2의 RuleError 사전 검사는 모두 **변형 전에** 던지며, generator가 거절 시 무변형을 assert한다(`gen_traces.mjs:127`).
- **JS 부분 변형 경로**(정상 진행에서는 생기지 않음): enter 안의 startBattle 실패(legacy 0명/4명 이상 파티의 PARTY_SIZE, UNKNOWN_*), makeShop/fx.card 빈 pool의 RNG_BOUND(현재 데이터로는 도달 불가), capture 중 Memory 오류, overflow.
- **C# 권장**: 모든 Run 명령은 `state.Copy()` 후보에서 실행한다. 전투 명령이면 `battle.Fork()`도 후보로 쓴다. 성공했을 때만 교체하고, 교체 후 `state.Battle`을 live battle 상태에 다시 연결한다. 성공 경로와 사전 거절 경로는 JS와 같으므로 corpus parity에 영향이 없다. 부분 변형 경로는 C#에서 원자적으로 강화되며 parity 행으로 세지 않는다. OverflowException은 `RUN_NUMBER_OUT_OF_RANGE`로 바꾼다(R0 `Run.cs:17-21` 관례).
- **재생 해시**: 매 시도마다 `CanonicalState`로 `{run: State.ToJsonGraph(), memory: store 또는 null}`을 해시한다(log/ev 제외). v2 commit이 성공하면 반환된 state가 memory를 대체한다(`gen_traces.mjs:88`). DONE 뒤에는 generator의 재생성 확인(`:183-185`, seed+99, v2는 reload runId/version)을 C#에서도 R0 `Run.Create`로 실행한다.
- **멱등**: Run 명령에는 rid/영수증이 없다. Battle 영수증은 내부에만 있고 상태 밖이다. 서버 request_id/state_version(§17.2)은 어댑터 책임이며, Core에 Run 영수증을 새로 만들지 않는다.

## 5. 범위 분리

| 범위 | 원천에 있는 것 | R1 처리 | 상태 |
|---|---|---|---|
| legacy 1000 trace 재생 | `gen_traces.mjs:101-269`, `rules-core.js:1976-2016` | R1a~R1c | 실행 가능 |
| v2 store version/receipt/history/preview/commit | `rules-core.js:2048-2081`, M0 규칙 | R1d + 별도 v2 corpus + 집중 사례 | 실행 가능 |
| HTML 로컬 저장·체크포인트 | `ui.js` 호출자(이전 문서 §5) | Core commit 성공을 영속 저장 성공으로 보지 않는다 | UI/Host 책임, R1 밖 |
| 복원 | 검증 없는 `new Run(data,state)`, Battle 영수증 소실(1519, 247) | R2에서 명령 경계 snapshot → 복원 → 같은 suffix 재생으로 JS 의미를 검증 | 의미는 실행 가능. **신뢰 snapshot 형식·검증·변조 거절 정책은 PENDING** |
| 포기 | §7.6 "저장 없음, 서버 확인 대화". Run 메서드 없음 | 구현하지 않는다 | **PENDING(서버 정책)** |
| 서버 API/DB | §17.2 골격. §17.5의 v2 접합은 미정이고 OpenAPI/SQL은 legacy 기준 | Core는 순수 규칙만 | **PENDING P1-2** |
| 실계정 이관 | M0 `Migrate` 규칙만 | 이관을 실행하지 않는다 | **PENDING P1-2/운영** |

## 6. 보스 위치 `available()` 추적 결론

- pos가 보스 id가 되는 곳은 `enter(bossId)`뿐이다. 그 뒤 전이: BATTLE → afterBattle로 DEFEAT 또는 REWARD → proceed로 ARCHIVE 또는 VICTORY → archive('return')면 VICTORY, 그 외면 **pos=null**과 MAP → commit으로 DONE. MAP에 들어가는 경로는 Create(pos null, 1530-1531), 비보스 proceed(1792), archive continue(1799)뿐이며, 사건 변주의 위치도 사건 노드다.
- 따라서 실제 진행에서 `phase==='MAP' && pos===보스`는 **도달할 수 없다.** 보스 위치의 모든 phase에서 JS와 C# 모두 phase 가드로 `[]`를 반환한다(`rules-core.js:1630`, `Run.cs:197`). enter도 양쪽 모두 NODE_UNREACHABLE이다.
- 결론: **API 변경은 필요 없다.** R1a 집중 사례로 보스 위치의 각 phase에서 `available()==[]`와 enter 거절을 고정한다. JS `undefined`는 변조된 snapshot에서만 나오므로, R2 신뢰 복원 검증에서 MAP+보스 pos를 거절하는 항목으로 넘긴다(typed 경계, parity 아님).

## 7. 소스 질문 — 이식은 현행 유지, 규칙 변경은 승인 후(§20.9)

| # | 현행 JS | 원본 명세 | 도달성 |
|---|---|---|---|
| Q1 | nextApBonus가 이후 모든 전투에 반복·누적된다(§3) | §7.7 "다음 전투 첫 턴 +2" | ev_rift 첫 선택이면 legacy corpus에서 도달 |
| Q2 | 상점 제거 뒤 removeCost=null이다. `coins<null`은 false, `coins-=null`은 0이므로 **같은 상점에서 다시 제거하면 무료**이고 removals는 계속 늘어난다(1868-1870) | §7.3 "카드 제거 1회" | 공개 메서드로 도달. HTML은 버튼을 숨기고(`ui.js:753`) corpus는 쓰지 않는다. **서버 노출 전에 결정 필요** |
| Q3 | done 뒤에도 phase가 EVENT면 eventChoose 재선택을 허용한다(1883,1914) | 사건당 1회 선택 | 공개 메서드로 도달, corpus 미사용 |
| Q4 | applyFoundPart·assign·forgeApply에 phase 검사가 없다(BATTLE·DONE 포함) | 화면 흐름의 지정 시점 | 공개 메서드로 도달. corpus는 REWARD에서만 호출 |
| Q5 | archive는 `'return'` 외 모든 값(null 포함)을 continue로 처리한다 | 두 가지 선택 | 공개 메서드로 도달 |
| Q6 | 포기 명령이 없다 | §7.6 포기 행 | 해당 없음 |

C#은 Q1~Q5를 그대로 재현하고 oracle로 고정한다. 서버 어댑터가 Q2~Q5를 외부에 노출하기 전에는 결정이 필요하다. `sion_forget` base0 충돌(A1 UNRESOLVED)은 Battle 범위로 남긴다.

## 8. 미래 데이터·typed 경계 (현재 차단 아님)

- **미래 데이터**: 시작 카드가 아닌 pool 카드가 없는 캐릭터(makeShop pick의 RNG_BOUND, 부분 변형), fx.card 빈 pool, 빈 events, fx.relic의 elite 분기(현재 사건은 "common"만 씀), 알 수 없는 무기 타입(R0 follow-up), 라이브에 없는 적 패시브/후반 행동(Battle이 NOT_IMPLEMENTED로 원자 거절).
- **typed 경계**(JS-native 진단으로 따로 집계, parity 아님): `buy`에 목록이 아닌 kind(`'type'`, `'removeCost'`이면 JS TypeError), eventChoose idx 문자열/소수, 문자열이 아닌 bosses 원소, 문자열 coinsBonus(정상 진행으로는 생기지 않음), 0명/4명 이상 legacy 파티의 enter, int32를 넘는 동전/HP. C#은 `MemoryBoundaryException("TypeError",…)` 또는 기존 코드로 원자 거절한다.
- **수치 동치**(정상 경로, oracle로 고정):
  - hpk = R0 `Run.Round(v·hpRef, 81·n)`, 즉 floor((2N+D)/2D)이며 음수 절반은 +∞ 방향이다. 예: 2인 파티 hpRef=243에서 maxHp -5는 JS -7이다.
  - archive 회복 `Math.floor(maxHp*0.15)`는 floor(maxHp·15/100)과 같다. 0.15의 이진 오차가 정수 경계를 넘지 않으며, maxHp가 20의 배수인 경우로 고정한다.
  - rest와 maxHpPct는 정수 곱 뒤 floor다.

## 9. 실제 차단·의존

1. R0 검증·통합 커밋 전에는 `Run.cs`/`RunState.cs`를 편집하지 않는다(Codex 진행 중). 새 파일은 먼저 작성할 수 있다.
2. corpus와 oracle은 소스 SHA-256에 묶여 있다. 지금 `rules-core.js`·`memory_v2.cjs`·`traces.jsonl.gz`가 미커밋 수정 상태이므로, C# 재생은 같은 커밋의 엔진 바이트와 corpus로만 판정한다. 엔진이 바뀌면 corpus를 다시 만든 뒤 판정한다.
3. `Program.cs` 연결과 Unity 실행은 부모(Codex)만 한다.
4. 정적 조사로는 그 밖의 Battle·M0 기능 차단이 없다. 재생 중 Battle 불일치가 나오면 Run에서 보정하지 않고 Battle 담당 결함으로 돌린다.

## 10. 구현 단위 — 순서·API·쓰기 소유·증명

공통 규칙:
- Core는 JSON 패키지 없이 typed로 작성한다.
- 공개 반환값은 분리된 사본이다.
- 모든 Run 명령은 후보에서 실행한 뒤 교체한다.
- log 문자열은 JS와 같다(NODE_NAME 1516, `카드 획득: `, `유물 획득: `, `진화: `, `패배`). 상한은 100개다(1635).

### R1a — 지도↔전투↔보상↔구역 루프 (첫 단위)

닫힌 단위인 이유:
- enter는 명령 하나이므로 makeShop과 사건 pick을 포함한 모든 노드 분기를 함께 옮겨야 한다.
- BATTLE을 벗어나는 길은 afterBattle뿐이고, REWARD에서 MAP으로 가는 길은 proceed, ARCHIVE의 출구는 archive뿐이다.
- 종료 capture는 legacy에서는 no-op이고, v2에서는 M0와 R0 `DeckMemorySnapshot`로 바로 닫힌다.

```csharp
public sealed partial class Run {                 // R0 API 유지 + Run(RunData data, RunState state), Battle? battle 필드
  public void Enter(string nodeId);                                              // 1637-1650
  public BattleResponse BattleCommand(string kind, BattleArgument? args = null); // 1673-1679, rid/v는 현재 battle에서 생성
  public RunBattleQueries? Battle { get; } // JS run.battle의 질의만(State/PublicView/AllowedModes/CostOf/NeedsTarget/EffectiveCard/Precheck). Command 없음 → afterBattle 우회 불가
  public void TakeCard(string cardId); public void SkipCard(); public void TakeRelic(string id); public void TakePart();
  public void Evolve(string key, string branchId);
  public void ApplyFoundPart(string partId, string key);                         // phase 무검사 유지(Q4)
  public void Proceed(); public void Archive(string? choice);                    // 'return' 외 모두 continue(Q5)
}
// RunState 추가: string? BattleType, MemorySnapshot? MemoryAfter. ToJsonGraph에는 설정된 경우에만 출력한다.
//   Pending → RunPending?(reward/rest/forge/shop/event/archive/save/done; relic·part null, sold/done/choice/fromEvent 부재, removeCost null을 보존)
//   Result → RunResult?{Outcome, Floor}
// Battle.RunLink.cs (partial Battle, internal): AssignDeckKeys(IReadOnlyList<string>), bool Finished, BattleState Live, Battle Fork()(상태+영수증 사본)
```

**쓰기 소유**
- R1a 담당:
  - Core: `Run.cs`(partial·생성자·필드만, R0 통합 후), `RunState.cs`(R0 통합 후), 신규 `RunPending.cs`, `Run.Progression.cs`, `Battle.RunLink.cs`
  - Host: 신규 `RunProgressionChecks.cs`, `RunTraceReplay.cs`
  - JS: 신규 `tools/gen_run_progression_vectors.mjs`, `engine/tests/run_progression_vectors.test.mjs`, `engine/tests/vectors/run_progression_vectors.json.gz`
  - 생성물: `engine/tests/vectors/traces_v2.jsonl.gz`(수정하지 않은 `gen_traces.mjs --memory-policy v2 --output …`로 생성)
  - Unity: 신규 `Assets/Tests/PlayMode/RunProgressionPlayModeTests.cs`(작성만)
- 부모: `Program.cs`, 실행과 보고.
- 동결: `rules-core.js`, `memory_v2.cjs`, `game_data.json`, `gen_traces.mjs`, `traces.jsonl.gz`, 기존 Battle\*.cs/MemoryV2.cs, R0 검사·oracle, 씬·프리팹.

**증명**

- **JS oracle**: R0 생성기 구조(`gen_run_create_vectors.mjs:25-91`)를 재사용한다.
  - 기록: sources에 `gen_traces.mjs`를 포함한다. 사례마다 실제 setup과 실제 명령열을 저장한다. 단계마다 outcome, error{kind,name,code}, 반환값(json 또는 undefined), 전체 state JSON·stateHash·rngHash·logHash·shape를 남긴다.
  - 분기 도달: ⓐ정당한 생성 입력(relics·bosses·stageLevel·difficulty·zones·파티 성장값)과 ⓑ생성기 안의 결정적 seed/선택 탐색으로 찾는다. **저장하는 것은 최종 setup과 명령뿐이며 상태를 주입하지 않는다.** JS 시험은 같은 dispatch로 다시 실행해 바이트를 비교한다.
- **사례군**:
  - 진입: 노드별 pending 형태, forge_ember 2회. NODE_UNREACHABLE은 없는 id·같은 층·이른 보스·MAP이 아닌 각 phase. 보스 위치 각 phase의 available.
  - 전투 연결: seed → 조우 RNG 순서, 고정 보스 배열·객체·falsy, eye_of_rift 토큰 무key, stageLevel·어려움 전달, memory_envelope·detective_notebook runFlags 병합, 0 성장 필드 생성, NO_BATTLE, Run을 거친 Battle 거절의 무변형, begin 재호출 WRONG_PHASE.
  - afterBattle: 세 종류 동전 RNG, lucky_coin, coinsBonus 초기화, 패배 hp0·통계 미갱신, 소환 적 처치 수, 공정성 need, chrono_crown 2칸, 공용 칸, 빈 pool continue, 보유 유물 제외, 25% 부품.
  - 보상 행동: BAD_REWARD 각각과 BAD_EVOLUTION, 공용 카드 party[0], 빈 목록 skip, virtue 4회 상한, 진화를 건너뛴 뒤 재제시.
  - applyFoundPart: BATTLE·DONE에서 호출, NO_PART·BAD_CARD.
  - proceed/archive: short VICTORY floor 7, full ARCHIVE, continue 회복·zone2 지도, return, 비표준 값의 continue, WRONG_PHASE.
  - v2 종료 memoryAfter.
  - 원자성: 0명/4명 legacy 파티의 enter. JS 부분 변형을 기록하고 C#은 무변형이어야 한다. parity로 세지 않는다.
- **.NET**:
  - 기존 출력은 그대로 두고 `RunProgressionChecks: n/n`을 추가한다.
  - legacy와 v2 각각 `RunTracePrefixReplay: scenarios 1000/1000, verified attempts …, mismatches 0, stoppedAt{kind:count}`를 출력한다.
  - 첫 미구현 Run kind에서 시나리오를 멈추는 것은 **하네스**의 일이다. Core에는 가짜 메서드나 스텁을 두지 않는다.
  - battle 명령은 scope를 뺀 요청 JSON으로 기존 `BattleDataLoader.Request`를 재사용한다.
  - 전량 재생 전까지 `BattleRunCommandReplay: NOT_IMPLEMENTED`를 유지한다.
- **Unity**: 집중 사례 1개(Create → enter → 실제 전투 → afterBattle → 보상 → proceed)를 UPM Core로 PlayMode 실행한다. 실행은 부모가 하며 Editor 증거로만 센다.

### R1b — 노드 서비스

- **API**(`Run.Nodes.cs`): `Rest(string choice, string? key = null)`, `RunForgeOffer ForgeOffer(string key)`(분리 사본), `ForgeApply(string partId)`, `ForgeRemove(string key)`, `Buy(string kind, string? id = null, string? key = null)`, `EventChoose(int index, string? key = null)`, `EventChoose(int index, IReadOnlyList<string> keys)`
- **사례**:
  - rest: 30/40%, CANNOT_HEAL, BAD_UPGRADE 3종, BAD_CHOICE, 2회 뒤 WRONG_PHASE
  - forge: 재offer RNG, 슬롯 병합 순서, BAD_PART, curse 제거, 사건 변주
  - 상점: 생성 RNG 순서와 가격표, kind별 구매, NOT_FOR_SALE과 COINS_LOW 순서, 제거 비용 증가, Q2 재제거
  - 사건: **8종 21선택 전부**. need, WOULD_KILL 경계, hpk 음수 절반, maxHpPct, curse 형태, card/part/partRare/relic, growth, cardCopy(key 있음·없음·무기 key), cardRemove(key 0/1/2개, 무효 key fallback)
  - Q3 재선택, Q1 nextApBonus의 두 전투 이상 수명
- **증명**: legacy·v2 모든 시나리오가 commitMemory 직전까지 일치한다.

### R1c — legacy 저장으로 legacy 전량 닫기

- **API**(`Run.Memory.cs`): `RunMemoryChanges MemoryChanges()`(정책별 형태와 label 문자열 포함, ToJsonGraph로 JS와 비교), `RunLegacyMemory CommitMemory(IReadOnlyDictionary<string, IReadOnlyList<long>> selection)`, RunState에 `SavedMemory` 추가
- **사례**:
  - 선택 형식, 없는 cid, 중복, 범위, 비정수(로더에서 BAD_CHOICE로)
  - DEFEAT_KEEP_ONE, OVER_CAPACITY detail, WRONG_PHASE
  - 재구성: remove/add/evo/plus/part, plus:false와 mods 부재, 공용 assigned
  - 반환 사본
- **증명**: legacy 1000/1000이 DONE까지 모든 시도·finalStateHash·재생성 확인과 일치한다. 그때 legacy에 한해 `NOT_IMPLEMENTED` 해제를 보고한다.

### R1d — v2 저장·배정으로 v2 전량 닫기

- **API**: `RunMemoryPreview PreviewMemory(IReadOnlyList<string> selectedIds)`, `MemoryCommitResult CommitMemory(MemoryCommitRequest request, MemoryStore current)`, `Assign(string key, string charId)`. 신규 `MemoryStore.cs`는 순서를 보존하는 graph로 알 수 없는 metadata를 보존하고, version/history/receipts는 typed로 접근한다.
- **처리 순서**: 이전 문서 §4를 따른다.
  - before 비교는 `JSON.stringify`와 같은 속성 순서 유지 직렬화로 한다.
  - receipt는 own key로만 찾는다.
  - history의 selectedChangeIds는 요청 원순서다.
- **교차 정책 호출**: JS 결과 코드를 따른다. legacy Run에 v2 요청은 WRONG_PHASE 또는 BAD_CHOICE, v2 Run에 legacy 선택은 INVALID_SELECTION이다.
- **사례**: 이전 문서 §6의 Memory 회귀·요청·이관 행. 여기에 비어 있지 않은 기존 덱, useMemory:false, version>0의 실제 연속 탐사를 더한다(첫 Run commit 결과 store로 두 번째 Run.Create).
- **증명**: v2 1000/1000.

### R2 — 복원 (별도)

`Run.Restore(RunData, RunState)`는 JS constructor 의미(Battle은 빈 영수증)만 옮긴다. 명령 경계마다 snapshot → 복원 → 같은 suffix 재생을 돌려 상태·RNG·응답이 같은지 본다. 신뢰 형식·검증·변조 거절은 정책이 정해지기 전까지 PENDING이다.

## 11. 다음 코드 작업

**R1a-1 — 지금 착수 가능, R0 파일 무편집**
1. `tools/gen_run_progression_vectors.mjs`, `engine/tests/run_progression_vectors.test.mjs`, fixture로 §10 R1a 사례군의 실제 JS oracle을 만든다.
2. `core/MemoryForge.Core/Battle.RunLink.cs`(AssignDeckKeys/Finished/Live/Fork)와 `RunPending.cs`를 작성한다.

**R1a-2 — R0 통합 커밋 직후**
1. `Run.cs`에 partial과 RunData/Battle 필드를 넣고, `RunState.cs`에 필드를 추가한다.
2. `Run.Progression.cs`에 Enter → StartBattle → BattleCommand → AfterBattle → 보상 5종 → ApplyFoundPart → Proceed → Archive → CaptureMemory를 §2·§3 순서대로 옮긴다.
3. `RunProgressionChecks.cs`와 `RunTraceReplay.cs`(legacy+v2 prefix)를 작성한다.

이후 개발 → JS/.NET 시험 → Codex 독립 검토 → 수정 → 재검증 → 부모 통합(Program.cs·Unity 실행) 순서로 진행한다.

## Root 회수 주석 / 2026-10-15

공식 Opus xhigh CLI process0·Opus-only modelUsage·구독 계정 일치로 이 문서를 실제 회수했다. 이는 소스 분석이며 실행 증거는 아니다. 위 미커밋 설명은 분석 worktree A의 상태다. Root에서는 엔진/기억 모듈/기존 corpus가 이미776380f에 통합됐고 정확한 두 CI도 통과했다. Q1/Q2/Q3는 원문과 공개 명령의 재현을 먼저 확인한다. 입증된 엔진 오류 수정은 이미 승인된 범위이며 C#에 결함을 영구 고정하는 것으로 해석하지 않는다. 수치·정책을 바꾸지 않고 실제 오류를 수정하면 영향 corpus를 재생성·대조하고 상대 검토를 거친다. 아직 R1 구현·명령 재생 수용은0이다.
