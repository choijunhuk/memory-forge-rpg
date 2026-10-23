# Author report: P1-RUN-MEMORY-BOUNDARY-ORACLE

- Author: Claude Code, Opus 5.5 (`claude-opus-5-5`), effort requested xhigh. Root records the session_id and invocation metadata.
- Scope: §8.9–8.14 / §16.2 / §17.5 / §20.9. This adds the public Run memory boundary oracle. It changes no economy, capacity, price or account policy. It does no disk, server or account storage.
- Status: **NOT_RUN**. This session had no execution tool, so no Node run, no export, no `--check` and no C# step happened. Nothing here claims PASS.

## Files (only the three allowed)
| File | Content |
|---|---|
| `tools/gen_run_memory_vectors.mjs` | Generator. Also provides `generateCases`, `checkFixture`, `exportVectors`, `fixturePath` and a CLI with `--check` and `--help`. |
| `engine/tests/run_memory_export.test.mjs` | 6 Node tests (see below). |
| `reports/run_memory_queries_20261016/author_report.md` | This report. |

The artifact goes to `engine/tests/vectors/run_memory_vectors.json.gz`. Root generates it with `node tools/gen_run_memory_vectors.mjs`. The write is atomic: a temp file, then rename, then a strict `decodeVectors` read-back.

No Core, JS engine, memory module, data, existing fixture or existing test was edited. The reused helpers were only imported: `generateScenario`, `stateHash` and `decodeVectors`.

## Real execution model
- **Bases (4).** Each base is a fixed gen_traces input. Its recorded commands, except its own final `commitMemory`, are applied from a fresh `MF.Run.create(setup)` through public Run methods or `run.battleCmd`.
  - Every step is checked against the trace outcome, the errorCode and the canonical stateHash.
  - A v2 base starts from the actual `MemoryV2.migrate(data, {}, {})` store, the same way gen_traces does.
  - Nothing is restored from snapshots.
- **Base inputs:**
  - `legacy-defeat` and `v2-defeat`: seed 1102, full.
  - `legacy-clear` and `v2-clear`: an ordered scan of `[1000 short, 1000 full, 1001 full, 1002 full]`. The scan stops at the first VICTORY save that also reaches OVER_CAPACITY naturally. If none does, it takes the first VICTORY save with a committable selection. For legacy that means at least one in-capacity kept change; for v2 it means a jointly previewable pair of change IDs.
  - The whole scan is recorded in `bases[].selection` and is checked structurally.
- **Scenarios.**
  - **Counts:** 15 scenario chains and 88 planned public calls: 25 legacy and 63 v2.
  - **Own Run:** each scenario opens its own Run, at `created` (fresh Create) or `terminal` (fresh Create plus the prefix). That opening state is checked to be JSON-identical to the base point.
  - **Recording:** every call records its args, the store spec plus `jsonHash`, `expect`, outcome, result or error, and `sourceAliases`. It also records before/after references into full `{run, memory}` points, including log, RNG, `stateHash`, `jsonHash` and `undefinedPaths`.
- **Store.**
  - **Live store:** `currentStoreMode: 'live'` means every returned v2 commit replaces the live store with that call's actual `result.state`.
  - **`store.mode`:**
    - `live`: passes that same object.
    - `live-patch`: removes the listed top-level members from a copy, then sets the listed ones. It is a declared controlled input.
    - `literal`: also a declared controlled input.
  - **What the C# consumer must do:** use its own Core-produced live store and compare the argument against `jsonHash`. It must never inject the recorded JS store.
- **Errors:**
  - `RuleError`: the normal rule rejection.
  - `MemoryError`: a memory_v2 `fail(code)`, which is a plain Error with a code. C# raises it as `RuleException` with the same code.
  - `NativeError`: a TypeError or SyntaxError at a declared boundary, recorded with its V8 name and message. C# raises it as `MemoryBoundaryException` and must not turn it into a RuleError.
  - Anything else, or any outcome other than the declared one, aborts generation and keeps the earlier output.
- **Checks on every op:**
  - Arguments, including the store, are JSON-unchanged after the call.
  - Queries and rejections leave the full point unchanged.
  - A returned commit ends in DONE.
  - `sourceAliases` records JS identity sharing honestly. A legacy commit returns `run.savedMemory` itself. Mutating a returned object is left to the C# consumer as a probe.

## Covered roles (labels in the fixture)
- **Legacy:**
  - A fresh Run's `memoryChanges` is empty. Preview gives WRONG_PHASE because of the policy. Commit gives WRONG_PHASE before any BAD_CHOICE on selection shape.
  - On the terminal save, each BAD_CHOICE detail is recorded: null, array, string, a non-party character, non-array indices, duplicate indices, a fraction, a string index, -1, and an index past the end.
  - OVER_CAPACITY, if reached naturally, then a valid base-kit rebuild, then DONE, then WRONG_PHASE again, then a `memoryChanges` query after DONE.
  - `{constructor:[0]}` gives a native TypeError. `{constructor:[]}` commits plain base kits.
  - On the real defeat (seed 1102): DEFEAT_KEEP_ONE, then a commit keeping at most one change.
- **v2:**
  - Fresh-Run order: INVALID_SELECTION, then WRONG_PHASE.
  - Terminal-save previews: empty, one id, a reversed pair, INVALID_SELECTION as a MemoryError (duplicate, unknown, non-string, non-array), and OVER_CAPACITY returned as `error` when reached.
  - Request shape is checked before runId, and runId before the store.
  - Store errors in order: INVALID_SNAPSHOT as a RuleError, then INVALID_SNAPSHOT as a MemoryError when memory is missing, then IDENTITY_REQUIRED. Then a null receipt gives TypeError, a receipt without a response gives SyntaxError, and REQUEST_CONFLICT comes before the version check.
  - VERSION_CONFLICT for a stale request, a stale Run and a store that moved on. It also fires for stored rows in a different order, or members in a different order, even when the content matches.
  - After DONE: own-receipt replays repeat the response with no new version, history or receipt. This holds with the selection reordered (same sorted body) and with an extra request member. A changed selection or version gives REQUEST_CONFLICT before the phase check. A new request ID gives WRONG_PHASE.
  - Non-party memory and M0 metadata are kept. Member positions are checked.
  - An own `"__proto__"` receipt works.
  - A controlled receipt carrying max-safe 9007199254740991 shows the body as JSON.stringify text and replays.
  - On the real v2 defeat: DEFEAT_KEEP_ONE, a request ID that was rejected stays reusable, and replay works.
- **NOT_COVERED:** an op becomes NOT_COVERED with a reason if its role can't be reached naturally. That applies to OVER_CAPACITY and to a second change on a defeat. The full list is in `coverage.notCovered`. Nothing is forced.

## Tests (6, all NOT_RUN)
1. The bases replay from the real gen_traces prefix. Covers provenance, denominators (4/15/88) and the terminal-save role.
2. A consumer-style replay of every scenario, using the actual live store.
3. Legacy semantics anchors: order, details, the native prototype key, and aliasing.
4. v2 semantics anchors: order, replay and conflicts, metadata, `__proto__`, max-safe, defeat.
5. Rejection of 15 structural tampers, plus a consistent forgery that only fresh generation catches.
6. Deterministic gzip, damaged containers, and 4 producer faults that must abort and keep the previous output: an unexpected Error, a wrong RuleError, caller-argument mutation, and an impure preview. Then the checked-in artifact must match fresh generation.

## Unverified risks for Root
- I did not run gen_traces, so I couldn't observe which clear candidate gets picked or whether OVER_CAPACITY is reached at all. If no listed candidate meets a role, the base is recorded NOT_COVERED, and test 1 fails on purpose.
- The native messages are V8 text: `Cannot read properties of null (reading 'body')`, `"undefined" is not valid JSON`, and `Cannot read properties of undefined (reading 'length')`. These assume Node ≥ 20. The fixture pins `nativeDiagnosticsRuntime.node`.
- Runtime: each check regenerates everything from scratch, including the candidate scan. The test file runs about 5 full generations and 4 base scans that abort.
- Next steps (Root): run the export, `--check` and `node --test engine/tests/run_memory_export.test.mjs`, then the C# consumer, an independent source read, and Unity. Record each separately as PASS, FAIL or NOT_RUN.
