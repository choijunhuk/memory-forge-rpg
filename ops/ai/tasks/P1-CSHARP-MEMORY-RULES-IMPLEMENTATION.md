# P1-CSHARP-MEMORY-RULES-IMPLEMENTATION — M0

- Requirement: original v0.8.1 §7/8/16.2/20.9 and approved `docs/decisions_v0.8.1.md`; current stable-identity behavior in `proposals/memory_v2.cjs:10-117` and `Run.capacityOf:1919-1928`. Exact Run/Host boundaries are in `P1-CSHARP-RUN-MEMORY-BOUNDARY.md`.
- Role: independently reusable C# memory validation, change selection, complete-result cost and preservation migration. This unit does not execute Run commands, persist data, authorize client-provided snapshots or migrate a real account.
- Base: committed `3d33c4cb2e0c7cee14ced2c67c33b8fb55cef267` in the separate managed `csharp-memory-v2` worktree. Ongoing Battle A1 files remain in their own worktree and are not dependencies.
- Keep original design/data/engine/HTML/art/PDF/manifests, all subscription/model/private policies, current capacity bonuses and original numbers. Do not apply the still-pending free-capacity redesign or change server/API contracts.

## Ownership and interface gate

1. One Codex GPT-6.1 Sol Max owner implements only new `core/MemoryForge.Core/MemoryV2.cs` (one additional new model file only if the concrete size warrants it), new `core/MemoryForge.CoreChecks/MemoryV2DataLoader.cs` and `MemoryV2Checks.cs`. Root alone wires Program, old helpers/tests and Unity.
2. Before product edits, report a concise public API/graph-preservation proposal and coordinate with the independent oracle owner. Prefer existing typed Battle definitions for authored card/part/character metadata, concrete memory cost/capacity input, result/change DTOs and ordinary ordered dictionaries/lists at the malformed-legacy trust boundary. No alternate JSON interpreter, generic operation framework or extra dependency.
3. Preserve field absence versus null/false/empty mods, unknown original metadata and complete quarantine/legacyBackup, including valid JSON fractional metadata in an invalid legacy row. Such opaque values are copied, never used in combat arithmetic or silently passed through integer-only CanonicalState. Host owns raw-byte backup and parsing. Do not widen shared BattleGraph/CanonicalState or weaken existing validators.
4. Independent Codex oracle owner writes only three NEW JS generator/test/gzip files. Scope `mf-memory-rules-v1`: method + inputs from actual authored data -> actual JS result/error and complete unchanged inputs. Parent agrees final schema/API before Host wiring. Pure Memory snapshot test inputs are legitimate boundary inputs, not evidence of actual Run execution.

## Complete M0 behavior

- `inspect`, `baseline`, `listChanges`, `cost`, `applyChanges`, `apply` (preview and commit), `recover`, `migrate` and current `capacityOf` all ported; no allowed-card subset or placeholder migration.
- Stable global key uniqueness and proper owner/common assignment; evolution lineage; permanent slot/rarity validation; all original error precedence.
- Exact source order: UTF-16 ordinal cid sort; before order remove/evolve/upgrade/c/e; after order add; selected change order controls application. Match encodeURIComponent including non-BMP, lone-surrogate error boundary and reserved characters.
- Cost of COMPLETE result: baseline identity matching when supplied, default base-kit identity behavior otherwise; removing and adding a same-kind distinct instance still costs2. New evolved card costs origin rarity + evolution + upgrade + both part slots.
- No selection preserves before; normal multiple changes and defeat0–1; preview returns error while commit rejects; complete capacity check; detached results and unchanged input/data on both success and rejection.
- Migration uses sorted cid order, deterministic base/legacy keys, preserves originals, rejects duplicate cross-character keys and quarantines the entire invalid/over-capacity character. Never truncate/discard old records or unknown metadata.
- Current capacity retains base plus existing ascension and applicable resonance capacity effects. Do not reinterpret the free-capacity goal as approved numerical changes.

## Independent acceptance and integration

- Source-pinned JS oracle, all10characters and authored eligible cards/evolution/parts, valid and malformed/duplicate/selection/capacity/migration/URI cases, independent literal costs, preserved full input/result shape, corruption/provenance rejection and deterministic regeneration.
- Core C#8/netstandard2.1/no external packages; actual .NET10 calls compare complete result and errors against the independent oracle. No JS bridge or fixture-to-state restore.
- Preserve existing primitive/canonical/opening/turn/query/player-action checks; final repository suites and selected actual Unity runtime coverage; opposite official Opus Max review after fresh checks; defects fixed and rechecked before integration.
- Separate counts/status for pure memory rules, Run generation, actual legacy/v2 replay, command-suffix restoration, persistence and server/device acceptance. M0 success does not make any later boundary complete.
