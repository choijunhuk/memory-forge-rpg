# Actual C# public Run memory consumer — author report (P1-RUN-MEMORY-CONSUMER)

- Author: Claude Code, Opus 5.5 (`claude-opus-5-5`), xhigh session (resume 093b92b9-…); this report was written in the follow-up High call.
- Execution: **NOT_RUN**. I had no Bash/MCP access in this CLI, so nothing was compiled or run. Root owns the build, run, mismatch triage, independent review and integration.
- Files written:
  - `core/MemoryForge.CoreChecks/RunProgressionChecks.Memory.cs`: new `static partial class RunProgressionChecks`, 813 lines.
  - `core/MemoryForge.CoreChecks/RunProgressionChecks.cs`: only two changes. The class is now `static partial`, and `Section("public Run memory boundaries", () => MemoryBoundary(root, data, definitions));` runs after Legacy and before `Report()`. Both edits are written.
- Unchanged: the existing 5-case, 1000-legacy, forgery and ownership controls and counters; production Core; JS producers; fixtures; Program.

## What the consumer does

### Trust boundary
- Reading and structure:
  - Uses the strict reader (`RunCreateChecks.Decode` + `UniqueKeys`).
  - Requires exact top-level fields in order, plus the exact schema `mf-run-memory-v1`, status `JS_ORACLE_ONLY` and scope.
  - Requires `dataVersion` to equal the current `game_data`.
- Sources:
  - Seven fixed source slots, including the generator `tools/gen_run_memory_vectors.mjs`. Every path is checked against the allowlist before any file is hashed.
- Contract and denominators:
  - The exact `contract` text in all 10 members.
  - The exact denominators, including the candidate lists.
- Native diagnostics runtime: validated only as `{node: semver}` provenance.

### Plan literal
- The four bases, with their policy, role, ordered candidates and selection-rule text.
- The 15 scenarios, with base, at and role.
- The 88 ops, each with label, method, declared expectation, store mode and the fact that may make it conditional.

### Structural checks
- **Candidate scan:** stops at the first complete candidate, and the chosen one is the first complete candidate, or else the first eligible one.
- **Prefixes:**
  - Each prefix equals the trace minus only its final commitMemory: prefix count = `commandCount` − 1, and outcomes + 1 = the summary.
  - Envelopes are restricted to the prefix methods; battle commands must have `rid` = `r{v}-{kind}`.
- **Points and the created state:**
  - Points have recomputed canonical and ordered-JSON hashes and undefined omissions.
  - `created` equals the trace's initial hash in a fresh MAP state.
  - `terminal` is the role's save, with the policy-correct store presence.
- **Facts:** the facts' party, defeat flag and first non-party character match the terminal point and the source data.
- **Op records:**
  - Exact fields per COVERED / NOT_COVERED / store mode.
  - Error records per kind: a MemoryError message must equal its code, and a RuleError detail must be a validated encoded result.
  - `sourceAliases` structure.
  - `before`/`after` reference chains; only a returned commit may append a point, and no point may go unreferenced.
- **Store records:** each store-record hash is recomputed from the before point as an integrity check only.
- **Coverage:** `summarize()` is recomputed from the records and compared with the recorded coverage. The recorded totals must also equal the literal 4/15/88/26/59/3/0. The fixture's 85 covered ops are counted separately.

### Execution
Each scenario starts from a fresh `Run.Create` of the recorded public setup.

- **Opening:**
  - The v2 store is the actual `MemoryV2.Migrate(empty, empty)` result.
  - The `created` point is compared in full, including log and RNG.
  - Terminal scenarios replay the recorded prefix through the existing `RunCall`/`BattleCall` binders, which check rid/v and the outcome/RuleError code. The full terminal point is then compared.
  - Expected states are never restored.
- **Store argument, by mode:**
  - `live` passes the host-owned actual store itself.
  - `live-patch` clones the actual store, removes the listed members, then sets the declared members into a fresh dictionary in source order.
  - `literal` passes the declared controlled value.
  - The actual argument's ordered-JSON hash must equal the recorded `jsonHash`.
- **Calls:**
  - Public `MemoryChanges`, `PreviewMemory(arg)` and `CommitMemory(arg[, store])`.
  - Arguments and the store argument must be JSON-identical after the call.
- **Returns:**
  - The complete graph is compared, with undefined omissions.
  - On a returned v2 commit, the actual `return.state` is copied into the host store first.
  - Then the return is mutated: the copy must change (positive control), and neither the live Run/store nor the arguments may change.
- **After point:**
  - A full comparison against the referenced point.
  - The live store's ordered JSON must equal the point's store (member order).
  - Pure queries and rejections must leave the full live context unchanged.
- **Error mapping:**
  - RuleError and MemoryError map to `RuleException.Code`.
  - NativeError must be a `MemoryBoundaryException` whose `JavaScriptName` equals the recorded name. A wrong class, code or outcome is a FAIL.
  - V8 vs C# messages are printed separately as `RunMemoryNativeOnly`, with no parity claim.
  - RuleError name/message/detail are not claimed.
- **NOT_COVERED (3):**
  - These are never counted as executed or passed.
  - Each must be the plan's conditional op, its fact must be naturally null, and its reason must be the exact source text.
  - At that point the reason is re-confirmed through pure actual C# queries, with a purity check:
    - legacy over-capacity: every party's total change cost ≤ capacity;
    - v2 over-capacity: no character's full-set preview returns `OVER_CAPACITY`;
    - defeat: fewer than 2 changes.

### Corruption seams
These reuse `Rejects`/`Control`, with a separate `memoryProbes` counter; 26 probes and 8 controls.

- **Header:**
  - A nonexistent substituted generator path, rejected before it is read.
  - The generator slot pointed at another real producer with its true hash.
  - A stale hash and a missing slot.
  - Status, scope or contract claiming C# replay.
  - `plannedOps` set to 85.
  - An invalid Node runtime value. Another valid Node release is accepted as a control.
- **Lists:** the last base dropped, a scenario dropped, scenarios reordered.
- **Base row:** a prefix envelope omitted, the terminal point edited without rehashing, the candidate seed changed, the facts party reordered.
- **Scenario row:**
  - A before reference re-pointed.
  - A NativeError reclassified as a RuleError.
  - A live store laundered into a literal of the expected committed store.
  - A returned commit relabelled as rejected.
  - An unconditional op laundered into NOT_COVERED.
  - A NOT_COVERED reason rewritten.
- **Coverage:** `returned` set to 27.
- **Self-consistent forgeries** (hashes recomputed; structure passes, actual execution must diverge exactly at the forged op):
  - the fresh legacy `memoryChanges` return;
  - the terminal v2 `preview-empty` return;
  - the committed legacy point (refs rehashed).

### Summary lines (separate from progression counters)
- `RunMemoryChecks` (the slice of the shared aggregate), `RunMemoryFixture`, `RunMemoryCore` (matched x/85 of 88; returned/26, rejected/59, native/3; NOT_COVERED 3 with confirmations x/3), `RunMemoryOpenings` (2 fresh, 13 terminal, prefix command count).
- `RunMemoryObserved` per-method C# outcomes, plus `RunMemoryNotCovered`, `RunMemoryNativeOnly`, `RunMemoryStops`, `RunMemoryGuards` and `RunMemoryScope`.
- Every Check enters the shared failures, and so the exit status.

## Known risks for Root's build/run (unverified)
- The file uses a C# 12 tuple type alias, `using MemoryOpPlan = (string Label, …);`. net10 defaults to C# 14, so it should compile; it is the first thing to check if the alias is rejected.
- The `live-patch` and ordered-store checks assume the store member order matches JS. I read the order in `memory_v2.cjs:95` and `rules-core.js:2075-2079` and it matches C#'s `MemoryMigrationResult`/`CommitMemoryV2`. If an ordered-store check fails anyway, it is a real persisted-text difference, not something to loosen.
- I don't know of any missing link or compile piece.

## Limits
- This is a JS_ORACLE_ONLY oracle, executed by the C# Core host only.
- Out of scope:
  - account storage, disk and server;
  - Unity Play and Android device acceptance for this consumer;
  - RuleError detail/message parity.
- The four bases are the producer's natural selection, and no role data was invented.
