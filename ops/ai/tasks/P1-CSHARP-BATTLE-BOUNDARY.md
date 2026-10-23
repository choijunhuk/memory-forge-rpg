# P1-CSHARP-BATTLE-BOUNDARY — executable opening slice

- Status: source analysis / implementation handoff, 2026-10-15; no C# implementation claimed.
- Analysis base: `9be537f4c5ef59e890322cb2d1de914fb49bfed6`; rebase anchors if the leader integrates newer changes.
- Owner of this analysis: Codex child; write scope is this file only. Product implementation belongs to the leader's next assignment.
- Contract: `AGENTS.md:29-39`, `docs/AI_WORKFLOW.md:98-126`; GPT-6.1 Sol only, core work Max, no recursive AI calls, original numbers/content preserved.

## Decision and completion boundary

Implement `Battle.Create` and the **real atomic `begin` command from a fresh CREATED state** first. This executes C# rules and RNG, rather than hashing supplied snapshots.
Cover all authored opening effects, not a chosen three-character demo. Explicitly reject `play`/`ult`/`end` and unsupported opening effects while those ports are absent.
Do not implement an incomplete public `StartTurn(false)`: `endTurn` immediately needs enemy actions, damage/death/phase hooks and reservation/card resolution (`engine/rules-core.js:559-661`, `:829-928`).
This is **opening parity only**. Full Battle/Run replay remains NOT_IMPLEMENTED; §5.16's second-turn/bulwark criterion also remains pending.

## Requirements and gameplay role

- Original §5.1-3/5.9/5.13-16: preserve battle setup, first-turn protection, shuffled draw, starting passives, intentions, private/public state, atomic commands.
- Original §6.1/6.3/6.6 and §7.8: use existing card IDs/DSL and relic/part semantics without changing definitions or numbers.
- Original §7.1/7.4 and §8.9/8.14: retain the later Run bridge and stable card lineage; no Run/reward/save implementation in this slice.
- Original §16.2 (`docs/source_v0.8.1/16_chapter.md:21-29`) and §20.9 (`20_chapter.md:74-87`): netstandard2.1/C#8, no packages, integer/BigInteger math, ordered deterministic execution.

## Smallest implementation ownership

| Owner lane | Files / responsibility |
|---|---|
| State/data | New `core/MemoryForge.Core/BattleDefinitions.cs`, `BattleState.cs`: concrete definitions, setup/state DTOs, deep copy and explicit JSON graph projection |
| Rules | New `core/MemoryForge.Core/Battle.cs`: create, resonance application, atomic command shell and opening lifecycle/helpers |
| Evidence | New `tools/gen_battle_opening_vectors.mjs`, `engine/tests/battle_opening_vectors.test.mjs`, `engine/tests/vectors/battle_opening_vectors.json` |
| C# acceptance | New `core/MemoryForge.CoreChecks/BattleOpeningChecks.cs`, `BattleDataLoader.cs`; leader alone wires `Program.cs:116-118` |

No Unity/HTML/server edits, no existing engine/data/vector changes, no project/asmdef changes needed: SDK-style Core already includes new C# files (`MemoryForge.Core.csproj:1-12`).
Keep fixture generation/checking separate from implementation ownership. Other agents' files and uncommitted changes are preserved.

## Concrete model boundary

- `BattleData`: `BattleRules`, ordered source arrays plus ID lookup for cards/characters/weapons/parts/relics/enemies; `indexData` is `engine/rules-core.js:54-63`.
- `BattleSetup`: ordered party/deck/enemy IDs, relic IDs, HP/maxHP, nullable seed, difficulty, nextApBonus and copied runFlags (`:249-266`). Do not derive account growth again here: Run supplies derived party values (`:1548-1550`).
- `BattleActorSetup`: charId/res/weaponId/refine/gauge/power4/hp/gearBps/skillLv/ultLv. Existing `GrowthEntry` lacks several fields and is not a replacement.
- `BattleCardSpec`: id/wielder/plus/mods(c,e)/growth/assigned/from; `CardState` adds uid/owner/temp/grown/used/costMod/token/free (`:233-243`).
- `BattleActorState`: preserve all 19 keys at `:273-275`, including passive **id/name/params/text**, full cardMods/hooks FX with `type`, battleStart/ultExtra/ultReplace and capacity.
- `BattleEnemyState`: all 18 creation keys at `:327-329`, including null intent/next/passive, status six-tuple, original appearance order. Future phaseIndex/pattern are absent until created by phase.
- `BattleState`: exact 59 root keys at `:254-266`. Explicit field groups follow; diagnostic log/ev exist even though state hashing excludes them.

| State group | JS keys to retain |
|---|---|
| Identity/health | schema,v,turn,phase,rng,seq,hp,maxHp,block,counter,diff,result |
| AP/status/turn | ap,apNext,apDrainNext,silenceUntil,breakApTurn,pst,buffs,drawPenalty,played,lastOwner,ownersThisTurn |
| Reservation/contract | debts,debtCap,restraint,bonus,sealedUntil,slotsBase,slotsTemp,res,resSeq,contracts |
| Growth/echo/flags | growthCount,growthCap,echo,echoMax,echoActive,echoStart,exhausted,exhaustStart,tempParts,freeNext,nextBoost,firstAtkDone,overloadFreeUsed |
| Collections/diagnostics | actors,enemies,cards,zones,relics,powers,turnFlags,battleFlags,runFlags,handMax,log,ev,killedMarked |

Use lists for execution order and explicit card creation order beside UID lookup; do not rely on Dictionary enumeration for `Object.values(cards)` semantics.
Concrete `PassiveDef`, `ResonanceFx`, `EffectDef`, `EnemyMoveDef`, `WeaponPassiveDef` data DTOs retain their source fields; use tagged switches, no reflection/dynamic/JS interpreter or generic execution framework.
The host loader uses existing CoreChecks `System.Text.Json` (`Program.cs:7-20`, `:123-146`); Core consumes typed definitions and does not acquire a JSON dependency.
`BattleState.ToJsonGraph()` explicitly emits existing `CanonicalState` dictionaries/lists/integer primitives (`CanonicalState.cs:15-18,38-75`). Do not pass CLR DTOs directly or fill absent optional keys with null.
Preserve JS fallback semantics (`||` versus `??`): seed 0 is valid; zero power4/vit/refine/skillLv use their specified fallbacks (`engine/rules-core.js:255,271-275`).
`Battle.create` ignores deck.key. Run subsequently assigns keys by creation order (`:1662-1670`); keep an optional internal card key slot but omit it until assigned, never turn a combat UID into a memory key.
Data contains fractional constants outside opening state (upgradeMult 1.3 at `data/game_data.json:30`, progression ratios at `:7727`); do not parse all content through integer-only CanonicalState or round those values to ints.

## Executable surface and command semantics

Expose concrete `Battle.Create(BattleData, BattleSetup)`, `Battle.Command(BattleRequest)`, `State` snapshot and `PublicView()`; `BattleRequest` has rid/v/kind/args, response has v/phase/ev.
Use `RuleException.Code` from `Rng.cs:9-13`; explicit temporary unsupported command/effect errors must be reported separately from JS rejection parity.
Port `command` ordering exactly (`engine/rules-core.js:453-475`): receipt lookup/conflict → stale version → cloned candidate with empty ev → execute → v+1 → invariants → replace → receipt.
Receipts are outside battle state and are not cloned into the candidate. Store copied responses, so mutating a returned response cannot corrupt a later retry.
Receipt identity is JS `JSON.stringify({v,kind,args:args||{}})`, **not** state canonical JSON: preserve argument property order and log/ev keys; exclude rid. Normalize through the trusted JSON adapter that also parses the request, not a caller-supplied digest.
Test receipt equality with property reordering and extra args; if first implementation only supports empty begin args, publish that restriction and reject other shapes explicitly rather than claiming full command-envelope parity.
Keep begin's phase guard (`:481-484`); a repeated identical rid returns the previous result before stale/phase checks, a new begin after success fails WRONG_PHASE without changing state or RNG.

## Opening flow and exact helper dependencies

1. Create all state, actors and recursively apply all 11 authored resonance tags (`:298-319`); apply relic setup overrides, card/enemy creation and invariants (`:250-331`).
2. Shuffle with existing `Rng.Below` using the JS descending Fisher–Yates order (`:49`, `Rng.cs:67-85`); clone RNG via `new Rng(state)` and serialize its four uint words.
3. Apply bronze_cog, pocket_brazier and each actor's weapon/start effects in existing order (`:481-491`). Preserve block event source strings/order.
4. Opening DSL is exactly **four** authored shapes: nia tune/amp/random (`data/game_data.json:400-406`), harin blk10 (`:786-791`), aila heal10 (`:1048-1053`), kaya st/mark2/rand (`:1310-1317`). Other effect execution must fail explicitly.
5. Reuse shared-style direct helpers for `weaponPassive`/strongest `partyWeapon`, party vitality, raw block, pseudo context, random target/status, heal/overflow and opening block (`engine/rules-core.js:348-365,826,1064-1127,1233-1255`).
6. Harin's opening block also adds his passive counter; pseudo context has no card/ultimate, so skill/ult multiplier stays level 1 even at resonance 6 (`:1071,1233-1237`). Aila's heal applies party weapon healing and passive overflow conversion.
7. eternal_seed samples creation-ordered eligible growth cards and uses real `applyGrowth` including symbiotic_moss (`:492-495,894-906`); it does not consume the capped reservation growth budget or grow evolved/saturated cards.
8. First turn follows `:499-549`: compute AP before increment, preserve initial block/counter, reset turn fields, apply infinite_circuit HP cost/envelope/early defeat, draw, then memory_anvil/nia tuning, intents, PLAYER/turn event.
9. Port draw/tuning unchanged (`:1432-1465`): curse exclusion, no duplicate amp, hand overflow to discard, recycle/shuffle when needed. Strongest startDraw weapon is once per party; startBlock applies per actor.
10. Fresh create has no due reservations, powers or pending copies; retain their state containers, but do not pretend an arbitrary restored state can run this slice. Unsupported restoration/later-turn paths must fail, never skip work.
11. Early circuit defeat uses `loseHp/checkEnd/finish` at `:677-699`, including once-per-run envelope and a single end event. Invariants `:1481-1499` and private/public view `:1503-1511` are part of this slice.

Use `BigInteger` numerator/denominator and floor once for opening block/heal, integer JS round for nonnegative averages/scaled enemy HP. Preserve numbers and operation order; `Rng`, `Stats`, `Gacha` stay unchanged.
Do not embed `ReservationBook` as a second reservation state: its CardUid shape/private sequence differ from JS uid/resSeq and it lacks delayed metadata (`ReservationBook.cs:10-29,64-69`; engine `:629-635`). Defer its narrow adapter decision until reservation commands are ported.

## Acceptance and next handoff

- JS exporter stores source hashes, full setup, CREATED state/hash, request, response/events, final state/hash; C# independently creates and executes begin before comparing every field/hash/event. Reject stale fixtures and duplicate/missing case IDs.
- Parameterized coverage: all 10 characters × resonance 0..6, all 22 weapons × refine 1..5, all 32 relics singly, then interacting starts and fixed seeds including 0/uint-max. Include duplicate card types, weapon/common ownership, lineage, difficulty and derived vitality/gear.
- Independent asserts: Harin res4 + bronze_cog + ledger_lock R5 gives block26/counter2 (`engine/tests/review_v071.test.mjs:14`); eternal_seed+moss gives growth1/block3 (`:24`). Keep those real expected values.
- Edge vectors: mixed actor vitality/gear, full-HP Aila overflow plus heal weapon, pocket_brazier then Kaya RNG target, quick_hands/startDraw/twin_pendulum overflow, double tuning with only one eligible noncurse, saturated/evolved seed candidates, circuit fatal/envelope-already-used/envelope-unused, AP cap/bonus/hourglass/crown.
- Atomicity asserts include rejected unsupported effects after RNG consumption: complete state, RNG, v, ev/log and receipts unchanged; same rid+body returns copied response, changed body conflicts, stale v rejects, failed rid can retry after correction.
- PublicView asserts exclude RNG/draw order and retain public intentions/hand/reservations. Snapshot projection omits only genuinely absent optional keys and canonical log/ev exclusions.
- Run fresh opening JS/C# checks, existing 202 primitives + canonical/snapshot/fixture checks, `npm test`, proposal/Python/docs/handoff suites. Preserve `BattleRunCommandReplay: NOT_IMPLEMENTED`; opening counts get their own label.
- Leader then obtains Opus independent diff/source review, fixes/rechecks, integrates, and continues to play/reservation/effect execution → end/enemy lifecycle → Run/memory → complete 700+300 replay. Do not mark §5.16/§16.2/§20.9 complete until their full conditions pass.

Analysis verification: original §5/6/7/8/16.2/20.9 and actual JS/C# sources read; read-only JS inspected the 59/19/15/18 root/actor/card/enemy fields and reproduced block26/counter2. Before this file was written, `npm run verify:manifest` found the existing 26 CHANGED input files (423 entries), no missing/index/hash-pair errors; manifests were not rewritten.
Not run here: product implementation/tests, Claude or other AI calls, Editor operations, complete C# command replay. This analysis adds only this task file.

## Oracle implementation correction

실제 authored fresh opening의 최대 뽑기는 기본5+quick_hands2+가장 강한 startDraw무기2=9이며 twin_pendulum 손패 상한9를 넘지 않는다. 여러 startDraw무기는 합산하지 않는다. 위 제안 중 opening overflow는 도달 불가능한 기대였으므로 실제 최대9/강한무기1회만 검증한다. 나중 카드 draw 효과의 overflow 검증과 혼동하지 않으며 가짜 초기 상태나 데이터를 만들어 opening 범위를 넓히지 않는다.
