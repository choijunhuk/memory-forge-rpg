# P1-EXPEDITION-BATTLE-FORECAST — author report (Claude, Opus 5.5)

Tasks: `P1-EXPEDITION-BATTLE-FORECAST-CORE` (Max, timed out after the production source and producer were written) and
`P1-BATTLE-FORECAST-CHECKERS-FINISH` (xhigh, this resume). Basis: v0.8.1 §14.3 (combat information/confirmation), §6 card effects,
§16.2 shared deterministic rules. Included subscription only; no Bash/MCP/agents/images were used.

**Nothing in this report was compiled, executed or tested by the author: every compile/test/oracle result here is NOT_RUN by Claude.**
Root's own provisional evidence (not repeated as mine): the two production files compiled offline with netstandard2.1/C# 8 at 0 errors/0
warnings, and one original-JS generation in worktree B produced 9 cases, 70 forecasts and 40 commands (15994 gzip bytes).

## Files

| File | State |
|---|---|
| `core/MemoryForge.Core/Battle.Preview.cs` | `BattlePlayForecast` DTO and `Battle.Forecast`. Now read-only for Claude, provisional at Root. |
| `core/MemoryForge.Core/Run.BattleQueries.cs` | Adds only the forwarding `Run.BattleForecast`. Now read-only for Claude, provisional at Root. |
| `tools/gen_battle_preview_vectors.mjs` | Original-JS producer. This resume made only the codec correction (see below). |
| `engine/tests/battle_preview_vectors.test.mjs` | Node checks: denominators, independent literals, a second public-API derivation, freshness/tamper, strict codec, checked-in fixture. |
| `core/MemoryForge.CoreChecks/BattlePreviewChecks.cs` | C# consumer: `BattlePreviewChecks.Run(root)`. It is not registered yet. |

Codec correction: `--check` and `exportVectors` now use the shared strict `decodeVectors` from `tools/gen_run_create_vectors.mjs`, which
`gen_run_progression_vectors` and `gen_run_json_number_vectors` also use. The decoder file is pinned as source `runCreateDecoder`. The
cases, expectations and coverage assertions did not change. However, the producer's SHA and the source list changed, so **the gzip
currently in B is stale and Root must regenerate it.** Until then, the checked-in-fixture Node test and the C# source-pin validation
fail by design.

Root's one-line integration in `core/MemoryForge.CoreChecks/Program.cs` is still to do: add `int previewFailures = BattlePreviewChecks.Run(root);`
and `&& previewFailures == 0` to the return condition.

## API semantics

`Battle.Forecast(uid, mode = "now", target = null, picks = null)` / `Run.BattleForecast(...)` both return a fresh `BattlePlayForecast`:

1. Battle.Check runs first, with its exact error-code contract. The picks are copied once and never retained.
2. The real play runs through `Command` on `new Battle(data, state.Copy())`. This is the same isolated candidate Command itself builds;
   it uses rid null, so no receipt is stored. The forecast uses no restore API, no exposed mutable state and no duplicated
   cost/damage/condition formula.
3. `Error` is the Check code, or the real command's RuleException code (a late rejection). All other fields then keep their defaults. The
   forecast returns rule rejections and never throws them. Outside battle, `Run.BattleForecast` returns `Error = "NO_BATTLE"`, like
   `BattleCheck`.
4. Fields:
   - `Mode`: echoes the request.
   - `Cost`: the play event's AP.
   - `Due`: the reserve event's absolute turn for next/after (null for now). A reservation reports no outcome numbers.
5. A now play is `Hidden` if the effective definition has tgt "rand", pick "random" or a draw anywhere in if/then/else (the accepted
   practice-view rule). It is also hidden if the actual candidate play changed the RNG or the draw pile through a relic/passive hook.
   The numbers stay zero, and `Defeat` is also suppressed, matching the practice view.
6. Otherwise the fields are the actual results:
   - `EnemyHpLoss`: the summed enemy HP lost, excluding damage absorbed by enemy block.
   - `BlockDelta` / `HpDelta`: signed party changes.
   - `Defeat`: the play ends the battle in defeat.

Intentional difference from the practice view: a reservation or a definition-hidden card also runs the candidate. A late rejection
therefore reports the real command's code instead of cost/timing or "hidden". With authored data this is unreachable.

## Evidence design (bounded, not a matrix)

Expectations come only from the original JS public API. `check()` gives the code. Otherwise a fresh `Battle.create` + recorded requests
(or `Run.create` + `enter` + `battleCmd`) replays the point, plays the request once, and the before/after states are read. The C# method
is never consulted.

| Case | Covers |
|---|---|
| damage-block-targets | Exact damage 8, block +8, enemy block6 absorbing (HP loss 2 and 0 with block +5), CREATED `WRONG_PHASE`, `BAD_TARGET`, `INVALID_MODE`, `NOT_IN_HAND` (unknown and played uid), next due 2 |
| heal-reservation-slots | Heal +5, `RESERVE_ONLY`/`NOT_RESERVABLE`, empty_clock costs and due turns, `SLOT_FULL` |
| reflect-defeat | Lethal reflect stops later hits: HP loss 4, HP −3, defeat; terminal `WRONG_PHASE` |
| overload-self-damage | Self-damage −4 with Lumi's low-HP +30% (23, 10); `OVERLOAD_WOULD_KILL` |
| hidden-definition | Draw / rand / nested if-then draw hidden (including a real draw that neither the RNG nor the pile reveals); visible control 8; `AP_LOW` |
| hidden-hook-draw | unfinished_score: draw-pile-only change hides a non-random card; an earlier owner stays visible |
| hidden-hook-rng | void_shard: RNG-only change hides a block card |
| picks-pull | Caller picks: resolve the next reservation (12) vs move the after reservation (0) |
| run-expedition | v2 Run: `NO_BATTLE` on the map and after the battle, 3 fixed probes, a forecast before each of 13 policy plays, 17 Run commands |

Fixed denominators:

- Battle cases: 51 forecasts / 23 commands.
- Run case: 6 fixed forecasts + 13 policy forecasts / 17 commands.
- Total: **70 / 40**.

The 13/17 values are Root's measured generation, pinned in Node and C#.

### C# consumer (per forecast and per command)

Per forecast:
- The nine fields equal the oracle.
- The complete live state (log/events/RNG/version), the public view (Run: the complete RunState incl. memory/versions) and the
  definitions are byte-identical.
- The caller's picks are untouched.
- After mutating the returned object and the picks, a repeat gives an identical result; omitted picks equal empty picks.
- For hidden forecasts, the C# effective definition agrees with the oracle's definition-or-hook reason.

Per real command:
- Stored receipts still replay, and an unknown-rid probe proves no forecast stored a receipt.
- The command executes once (v+1), with the full JS response and state.
- The same rid replays without effect.
- When the command equals a forecast, the actual cost/due/deltas/defeat (or the RNG/draw movement named for a hidden play) match it.

Also checked:
- Strict codec: `RunCreateChecks.Decode/UniqueKeys`, with C# controls for empty-member prefix/suffix, a truncated footer and a CRC flip.
- Allowlisted source pins (path before hash), contract members and per-case denominators.
- Setup-only inputs (no state injection) and record self-consistency.
- 22 fixture tamper rejections and independent hand-derived literals.

Categories reported honestly:
- **NOT_COVERED**: the `pick:"random"` definition branch. No authored card has it; only Nia's resonance battleStart does.
- **C#-only**: late command rejection. A local-definition fault after real Create/begin (harin_shield + blk Int32.MaxValue) makes the forecast
  return `BATTLE_NUMBER_OUT_OF_RANGE` with the live state unchanged. The real play rejects atomically with the same code, and the corrected
  definition forecasts block +8 and executes once.

## Limits

- Unity view integration, Editor/Android/device, server and native input acceptance are separate and PENDING.
- The literals were derived by hand from the original data. If a literal fails while the oracle comparison passes, the literal is wrong:
  report FAIL rather than editing the oracle.

## Root verification (NOT_RUN by Claude)

1. `node tools/gen_battle_preview_vectors.mjs` (regenerate), then `node tools/gen_battle_preview_vectors.mjs --check`.
2. `node --test engine/tests/battle_preview_vectors.test.mjs`, then `npm test`.
3. Add the Program.cs registration, then `dotnet run --project core/MemoryForge.CoreChecks -- <root>` (all checkers; the new
   `BattlePreviewChecks` line must show 70 forecasts / 40 commands and 0 failures).
4. `npm run test:proposals`, the Python tests, `npm run test:docs`, `npm run test:handoff` as AGENTS.md requires.
5. Root's independent diff review and the opposite AI's review, then later Unity UI integration and actual Editor validation.
