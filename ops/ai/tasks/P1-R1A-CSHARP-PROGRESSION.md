# R1a real C# expedition node/battle/reward execution

Official Claude Code Opus only, included subscription only, Max for this core combat/progression implementation. No API/credits/fallback/agents/Bash/MCP. You are not alone. A worktree owns ONLY core/MemoryForge.Core/Run.cs, RunState.cs, Run.Progression.cs, Run.Rewards.cs, Battle.RunLink.cs and reports/run_progression_core_20261016/author_report.md. Preserve all other accepted code. Root owns independent host checks/Program wiring, actual .NET/JS/Editor execution, integration and opposite-author review. Do not run or claim unexecuted PASS. Root is the only Editor writer.

## Requirements, game role and acceptance

Original v0.8.1§7.1–7.7/§8/§16.2/§20.9 P1-1. Role: real Run.Create -> enter -> Battle begin/player commands -> afterBattle -> rewards -> next map/archive/terminal save pending, preserving original values/RNG/keys/log/error order. Completion is actual Core execution matched to JS full-state evidence and source trace prefixes, not snapshots restored into Core. This is the next dependency for a visible Unity exploration loop. No design shrink, no data/rates/prices/economy/asset-policy change. No whole Run/server/device acceptance claim until all commands actually replay.

Current source engine dae99192bfa137762c1c863efda8207b3851e3a6e7ecf2ff029bed3585a33ab4 is committed/pushed8d17999 with both GitHubCIgreen. It fixes four demonstrated source bugs; preserve those corrected behaviours, not older boundary-doc advice to copy Q1/Q2/Q3 defects. Source/data/M0/Core copies are current. Input data remains6a09e6dd2d81055738c84951ea5b5ae50c25fd8c9732c8443e81ec1498625688.

Root actually generated the five-case JS oracle:2684attempts/1370committed/1314rejected/1074purequeries,29,905,373uncompressed bytes. Full states/returns/log/RNG/undefined paths and strict regeneration compare PASS. Source seeds1000short/fulllegacy,1102fulllegacy/v2(begin death),2654fulllegacy(two earned bonuses). After fixing a mistaken cross-seed assertion, targeted5tests PASS; cross-runtime checks are being completed. Root independent host will replay the supported command prefixes and explicitly stop before still-absent services/commit methods; no product stubs and no full replay inflation.

## Reuse and write boundary

- RunData.Memory.Battle already contains the FULL accepted BattleData. Reuse it and the existing MemoryRulesData/Stats/RunValues/MemoryJson/Rng helpers. No new data layer, JSON dependency, DI framework, duplicate combat, utility package or public raw-state restore.
- Preserve Run.Create(RunData,RunSetup) and the accepted R0 factory/query graphs/field absence. Store the RunData and internal live Battle on Run; make Run partial with private constructor ownership. Existing public State remains a deep detached copy. Reuse RunState.Pending/Result graph paths, existing metadata fields and helpers before adding DTO hierarchies.
- A new Battle.RunLink.cs partial may provide minimal internal live state/terminal status/key binding required by Run. Do not edit existing Battle files under this scope or expose a mutable Battle publicly. Source assigns deck keys to the first deck-count card-order entries BEFORE begin; extra token cards keep no run key. Preserve keys through Battle.Command cloning and growth persistence. If an existing private copy routine prevents this, report the exact blocker instead of silently losing keys or widening scope.
- Add only state fields actually needed (battleType, v2 memoryAfter and their detached-copy/JSON presence semantics); do not add null fields to R0-created states. Runtime graph key/array order, own-undefined handling and metadata must remain faithful. M0 and existing Battle/Memory/R0 source/tests are accepted and stay intact.

## Core public commands for this unit

Use these clear typed APIs so Root can write the independent consumer:
- `void Enter(string nodeId)`
- `BattleResponse BattleCommand(string kind, BattleArgument? args = null)` — source Run.battleCmd convenience semantics: construct the existing BattleRequest using the CURRENT internal battle V and `r{V}-{kind}`, delegate to Battle.Command, update Run's state pointer, finalize once if over. NO_BATTLE ordering stays source-correct. Do not invent Run receipts.
- `Dictionary<string, object?>? BattlePublicView()` — existing Battle.PublicView return, or null with no active battle; must be detached/pure, no mutable battle escape.
- `void Evolve(string key,string branchId)`, `TakeCard(string id)`, `SkipCard()`, `TakeRelic(string id)`, `TakePart()`, `ApplyFoundPart(string partId,string key)`, `Proceed()`, `Archive(string choice)`.

Internal helpers must fully implement the corresponding original flow, not return fixture rewards or leave ended battles in BATTLE:
- Enter validation uses current Available/NodeById. Initialize all authored node pendings in source order, including real makeShop inventory/RNG and real event pick. Rest/forge/shop/event SERVICE COMMANDS are deferred, but entering them is a genuine supported step, not a stub.
- StartBattle builds actors from Run-derived party fields (not untrusted setup combat overrides), deck specs in order, source seed draw THEN encounter choice, original fixed-boss truthiness, difficulty/relics/flags. Event bonus still reaches first turn, but Battle's copied nextApBonus is0 and Run consumes once.
- Source corrected ordering is original node-entry log, then existing-style finalizer if begin already ended the battle. Do not move the log earlier or rerun begin. Defeat HP0 -> result/pending save, capture v2 memoryAfter, no rewards/stats/extra Run RNG, clear live/state Battle once.
- AfterBattle merges persistent flags, persists key-bound growth, exact coin RNG/base/bonuses, card/relic/part choices and offered balancing, evolution offers and stat changes. Reuse the existing terminal Battle branch for actual player-command victory/defeat as well.
- Reward actions/proceed/archive/captureMemory must preserve exact original check order, optional/null/absent fields, HP rounding, zone/map RNG, IDs, prices and flags. Keep existing Q4/Q5 phase flexibility/unknown archive choice behaviour where representable; do not silently add a validation policy. Current invalid authored setup can partially mutate pos/floor/RNG, so do not claim a new general transactional Run.
- v2 capture can reuse existing DeckMemorySnapshot/M0 primitives. Legacy has no memoryAfter. Only compute terminal pending/snapshots here; actual legacy/v2 commitMemory, memoryChanges/store history/receipts/restore are subsequent units. No automatic permanent save.

No Rest/ForgeApply/ForgeRemove/Buy/EventChoose/Assign/CommitMemory stubs. Those actual methods will be added in R1b/R1c/R1d; Root's test adapter stops before an absent method and reports prefix counts separately. Do not call the full1000C#Run corpus complete.

## Validation contract and result

Read relevant full source sections and the existing R1 boundary addendum, not unrelated whole-roadmap files. Source typed definitions and owned helpers are already copied; runtime aliases/argument types are in BattleState.cs and RunDefinitions.cs. BattleRequest is an immutable constructor type; BattleArgument is the existing structured argument union. RuleException currently exposes CODE ONLY: preserve the accepted semantic error codes/order and don't invent JS native-error equivalence or edit the exception class. Root records diagnostic message/detail coverage separately.

Return the exact APIs/state-copy/bridge decisions and which source methods are implemented, with explicit NOT_RUN. Root will compile, independently replay from real Create through supported commands against full graphs/returns/queries, retain all old R0215677 and Battle/Memory checks, add mismatch/ownership tests, run actual Unity, and fix/review before integration. If a blocker requires a file outside ownership, report it precisely and complete independent parts; do not introduce fake success or delete existing features to compile.
