# Pure battle forecasts — Core acceptance (view integration separate)

Original v0.8.1 §§6,14.3/14.7 and16.2. Requirement: compute the actual cost/visible outcome before confirmation without consuming the live battle, hidden draw order, RNG, version or receipts. Role: supply the Run-owned battle's derived data to the expedition view; no new rule, probability, price, capacity or damage formula.

## Implemented and independently reviewed

Official Opus Max authored `BattlePlayForecast`, `Battle.Forecast` and the thin `Run.BattleForecast` wrapper plus the original-JS producer. The30-minute CLI cutoff left those complete and the two test files pending; Root preserved the exact checkpoint and resumed the same Opus session at xhigh to finish the consumer/Node tests/shared-codec correction. This was a time limit, not a subscription limit. No paid fallback or alternate model was used. All actual metadata is separate from raw reasoning/billing data.

Codex read the complete production API, producer329lines, Node tests and543-line consumer before import. The pure query uses the existing private `state.Copy()` candidate and real `Command`, copies caller picks and returns primitive detached fields. Check/rejection order remains source-defined. Future reservations report cost/due only; now plays with draw/random definitions or RNG/draw-pile-changing hooks suppress outcome numbers/defeat. Raw state and a mutable Battle never escape. Run outside battle returns NO_BATTLE.

Root added only the checker registration/failure aggregation, a snapshot/check of the actual separate Run-owned five-list catalog (rather than only the unused standalone data instance), and test-only null-forgiving annotations after real runtime non-null/active assertions. Opposite Opus High source APPROVE is `root_patch_review.md`; a third identical test annotation was subsequently required by the fresh Unity compiler. Assertions, timer, expected values and control flow are retained; actual87Play rerun passed.

## Actual verification

- `root_export.json`: original-JS9authored cases, **70forecasts/40realcommands**:51standalone forecasts/23commands plus6fixed/13policy Run forecasts/17commands. Each starts from public Create plus actual recorded commands. Expected snapshots/results are comparisons, never injected.
- `node22.txt` / `node25.txt`: **6/6 each**. Fixed denominators, independent original-source literals, second public-API derivation, exact fresh generation, source/tamper controls and strict single-member gzip/UTF-8/duplicate-key/CRC/previous-output preservation.
- `build.txt`: actual .NET10 build, **0errors/0warnings**, Core remains netstandard2.1/C#8 with no external package dependency.
- `all_checks.txt`: **BattlePreview895/895**,70actual first forecasts plus repeated/mutated-return/picks purity probes,40real commands,85receipt probes. All original suites retained, including Run21902, Create215677 and1000actual legacy completions/273504attempts. The source-manifest audit remains423entries/29historical changes after the separate diagnostic-script restoration.
- `actual_mono_play_smoke.json`: actual Unity Play Mode Mono reports shieldforecast cost1/block8, pure live state; explicit play yields block8/AP3/version2. A real Sion draw card is Hidden with all outcome numbers0 and no live-state change. This is an actual Core probe inside Play, not the upcoming new client forecast UI.
- `actual_play87_regression.json`: **87/87 actual PlayMode**, no skips, after importing the new Core and preserving existing client/notice/card-information tests. Fresh compiler warnings from test-only nullability were corrected. Native/Android/IL2CPP/server acceptance is not inferred.

## Explicit limits / continuation

No authored card has `pick:"random"`; that definition branch is NOT_COVERED. Late-command rejection uses a separate local C# definition-overflow fault and is not JS parity. The catalog assertion covers cards/characters/weapons/parts/relics and does not claim every possible data collection. The UI still honestly says forecast is absent until the separately assigned view integration is actually compiled/tested/observed. Old battle practice is preserved. Physical native mouse behavior, account/disk persistence, unfinished-run restoration, base0's original design conflict, final art and32balance misses remain separate.

Fresh complete Node22 suite after the new tests: **326/326,0failures/0skips**, recorded in full_node22_326.txt. Docs9/handoff13PASS. Current Core-only acceptance precedes the separately running UI implementation.
