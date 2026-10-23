# Numeric test recheck (opposite author) — P1-RUN-MEMORY-CONSUMER

- Reviewer: Claude Code, Opus 5.5 (`claude-opus-5-5`). Static read only; this file is the only numeric-related edit.
- Execution: **NOT_RUN** by me. Root's receipts are recorded below as Root's, not mine: Mono private 2088 cases 41→0 with A's BigInteger fix; Unity Play 77/77 including 2176 strings, the positive public receipt and both nearby conflicts; .NET RunJsonNumber 2189/2189.
- Files read: `tools/gen_run_json_number_vectors.mjs`, `engine/tests/run_json_number_export.test.mjs`, `core/MemoryForge.CoreChecks/RunJsonNumberChecks.cs`, `client/MemoryForgeUnity/Assets/Tests/PlayMode/RunJsonNumberPlayModeTests.cs`, `core/MemoryForge.CoreChecks/Program.cs` (worktree B copy).

## Verdict: **APPROVE** (F1–F3 resolved; two non-blocking notes)

- **F1, resolved.** CoreChecks `PublicReceipt` and Unity `NearbyNumericReceiptBodiesRemainConflicts` both run two checks, each on a fresh `Run.Create` with a fresh M0 store:
  - **next-double:** the request is af6 (JSON `1e+23`) against the af5 body.
  - **wrong-body:** the stored body is `…98e+22` and the request is af5.
  - Both assert `REQUEST_CONFLICT`, phase `MAP`, an unchanged canonical Run hash and an unchanged serialized store.
  - Both conflicts catch a loosened comparison. `…98e+22` parses back to af5, so a parse-and-compare loosening would replay and fail the test. On the old Mono formatter, wrong-body correctly goes RED.
  - Nothing in input validity or the body comparison was relaxed.
- **F2, resolved.** The Node test `actual JS public numeric receipt replay…` builds the body with builtin `JSON.stringify` and pins it to the exact C# literal. It then runs JS `Run.create` with the same setup and an M0 store:
  - **matching:** the response is deep-equal and the phase is `DONE`.
  - **next-double and wrong-body:** `RuleError REQUEST_CONFLICT`, with `run.s` JSON unchanged.
  - The store JSON stays unchanged in all three modes.
- **F3, resolved.** The seed list now includes:
  - ±MAX_SAFE, ±2^53 and ±(2^53+2);
  - ±2^e for e ∈ {−1022, −1021, −1000, −100, −53, −1, 0, 1, 52, 53, 54, 100, 1000, 1023}, each with ±1-bit neighbours. This covers min normal `0010000000000000` and max subnormal `000fffffffffffff`.
  - Expected strings still come only from builtin `JSON.stringify`.
  - The self-asserted count is 2176, and the Node test, CoreChecks (count, array length and executed set) and Unity all use 2176.

## Notes (non-blocking)

1. **Weaker "Run unchanged" assertion in C#/Unity.** C# and Unity check it with `CanonicalState.Hash` (which omits `log`/`ev`) plus the phase, while JS compares the full `JSON.stringify(run.s)`. A conflict throws before any log write, so this is acceptable. A full-JSON comparison would match JS exactly.
2. **Program wiring not present in worktree B.** `Program.cs` in B (lines 117–126) still does not call `RunJsonNumberChecks.Run`. Root reports that its integration tree adds `numberFailures` to the aggregate; I could not verify that here.

F4 from the earlier review still applies: a .NET pass is not Mono evidence. The decisive receipts are Root's Unity Play and Mono runs above.
