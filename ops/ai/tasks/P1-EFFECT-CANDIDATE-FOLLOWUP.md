# Follow-up, NOT DISPATCHED: effect-specific UI candidates without changing Core fallback

Existing known UX boundary from S3 independent review, outside the current one-unsent-intent write scope. The prototype/shared UI candidate list filters zone/source/repeats/growth definition, but some Core effects have narrower predicates. Do not mark this fixed or redefine Core automatic targeting.

Primary current source anchors: core/MemoryForge.Core/Battle.Effects.cs and Battle.PlayerActions.cs TakePick.
- growPick: growth definition AND current Growth<3 (Effects:259).
- tune one: no existing identical Temp part AND rarity!=C (Effects:286).
- costdown one: current CostOf(uid,now)>0 (Effects:289).
- recall: discard AND rarity!=C (Effects:292).
- copyPick: existing IsGrowthInstance predicate (Effects:294).
- discard/exhaust: actual current zone; Core consumes a UID from its zone before later effects.

Potential next bounded implementation: provide enough existing-effect metadata for the displayed request so UI filtering shares actual intended validity; keep explicit Auto and Core TakePick's current skip/fallback/order/RNG semantics unchanged. Reuse current query/definitions/part validation rather than a generic engine or alternate RNG. Check dependent sequential-choice semantics carefully: earlier effects can change zones before later choices, so don't invent future state eligibility or blindly copy a frozen predicate. Produce real input/output witnesses for a displayed-but-Core-ineligible candidate before changing UI; no content/numeric changes, no new dependency. Independent opposite-provider review and actual Unity tests required. No implementation in this note.
