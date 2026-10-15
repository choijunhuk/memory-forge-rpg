# M0 independent native source review — initial frozen snapshot

Date: 2026-10-15. Lane: read-only native Codex source review; only this report was written.

**Verdict: CHANGES REQUESTED for the two P2 boundary-parity findings below.** No normal-flow permanent-memory loss was found in the reviewed code. This is a source verdict, not Run execution, real-account migration, persistence, server/device acceptance or official Opus approval.

## Identity and authority

Review workspace: `/Users/choi/.codex/worktrees/csharp-memory-v2/Memory_forge`. All supplied hashes matched at the beginning and at the end of inspection.

| Reviewed file | SHA-256 |
|---|---|
| `core/MemoryForge.Core/MemoryV2.cs` | `48715591702b28209491ab8e7557686f41329c89e46eca0cced5614f3346b4fa` |
| `core/MemoryForge.CoreChecks/MemoryV2DataLoader.cs` | `8dbd4094b5232ab2c2fc2ce6629a07f15989ec77a636a7a34e85954f9d896740` |
| `core/MemoryForge.CoreChecks/MemoryV2Checks.cs` | `45bb6e52878058c828815bc99ec52bc122a93a6f732ca60e1102d2544cf7adba` |
| `core/MemoryForge.CoreChecks/BattleDataLoader.cs` | `16a7c2786033256bc745ff9488748cf86f25dd34baa533240887f47ce05c95d7` |

The last file was byte-compared with Root: its only change is `Name(JsonProperty)` becoming internal at line294. The shared combat/snapshot numeric reader remains unchanged.

Pinned comparison source is **Root** `proposals/memory_v2.cjs`, SHA-256 `825b671e4ac399b06f26517ea461c645734e1afa76cb4fc16503a648c735c508`; the worktree JS was not substituted after the parent's independent source fix. Also read current `Run.capacityOf:1919-1928`, existing A0 typed definitions, the M0 implementation and Run/Memory boundary tasks, original §7/8/16.2/20.9 and approved SAVE-081-11/12/QA-081-13 decisions.

Native runtime evidence was independently read from `native_runtime_metadata.json` and its exact referenced local session file, filtering only `turn_context` fields. Session `01a13ca9-097b-7b40-9361-2adb430aec82` has latest context `2026-10-15T01:25:39.133Z`, model `gpt-6.1-sol`, effort `max`. This confirms the current review context, not every historical turn or a prompt-only declaration. No transcript bodies were output; no other model, AI CLI/API or delegated agent was invoked here.

## 1. P2 — inspect does not apply JS integer-index property ordering

Location: `MemoryV2.cs:127-134`; affected input reader: `MemoryV2DataLoader.cs:31-37`.

`Validate` directly enumerates the .NET dictionary in its insertion order. JS `inspect` uses `Object.entries(snap)` at `memory_v2.cjs:15`, which visits canonical integer-index property names before other strings. Thus a malformed numeric cid can change which error is returned when it follows a malformed known-character row in raw JSON.

Concrete raw text:

```json
{"sera":[null],"0":[]}
```

The existing Host Graph reader adds `sera` and then `0` in raw property order. The frozen Core visits `sera` first and throws `INVALID_CARD` at line134. The reviewer executed the pinned JS: `Object.keys` is `["0","sera"]`, so it throws **`INVALID_SNAPSHOT`** for unknown cid0 first. With `sera:[{}]`, the equivalent C# error is `IDENTITY_REQUIRED`, while JS still returns `INVALID_SNAPSHOT`.

Minimal repair: enumerate the validation object's keys using JS array-index ordering (canonical uint index keys before other keys, remaining strings in insertion order). Do not sort all cids here: that would change source precedence among ordinary strings. The explicit ordinal sorts used by ListChanges/Migrate serve a different source contract and should remain intact.

Regression: parse the raw literal above through the actual Host Graph reader, or directly add the two .NET dictionary entries in that order, then inspect. A JSON.stringify-generated fixture normalizes the integer-index order before serialization, so that fixture alone does not exercise this Host/Core insertion-order seam. Also keep a non-index unknown-cid case to prove the remaining insertion order is preserved.

Severity is boundary error parity; no successful save or data-loss path is claimed. The C# result is source-derived, not a C# execution by this child.

## 2. P2 — cost accesses a null baseline entry even when the candidate pool is empty

Location: `MemoryV2.cs:225-230`.

`RequirePropertyTarget(raw)` runs before `pool.FindIndex`. In JS `cost:62`, `sp.key`/`sp.id` is accessed only inside the findIndex predicate. When the pool is empty, that predicate is never evaluated, and the missing match simply charges removal.

Concrete call against unchanged authored data:

```text
cost(data, "sera", [], [null])
```

The reviewer executed the pinned JS and observed **1**, the existing removal cost. The frozen C# throws `MemoryBoundaryException("TypeError", ...)` before entering FindIndex. This is distinct from a nonempty pool, where the source really does access the null entry and throws TypeError.

Minimal repair: defer the null/property-target guard to the predicate/access point. Preserve the actual source evaluation order, rather than adding an eager baseline validation rule. Add both empty-pool/result1 and nonempty-pool/TypeError tests; neither test changes cost policy or trusts a malformed baseline for real account storage.

Severity is pure-method boundary parity. The C# outcome is determined from the frozen call order; this child did not execute C#.

## Reviewed behavior and preservation checks

- Inspected all nine methods: inspect, baseline, listChanges, complete-result cost, selected-order applyChanges, preview/commit apply, recover, migration and current capacityOf.
- Stable key uniqueness is global; owner/common assignment, valid evolution lineage, optional bool upgrade and permanent slot shapes are checked in source order within each row. Change IDs use strict UTF-8 encodeURIComponent-compatible escaping, including URIError for unpaired surrogates; cids used for diff/migration sort by UTF-16 ordinal order.
- Default baseline matches original card kind; explicit baseline matches instance key. Selected changes apply in requested order to a copy of before, then full result validity/cost and defeat0–1/capacity are checked. Preview retains its error field; commit rejects. Empty selection preserves the existing deck.
- Migration uses deterministic base/legacy keys, successful-cid global key ownership and whole-character quarantine. Complete original quarantine values and legacyBackup are copied; opaque finite-double metadata is preserved through a separate boundary copier. Core uses no JSON package or alternate Run executor.
- Snapshot/result/data projection paths were reviewed for detachment, including fractional/null/false metadata, change payloads, quarantine and backup getters. The copier rejects foreign CLR objects, nonfinite doubles, cycles, excess depth and multidimensional arrays. Current ascension/resonance bonuses and all authored costs remain unchanged; the free-capacity redesign is not applied.
- Read the 1,071-case fixture manifest and both JS/Host coverage/rejection logic. Four source identities, schema/scope/version, unique IDs/count, nine methods, character baselines/capacities, unchanged inputs/data and targeted tamper cases are checked. The two concrete gaps above are absent from the reviewed cases; method/case counts alone do not prove those boundaries.

## Known JS defect and verification limit

The pinned JS migration loses the own quarantine record for unknown cid `__proto__` by invoking the prototype setter. C#'s ordinal dictionary preserves that original record and its backup, as required by policy. This difference is **not a C# finding**, and this review does not propose reproducing it. The parent's `quarantine_property_probe.json` and `source_fix.json` were read; the latter records an isolated JS correction to hash `6ee5aa939e3169d034c99418ceebdc864473fe5cc9b4464fccf7e80dcbec308d`. This pass compared the instructed frozen Root source, not that later JS body or regenerated corpus.

The supplied 5,416/5,417 report is child/parent context, not a fresh result from this reviewer. This child ran only read-only file/hash inspection and pinned-JS boundary probes. It did not build/run C#, run repository suites, call another AI, operate Unity, mutate product code or touch real account data. Patched-source review, Root fresh execution, regenerated source-pinned corpus, official Opus and applicable Unity/integration checks remain separate gates.
