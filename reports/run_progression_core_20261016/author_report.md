# P1-R1A-CSHARP-PROGRESSION / FINISH-STATE-LINK — author report (Claude Code worker)

- Worker: official Claude Code, Opus 5.5. Effort/session metadata is not visible to me; Root holds the CLI record. Subscription only.
- **Execution: NOT_RUN.** This session has no Bash/MCP. I did not build, run CoreChecks/JS/Unity, or compute any hash, and I claim no compile success and no PASS.
- Edited files: `Run.cs`, `RunState.cs`, `Run.Progression.cs`, `Run.Rewards.cs`, `Battle.RunLink.cs`, and this report. No Battle/MemoryV2/definition/host/Scene files were touched.
- The FINISH task's two links are **already in this worktree's files** (written in the first session before the runner limit). In this call I checked them and changed nothing. If Root's preserved copy of `Run.cs`/`RunState.cs` is still R0, apply exactly the diff in §2.

## 1. Public Run API added (R0 API unchanged)

```csharp
public void Enter(string nodeId);                                              // rules-core.js enter 1637-1651
public BattleResponse BattleCommand(string kind, BattleArgument? args = null); // battleCmd 1674-1680
public Dictionary<string, object?>? BattlePublicView();                        // battle?.publicView(), null outside battle
public void Evolve(string key, string branchId);                               // 1774-1783
public void TakeCard(string id); public void SkipCard();                       // 1752-1767
public void TakeRelic(string id); public void TakePart();                      // 1768-1773
public void ApplyFoundPart(string partId, string key);                         // 1846-1854, no phase check (Q4)
public void Proceed();                                                         // 1784-1794
public void Archive(string? choice);                                           // 1795-1801; non-"return" incl. null continues (Q5)
```

- `Archive` takes `string?` so that Q5's null input can be expressed. Callers that pass a `string` still compile.
- Internal helpers implement these source methods in full:
  - flow: startBattle, encounterFor, afterBattle, makeShop, captureMemory/memorySnapshot
  - rewards: pendingEvolutions, rarityRoll, cardChoices, relicChoices, partChoices
  - the event pick on enter
- The following do not exist, as stubs or otherwise: Rest, ForgeOffer, ForgeApply, ForgeRemove, Buy, EventChoose, Assign, MemoryChanges, PreviewMemory, CommitMemory. There is no public constructor, restore, raw-state access or Battle accessor.

## 2. Field, copy and bridge decisions

### `Run.cs`
- `public sealed partial class Run` with three fields: `readonly RunData data`, `readonly RunState state` (the name is kept because the RunCreateChecks identity probe reflects on `state`) and `Battle? battle`.
- The constructor is private, `Run(RunData, RunState)`. `CreateCore` returns `new Run(data, s)`. Every other R0 body is unchanged.
- RunData is not copied, matching the JS `this.data`. Callers must not mutate it after Create.

### `RunState.cs`
- Two new fields: `string? BattleType` and `MemorySnapshot? MemoryAfter`.
- `Copy()` adds `MemoryAfter?.Copy()`. The string field is already copied by MemberwiseClone.
- `ToJsonGraph` emits `battleType` and then `memoryAfter`, after `playBefore`, and only once each is set. States created by R0 get no new keys and no new nulls.

### Battle bridge (`Battle.RunLink.cs`, internal)
- `Live`, `Version` and `Finished` (which is `IsOver()`).
- `BindRunKeys` puts the deck keys on `CardOrder[0..deckCount)` before begin. It only works on a battle that is still CREATED at V0.
  - The eye_of_rift smudge token is created after the deck, so it gets no key.
  - The key survives `Command` candidates and `RunState.Copy` through `BattleCardState` MemberwiseClone.
- `RunState.Battle` is an alias of `battle.Live`. It is refreshed after every successful command, matching `this.s.battle = this.battle.s`.
- `Run.State` deep-copies, so no mutable Battle or battle state escapes.

### Pending and result
These stay the existing `object?` graphs, with JS literal key order:

| Graph | Shape |
|---|---|
| rest | `{type,uses}` |
| forge | `{type,uses,offers:null}` |
| shop | `{type,cards[{id,price}],parts,relics,removeCost}` |
| event | `{type,id}` |
| reward | `{type,coins,cards,relic,part,evolutions[{key,options}]}` |
| archive | `{type}` |
| save | `{type,defeat}` |
| result | `{outcome,floor}` |

### Ordering and atomicity
- Commands mutate in place, in source order. Every source RuleError check runs before any mutation, so a legal rejection leaves state unchanged.
- The source's partial-mutation paths are kept on purpose; this is not a transactional Run. Those paths are:
  - startBattle failures after the seed draw, the encounter RNG and the pos/floor change
  - an error during begin, after the `nextApBonus` reset
  - a makeShop or event-pick `RNG_BOUND` after the phase is set
  - capture errors
- Overflow handling:
  - A public command converts an OverflowException to `RUN_NUMBER_OUT_OF_RANGE`.
  - An overflow inside `Battle.Create` becomes `BATTLE_NUMBER_OUT_OF_RANGE`.
- Source corrections kept:
  - The battle copy gets `nextApBonus` 0, and the Run consumes the bonus once, so there is no refund.
  - The entry log is written first; a battle that begin already ended then goes through the existing finalizer once.

## 3. Open static concerns, for Root to check when compiling and replaying

1. **Compilation is unverified** under C# 8, netstandard2.1, `Nullable` and `TreatWarningsAsErrors`.
   - The code relies on flow analysis after `TryGetValue` and pattern checks.
   - It assumes expression variables leak from `if` conditions and that the same names are not reused in one block.
   - It uses throw expressions inside `?:`.
2. **`gauge`** is read from the Run party entry (`p.gauge || 0`), the same way JS `Battle.create` reads it. `power4/hp/gearBps/skillLv/ultLv` are the values R0 derived.
   - If setup gauge should be refused, that is a source and policy decision, not something to change here.
3. **Typed domain, with no parity claimed.** These values never occur in traces:
   - **Truthy non-integers** in JS `|| n` positions throw `RUN_NUMBER_OUT_OF_RANGE`. The positions are res, refine, gauge, difficulty, nextApBonus, coinsBonus, removals and growth.
   - **Non-string ids** throw `UNSUPPORTED_STATE_VALUE`. This covers weaponId, mods.c/e, wielder, assigned, from and the reward part.
   - **A non-integer deck growth** is replaced by the sentinel -1. `Battle.Create` then rejects it with `GROWTH_INVARIANT`, at the point where the source rejects it.
   - **A non-string fixed boss** is turned into a key with JS `String()`.
     - An unknown key gives `UNKNOWN_ENEMY` from `Battle.Create`.
     - A key that coerces to a real enemy id gives `UNSUPPORTED_STATE_VALUE`.
     - Number formatting is an approximation.
   - **A non-integer `refusals`** gives no virtue bonus.
   - **A non-object `mods`** is treated as `{}`.
   - **An empty part pool** removes the `reward.part` key, because JS stores undefined there.
4. **Unreachable inconsistency guards** use the R0 `MemoryBoundaryException("TypeError")` convention. They cover:
   - an unknown card, part or relic definition
   - a missing evolved deck card
   - non-list evolutions
   - an unknown found part in legacy

   No JS native-error parity is claimed.
5. **Insertion order.** The code relies on .NET Dictionary insertion order for pending, runFlags and deck card fields. The only removal is the unreachable `reward.part` case.

## 4. Root next steps (all NOT_RUN here)

- **Build and existing suites:** build with warnings as errors, then run all existing suites: R0, Battle, Memory and trace hashes.
- **Independent replay.** The same consumer covers both of these:
  - the five-case oracle full states, returns and queries
  - the legacy/v2 trace prefixes, stopping before absent methods
  - Report the prefix counts separately; a fully completed C# Run is still 0.
- **Unity:** PlayMode.
- **Targeted cases worth pinning:**
  - 1102 begin death: log order, battle cleared once, no stats or RNG
  - the eye_of_rift keyless token
  - growth field created even when 0
  - per-type coin RNG and the consumed boss rarity roll
  - the fairness `need` slot
  - the chrono_crown two-slot case
  - the common slot and the empty-pool `continue`
  - the virtue_of_refusal cap
  - `ApplyFoundPart` called during BATTLE
  - archive heal
  - v2 `useMemory:false` `memoryAfter`
  - `BattleCommand` after the battle ends gives `NO_BATTLE`; a second begin gives `WRONG_PHASE`
