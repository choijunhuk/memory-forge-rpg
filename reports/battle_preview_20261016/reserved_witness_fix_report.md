# Same-point witness repair — P1-RESERVED-FORECAST-WITNESS-FIX (Claude, Opus 5.5 High)

Finding: the v2 structural witness "distinct next/after estimates of one card" compared equal uid/target with different modes across every
case and step. Uids recur and states differ between steps, so a cross-state pair could satisfy it. The explicit literals (timed 22/30) were
already correct; this repairs the coverage guard only. No production Core, fixture, UI or Program change.

**Build, tests and fixture regeneration: NOT_RUN by Claude.**

## Changed guard (producer and consumer agree)

The witness now holds only for two visible forecasts at one forecast point: the same case and step, the same uid, target and picks, one
`next` and one `after`, whose estimates differ in any outcome field (enemyHpLoss, enemyDamage, blockDelta, hpDelta, defeat, newlyBroken).

| File | Change |
|---|---|
| `tools/gen_battle_preview_vectors.mjs` | New exported `nextAfterDiffers(cases)`, which scans each step's own forecasts. It replaces the global `visible.some(a => visible.some(b => …))` witness entry. |
| `core/MemoryForge.CoreChecks/BattlePreviewChecks.cs` | `Seen.Point` advances once per case step (battle and Run validators). `Seen.Estimates` now holds `(Point, Uid, Target, Picks raw JSON, Mode, Outcome)`, and `reserved:next-after-differ` requires the same Point/uid/target/picks with next against after and a different outcome. New tamper (29 total): equalise the step-1 timed keeper pair. It must be rejected, and the error message must name `reserved:next-after-differ`. |
| `engine/tests/battle_preview_vectors.test.mjs` | Structure test: the witness holds; exactly one point holds it, `reserved-estimates` step 1 (`timed next keeper`/`timed after keeper`). Control: with that pair equalised, the old global comparison still finds a cross-point lookalike (step 5 `timed next broken keeper` 18 against step 1 after 22), but `nextAfterDiffers` returns false. |

Unchanged:
- 12 cases, 91 forecasts, 52 commands and 131 receipt probes;
- the v1 queries and commands;
- the literals, the fresh original-JS oracle, the HTML `preview()` execution, source pins, schema, decoder and failure checks.

The only value change is the producer's own SHA in `sources.generator`, which comes from the source edit.

## Root steps

1. `node tools/gen_battle_preview_vectors.mjs` (the producer pin changed), then `--check`.
2. `node --test engine/tests/battle_preview_vectors.test.mjs` and `npm test` on Node 22 and 25; expect the same 6 tests.
3. `dotnet build`, then the CoreChecks run. Expect BattlePreview to report 91/52/131 and one more passing check, from the new lookalike tamper.
