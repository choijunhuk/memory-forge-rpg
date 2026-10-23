# P1-CSHARP-PLAYER-EXECUTION-IMPLEMENTATION — A1

- Requirement: original v0.8.1 §5.4–6/5.10–18, §6, §16.2/20.9; exact source/closure/acceptance is [PLAYER-ACTIONS-BOUNDARY](P1-CSHARP-PLAYER-ACTIONS-BOUNDARY.md). Keep all173 authored cards,378 base legal modes,39 ops,10 character ultimates and their produced lifecycle. No now-only/card allowlist acceptance.
- Role: turn the typed A0 card-choice boundary into actual atomic `play`/`ult` execution. Whole Run/memory replay remains a later requirement.
- Prerequisite: integrate the independently reviewed A0 source before enabling command dispatch. A1 oracle development is independent and may run while A0 review finishes. Record actual integrated base hash when assigned.
- Ownership: one Codex GPT-6.1 Sol Max Core owner for `Battle.cs`, `BattleState.cs` and the narrow existing `ReservationBook.cs` pure capacity reuse. Root handles Program/old assertion adaptations/Unity. Independent oracle owner handles only new player-action generator/test/gzip. Host owner handles only new player execution checks and explicitly delegated old typed-snapshot caller adaptations. No shared file edits without handoff.
- All code stays C#8/netstandard2.1 without external packages, JS bridge, expected-state restore or alternate engine. No game data/engine/HTML/source design edits, new costs, public release or model fallback. Official Opus Max independently reviews each meaningful completed execution slice after fresh checks.

## Required implementation

1. Keep existing receipt → stale → candidate shell and immutable ordered request identity. Add read-only argument access only for actual execution; preserve args truthiness/defaults and exact error precedence. Pure Check/precheck does not consume RNG or mutate state.
2. Implement the complete source play/precheck/check/cost/target/choice order; immediate/next/after, contract/AP/debt/slot/mark/collateral/restraint/pull/overload errors must match. Do not schedule a card as accepted if its due resolver is missing.
3. Use A0 effective definitions, parts and pure queries. One shared effect context/executor handles all39 ops and card/ult/opening differences. Reuse existing raw enemy damage, growth, heal, draw, kill, numeric and RNG helpers; replace partial helpers only after regression equivalence is demonstrated.
4. BattleState owns a single reservation store and shared resSeq for reservation order and loan IDs. Reuse existing pure capacity calculation from ReservationBook if appropriate; do not maintain two mutable queues or copy/synchronize them between commands.
5. Port typed reservation/debt/power/copy state, deep copies and exact graph presence. Initial state stays59keys; pendingCopies and echoCopy appear only at source-producing events and retain present-empty distinctions. Events preserve required nulls (play target, resolved reserved, ult card) and all false/zero values.
6. Implement start/end integration for debts, due ordering, recall/pull, copy delivery, powers/buffs, break/stun/recovery and silver needle. Remove only the guards for newly implemented producers; proposal-only phase/late actions remain explicitly unsupported.
7. Match damage/block/upgrade rounding, per-hit/target terminal checks, reflect card-only behavior, same-hit counter/break/kill/reward before defeat, post-terminal afterResolve/exhaust cleanup and the reserved void-shard final kill. Keep one end and correct unused-current/future-due card return order.
8. Implement actual ult context/gauge/extra/once/replace/R6 behavior without card AP/hand/owner/echo side effects. Preserve source firstDmg differences and HP0 ordinary-heal non-revival.

## Verification and limits

- Independent JS oracle uses actual Create/begin/play/ult/end requests with real prerequisite commands, full state/RNG/log/events/public view, receipts/rejections and source hashes. No state or data monkeypatching. Every positive denominator is mandatory; familiar errors are not a reason to skip a card/mode.
- Preserve all existing opening267, turns356/1740, A0 queries702/8340 and primitive/canonical/trace checks. Update prior unsupported-play assertions to the newly implemented source behavior, retaining their rejection/atomicity meaning.
- Cover all173/378 plus effect observability, growth/lineage/exhaust, parts/weapon/refinement/relic/resonance interactions, debt/reservation ordering, copied-card metadata, earned and explicit-input gauge, terminal and rollback cases. Full legacy Run snapshots remain distinct from actual C# Run command parity.
- Original base0 sources conflict (§6.3 versus §9 sion_forget). Preserve current JS/base5 and metadata; hold only a rule change and do not claim generic conformance. No unilateral retune or deletion.
- A0 query-only tests or a few playable cards are not A1 completion. A1 is complete only after its closed legal paths, opposite-AI review, actual Unity runtime checks and integration evidence. Continue to Run/memory afterward.
