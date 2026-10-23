# P1-CSHARP-RUN-CREATE-IMPLEMENTATION — R0

- Requirement: original v0.8.1 §5/7/8/16.2/20.9 and `P1-CSHARP-RUN-MEMORY-BOUNDARY.md`; current `Run.create/baseKit/defaultWeapon/nextCardKey/genMap/available/nodeById` behavior. Role: create the actual typed expedition state, derived party/deck/memory identities and deterministic map before connecting Battle/node actions in R1.
- Acceptance: actual JS factory/query oracle versus actual C# Create and pure queries, complete state/map/private RNG/field presence and hashes, every authored character/weapon/resonance/stat/memory setup and map branch represented, input/data unchanged, detached results, numeric/error boundaries. No expected-state restore or injected afterBattle; R0 does not claim actual expedition commands or saved-game restoration.
- Base: reuse the active `csharp-battle-opening` worktree for NEW R0 files only. Its accepted A1 files are already copied and frozen in Root and the separate official Opus review tree. Do not modify the A1 source, generators, corpora or checks. Parent will provide the reviewed M0 source/loader dependency before compiling R0.
- Preserve original design/data/current rules/capacity/economy and all model/subscription/private/image/Editor policies. Typed projections must retain every field needed by source Run behavior; full narrative/raw content remains in original files, not silently removed.

## Ownership

- Core owner: only NEW Core/RunDefinitions.cs, RunState.cs, Run.cs and CoreChecks/RunDataLoader.cs. First send concrete public API/DTO/dependency proposal (not a new speculative architecture) to Root and the oracle/Host owners, then implement within the approved contract.
- Independent oracle owner: only NEW tools/gen_run_create_vectors.mjs, engine/tests/run_create_vectors.test.mjs, engine/tests/vectors/run_create_vectors.json.gz. Schema mf-run-create-v1, source-pinned actual Run.create plus available/nodeById, complete state and pure query checks.
- Host owner: only NEW CoreChecks/RunCreateChecks.cs after API/schema agree. Root owns Program and any necessary tiny visibility change to existing Host parsing helpers; no duplicate JSON parsers.
- Root alone integrates, edits old shared helpers, runs final suites/Editor, calls official Opus and commits. No worker commits, CLI/other-AI/model/Unity use or recursive delegation.

## Required behavior

1. Full current setup/state DTO and exact absent/null/false shapes, both legacy and v2 creation including useMemory:false, memoryBaseline/Before/playBefore/startMemory/seqCard/runId/version and collision-safe keys. Input memory snapshots are legitimate factory inputs; expected outputs are never constructor/restore inputs.
2. Actual party Stats.Derive/StageScale/defaultWeapon behavior using existing Rng/Stats and authored metadata. Do not trust supplied derived power/HP where JS derives them. Keep current clamps, fallbacks, ownership, gear/skill/ult/res/asc and signed rounding distinctions. No new balance or account-ownership policy.
3. Preserve deck order and all key/id/owner/assigned/wielder/weapon/plus/mods/growth/from/gained/evolved fields, permanent versus battle-only metadata, base-key versus new-run-key distinction, duplicate physical copies and valid evolution lineage.
4. Map construction and every node/boss field/next ordering, short/full and explicit zones/bosses/stage/difficulty/seed. Match RNG evaluation order, including one-item choices. Pure available/nodeById does not change state/RNG or publish private state as a safe network view.
5. Current MemoryV2/capacity rules are dependencies; reuse the reviewed implementation. R0 must not fake v2 state or silently implement legacy-only. No enter/battleCmd/reward/afterBattle/commit no-ops; these remain unimplemented R1 scope until their actual closed paths are ported.
6. Preserve explicit C# numeric/JSON domain and atomic factory failure. Do not invent an arbitrary snapshot restore API or broaden malformed-input coercion claims beyond actual supported behavior. Record genuine original-source questions instead of changing data.

## Evidence and continuation

Run fresh focused tests/source/gzip/shape/provenance corruption checks, actual C# full graph comparison and all retained suites, opposite Opus Max review/fix/recheck, actual Unity runtime subset, and source/data preservation before integration. Separate factory/query counts from full Run commands (still0 until R1). After R0 continue the actual Run and memory-saving command pipeline; phase completion is not the session stop condition.
