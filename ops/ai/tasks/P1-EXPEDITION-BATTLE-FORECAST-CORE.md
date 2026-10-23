# Actual battle forecast query for the expedition — Core and independent checks

Official Claude Code Opus5.5 **Max**, included subscription only. No API/Console/credits/extra/auto-reload/fallback/other models/Bash/MCP/agents/images. Separate B worktree. You are not alone: do not revert other work. Root is the only Unity Editor writer.

## Requirement, role and completion

Original v0.8.1 §14.3 combat information/confirmation, §6 card effects, §16.2 shared deterministic rules. The accepted expedition client currently shows exact effective card text/current costs but explicitly lacks damage/block/HP forecast; the existing Battle practice already has a deterministic visible-outcome preview. Restore that capability for the Run-owned private battle without exposing a mutable Battle or reconstructing an expected state. Original data/rules/numbers/probabilities/targets remain unchanged. Root has pushed accepted Core9986288 and UIbcf28e4 (actual87Play/3Edit, actualmacOS build/launch; physical native mouse still separate).

Completion for THIS slice: a pure typed forecast API on the existing Battle, a thin Run wrapper, a small original-JS-derived oracle generator + node test + C# consumer. Not the Unity view integration, which follows after independent review. Author compile/tests are NOT_RUN; Root executes them. Do not edit Program.cs: tell Root the one checker registration needed.

## Ownership — only these existing scoped placeholders/files may be edited

- core/MemoryForge.Core/Battle.Preview.cs (new partial, DTO may live here)
- core/MemoryForge.Core/Run.BattleQueries.cs (only add the forwarding forecast query)
- core/MemoryForge.CoreChecks/BattlePreviewChecks.cs (new independent checker)
- tools/gen_battle_preview_vectors.mjs (new producer; Root will execute to create the new fixture)
- engine/tests/battle_preview_vectors.test.mjs (new source/strict-freshness/purity checks)
- reports/battle_preview_20261016/author_report.md

All other source/data/design/fixtures/UI/scenes/settings readonly. Do NOT create a second rule engine, serialization/snapshot restore API, service/container abstraction or dependency. No gzip/binary writing through Edit: Root runs your generator. Existing core/checker sources and original practice view have been copied from accepted bcf28e4 to this older-HEAD worktree; use those current bytes. Do not reset the branch or assume its old Git HEAD is the source baseline.

## Existing seams and constraints

- Battle.cs has private constructor `(BattleData data, BattleState state)` and already uses `new Battle(data,state.Copy())` for atomic Command. State getter returns a copy. Reuse this internal clone capability INSIDE Battle; never expose a mutable clone/state/hidden deck through the new result.
- Run keeps `battle` private. Run.BattleQueries.cs has LiveBattle and detached/pure AllowedModes/CostOf/NeedsTarget/EffectiveCard/Check wrappers. New wrapper must not mutate Run state, logs, memory, versions or receipts; outside battle use the existing NO_BATTLE contract consistently.
- MemoryForgeBattleClient.Preview at lines111–141 is the accepted visible behavior to preserve: Check first; future modes display cost and resolution timing, no invented future damage; now modes hide draw/random outcomes; deterministic candidate uses the actual game rules; actual enemy HP loss/block/HP delta and defeat indication. Its HiddenOutcome recursively checks `Target==rand`, `Pick==random`, `Op==draw`, Then/Else. RNG/draw-pile changes caused by hooks also suppress numeric outcome. Do not reveal hidden card order or consume live RNG even through passives/relics.
- Prefer a small detached typed result over localized UI text, so the view can label it. Describe exact supported fields/error/mode/due/hidden/actual deltas. Do not add options unrelated to the current UI requirement. Validate through existing public queries/Command semantics; no duplicate cost/damage/condition formulas.
- A candidate may execute the real play in isolation. Live state/log/events/RNG/version/receipts/data/picks must stay byte-identical across any success/error/repeat preview; a subsequent real command must still execute exactly once and match the original source. Existing receipt validation is not weakened.

## Meaningful verification artifacts

Use a bounded set of real authored setups/histories/card actions (roughly8–12 cases, not a second173-card matrix). Cover exact deterministic damage/shield/HP/reflect-death, reservation costs/timing, invalid/phase/outside-battle behavior, draw/random/hidden-hook suppression and caller-result/input mutation isolation. Existing87991 player-action checks remain authoritative for broad combat mechanics. Make expected numbers/results independently from original JS public Create/commands, not the new C# method; record actual setup/history so the C# consumer starts fresh and replays actual commands. Keep source allowlists/hash pins, fixed denominators, strict fresh regeneration and no expected-state injection. Report any genuinely unreachable case as NOT_COVERED, never a silent skip or expected PASS.

Read the current seams before implementing, use the smallest complete solution and stop only with complete source/checker/producer/report within this slice. Root performs compiler, oracle, all relevant tests and independent diff review, then UI integration and actual Editor validation. No scope expansion into lobby/server/economy/art or native input migration.
