# Actual recruitment and shared UI — 2026-10-21

User explicitly chose actual recruitment linkage rather than an animation preview. User allows Codex and Claude approximately equal implementation. This is the execution plan; no prices, probability curves, owned-asset meanings or production rollout are changed.

## Existing sources and scope
- docs/design/12_gacha_economy.md §§12.1–12.9; server/openapi.yaml gacha contracts; existing Core Gacha, deterministic parity vectors; content/gacha.mjs.
- Core and pure reference retain identical seeded outputs. The authoritative server uses injected cryptographic bounded integers, never the deterministic seeded battle RNG.
- Extend existing authenticated, loopback-only Development server and atomic account envelope. This is actual durable recruitment on development accounts; production guest/Play Games auth, payments and commercial deployment remain outside this unit.
- Standard and beginner banners have unambiguous activation. Limited/weapon rotation requires an epoch; do not invent a live schedule.
- Launch reward in content/progression.mjs is 1600 free crystals +10 standard tickets. An explicit once-per-account launch-claim action can grant exactly this approved reward. Do not grant the additional beginner40 or nia/harin/rien without the specified prologue eligibility.
- Existing fixed practice parties are battle fixtures, not owned roster. Keep existing run replay inputs fixed; recruited ownership is displayed separately, never silently changing a run.

## Ownership and sequence
1. Codex: shared eight-icon atlas, Unity connection/recruitment/owned collection/reveal, actual device validation, independent server review.
2. Claude Opus: Core injected randomness/config loader and server wallet/pity/ownership/receipt/history/launch-claim/selector plumbing and tests in an isolated worktree. No UI/art/original edits. No recursive AI.
3. Agree public DTOs before Unity integration. Reuse current outbox persistence and account ownership identity; recruitment must have its own receipt parser rather than being parsed as a run receipt.
4. Code → targeted tests → opposite-AI review → fixes → verification → private PR2 integration. Respect model and subscription-only restrictions.

## Completion criteria and tests
- Real authenticated account snapshot/banners/rates and balance; unauthenticated/error/empty/insufficient states are honest.
- One/ten pulls debit tickets before crystals, free before paid; beginner retains existing rules and10-only cap. Core discrepancy allowing crystal fallback for beginner is documented and disclosed, not silently changed.
- CSPRNG results, debit, pity, duplicate rewards, immutable ledger/receipt and ownership persist in one transaction before animation.
- Stable requestId+canonical payload yields byte-identical committed reply on retry/restart/concurrency; changed payload/operation conflicts; no private roll/RNG/credential in public views.
- Skip/reduced motion/crash cannot change or lose results; interrupted requests stay pending across restart, credentials stay memory-only.
- Starter claim is once, data-driven, same atomic ledger; no fixture party-to-roster inference.
- Existing exact-Core-module store binding continues to refuse old incompatible stores. Use fresh isolated accounts for validation; preserve old stores, no silent migration/reset.
- Existing seeded gacha and battle vectors unchanged; file and PostgreSQL transaction suites, malformed input/rollback/isolation/idempotency, Unity pending-result/skip/owned parser tests; desktop capture, a fresh desktop development build and actual PC loopback pull/recovery evidence. Phone/device work is explicitly deferred by the user on2026-10-21.
- Show user-facing development-connection identity. Do not claim production/account-store rollout or payment acceptance.

## Risks
Strict Core MVID binding means Core changes require a separately reviewed existing-store migration in a future unit. This unit must fail closed and use new explicitly isolated development stores. R material result IDs and afterglow are defined; unspecified experience/credit quantities are not invented. Account-owned party editing is a separate progression boundary and is not implied by owned catalogue.

## Review corrections
- Recruitment mutations carry expectedRevision, validated after receipt lookup under the same transaction. Rule refusals cannot later execute after balance changes.
- Private CSPRNG tape and Core replay reconcile the ledger to wallet, pity, copies and duplicates; private rolls never enter public snapshots.
- User removed the phone. Continue computer/Unity/server testing; do not use ADB or claim new device acceptance.
- Claude client review found3P2 andP3: explain preserved outbox errors, beginner total-based guarantee/completion, full individual/consolidated rates, late-result navigation, stale/result-status notices and keyboard focus. Regression tests cover these before rereview.
