# P1-R1C-HOST-RECHECK — second, final opposite-author check of the corrected completion host

- Reviewer: official Claude Code, Opus 5.5, included subscription only. Requested effort: xhigh. This resumes session c1b34a4b-d227-4de8-9504-bb76ae06d41a. I can't see the applied effort from inside the session; Root's CLI record has it.
- **Execution: NOT_RUN.** I had Read/Glob/Grep/Edit only. All counts quoted below are Root's fresh receipt, not my execution.
- Reviewed:
  - `core/MemoryForge.CoreChecks/RunProgressionChecks.cs` (current copy; it matches `host_recheck_diff.txt`, e.g. `CompletionForgery` at :773-795)
  - `reports/run_memory_core_20261016/host_recheck_diff.txt`
  - `core/MemoryForge.CoreChecks/TraceHashChecks.cs` (the copy Root provided)
  - `client/.../Tests/PlayMode/RunProgressionPlayModeTests.cs`
  - `reports/run_services_core_20261016/playmode29_pass.json`
- Edited only this file.

## Verdict: APPROVE — P1 and P2 are resolved; nothing blocks integration

The notes below are non-blocking. They limit how the new evidence may be described; they don't block the code.

## P1 (v2 `commitMemory` arity): RESOLVED

- **Arity by the actual policy:** `RunCall` (:585-593) branches on `run.State.MemoryPolicy`. It requires exactly 2 recorded arguments for v2 and exactly 1 for legacy, so the arity is never inferred from a null.
- **Recorded store is comparison data only:** for v2, `args[1]` (the JS store recorded at call time, `gen_traces.mjs:169-170`) is compared with `currentMemory`. That is the host's independently migrated actual M0 store (:469), never a fixture value.
- **Core gets the actual store:** the call is `run.CommitMemory(args[0], currentMemory)` (:593). `args[1]` never reaches Core, so a forged recorded store can't steer C#.
- **Mismatch fails the case:** a wrong count or a differing store throws `InvalidDataException`.
  - In the oracle it escapes the inner filter (:495) and is recorded as a failure for that case (:221).
  - Under probing, `Rejects` counts it as a rejection.
  - In the legacy corpus, the outer catch records it as `LegacyMismatch`.
- **Legacy path unchanged:** the legacy corpus call `RunCall(run, kind, args)` stays one-argument, and the corpus is legacy-only (:811).
- **Matches the observed failure:** Root's first actual run (2683/2684, 4/5) is the outcome I predicted. The fixed run's 2684/2684 and 5/5 + 5/5 are consistent with this fix (Root receipt).

## P2 (Unity claim and diagnostics disclaimer): RESOLVED

- **New wording** (:163): "Assign 0 attempts in this host (separate Unity evidence); RuleError name/message/detail unclaimed (code only)".
  - The host claims no Assign execution.
  - The code-only disclaimer is back, and the host still compares only codes (:497).
- **The separate evidence exists:**
  - `RunProgressionPlayModeTests.ActualCommonAssignmentMatchesOriginalJs("legacy"/"v2")` (:93-116) makes 5 actual `run.Assign` attempts per policy. It checks the error code and the complete state hash after each one against an embedded JS reference.
  - `playmode29_pass.json` records 29/29 Passed, with both cases Passed (:258-273).
- **Caveat:** that receipt is under `run_services_core_20261016`, an earlier unit. It supports the "separate evidence" wording, but it isn't a fresh Unity run after R1c. Don't present it as one.

## P3 follow-ups: verified

- **Input store immutability:**
  - :486 captures `Full(store)` before every v2 `commitMemory`.
  - :498 checks it is unchanged after the call, for both committed and rejected calls, before the host reassigns the store (:499-501).
- **Completion is probe-isolated:**
  - The `!probing` guard is gone (:522).
  - `completedCases` and `actualReloads` advance only through `Count` (:525, :549), and both are `int`, so probing never moves them.
  - `ReloadActual` has no other unguarded side effects.
  - A probe case can reach the reload only after every command matched and no new failure occurred (:522-525).
- **The two completion forgeries are sound in construction:**
  - **Terminal return:** adds `forgedProbe` to the last step's `result.value` and recomputes `jsonHash` with `Ordered`. `Result()` accepts it, and nothing else in `ValidateCase` ties `result.value` to another record, so structural validation passes.
  - **Reload:** changes `reload.created` `run.coins` and recomputes `stateHash` (`CanonicalState.Hash`) and `jsonHash`. `Point()` and `ValidateReload` accept it.
  - At run time, the first is rejected by `Return`'s `Difference` at the last step, and the second only by `ReloadActual`'s `Compare` (:547).
- **Probe count:** the 22 existing probes are untouched, and the 2 new ones make 24. Note that `probes` is printed, not checked against a literal (true before as well).
- **`TraceHashChecks` callback keeps `finalState`:** `ReadSnapshots(..., Action<Dictionary<string, object?>>? scenario)` (:127) calls `scenario?.Invoke(trace)` (:222) only after all strict checks. Those include `CheckSnapshot(finalState)` (:184) and the terminal commit check (:218), and the callback gets the same parsed dictionary, which those checks only read. This closes my first review's NOT_VERIFIED item.
- **Unchanged:** Schema, Status `JS_ORACLE_ONLY`, Scope, Contract, `Totals` (csharpCommandReplay 0), the command/method literals and the fixture.
- **Arithmetic:** 182528 + 90976 = 273504.

## Non-blocking notes (no third round needed)

1. **The completion forgeries don't pin where they're rejected** (:773-795). Unlike `Forgery` (:757 `reached == forgedAt`), `CompletionForgery` discards `Execute`'s return value, and there's no untampered round-trip execution control. The probe proves "rejected", but not "rejected by actual save/reload", which is what its label says.
   - It is deterministic and very likely sound, because the round-trip control keeps every hash.
   - Cheap fix:
     - Require `reached == target.Prefix - 1` for the return forgery and `reached == target.Prefix` for the reload forgery.
     - Add `Control("untampered round trip completes actual save/reload", () => Execute(roundTripCase, ...))`. This also proves the claim "valid probe cases reach ReloadActual".
   - Until then, describe these as "2 completion forgeries rejected" without the location claim.
2. **Some claimed cleanups are not in the reviewed file:**
   - :49 still says "commitMemory alone still stops a case before it is attempted".
   - :569 still labels the rest/buy/eventChoose shape comment as "R1c actual terminal saving".
   - :322 and :518 still print "before DONE".
   - No text in the host says the legacy committed/rejected, battle or archive counts are observed rather than literal-asserted (grep finds no "observed").
   - The Return comment (:621) and the "canonical final state" message are corrected.
   - Either fix these, or don't list them as done in the integration report.
3. **Root's receipt numbers** (21024/21024; 2684/2684, 1370/1314, 1074; 5/5 + 5/5; 273504/273504, 1000 DONE, 182528/90976, 6657 battles, 275 archives, 567 + 1000 probes) are Root's execution. I didn't reproduce them. The two new probes were still rebuilding when Root wrote and are not yet PASS. Approval of the code doesn't stand in for that receipt.
