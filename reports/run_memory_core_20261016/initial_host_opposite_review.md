# P1-R1C-COMPLETION-HOST-REVIEW — opposite-author review of the Codex completion/reload host

- Reviewer: official Claude Code, Opus 5.5, included subscription only. Requested effort: xhigh. I can't see the applied effort or the session id from inside the session; Root's CLI record has both.
- **Execution: NOT_RUN.** I had Read/Glob/Grep/Edit only. I ran no Bash, build, CoreChecks, JS or Unity. Every statement below comes from reading the source.
- Reviewed: `core/MemoryForge.CoreChecks/RunProgressionChecks.cs` (963 lines in this worktree), `reports/run_memory_core_20261016/host_review_diff.txt`, `Run.Memory.cs`, `RunState.cs`, `RunDefinitions.cs` (`RunValues.Fields`), `CanonicalState.cs`, `MemoryV2DataLoader.Difference`, `engine/rules-core.js` (commitMemory 1977-2017, commitMemoryV2 2056-2082), `tools/gen_traces.mjs`, `tools/gen_run_progression_vectors.mjs`.
- Edited only this file.

## Verdict: REQUEST_CHANGES (one P1 blocker)

This isn't a Core defect. With the current host, the v2 case can't reach its terminal commit. The run will FAIL rather than falsely PASS. Please fix P1 and re-run before anyone reports a completion count.

## P1 — the v2 `commitMemory` envelope has two recorded arguments, but the host accepts only one

- **Path:**
  - `RunProgressionChecks.cs:583-585` (`if (args.Count != 1) throw new InvalidDataException(...)`)
  - `:494` (`catch ... when (error is not InvalidDataException)`)
  - `:220-221`
- **Source of the shape:**
  - `tools/gen_traces.mjs:169-170` records `command('commitMemory', {requestId, runId, expectedMemoryVersion, selectedChangeIds}, context.memory)`.
  - `:131` turns this into `args = [request, store]`.
  - `:87` and `rules-core.js:1977` (`commitMemory(selection, currentMemory)`) take both arguments.
  - `gen_run_progression_vectors.mjs:119,128` record `clone(expected.command)` unchanged.
  - Legacy calls record one argument (`gen_traces.mjs:181`).
- **Reproduction (by reading):**
  1. `seed1102-full-v2` step #221 is `run:commitMemory` with `args.Count == 2`.
  2. `RunCall` throws `InvalidDataException`.
  3. The exception filter at :494 lets it escape `Execute`.
  4. `Oracle` records `seed1102-full-v2 C# execution: commitMemory expects one selection/request object`.
- **Expected result:**
  - matched 2683/2684, committed 1369/1370
  - `completedCases` 4/5, `actualReloads` 4/5
  - no `RunProgressionStops` entry for the v2 case
  - the v2 store and the v2 reload path never execute
- **Fix:**
  - Choose the arity by policy, not by `currentMemory != null`: v2 → 2, legacy → 1.
  - Treat `args[1]` as comparison data only. Require `MemoryV2DataLoader.Difference(args[1], currentMemory) == null`, or throw `Divergence` with the diff.
  - Keep passing `currentMemory` to `run.CommitMemory`. That is the host's actual M0 store, never `args[1]`.
  - Pass the policy explicitly, e.g. `RunCall(run, kind, args, store, c.Policy == "v2")`. The legacy corpus call at :837 stays one-argument.
- **Why bind `args[1]`:** today `args[1]` is ignored, so a forged recorded store could never steer C#, which is correct. Comparing it still keeps the recorded command honest about the input that was actually used. The before-point hash already fixes the store, but it doesn't fix this argument.

## P2 — the new scope line makes an unsupported Unity claim

- **Path:** `RunProgressionChecks.cs:163` ("Assign separately checked in Unity").
- **Problem:** in this tree, no `.cs` file calls `Run.Assign` (not CoreChecks, not `client/.../Tests/PlayMode`, not Scripts). The only definition is `Run.Services.cs:18`. The removed text was a measured fact: "Assign 0 attempts (not in the gen_traces vocabulary)".
- **Also dropped:** the explicit disclaimer "RuleError name/message/detail unclaimed (code only)". The host still compares codes only (:496). "Native diagnostics" doesn't say that.
- **Fix:** restore both statements, or cite the actual Unity test and its run evidence. Unity Play and device acceptance should stay reported apart from this host.

## P3 — non-blocking

1. **No check that commit leaves the input store unchanged** (:491-499). The host passes its own `store` by reference and then replaces it, so nothing would notice if Core ever mutated the caller's store. JS clones `current` (`rules-core.js:2075`). Current Core is correct: `RunValues.Fields` → `MemoryJson.Copy` is a deep copy (`Run.Memory.cs:254`, `RunDefinitions.cs:11`).
   - Fix: take `Full(store)` before the call, then Check it is unchanged after both committed and rejected calls, before reassigning.
2. **No negative control for the newly live paths.** The five cases never call `forgeOffer`, so commitMemory is the first five-case use of the `IDictionary` branch of `Return`. `ReloadActual`'s `Compare` is also new. Neither has a probe showing a self-consistent forgery is rejected, i.e. a forgery with recomputed `jsonHash`/`stateHash` on the last step's `result.value` or on `reload.created`, which passes structurally and is rejected by actual C#.
   - All 22 existing probes are retained (13 header, 7 case, 2 forgery), so this belongs to the future Run-memory negative matrix. Report it as absent, not covered.
3. **Stale or contradictory evidence text left by search-and-replace:**
   - :21-22 still describes literals "before each case's commitMemory" and a "JS-only suffix".
   - :49 says "commitMemory alone still stops a case before it is attempted".
   - :458 says "until the first unimplemented one".
   - :567 now labels the rest/buy/eventChoose shape comment as "R1c actual terminal saving".
   - :474/:516/:320 print "before DONE".
   - :616 has a grammar error ("graph return are").
   - :880 says "full final state" for a canonical (log/ev omitted) comparison.
4. **Legacy return aliasing differs from JS.** JS legacy `commitMemory` returns the same object it stores as `s.savedMemory` (`rules-core.js:2015-2016`). The host's 1000 detachment probes therefore enforce a C# ownership contract stricter than the JS oracle. Core documents this (`Run.Memory.cs:237`). The host comment at :613 should say so too.
5. **Some legacy counts are observed, not asserted.** The host prints the legacy committed/rejected split (182528/90976 per the brief, sum 273504), `battlesStarted`/`battlesFinished` and `archives`, but no independent literal checks them. Only the per-method attempt denominators are literal. Each attempt's outcome is matched against JS, so the split is JS-derived. Report these as observed counts, not as literal checks. This predates the diff.

## Checked, no finding

- **Literals:**
  - Per case, Prefix = Committed + Rejected: 148 = 116+32, 246 = 173+73, 222 = 174+48 (×2), 1846 = 733+1113.
  - Totals: 2684 / battle 2474 / battle commits 1162 / queries 1074 / committed 1370 / rejected 1314.
  - Each case adds +1 attempt, +1 commit and +2 queries (available + nodeById after a committed Run command with no battle). The result equals the fixture's own `Totals` (:39), which the diff didn't change.
  - The legacy per-method sum is 273504, including `commitMemory` 1000.
  - No expected value was weakened.
- **Store boundaries:**
  - The v2 starting store is the actual M0 `Migrate` of an empty account (:469).
  - After a C# commit, the store is a Serialize/parse copy of the actual returned `state` (:499). The copy is taken before `live` (:500) and before `Return` scribbles the returned graph (:501).
  - The scribble therefore hits Core's return and not the host store, so the host's own alias can't cause a false Core failure.
  - `Return` compares the raw return, not the copy, including `undefinedPaths`. It also has a positive control and a live-unchanged check.
- **Reload:**
  - Base input is the original `trace.setup` (the same input as the first Create). Memory comes from the actual `SavedMemory`. Seed is `(seed+99) & 0xffffffff`. v2 adds `runId+"-reload"` and the actual store version.
  - Member positions are kept, as with JS spread.
  - The JS reload setup and created point are only compared (:542, :545). JS `savedMemory` never builds a live Core.
  - The created point's `memory` is the post-commit store, the same as `gen_run_progression_vectors.mjs:146`.
  - There's no 1000-trace reload claim.
- **Completion gating:**
  - Five-case: `completedCases` requires that every command executed, there were no new failures (including :516-518), the phase is DONE and `SavedMemory` is set (:520-523). `actualReloads` requires a failure-free reload. A reload exception escapes, gets caught at :221 and leaves the count short.
  - Legacy: `legacyPrefixes` requires that every command matched, the save probe passed, the phase is DONE with `SavedMemory`, and the canonical final state equals `finalState` (:876-882).
  - Unknown commands always fail: `Pending` is empty, so :370 rejects them, and legacy rejects at :819.
- **Legacy final normalization:** `CanonicalState.Serialize` skips `log`/`ev` (`CanonicalState.cs:96`), which matches the gen_traces `snapshot()` (`gen_traces.mjs:54,58`). The per-command hash checks are unchanged. The five full cases still compare full graphs including Log/Ev (:604).
- **Legacy save probe:** the return is compared with the actual `SavedMemory`. That equals JS through the per-step canonical hash, which includes `savedMemory`.
- **Provenance unchanged:** Schema, Status `JS_ORACLE_ONLY`, Scope, Contract, `Totals` with csharpCommandReplay 0 and `Coverage.ToGraph` remain JS generator provenance. The runtime counts are separate and derived.

## NOT_VERIFIED

- In this worktree, `TraceHashChecks.cs` has only the 5-argument private `ReadSnapshots`, a private `SnapshotStats`, and no `ReadGzip`. The host's call at :789-791 depends on Root's newer version, which I couldn't read. So I couldn't confirm that the callback gets the trace with `finalState` intact.
- If `finalState` were stripped, all 1000 traces would false-FAIL at :880, not false-PASS.

## Next

1. Fix P1, and P2 if possible.
2. Rebuild fresh and re-run, then report the actual `RunProgressionCore` / `Scope` / `TracePrefixReplay` lines.
3. Send any P1 change back for a short re-review. P3 items can go to the Run-memory negative/replay matrix task.
