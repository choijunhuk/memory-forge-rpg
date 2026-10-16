# P1-RESERVED-FORECAST-CORE — author report (Claude, Opus 5.5 Max)

Requirement: original v0.8.1 §14.2 (estimate from a clone of the current state with the actual engine), §14.3 (next/after damage and break
shown before committing), §5.4–5.6 (actual resolution), §6 and §16.2.

The accepted v1 forecast gave reservations only their cost and due turn, which left a staged gap. This change restores the original HTML
preview's current-state reservation estimate, plus damage including enemy block and the count of newly broken enemies, in the existing
pure Core query. Play, reservation and resolution rules, data, the UI, fixtures, Program.cs and other Core files are untouched.

**Compile, tests, oracle generation, Unity and screenshots: all NOT_RUN by Claude.** Root generates the v2 fixture from the producer and runs
every check.

## Files (only the five allowed)

| File | Change |
|---|---|
| `core/MemoryForge.Core/Battle.Preview.cs` | `BattlePlayForecast` gains `EnemyDamage` and `NewlyBroken`; reservations get a current-state estimate (`ReservedEstimate`). |
| `core/MemoryForge.CoreChecks/BattlePreviewChecks.cs` | v2 schema/sources/fields/plan/counts; 11-field comparison; v2 structural witnesses; extended literals, tamper probes and the C#-only estimate-rejection probe. |
| `tools/gen_battle_preview_vectors.mjs` | Schema v2, HTML preview pinned and statement-checked, reserved estimate oracle, three appended cases. |
| `engine/tests/battle_preview_vectors.test.mjs` | v2 counts, literals, an independent second derivation, and the original HTML `preview()` executed against every visible empty-pick forecast. |
| this report | — |

## Core API and semantics

Fields:
- The 9 existing fields keep their names.
- New `EnemyDamage` = sum over enemies of max(0, (HP+block) before − (HP+block) after). This is exactly the HTML `dmg`.
- New `NewlyBroken` = the number of enemies whose `BrokenUntil` went from 0 to non-zero. This is exactly the HTML `brokeN`.
- `EnemyHpLoss` still means HP only.

Order inside `Battle.Forecast`:
1. `Check` runs first, as in v1.
2. The real play runs through `Command` on an isolated copy, as in v1. This supplies the actual cost, the due turn (from the reserve event) and the immediate (now) outcome.
3. **Next/after:** `ReservedEstimate(uid, mode, storedTarget)` follows the original HTML seam (`prototype/index.html` 3274–3292):
   - It works on a *fresh* `state.Copy()` of the unpaid current state.
   - It removes the card from the hand, sets phase `RESOLVING`, and calls the existing private `ResolveCard(uid, mode, target, empty picks)`.
   - The target is the one the real reservation stores, which is what StartTurn later passes.
   - It does not pay, advance the turn, do played/echo bookkeeping, run an enemy turn or capture power. The copy is discarded.
4. All outcome fields describe this hypothetical for reservations. It is never a promise of the future.

Hidden and error rules:
- **Hidden:** the effective definition has rand/random/draw (as before), or the RNG or draw pile changed. For now that means the real play; for next/after, the estimate. When hidden, all outcome fields stay at default, including the new ones and Defeat.
- **Errors:** Check codes and real-command rejections are unchanged. New: a RuleException raised while the estimate resolves (overflow is mapped to `BATTLE_NUMBER_OUT_OF_RANGE`) becomes `Error`, with all fields at default.

Purity:
- Live state, RNG, events, log, version, receipts and data are never written.
- Picks are copied and never retained.
- No mutable state escapes.

Deliberate difference from the HTML: the HTML UI passes `app.target || first alive`, while the estimate passes the stored reservation target.
- They are identical for targeted cards.
- For untargeted cards no authored effect reads the target: every `st` has a `tgt`, and every `same`/condition/`breakBonus`/`targetBurn` follows a targeted damage op.
- The Node test runs the real HTML `preview()` against these records, so this claim is checked rather than assumed.

The HTML can reveal draw/random outcomes; we keep the stronger nondisclosure rule.

## Producer / schema transition (v1 → v2)

- **Schema and scope:** schema becomes `mf-battle-preview-v2`, with a new scope string.
- **Fields:** 9 → 11; `enemyDamage` and `newlyBroken` are appended.
- **Sources:** `htmlPreview: prototype/index.html` is added before `generator`. Generation also requires five HTML preview statements verbatim.
- **Contract:** the texts are updated and a `transition` member is added. `reachability.csharpOnly` gains `estimateRejection`.
- **How expectations are derived:**
  - The precheck code comes from original JS `check()`.
  - Otherwise, an independent fresh replay plays the request for cost, due and the now outcome.
  - For next/after, another independent fresh replay is JSON-cloned and run through the HTML seam: splice the card out of the hand, set `RESOLVING`, then `resolveCard(uid, {reserved, target: stored reservation target, picks: []})`.
  - C# output is never used.
- **v1 corpus kept:** all 9 v1 cases, 70 queries, 40 commands and 85 receipt probes are kept in place. The reserved v1 expectations legitimately gain estimates, including:
  - `turn1 sera_round next` 12/12;
  - page-killing 12s in heal-reservation-slots;
  - `reflect reservation` 6 HP loss, −3, defeat;
  - `nested draw reserved` now hidden by its definition;
  - `third owner reserve` visible at rv10.
- **Appended after the expedition** (its index 8 is unchanged):
  - `reserved-estimates`: 13 forecasts, 6 commands. Covers distinct next/after values 22/30, block in an estimate (+7), a newly broken keeper (1) against an already broken keeper (Sera foresight × broken: rv6 → 9, rv22 → 34 → capped 18), cog block absorption (HP 6 / damage 12; now 2/8), a zero estimate (`sera_aim`), a reserved `AP_LOW`, and an immediate break (now `newlyBroken` 1, checked against the real play).
  - `reserved-hidden-definition-rng`: 4 forecasts, 2 commands. Covers a reserved draw definition, rand+draw definition (`definition+rng`), a void_shard exhaust hook (`rng` only), and a visible control 12.
  - `reserved-hidden-draw-hook`: 4 forecasts, 4 commands. Covers a detective_notebook marked kill that draws inside the estimate (`rng+draw`), and the kaya_crossfire marked branch (next 30 vs now 21).
- **New counts:**

| | Cases | Battle forecasts | Battle commands | Run | Total forecasts | Total commands | Receipt probes |
|---|---|---|---|---|---|---|---|
| v1 | 9 | 51 | 23 | 6 + 13 forecasts / 17 commands | 70 | 40 | 85 |
| v2 | 12 | 72 | 35 | unchanged | 91 | 52 | 131 |

## Consumer and Node checks

C# consumer:
- All 11 fields are compared, with live-state/receipt/data/picks purity and repeat-after-mutation for every query.
- When a real immediate play equals a forecast, its actual HP loss, damage, newly broken count, block, HP and defeat must equal that forecast. A real reservation is checked for cost and due only.
- Structural witnesses derived from the records must all be present (11 tags, e.g. reserved visible/zero/broken/defeat/absorbed/hidden-definition/hidden-hook/error/next-after-differ and now absorbed/broken). Damage must be ≥ HP loss, and rejected or hidden records keep their defaults.
- Six new tamper probes are added (28 total) and the literals are extended.
- C#-only: an estimate-rejection probe. A local `blk Int32.MaxValue` fault means the legal reservation pays and schedules but its estimate overflows. The forecast returns the code with defaults; after the fault is removed it estimates +11 block, and the real reservation pays AP1 and schedules due 2 without resolving.
- Root's Run-owned catalog purity check and the strict decoder controls are preserved.

Node test:
- Exact v2 counts, plus the v1 subset 70/40.
- All literals.
- A second independent derivation, which uses `filter` and `structuredClone` instead of the producer's `splice` and JSON.
- The original HTML `preview()` text executed as-is for every visible empty-pick forecast, both now and reserved, with live state checked unchanged.
- Freshness, source pins, tampers, strict decoder and atomic export are kept.

## Limits

- **Estimates, not promises:** the current-state estimate deliberately ignores everything between now and the resolution turn. For example, the reflect reservation estimates a defeat although the reflect resets before turn 3.
- **Draw-only reserved hook not authored:** a reserved forecast hidden only by a draw hook (non-empty draw pile, no reshuffle) has no deterministic authored ≤5-card setup here. The immediate draw-only hook is covered.
- **Already not covered:** `pick:"random"` remains NOT_COVERED.
- **C#-only:** estimate and late-command rejections are C#-only probes.
- **UI unchanged:** it still labels reservations without numbers until Root's separate UI unit. Unity, Android/device and server acceptance are not claimed; neither is full §14.3.

## Root commands

1. `node tools/gen_battle_preview_vectors.mjs`, then `node tools/gen_battle_preview_vectors.mjs --check`.
2. `node --test engine/tests/battle_preview_vectors.test.mjs`, then `npm test` (Node 22 and 25).
3. `dotnet build` of Core (netstandard2.1/C# 8), then `dotnet run --project core/MemoryForge.CoreChecks -- <root>`. Expect BattlePreview to report 91 forecasts / 52 commands / 131 receipt probes, and all other checkers unchanged.
4. Actual Unity PlayMode/EditMode. **Expected UI-test conflict (readonly for me, reported):** `ExpeditionClientPlayModeTests.ActionLineShowsCoresForecastWhileSelectingAndOnlyTheConfirmedPlayMatchesItOnce` asserts that a reservation forecast has `(Hidden, EnemyHpLoss, BlockDelta, HpDelta, Defeat) == (false, 0, 0, 0, false)`. With v2 Core, reservations carry current-state estimates and definition/hook hiding, so that assertion fails for opening-hand reservations.
   - The client's `ForecastText`/`ForecastOutcome` ignore reservation numbers (the mode check comes first), so every other existing label and test string is unchanged.
   - The UI unit must update that one assertion together with the new labels.
5. Independent diff review.
