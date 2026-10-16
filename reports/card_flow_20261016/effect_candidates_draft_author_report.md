# P1-CARD-FLOW-EFFECT-CANDIDATES — author report

- Author: Claude Opus 5.5 (`claude-opus-5-5`), requested effort xhigh, same author session. Codex collects the session_id from the CLI metadata.
- **Author execution: NOT_RUN.** I had no Bash, Unity or Editor access, so nothing was compiled or run. Root's targeted, full Unity, EditMode and native runs are the evidence.
- Edited files (allowed list only):
  - `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs`
  - `client/MemoryForgeUnity/Assets/Tests/PlayMode/EffectCandidatePlayModeTests.cs` (new)
  - `ExpeditionPickerGuardPlayModeTests.cs` (one expectation)
  - `PreparedIntentPlayModeTests.cs` (one witness predicate)
  - this report
- Core, content, resources, scenes, settings, dependencies and the legacy `EventText` are unchanged. No Core write was needed; one Core query gap is reported in §5.

## 1. Requirement, completion criteria and source reason
- **Requirement:** original v0.8.1 §14.3/§14.7 and the latest supplemental-reference request. An explicitly chosen effect target must be one the reached Core effect actually takes. Today an ineligible card can be offered, and Core silently replaces it with another card. This is the documented S3/Max-review P3 boundary; no new game rule is added.
- **Source reason** (Core, read-only):
  - `Battle.PlayerActions.cs:260-275` `TakePick`: manual picks are consumed **in order across all of a card's pick effects**. A pick that is outside the zone or fails the effect's predicate is discarded, and the next pick (or Core's fallback) is used instead. Nothing reports the replacement.
  - The predicates live in `Battle.Effects.cs`:

    | Effect | Line | Predicate |
    |---|---|---|
    | growPick | 257-260 | cap check before choosing, then `Growth == true && Growth < 3` |
    | tune(one) | 286 | `!Temp.Contains(part) && rarity != C` |
    | costdown(one) | 289 | `CostOf(u, "now") > 0` |
    | recall | 291-292 | full-hand skip before choosing, then `rarity != C` |
    | copyPick | 294 | `IsGrowthInstance`, `Battle.cs:282-286`, which includes an evolution whose `From` root is a growth card |
    | discard, exhaustPick | 233, 280 | any card |
    | pull | 245 | `PickReservation`: any reservation |
- **Completion criteria:**
  - The drawn sheet, `PickCard` acceptance and prepared-pick revalidation all use the same effect-eligible list.
  - Rejected explicit options spend nothing and never become a Core fallback.
  - Eligible manual picks and explicit Auto still give the independent original command result.
  - Every covered predicate has a current-state witness.
  - Core resolution, fallback order, AP, RNG, reservations, effect order and outputs are unchanged.

## 2. Effect-chain analysis (authored content, `data/game_data.json`)
Every authored pick card has exactly one pick effect:

| Card | Effects | Effects before the pick that could change its eligibility |
|---|---|---|
| nia_tune | tune(amp, one) | none |
| nia_overclock | costdown(one), tune(same) | none; the play's commit spends one FreeNext charge (§3) |
| nia_rebuild, com_recall | recall (+blk) | none |
| yuri_dew | growPick | none |
| wc_yuri | blk, growPick | blk cannot change growth or the cap |
| yuri_metamorph | copyPick | none |
| sion_erase, com_salvage | exhaustPick hand (+draw/ap) | none |
| sion_forget | exhaustPick discard ×2 (+blk) | the second pick only excludes the first, already handled by "not already picked" |
| wc_sion | dmg, exhaustPick hand | a kill can end the battle, so the effect is never reached and the pick is unconsumed; no substitution |
| sera_reload, com_sort | draw, discard | a hidden draw only adds cards; the predicate takes any hand card |
| sera_pull | pull | none |

- **Nested effects:** no `if` branch contains a pick effect, and `ChoiceRequests` reads only top-level effects (unchanged).
- **No future-eligibility promise:** in authored content no earlier effect changes whether a currently public card is eligible. The list is therefore never wrong for that reason, and it never claims eligibility for a card that is not yet public.
- **Misalignment:** because each card has one pick effect, a skipped effect (growth cap, full hand) cannot leak its pick into a later effect. The client still offers nothing for a skipped request, so a multi-effect fixture cannot misalign either.

## 3. Current state versus what the play's commit changes
The client evaluates each predicate on the current public battle snapshot, adjusted only for what `Play`'s commit (`Battle.PlayerActions.cs:124-142`) changes before resolution:

| Commit change | How the client handles it |
|---|---|
| The playing card leaves the hand | Already excluded |
| `FreeNext--` | costdown only, see below |
| `Played++`, echo, owner tracking | Matter to cost only through `costIf`, which no authored card has |
| Relic or power draws (`unfinished_score`, `echochamber`) | Hidden; see the remaining branches in §7 |

costdown under FreeNext:
- **FreeNext = 0:** `Run.BattleCostOf(uid, "now")` is exact. It already includes parts (`c_lite`), `CostMod`, `Free`, the definition cost and `costIf`.
- **FreeNext ≥ 2:** cards are still free after the commit, so nothing is eligible. The current cost (0) gives exactly that.
- **FreeNext = 1:** the commit spends the last charge. The current cost query returns 0 for every card, but at resolution the real costs apply. Excluding every card would wrongly exclude eligible ones.
  - Safe narrowed behavior: `PickBlocked` returns `PICK_COST_UNKNOWN`. The sheet offers no manual card and says why; explicit Auto still lets Core choose.
  - Authored witness: Rien's ultimate gives two free cards. Playing one card leaves one charge, then nia_overclock. That party is not the reference party, so the pure test reaches it through the real ultimate in `Battle.Create` (§6).

## 4. Implementation and API/metadata map (`MemoryForgeExpeditionClient.cs`)
- **`Choices(card, mode, content)`** (private iterator): one switch that yields each request together with its effect rule `(string Op, string? Part)`.
  - `ChoiceRequests` (public signature unchanged) selects the requests.
  - **New** `public static ChoiceRules(BattleCardDef, string mode)` selects the rules, aligned index for index.
  - This is a small metadata record, not a command framework.
- **`PickCandidates(b, source, request, picked, definitions, rule = null, costNow = null)`:** existing signature plus two optional parameters, so every old caller compiles unchanged.
  - **Without a rule:** the old zone list (hand without the source, discard or reservations; growth definitions if `GrowthOnly`; not already picked).
  - **With a rule:** the same zone and not-picked filter, then empty if `PickBlocked`, else filtered by `EffectEligible`. copyPick no longer uses the growth-definition filter, so evolved instances are no longer missed.
- **New** `public static string? PickBlocked(BattleState, string source, rule)` returns one of:
  - `PICK_GROWTH_CAP` when `GrowthCount >= GrowthCap`
  - `PICK_HAND_FULL` when the hand minus the source is at least `HandMax`
  - `PICK_COST_UNKNOWN` for costdown with `FreeNext == 1`

  These are also new `public const` strings.
- **New** `public static bool EffectEligible(BattleState, rule, uid, definitions, costNow)` copies Core's predicates from §1. Rarity, Growth and Evolutions are read from the definition by id, exactly as `data.CardIndex` is read in Core. Temp, Growth and From come from the public card state. Cost comes from Core's own query; there is no cost arithmetic.
- **One shared function for all three paths:**
  - `PickOptions` (used by the drawn sheet, `PickerOptions` and `PickCard` acceptance) passes `pick.Rules[pick.Index]` and `CostNow`, which is `run.BattleCostOf(uid, "now")` with a guard that treats a `RuleException` as "not above 0".
  - Prepared-intent `Unsendable` / `PicksHold` replays the frozen picks through the same function, and also compares the frozen `Rules` with the current ones.
  - `BattlePick` and `PreparedPlay` now carry `Rules`; `ConfirmCard` and `Prepare` fill them.
- **Empty sheet:** it says why (cap reached, hand full, cost unknown, or "no card this effect can take"). The label is 96 px tall over the empty list. No card is named.
- **Unchanged:** Core's `TakePick` fallback order, explicit Auto (it still sends the picks made so far, and Core chooses the rest), forecasts, B, S2 and S3 contracts, gates, timing and selection.

## 5. Core pure-query gap (not built; for Root)
`costdown(one)` with exactly one FreeNext charge cannot be decided from existing public queries.
- `Run.BattleCostOf` returns 0 while `FreeNext > 0`.
- `Battle`'s constructor is private, so the client cannot reach the post-commit cost.
- Proposed minimal Core scope: a pure, read-only `Run.BattlePickEligible(sourceUid, mode, requestIndex, candidateUid)`, or narrower, `Run.BattleCostAfterCommit(sourceUid, uid)`. On a detached copy it would apply only `Play`'s commit step and evaluate the reached effect's own `valid` predicate, with no effect executed. Until then the UI blocks manual picks in that one case and leaves explicit Auto to Core.
- **Caution:** any such query must not reveal hidden commit-time draws, for example when the result depends on a card drawn by `unfinished_score`.

## 6. Tests (`EffectCandidatePlayModeTests.cs`, NOT_RUN)
The file holds two classes.
- **`EffectCandidatePlayModeTests`** (partial; three UnityTests) uses only old public client APIs plus documented seams: the `data` and `store` reflection and the mirror. It compiles against the old Main on its own.
- **For the old-Main RED run, drop the `EffectCandidateCoreWitnessTests` class.** It is the middle contiguous block of the file and references the new API.

Integration regressions: seed 1234, real reference party and exported content, with the complete Run graph, log and battle log/events, plus the whole store compared against an independent Create mirror.
- The witness is driven to the first playable nia_tune that has at least two other hand definitions.
- **Fixture:** the source's only rarity-C cards are curses, which the reference party does not hold at that point. One explicitly **TEST-LOCAL fixture** therefore sets the first other hand card's definition rarity to C, in the client's own parsed content and separately in the mirror's, restored in finally. It is not authored-game or balance evidence.
- **Note on the task's example:** `com_slash` and `com_guard` are owner "common" but rarity B, so a "common card" is eligible and would not demonstrate the defect.

1. **`TuneSheetOffersAndAcceptsOnlyCardsItsCoreEffectTakes`** — **old-Main RED** at `PickerOptions == eligible`.
   - The drawn `Pick:` buttons equal the eligible list.
   - Every ineligible `PickCard` returns `PICK_INVALID`, nothing is appended, and the state stays pure.
   - A real tap on an eligible card produces the mirror-equal play, and Core tunes exactly that card and no other.
2. **`ExplicitAutoStillSendsCoresOwnChoiceAndNeverAHiddenCard`**
   - The list is filtered (RED on the old Main as well).
   - Auto is exactly one play with no picks, equal to the mirror, and Core's own choice is one of the offered cards.
3. **`APreparedTunePickUsesTheSameEligibleList`** — **old-Main RED**.
   - During a plain play's display the sheet offers the eligible list and refuses ineligible picks.
   - The final eligible pick becomes the unsent intent with that frozen pick. The dispatch sends exactly that play, and Core tunes it.

Pure Core witnesses (`EffectCandidateCoreWitnessTests`, NUnit `[Test]`):
- Every case uses public `Battle.Create`/`Command` over real exported content.
- Decks are drawn whole, so hand membership does not depend on the shuffle.
- Each compares the client function with Core's own resolution on a twin battle.
- Nothing is injected into any state.

| Test | What it checks |
|---|---|
| `ChoiceRulesNameTheCoreEffectOfEveryRequestInOrder` | All 14 authored pick cards map to their effect. Every content card has one rule per request, and none for `next`. Any newly exported pick card fails until it is covered. |
| `TuneTakesNeitherACurseNorACardAlreadyCarryingItsPart` | `eye_of_rift`'s real smudge (rarity C) is excluded; on a twin battle Core silently tunes another card for a smudge pick. A card tuned on turn 1 is excluded on turn 2 because Temp persists through the battle; a repeated amp pick makes Core tune a different card. |
| `CostdownOffersOnlyHandCardsWhoseActualCurrentCostIsAboveZero` | Core costs are nia_tune 0, sera_round with `c_lite` 0 (definition cost 1), sera_timed 2 (reserve-only but eligible, because Core uses the now-mode cost) and com_slash 1. The list is [timed, slash]. On a twin, Core replaces a c_lite pick with timed. With no cost function, nothing is offered. |
| `CostdownUnderFreeCardsIsExactWithTwoChargesAndBlockedWithOne` | After Rien's real ultimate (FreeNext 2) nothing is offered, and a twin shows Core takes nothing. After one more play (FreeNext 1) the result is `PICK_COST_UNKNOWN` with an empty list, and Core then **does** take com_slash (CostMod −1). That proves the gap in §5. |
| `RecallSkipsCursesWhileExhaustPickTakesAnyDiscardCard` | Discard is [smudge, com_sort, sera_round]; recall offers [com_sort, sera_round]; sion_forget's exhaustPick takes all three. On a twin, Core brings back sera_round for a smudge pick. |
| `GrowPickOffersGrowthCardsBelowThreeAndNothingAtTheBattleCap` | A cocoon at growth 3 and the evolved thorn are excluded; Core grows sprout for a cocoon pick. In a 7-card `quick_hands` battle, three dews reach the cap and the card still passes `EffectEligible`, but `PICK_GROWTH_CAP` empties the list and Core skips the growth. |
| `CopyPickOffersEvolvedGrowthInstancesAndItsFreeCopyLeavesCostdown` | yuri_sprout_thorn (from yuri_sprout) is offered, which the growth-definition list missed. Core copies thorn as asked, and the copy is free, so it fails costdown while the original passes. On a twin, a non-growth pick gets Core's own growth copy. |

Adapted tests (deliberate expectation changes only; no new API used, so both files still compile against the old Main):
- **`ExpeditionPickerGuardPlayModeTests.TheSheetAcceptsOnlyItsSharedCandidates…`:**
  - The zone helper still equals hand-without-source.
  - `PickerOptions` and the drawn buttons now equal that list filtered by an independent copy of tune's predicate. At the usual first nia_tune the two lists are identical.
  - Refused-id coverage adds any filtered-out hand card. Every other assertion is kept.
- **`PreparedIntentPlayModeTests.Tune` witness:** the other hand card must also be tune-eligible, because its `chosen` comes from the now-filtered sheet list.

## 7. Risks, out-of-scope callers and honest uncovered branches
- **Out-of-scope callers (not editable here):**
  - `ExpeditionClientPlayModeTests.PickSheetForecastsMatchTheAutoPromiseAndEachConcreteFinalCandidate` asserts a caption for **every** hand-minus-tune card and clicks `candidates[0]`.
  - `ExpeditionTurnFlowPlayModeTests` (stale-sheet test) takes the first drawn `Pick:`.
  - Both still pass unless their seed-1234 witness hand contains a curse (from `e_smudge`'s insert) or a card already carrying amp. If either fails, it needs the same eligible-list narrowing as PickerGuard.
- **Mandatory witnesses that may FAIL instead of skipping:**
  - the integration driver needs nia_tune with two other hand definitions (S3 actual runs saw four options at the first nia_tune)
  - the pure tests assume e_cog survives one sera_round (24 HP) and that whole-deck hands hold
- **Uncovered branches:**
  - `PICK_HAND_FULL` cannot be reached for an authored first-effect recall, because the source always frees a slot. It is defensive.
  - Hidden commit-time draws (`unfinished_score`, `echochamber`) could fill the hand or reshuffle the discard before recall or copyPick. The client cannot know without revealing hidden cards. Analysis shows these cases produce an unconsumed pick or no candidate, not a substitution; not tested.
  - `costIf` conditions that read commit-changed state (`Played`, hand) would make the current cost diverge. No authored card has `costIf`.
  - Cross-effect eligibility in non-authored multi-effect cards (for example, discard then recall of the same card) is not modeled. The "not already picked" rule still applies.
  - `pull` is unchanged: any reservation. A pick that would make Core refuse (`PULL_DESTINATION_FULL`) is refused on send, not substituted.
- **Not covered:** device input, the 130%-text layout of the 96 px empty-sheet note, and final art.
