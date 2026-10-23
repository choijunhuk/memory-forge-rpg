# C# late-action implementation report (P1-CSHARP-LATE-ENEMY-ACTIONS, author A)

Status: **source written, author execution NOT_RUN.** Claude had no shell or runtime in either session (Max implementation, then the xhigh finish after Root's host restart). Everything under "Root executions" was run by Root, not by the author. The C# consumer, the Node test and every C# replay below are **unexecuted** until Root runs them. This unit does **not** approve or activate late content. The original 1320/32 and expanded 5280/21 balance failures stay open and separate.

## Requirement, role and completion bar

- Sources: design v0.8.1 §§5.3–5.7 and §§10.4–10.6, the current original `engine/rules-core.js` (the only behavior oracle), and `proposals/late_enemies.cjs` (23 candidate enemies, test-only).
- Role: close the C# engine gap for the six existing late behaviors: apDrain, sealSlot, delayRes, silence, eatName and phase.
- Completion bar: fresh C# Create plus begin/play/ult/end replays match the original JS exactly. That covers state, events, log, RNG, public view, rejections and receipts.

## Files

| File | Owner state |
|---|---|
| `core/MemoryForge.Core/Battle.cs`, `BattleDefinitions.cs`, `BattleState.cs` | Written in the Max session. Root built Core with 0 errors and 0 warnings. Now readonly. |
| `core/MemoryForge.CoreChecks/BattleDataLoader.cs` | Written in the Max session. Now readonly. |
| `tools/gen_battle_late_actions_vectors.mjs` | Producer, unchanged in this session. |
| `core/MemoryForge.CoreChecks/BattleLateActionsChecks.cs` | C# consumer. Written. NOT_RUN. |
| `engine/tests/battle_late_actions_vectors.test.mjs` | Node checks. Written. NOT_RUN. |

## Exact engine semantics ported from the original JS

- **apDrain**
  - `n` must be a non-negative integer; otherwise the move rejects with `INVALID_ENEMY_MOVE`. A missing `n` is invalid too.
  - The drain accumulates in `apDrainNext`.
  - `StartTurn` subtracts it separately from due debt (original `nextTurnAp` outside a player phase), clamps once to [0, apMax], then clears it. It is consumed once.
  - The nonzero drain/silence support gate was removed. The power, buff and passive gates remain.
- **sealSlot**
  - Targets the next column of the following player turn (due turn+2).
  - It falls back to enemy block +10 (flat) when that turn is already sealed (`sealedUntil >= turn+1`) or when `nextPlayerSlotCapacity` is full. Temporary slots expire first, so only base slots count.
  - Event: `{uid, until, fallback}`.
- **delayRes**
  - Acts on the newest reservation (highest order; ties keep the first in array order).
  - The reservation moves to due+1 and keeps its `kind`, so its power is unchanged. It is marked `delayed: true`.
  - If the destination capacity (seal-aware; 0 beyond the two-turn board) is full, the reservation goes to discard with no refund, no resolution and no fizzle.
  - With no reservation the event is `{uid: null, discarded: false}`.
  - Player pull still clears `delayed`.
- **silence**
  - `n` is absent or exactly 1; anything else rejects with `INVALID_ENEMY_MOVE`.
  - Sets `silenceUntil = max(old, turn+1)`. Repeats do not stack.
  - The existing `CostOf` order holds: a free instance costs 0, then +1 for next/after reservations, then freeNext sets 0.
  - The existing EndTurn expiry is unchanged.
- **eatName**
  - Picks from discard tokens first, otherwise the whole discard, with `Below(pool.Count)`.
  - Exhaust goes through the existing `ExhaustCard` hooks: oblivion, exhaustGuard and void_shard. Oblivion ink only gives AP in a player phase.
  - An empty discard emits `{uid: null}` and uses no RNG.
- **phase**
  - `phase` is descriptor data, never a pattern move.
  - At a boss's first HP 0, `KillEnemy` opens the descriptor instead of dying:
    - `t != "phase"` rejects with `INVALID_ENEMY_MOVE`.
    - A non-boss, nonzero HP or an existing phase index rejects with `WRONG_PHASE`.
    - The original value checks on hp, brk and weak (non-empty, unique, known attribute) and on pattern (non-empty, no nested phase) reject with `INVALID_ENEMY_MOVE`.
  - The same UID then gets `phaseIndex = 1`, difficulty-scaled HP, brk/brkMax, a copy of the weaknesses, a deep pattern copy, `pi = 0`, block 0, fresh status, `stunned = false` and `brokenUntil = 0`, and new intents.
  - Event: `{uid, index: 1, hp, brk}`.
  - No excess damage carries over, `ctx.killed` stays false and kill hooks wait for the final body.
  - `EnemyPattern` prefers the second body's own pattern.
  - In atk/atkdebuff, a body change during the multi-hit (for example a counter kill) ends the old body's action.
- **Optional serialization**
  - Enemy `phaseIndex`/`pattern` keys and the event keys `until`, `fallback`, `from`, `to`, `discarded`, `index`, `hp` and `brk` appear only when they are set.
  - eatName and delayRes add an explicit null `uid`.
  - Old states, events and public views keep their exact shape. Copies are deep, including the pattern lists.

## Production limits named explicitly (not silently skipped)

1. **The Unity/export bridge is NOT implemented.**
   - The phase descriptor (`BattleEnemyDef.PhaseDescriptor()/SetPhaseDescriptor`) and attribute IDs (`BattleData.SetAttributeIds`) are stored through methods and private fields.
   - This keeps `tools/UnityContentExport` (no public properties; every public field is compared) and the Unity Newtonsoft round-trip test (`ExportedContentLoadsEveryOriginalDefinitionAndRoundTripsExactly`, exact member sets) unchanged for live content.
   - Consequences:
     - Exported Unity content carries no phase or attribute data.
     - Candidate auto-export, loading and activation in Run/Unity are not implemented by this unit.
     - A candidate-only Mono/Unity probe must call `SetPhaseDescriptor` and `SetAttributeIds` itself.
     - Undeclared attributes fail closed: a phase change rejects with `INVALID_ENEMY_MOVE`, which matches the original engine with no attributes.
2. **C#-only rejection codes outside the shared oracle.**

   | Input | C# code | Original JS behavior |
   |---|---|---|
   | Pattern-held `phase` move | `BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED` (kept; the existing BattleTurnChecks probe expects it) | `WRONG_PHASE` |
   | Unknown move in a second-body pattern | Passes the original descriptor check, then rejects at its action with `BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED` | `UNSUPPORTED_EFFECT` |
   | Extra members on a late move | `BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED` (existing trust boundary) | Ignored |
   | Int32 drain overflow | `BATTLE_NUMBER_OUT_OF_RANGE` | Keeps a larger double |

   All of these reject atomically. They are covered only as C# local probes.
3. **`zone` is not typed.** The overlay `zone` member is not on `BattleEnemyDef`; Battle never reads it. The consumer strips only `zone` when comparing typed definitions with the source and compares every other member, including absences, exactly.
4. **No new public preview helpers.** `nextTurnAp`, `nextTurnDrain` and `previewDelay` stay private or absent.
5. **B's forecast with phase transitions.** B-owned `Battle.Forecast` runs the real command, so a lethal hit on a phase boss reports the HP difference to the second body. Preview semantics for that case belong to B/Root and were not changed here.

## Oracle corpus and fixed denominators (producer unchanged)

| Group | Cases | Requests | Notes |
|---|---:|---:|---|
| Candidate duels | 23 | 149 | All committed. Each candidate runs two full pattern cycles alive: 243 HP, live base kits, seed 0. |
| Candidate encounter groups | 23 | 151 | All committed. 2 × the longest pattern of each group. |
| Boundary edges | 34 | 166 | 24 real rejections. Datasets are listed below. |
| Protocol | 1 | 7 | 3 committed, 2 receipt replays, 2 rejections (`IDEMPOTENCY_CONFLICT`, `STALE_VERSION`). |
| **Total** | **81** | **473** | 445 committed, 2 replayed, 26 rejected |

Edges by behavior:
- **apDrain:** drain-debt-clamp.
- **silence:** silence-free-next (rien ultimate gives freeNext), silence-free-instance (free copyPick copy; surcharge-only AP_LOW).
- **sealSlot:** seal-protected-turn, seal-fallback-full, seal-already-sealed (player collateral, then NO_EMPTY_SLOT), seal-two-sealers.
- **delayRes:** delay-original-kind (sera_timed resolves at rv 22, not ra 30), delay-overflow-discard, delay-outside-board, delay-pull-clears, delay-seal-capacity.
- **eatName:** eat-token-priority, eat-empty-discard (RNG unchanged), eat-exhaust-hooks.
- **phase:**
  - Candidate b_nameless: nameless-kill-hooks (marked final kill gives Kaya deduction and notebook coins), nameless-multihit, nameless-phase2-cycles (burn opens the second body; its pattern runs two cycles at a recorded 500 HP).
  - Test-local: phase-counter-multihit, phase-reflect-defeat, and 10 invalid descriptors (9 `INVALID_ENEMY_MOVE`, 1 non-boss `WRONG_PHASE`).
- **Invalid moves:** 3 test-local cases (`INVALID_ENEMY_MOVE`).
- **Terminal stop:** late-terminal-defeat (drain, silence and seal stay set and visible at DEFEAT).

Datasets:
- `candidate` is live data plus the unchanged overlay.
- `boundary` adds 17 fixed test-local `t_*` enemies defined in the pinned producer. They are explicit source inputs, never expected-state injection.
- Candidate numbers are never changed. Only party HP (500), difficulty HP (1) and relics are recorded per case as public Battle.create inputs.

## Consumer and Node coverage (written, NOT_RUN)

**C# `BattleLateActionsChecks.Run(root)`**
- **Decoding:** shared strict single-member gzip, fatal UTF-8 and unique-key decoder, plus four decoder corruption controls.
- **Fixture identity:** exact schema, status and scope; six allowlisted sources in fixed order, hashed only from fixed paths, so a fixture cannot steer which file is read.
- **Independent transcriptions:** the C# file holds its own copies of the proposal provenance, the 23 candidate definitions, the encounters, the 17 test-local enemies and the protocol requests, and requires the fixture to equal them.
- **Case structure:** exact ordered case identity and datasets; live-matrix setups for duels, encounters and protocol; structural Battle.create-only setups and exact enemy lists for edges; request continuity; checkpoint hashes; fixed denominators.
- **Typed data:** loaded through `LoadWithEnemies(root, overlay.enemies[, boundaryEnemies])`. `Load(root)` must stay live 20 with no phase and no late moves. Typed definitions are compared member and absence exact, except the named `zone` limit, and must be unchanged after all replays.
- **Replays:** all 81 cases and 473 requests through the public `Command`, with:
  - exact rejection codes and atomicity;
  - no hidden receipts for rejected requests;
  - full response, state, public view and hash comparison;
  - card conservation;
  - stored receipt replays checked after each case;
  - a mutation probe after every step: snapshot fields, enemy pattern copies, intents, reservations, and the public graph pattern, intent and res lists.
- **Independent literals:** for every edge and duel, plus a branch-outcome union.
- **C#-only local probes:** shape rejections, the pattern-held phase, the unknown second-body move, drain overflow, undeclared attributes, detachment of the second-body definition, loader rejections (non-object phase, unknown members, non-array overlay, overwrite and double overlay, null phase) and live 20 after overlays.
- **Tamper rejections:** 26 cases.

**Node test**
- Exact overlay, provenance and test-local enemies; live content has no late moves; independent boss and second-body literals.
- 23 candidates × 2 cycles alive with every late move executed; 23 encounter groups.
- Full independent replay of all 473 requests with a separate sorted-replacer hash.
- Boundary literals for all six behaviors.
- `checkFixture` tamper rejections.
- Strict-decoder controls through the existing `decodeVectors` from `tools/gen_run_create_vectors.mjs`: concatenated members, truncation, CRC, duplicate key, BOM and invalid UTF-8.
- Deterministic export and preservation of the old fixture on injected failure.
- Losslessness of the checked-in fixture.

## Root executions (attributed to Root, not the author)

- Built Core: 0 errors, 0 warnings.
- Exported this producer to a private file: 81 cases / 473 commands / 445 committed / 2 replayed / 26 rejected; 204,824 gzip bytes; 4,357,889 JSON bytes. Engine, data and proposal source hashes matched Root's.
- Not yet run: the C# consumer, the C# replay, the Node test, the old C# and Node suites with this Core, and Unity.

## Root needs before integration

1. **Program registration.** Add `int lateFailures = BattleLateActionsChecks.Run(root);` and `&& lateFailures == 0` to the final aggregate, keeping all existing suites.
2. **Fixture.** Export `engine/tests/vectors/battle_late_actions_vectors.json.gz` with `node tools/gen_battle_late_actions_vectors.mjs`, then `--check`.
3. **Runs.** Node 22 and 25 for `node --test engine/tests/battle_late_actions_vectors.test.mjs`. Then the full CoreChecks build and run, confirming the old suites stay green. BattleTurnChecks' eatName/phase/loader probes must keep their expected codes.
4. **Unity.** Recompile with the shared Core package. `battle-content.json` and its round-trip test must be unchanged; no re-export should be needed, because no public member was added to exported types. Late content is not loaded in Unity.
5. If the consumer finds a real C#/JS mismatch, return the exact diagnostic. Do not edit expected values.
