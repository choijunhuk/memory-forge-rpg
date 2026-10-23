# P1-CSHARP-NEXT-TURN-BOUNDARY

- Status: **source analysis / implementation handoff**, 2026-10-15. No product implementation or C# execution is claimed here.
- Analysis workspace: `/Users/choi/.codex/worktrees/csharp-battle-opening/Memory_forge`, branch `codex/csharp-battle-opening`, HEAD `2ffc29ecdbdeee5027258ad6f73302b98820fc35` plus the uncommitted opening implementation. Rebase the anchors after its review fixes/integration.
- This analysis owns only this task file in `/Users/choi/Documents/Memory_forge`. The leader owns implementation assignments, final verification, official Opus review and integration. Do not start the oracle assignment without a separate ownership message.
- Contract: `AGENTS.md` project contract and `docs/AI_WORKFLOW.md:98-126`; GPT-6.1 Sol only, core work Max, subscription only, no recursive AI calls. This child did not call Claude/other agents, Unity or a build.
- Source identity: `engine/rules-core.js` SHA-256 `b1ba35f433197072200825ffa85cac49cf3d4812c4bf02032793fef8491d63eb`; `data/game_data.json` SHA-256 `6a09e6dd2d81055738c84951ea5b5ae50c25fd8c9732c8443e81ec1498625688`.

## Decision, role and completion boundary

After the opening slice passes its receipt/provenance fixes and independent review, implement the **real atomic `end` command, repeatedly, from `Battle.Create → begin` while every player turn is skipped**. This is the next meaningful command slice: enemy actions, round end and the next player turn actually execute in C#.

The slice is closed under fresh creation plus successful `begin`/`end` commands with the unchanged live 20-enemy data. It must not impose a turn-2 limit, choose only harmless enemies, disable opening relics/resonances, ignore inserted curses or stop before the second pattern cycle. `play` and `ult` remain explicit unsupported commands. Restoration/Run/late-enemy overlay acceptance remains separate.

This advances §5.16's **first-turn protection → ordinary second-turn reset** criterion. It does **not** complete its bulwark criterion, all of §16.2, or the 1,000 full Battle/Run command replays in §20.9. Keep those requirements pending until actual producing commands and their dependencies execute.

Requirements: §5.2-3 (composition/lifecycle), §5.7-9 (patterns/status/damage/survival), §5.13-16 (zones/end/atomicity/public view/turn boundary), §6.3 (existing effects, no changed numbers), §10.2 (all live enemies), §16.2-3 and §20.9 (integer C#8/netstandard2.1, deterministic command replay). Gameplay role: the player can pass repeatedly and receive correct enemy intentions, damage, curses, summons, victory/defeat and next-turn state.

## Reachability proof and excluded producers

Fresh Create has no reservations, debts, powers, buffs or pending copies. Begin produces block/counter, burn/mark, growth/tuning and initial HP/AP/echo changes. The 12 live enemy move tags can produce attacks, strength, enemy block/reflect/vulnerability, party weak, inserted smudges, summoned seeds and healing. Counter/burn can kill; marked kills can draw through the notebook. Repeated end can recycle those cards and incur curse/circuit damage.

None of those producers creates a reservation, debt, power, player buff, temporary reservation slot, pending copy, player card-resolution effect or ultimate. Smudges **discard** at skipped-turn cleanup; their exhaust keyword runs only when played. A stored `exhaustStart` resonance is not an exhaust event. Loading a power card in the deck does not install its power.

| Hook/effect | Reachable in this slice | Required treatment |
|---|---|---|
| Harin R4+ opening block / passive counter | Yes, first enemy phase | Retain until that phase ends; counter every actual hit, then clear on the next player turn. |
| pocket_brazier burn / Kaya R4+ mark | Yes | Raw burn damage, decay and marked-kill bookkeeping are mandatory. |
| detective_notebook | Yes, raw counter/burn kills a marked enemy | Draw 1 and increment `runFlags.coinsBonus` by 10 even during RESOLVING and even before a simultaneous defeat is finalized. |
| Kaya deduction / Kaya R2 follow-up burn | No, gated by `s.phase === 'PLAYER'` in killEnemy | Preserve the phase gate; do not award AP/draw/follow-up burn for an enemy-phase kill. `killedMarked` still increments. |
| Enemy seedHeal / lowest-ratio heal | Yes | Burn precedes seed healing; count alive seeds; stable target ties; report requested heal `n` even if clamped. |
| Enemy reflect | State/intent lifecycle only | Set/clear the field exactly. Counter and burn raw damage do not trigger card-only reflect. No reflected card damage is claimed. |
| memory_envelope / infinite_circuit | Yes | Once-per-run survival on each loss source; later lethal hits/costs stop at the source's existing checkEnd boundary. |
| Start block/heal/growth/tuning relics, quick_hands, w_startDraw | Yes, begin only | Preserve their products; do not repeat them on later turns. Strongest party weapon selection stays unchanged. |
| chrono_crown / cracked_hourglass / tuning_fork | Yes | Crown each turn; hourglass AP penalty only at turn 0; reset echo each turn from echoStart only when active. |
| bulwark / rebirth / counterDouble / oblivion / other powers | No, require play/ult | Reject nonempty unsupported state, rather than running a no-op placeholder. Full power acceptance remains pending. |
| break/stun/recovery, silver_needle | Not produced by counter/burn (raw damage does not reduce brk) | Do not claim break acceptance. If the corresponding lifecycle branches are not ported, explicitly reject such states. |
| exhaust, w_exhaustGuard, void_shard, oblivion_ink | No live end-only producer | Do not accidentally exhaust discarded smudges. An inert relic may remain equipped. Real exhaust handling belongs to its producing command slice. |
| enemy “steal” / eatName | No live move named steal; eatName is a proposal-overlay move | No invented alias/action. Reject unsupported overlay execution; eatName would require real exhaust and all its hooks first. |
| apDrain/sealSlot/delayRes/silence/phase | Not in live 20-enemy patterns | Preserve JS implementation/content; do not activate proposals or claim these ported. Reject unported definitions/states explicitly. |

## Complete live action inventory

The exact live union is **12** tags: `atk`, `atkdebuff`, `blockAll`, `buff`, `charge`, `debuff`, `def`, `expose`, `heal`, `insert`, `reflect`, `summon`. `expose.then` recurses into `atk`. Reference: `engine/rules-core.js:604-661`; source data is `content/world.mjs` / `data/game_data.json`, not the late-enemy overlay.

| ID | Fixed pattern, in order | Required end commands for two full cycles |
|---|---|---:|
| e_moth | atk12 → atk7×2 → str+1 | 6 |
| e_cog | def6 → atk18 → atk14 | 6 |
| e_smudge | insert smudge1 → atk13 → atk10 | 6 |
| e_hound | atk17 → atk9×2 → def5 | 6 |
| e_lantern | party weak1 → atk18 → heal lowest6 | 6 |
| e_page | atk7×2 → atk12 | 4 |
| e_bellwarden | charge → atk48 → def12/str+2 → atk12×2 | 8 |
| e_mirrorclerk | reflect3 → atk24 → atk13×2 | 6 |
| e_gardener | summon seed2 → atk25 → atk14×2; seedHeal5 | 6 |
| e_seed | def2 | 2 |
| e_scribe | atk17 → atk11/party weak1 → def8 | 6 |
| e_shelf | def10 → atk25 → blockAll6 | 6 |
| e_inkswarm | atk7×2 → insert smudge1 | 4 |
| e_keeper | str+2 → atk13×2 → atk20 | 6 |
| e_censor | party weak2 → atk26 → charge → atk49 | 8 |
| e_twinink | atk12×3 → reflect4 → def16 | 6 |
| b_bell | atk12/party weak1 → def25 → expose2 then atk52 → atk11×3 | 8 |
| b_notary | reflect4 → atk22 → atk10×3 → charge → atk50 | 10 |
| b_garden | summon seed2 → atk22 → atk14/party weak1 → atk12×2; seedHeal7 | 8 |
| b_archivist | blockAll20 → atk28 → insert smudge2 → charge → atk55 | 10 |

Read-only JS probe executed all these loops from real Create/begin with **three reference actors, HP/maxHp 243, no relics, source base kits, seed 0**. Every duel stayed PLAYER through two complete cycles; final HP ranged from 39 (b_notary) to 243 (e_seed). No HP inflation or enemy-data mutation is needed for this acceptance layer.

## Exact lifecycle to implement

Port `endTurn` in source order (`engine/rules-core.js:559-601`), keeping every command atomic through the existing candidate shell:

1. PLAYER guard, then RESOLVING. Keep restraint/bonus expiry semantics; unsupported nonempty producers must already have an explicit guard.
2. Iterate a snapshot of the old hand in order. Each smudge calls `loseHp(scaleEnemy(2),'smudge')`; **finish the whole smudge loop**, discard the whole hand and clear it, then checkEnd. Do not add an early break per smudge: that changes logs/envelope ordering.
3. Decrement party weak once and expire silence at its existing boundary. Live actions only create weak; never silently drop a new party status type.
4. Iterate a snapshot of the enemy list in appearance order. Skip dead entries. Clear **that acting enemy's** block and reflect immediately before its action, not all enemies' block at phase start. Increment pi before executing its move, including on a fatal attack.
5. An atk/atkdebuff move processes one hit at a time: JS round(base × difficulty/100), add strength, enemy-weak reduction where supported, block, hit event, HP loss, then counter. Preserve the current hit's counter/kill/reward even if that hit reduced party HP to zero. Check terminal state after that hit; stop later hits/effects/enemies. atkdebuff has no partyDebuff event; plain debuff does.
6. `summon` reuses enemy creation and the shared seq, admits each new seed only while alive count <5 and sets its intentions. New summons do not act in this snapshot, but are present for later actions, round-end effects and the next intents. Dead list entries do not consume alive capacity. Do not invent a summon event.
7. `heal` selects the lowest current HP/maxHp ratio, retaining appearance order on ties. Use integer/BigInteger comparison under the project's no-floating-point contract and verify against actual JS including large supported values; do not truncate integer division to zero. If a near-tie exposes a JS rounding discrepancy, resolve/report that item before claiming exact parity, rather than silently changing targets or narrowing accepted setups.
8. `def`/`buff`/`reflect`/`blockAll` make their exact mutations without extra events. `charge`, `expose`, `insert` and standalone debuff emit only their existing events. `expose` mutates vulnerability before its recursive move. Insert creates full default token instances in discard, with creation order and shared seq preserved.
9. Round end: every alive burned enemy takes scaled raw damage (block bypass), then its burn counter decrements, including on the newly dead body. Then every alive seed-healer heals from the current alive seed count. Then alive enemies decay vul/weak/mark and recover expired break state if supported. Finally checkEnd; only a nonterminal battle starts the next player turn. Do not call finish prematurely inside raw kill and reorder round-end hooks.

Raw damage/kill dependencies (`:1129-1153`) are part of this slice, not optional helpers: party power is a rounded average, scaleAtk floors once, only burn bypasses block, raw damage never applies card modifiers/vulnerability/reflect/break, dead or nonpositive damage returns without an event. Kill emits `kill {uid,id}`, increments killedMarked for marked victims and executes notebook draw/coins. Keep enemy-array/card-array order when that draw reshuffles.

Generalize **the existing** StartFirstTurn to a shared internal StartTurn(first) (`:499-549`), rather than maintaining two near-identical turn engines:

- Compute next AP before increment. Preserve first-only hourglass subtraction, consume apNext once, cap 0..9, retain crown each turn. Never pretend due debts were processed if debt execution is still unsupported.
- Increment turn and enter RESOLVING. On subsequent turns clear ordinary block/counter at this point, **after** the preceding enemy phase; on first turn preserve opening values.
- Reuse the current reset order for buffs, owners/contracts/freeNext/nextBoost, echo, temp slots/turn flags/break AP, actor gaugeTurn and card costMod. Persistent temp parts, growth, used/free/token/card metadata and battle/run flags are not reset wholesale. Set seraR6Turn=0 at its existing later point.
- Apply infinite_circuit and checkEnd before drawing. A fatal turn-2 cost ends at turn 2 with an empty hand; do not draw, reset intentions or emit turn afterward.
- Explicitly reject unsupported due reservations/powers/copies instead of skipping their source positions. In this slice the reachability proof keeps them absent; their guards protect later API expansion.
- Reuse Draw/Shuffle with the existing hand, including notebook draws already made during RESOLVING. Later normal draw is 5 minus penalty; quick_hands/startDraw/tuning are first-only. Recompute intentions from each current pi, enter PLAYER and emit turn.
- Reuse CheckEnd/finish, RNG state updates, invariant checking and PublicView. Preserve all root/card/enemy fields and the capped 400-entry log.

## Smallest complete implementation ownership

| File | Necessary change |
|---|---|
| core/MemoryForge.Core/Battle.cs | Add atomic end dispatch and EndTurn; share StartTurn, SetIntents, AddEnemy; implement 12 moves, EnemyHit, scaleEnemy/partyPower/scaleAtk, raw damage and marked kill. Reuse MakeCard, Draw, RNG, LoseHp, CheckEnd, Emit and command receipts. No parallel Battle implementation or JS bridge. |
| core/MemoryForge.Core/BattleDefinitions.cs | Only fields consumed by this slice, e.g. weakBps if porting the exact weak-damage branch; existing EnemyMoveDef already represents every live move including then. Keep unsupported tags/shapes explicit. |
| core/MemoryForge.Core/BattleState.cs | Extend BattleEvent for `hit {d,blocked}`, `dmg {n,blocked,src}`, `kill {uid,id}`, `insert {card,n}`, and existing new event tags. Omit absent keys; retain zero values. Extend Copy/ToJsonGraph with each added field. No fabricated phase/pattern/pendingCopies keys. |
| core/MemoryForge.CoreChecks/BattleDataLoader.cs | Read only newly consumed rules; reject unsupported enemy phase metadata instead of dropping it, and retain strict move-field loading. Reuse existing setup/request/graph parsing. |
| tools/gen_battle_turn_vectors.mjs (new) | JS oracle of **actual Create and command sequences**, source-checked and atomically exported. Reuse opening scenario setups, canonicalJSON/stateHash and existing authored data. |
| engine/tests/battle_turn_vectors.test.mjs (new), engine/tests/vectors/battle_turn_vectors.json.gz (new) | Pin coverage/order/independent values/provenance/tampering/atomic export. Keep opening vectors intact unless an upstream source correction requires a truthful regeneration. |
| core/MemoryForge.CoreChecks/BattleTurnChecks.cs (new), Program.cs minimal wiring | Independently Create and execute requests; compare each step. Share the existing loader and follow existing assertion style. Keep opening checks labeled separately. |

No Unity, HTML, server, source design, content data, dependency or policy changes are needed. Do not embed ReservationBook: Battle already owns res/resSeq/slots, and ReservationBook has a different entry shape/private sequence and no delayed metadata. End-only neither needs a second reservation store nor justifies changing its passing primitive tests. A later reservation adapter must preserve one authority.

The current typed events cannot yet represent several mandatory enemy events. The current enemy construction is inline in Create and must be reused for summons. The current StartFirstTurn rejects all later turns. These are mandatory implementation work, not grounds to remove summons/counter/relic cases.

## Oracle and acceptance contract

Use a new scope/schema label such as `mf-battle-turns-v1`: “real Create/begin/repeated end; player play/ult and full Battle/Run replay NOT_IMPLEMENTED.” Include source hashes for data, engine, memory dependency, canonical serializer, opening setup generator and new generator. If the opening receipt fix changes engine source, regenerate **all affected source-pinned corpora** and recheck them; never just change a metadata hash or skip stale corpora.

Each case contains setup and CREATED snapshot, then an ordered request list beginning with begin. Each committed step records response, complete state including log/ev, PublicView and exact RNG. C# gets only setup/requests as execution inputs; expected snapshots are comparison data. No restored expected state, monkey-patched engine/data, injected powers or JS subprocess results may stand in for C# execution.

For every CREATED/committed/rejected checkpoint compare full graphs and pin hashes for **state, RNG array, event array, log array and PublicView**. Use the established state canonical contract (UTF-16 ordinal key sorting, integers, recursively omit log/ev). Hash event/log arrays separately with that serializer so diagnostics are actually covered; hashing a response object with an ev key alone would omit its events. Preserve array order and all event keys. Also compare the complete response and v/phase, including idempotent retries and terminal errors.

Coverage is required programmatically, with duplicate/missing case IDs rejected:

1. **All 20 live enemies:** the normal-HP 243 duel matrix above executes at least two whole pattern cycles, including last pattern entries. Add actual source encounter arrays from data.encounters to test multiple enemies, blockAll ordering and appearance order. Record their exact initial party/deck/HP/seed; no inflated endurance or changed enemy data.
2. **All opening inputs remain admitted:** extend each of the current 267 opening setups through repeated end where nonterminal. At minimum cover all 10×7 character/resonance states, all 32 relics singly and all 22×5 weapon/refine states, plus the existing opening interactions. Opening-terminal cases receive a truthful terminal end rejection instead. Explicitly assert membership against current data rather than accepting a reduced hardcoded count.
3. **Second-turn resets:** actual Harin R4 + bronze_cog + ledger_lock R5 starts block26/counter2; against e_shelf, end1 reaches turn2 block0/counter0, enemy def10 remains, pi1/intent atk25. Growth+moss starting block clears normally; growth/temp part metadata persists. Crown/hourglass opening AP4 becomes AP5 next turn, carried setup AP is not repeated; quick_hands/startDraw/memory_anvil/Nia tuning do not fire again.
4. **Summons and periodic healing:** gardener/b_garden first end adds exactly two seeds with pi0/block0; they first act next enemy phase. At full gardener HP, first round still emits enemyHeal n10 (b_garden n14). Repeated cycles reach alive cap5. Test an initial cap-bound group and an order where blockAll affects new summons. Burn damage occurs before seed healing, and dead seeds do not heal anything.
5. **Healing ties:** actual party `[harin R4, sera, nia]`, their base kits, HP243, enemies `[e_lantern,e_page,e_page]`, seed0. After end1 both pages have HP8/12. On end3 the first page heals to12; the second stays8. Event n6 is requested amount although actual healing is4. Use stable rational comparisons, including unequal ratios and supported large-number cases.
6. **Curse/zone/RNG lifecycle:** e_smudge, e_inkswarm and b_archivist insert source token smudges into discard with exact new uid/card order. Continue until they really reshuffle, draw and cause scaled HP loss. Mix eye_of_rift and existing smudges/penalty_note; penalty_note has no skipped-hand penalty. Check full smudge cleanup after fatal HP, empty/small decks, repeated card recycle and unchanged exhaust count.
7. **Raw kill and notebook:** party `[kaya R4, sera, nia]`, source base kits, HP243, seed2, enemies `[e_seed,e_seed]`, relics `[pocket_brazier,detective_notebook]`. Burn and mark choose the same first seed. End2 kills it while marked; turn3 remains PLAYER, hand6, killedMarked1, coinsBonus10, with no Kaya deduction/AP/follow-up burn. The read-only JS probe reproduced this from real commands. Also test the same kill as the last enemy, and initial coinsBonus accumulation.
8. **Counter/reflect/status:** Harin counter triggers once per actual hit including fully blocked hits, only on first-turn opening counter unless a future producer is ported. Raw counter/burn ignore enemy reflect and card-only multipliers; no break event. Preserve reflect fields until that enemy's next action, party weak decrement before new debuff, mark/vul decay, persistent strength across cycles and boss expose-before-hit order.
9. **Terminal ordering, all from setup → commands:** unprotected lethal first hit stops a multihit; unused envelope permits the next hit, then actual death stops remaining hits; used-envelope flag never rescues again; all smudge losses occur before the cleanup check; infinite_circuit can kill at the next turn before draw/intents/turn event; raw counter kill stops remaining hits; burn victory stops the next turn; end occurs exactly once and a new end after terminal rejects atomically.
10. **Same-hit defeat and reward:** a targeted boundary setup may use explicit accepted setup difficulty without mutating source data: `[harin R4,kaya R4,sera]`, base kits, hp1/maxHp243, e_page, difficulty `{hp:1,dmg:200}`, detective_notebook, seed0. JS gives enemy HP1; hit d14/blocked10 → hpLoss4 → counter dmg2 → kill → **DEFEAT**, coinsBonus10 and hand1 from the notebook. Preserve this same-hit ordering and defeat priority; it is a setup-domain edge, not a balance/Run claim.
11. **Long-lived harmless battle:** repeat end against e_seed with a noncurse deck beyond 400 logged events. Pin exact truncation, RNG from reshuffles, v/turn progression, card conservation and stable public intentions. There is no arbitrary public API cap after turn2.
12. **Atomicity/receipts/rejections:** end before begin, stale version, end after terminal, duplicate rid after later commands, changed-body conflict (including args ordering/log/ev), copied returned response, unsupported play/ult, unsupported candidate state/move and checked numeric overflow after partial work. Verify state including diagnostics/RNG/v and receipts are unchanged on failure. Only successful new requests increment v. Test unsupported faults as negative tests, never erase them from the positive live-case denominator.

The targeted difficulty edge above is deliberately separate from the unchanged HP243 content matrix. No positive production-data case is skipped on a familiar RuleError. Stop on unexpected errors; generator coverage/assert failures preserve the previous fixture.

## Explicit unsupported boundary and blockers

- The public Battle API remains Create + command(begin/end) + detached State/PublicView. Do not add an arbitrary restore/startTurn API as a shortcut. Nonempty reservations, debts, powers, pending copies or other unported producer state must reject with a documented unsupported-state code before commit. Missing optional keys in typed state must not silently discard such inputs at a future loader boundary.
- A live action or opening combination reaching an unported hook blocks this slice until that hook is implemented and independently checked. Counter, burn, mark/notebook, insert, summons, seedHeal, reshuffle and envelope are already proven reachable; disabling their producers would shrink the game and is prohibited.
- Unknown move tags/passive IDs/shapes reject explicitly. The raw data loader currently does not model enemy phase metadata: it must reject such metadata rather than turning a two-phase boss into an ordinary kill. Do not claim proposal-overlay compatibility because an unknown field was dropped.
- Full §5.16 bulwark proof needs a producing command. Authored `harin_bulwark` is cost2, now-only, power keep10, exhaust (`data/game_data.json:3072-3088`). Existing JS R071-03 injects keep7 and is an isolated legacy regression, not real-command C# proof. To close this criterion later, first port applicable play/precheck/cost/gauge/power/afterResolve/exhaust plus part/weapon/relic hooks, then run actual play→end. Likewise rebirth/exhaust/eatName/reservations/debts/copies require their real producers and dependencies, not bypasses.
- Baseline opening integration and its current receipt/provenance correction are prerequisites. Implementation must start from the reviewed current opening code, not overwrite it with the analysis snapshot. Independent Opus review belongs to the leader after fresh tests; no integration with unresolved core defects.

## Verification and handoff

Planned implementation checks: new JS generator --check and turn oracle tests; C# actual-command turn checks; retained opening/202 primitives/canonical/snapshot/fixture checks; npm test, npm run test:proposals, Python tests, npm run test:docs and npm run test:handoff. Preserve whole Battle/Run replay as NOT_IMPLEMENTED until it truly runs. Editor/Android/IL2CPP/device/server acceptance stays separate; this task does not authorize or claim it.

Analysis evidence: read the actual current JS/C# lifecycle, typed DTOs/loader/oracle, §5.3/5.7-9/5.13-16, §10.2, §16.2-3 and §20.9; ran read-only Node probes for all 20 two-cycle duels and the exact summon/heal/notebook/smudge/envelope/circuit/same-hit cases above. Before adding this file, `npm run verify:manifest` checked 423 entries and found **27 existing CHANGED input files**, with no missing/index/hash-pair error; no manifest was regenerated.

Not run here: product edits, C# build/tests, full repository suites, Claude or other model calls, Unity, full command replay. This document is ready for the leader's review; the child is available for explicit oracle ownership afterward.
