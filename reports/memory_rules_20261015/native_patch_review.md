# M0 native patch source review — second pass

Date: 2026-10-15. **Verdict: APPROVE the reviewed corrections, SOURCE ONLY.** Both initial P2 findings are closed; no additional actionable source defect was found in the diff and affected paths. The isolated JS own-property correction also satisfies the existing preservation requirement. This does not approve full repository/Run/persistence/account migration, official Opus or Unity acceptance.

## Exact reviewed bytes

Workspace: `/Users/choi/.codex/worktrees/csharp-memory-v2/Memory_forge`. Actual bytes were compared with all four preserved originals and their `ops/ai/local/m0-native-review-before/manifest.json`; preserved originals matched the initial review hashes.

| Source | Reviewed SHA-256 |
|---|---|
| `core/MemoryForge.Core/MemoryV2.cs` | `68d9b58dd81e731f1078d946b8a5e03f80a1aa565352552ff0c1d0dc1d8ed22d` |
| `core/MemoryForge.CoreChecks/MemoryV2Checks.cs` | `9ac8a48ac7ca53abe8d3751c6cde296651df095227aa3123482e45a956ea6f19` |
| `core/MemoryForge.CoreChecks/MemoryV2DataLoader.cs` — unchanged | `8dbd4094b5232ab2c2fc2ce6629a07f15989ec77a636a7a34e85954f9d896740` |
| `core/MemoryForge.CoreChecks/BattleDataLoader.cs` — unchanged | `16a7c2786033256bc745ff9488748cf86f25dd34baa533240887f47ce05c95d7` |
| WT `proposals/memory_v2.cjs` — one-line own-property fix | `6ee5aa939e3169d034c99418ceebdc864473fe5cc9b4464fccf7e80dcbec308d` |

Root/A1 comparison JS remains `825b671e4ac399b06f26517ea461c645734e1afa76cb4fc16503a648c735c508`; it was not overwritten. The initial `native_source_review.md` and RED evidence were not edited.

The same native session `01a13ca9-097b-7b40-9361-2adb430aec82` was checked through the exact session path referenced by `native_runtime_metadata.json`, filtering only turn_context fields. Latest observed context: `2026-10-15T01:40:39.364Z`, model `gpt-6.1-sol`, effort `max`. This is current-context execution evidence, not a prompt-only assertion or a statement about every historical turn.

## P2 corrections and impact

1. **JS property enumeration:** `MemoryV2.cs:127` now uses `MemoryJson.Entries` (`:399-401`). Canonical uint array-index names below `4294967295` sort first in ascending numeric order; canonical round-trip validation rejects `01`/`-0`/non-index forms. All ordinary string keys share the same sort key, so stable OrderBy retains their input enumeration order. No lexical cid sort was substituted for this behavior. ListChanges/Migrate retain their separate source-required UTF-16 sorts.

2. **Deferred baseline access:** `MemoryV2.cs:227-233` moves RequirePropertyTarget into the actual FindIndex predicate. An empty or previously drained pool does not access the next baseline entry and charges the original removal cost. A nonempty pool still accesses a null baseline and raises TypeError. Baseline matching, trait costs, ownership/identity validation and result copying are unchanged; this introduces no new cost or capacity rule.

The new native regression assertions are called by the normal `MemoryV2Checks.Run` at line30 after strict fixture validation; they are not confined to the separate forensic entry point. Nineteen assertions cover raw-JSON insertion order, index/non-index boundary names, unchanged input, initially empty and later drained pools, and the retained nonempty-pool error. The ordinary corpus count increases to1072; prior source provenance checks remain strict.

## Isolated JS own-property correction

The complete JS diff is the catch assignment at `memory_v2.cjs:113`: Object.defineProperty creates the same code/original record as an own enumerable, writable, configurable data property. Those attributes match ordinary assignment-created records. For `__proto__`, this avoids the inherited setter and retains the record required by policy instead of changing the quarantine object's prototype. No numbers, costs, capacity rules, clone expression, existing-record contents or input objects are changed.

A read-only probe executed the corrected module with `__proto__`, `constructor`, `toString` and ordinary unknown cids, including fractional opaque metadata. All four quarantine keys were own/enumerable, complete legacyBackup and input were unchanged, and descriptor flags were all true. Prototype checks used an ordinary object from the same module realm: the quarantine prototype and shared prototype accessor/property names were unchanged. An initial direct comparison to the REPL's Object.prototype was a cross-realm probe mismatch and was corrected; it was not a product defect. The original Root JS body remained frozen.

## Evidence read and remaining gates

The implementation owner's existing logs under WT `ops/ai/local/` were read:

| Log | Recorded result |
|---|---|
| `m0-native-red.txt` | 15/19 |
| `m0-native-key-order-fixed.txt` | 17/19 |
| `m0-native-green.txt` | 19/19 |
| `m0-rules-fixed-source-green.txt` | 5441/5441;1072 actual pure-memory oracle calls |
| `m0-global-pins-before-root-refresh.txt` | Existing global corpora correctly reject the changed memory source pin; global PASS is not established |

These are owner-run results, not executions by this reviewer or a fresh parent rerun. This pass inspected only the diff and affected paths, read the preserved evidence, verified hashes/runtime metadata and ran the local JS preservation probe. It did not repeat the initial full review, build/run C#, invoke another AI/CLI, delegate work, operate Unity, modify product files or access real account data. Only this new document was written. Fresh parent checks, truthful corpus regeneration/comparison after integration, official Opus and applicable Unity/full-repository acceptance remain required separate gates.
