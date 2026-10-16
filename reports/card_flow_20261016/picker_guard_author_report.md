# P1-CARD-FLOW-PICKER-GUARD (+ FINISH) — author report

- Author: Claude Opus 5.5 (`claude-opus-5-5`). The original unit ran at xhigh and stopped at the 1800 s CLI time cap, not a subscription limit. This finish pass ran at high. The session_id is in Codex's collection metadata.
- **Author execution: NOT_RUN.** I had no Bash, Unity or Editor access. Every PASS below is **Root's** execution, cited from Root's message and its receipts. None of it is mine.
- This finish pass edited only the new test file and this report. Main, the two existing client test files, Core, the digest, scenes, resources and settings were read-only.

## 1. Requirement, role and done condition (§14.3 card choice, §14.7)
- **Role.** The card-pick sheet is a *local, unsent intent* for exactly one play. Nothing is spent until its final pick or its auto action sends that play.
- **Done condition:**
  - Render and `PickCard` share one active choice list.
  - An invalid, duplicate, out-of-zone, already-used or source-card input cannot append or commit.
  - A stale sheet context cannot submit.
  - Explicit cancel restores the ordinary selection.
  - The automatic remaining-picks rule stays Core's existing rule, and the Auto label stays honest.
  - While the sheet is open, every unrelated command and selection is blocked. Pick, auto and cancel remain available.
  - Recovery and the S1 turn barrier are preserved exactly.

## 2. Behavior (`MemoryForgeExpeditionClient.cs`, written in the original unit)

### Gate
- `Blocked()` is now `Barred()` (recovery, then the turn summary), followed by the sheet lock: `picker != null` → `PICKER_OPEN`.
- Recovery and summary codes still take precedence.
- A refusal only sets `LastError`. The view is not redrawn, the same as under the barrier.
- Every public command and selection, and the private `Do` boundary itself, is refused while a sheet is open. This includes `ConfirmCard`, `PlayCard` (with or without picks), `EndTurn`, `UseUltimate`, `Select*`, `StartPractice`, `Depart`, `Abandon` and memory commands.
- Display-only reads stay available: forecasts and card info.

### Ownership
- `PickCard`, `AutoPick` and `CancelPick` pass only `Barred()`.
- `SendPicks` sets `picker = null` **before** `PlayCard`. The lock therefore never refuses the sheet's own single play, whatever Core answers.
- A failed final play leaves no open sheet, as before. This keeps the existing drivers' fallthrough to ultimate/end working.

### Freshness
- `BattlePick` snapshots its opening context: the Run reference, `Position` (the battle node), the battle version `V` and `target`.
- `Fresh()` additionally requires the `BATTLE`/`PLAYER` phases and the source card still in hand.
- The check is rerun in four places:
  - `ConfirmCard`: a sheet that would not be fresh is not opened (Core's check code is shown instead).
  - `PickCard`: before any append.
  - `SendPicks`: covers both the final pick and auto.
  - `Render` and `ResumeView`: a stale sheet is never drawn.
- A stale `PickCard` or `AutoPick` triggers `RetirePicker`:
  - the sheet closes and `PICK_STALE` is shown with a notice;
  - there is no send, no Core fallback play, no refund (nothing was spent) and no retarget.
- `ResumeView` drops a stale sheet silently. This was the existing resume rule; it now uses the same predicate.

### Shared list
- `public static PickCandidates(b, source, request, picked, definitions)` follows the prototype's `pickerOptions`:
  - **hand:** without the source card;
  - **discard:** the discard pile;
  - **res:** the reservations;
  - **growth requests:** only definitions with `Growth == true`;
  - in all cases, never a card already picked.
- `PickSheet` draws exactly this list through `PickOptions`. `PickCard` accepts nothing outside it (`PICK_INVALID`, nothing appended).
- Read-only properties: `PickerPicks` and `PickerOptions`.

### Cancel
- Closes the sheet without sending.
- Keeps the selected card, mode and target.
- Clears its own `PICKER_OPEN`/`PICK_INVALID` refusal codes.

### Core fallback preserved
- Auto still sends exactly the picks made so far. Core's `TakePick` chooses the rest.
- The labels are unchanged: "남은 대상은 자동으로 고르고 사용" and "자동으로 고르고 사용하면 · {Core forecast}".
- The displayed list is still the prototype's list, which is broader than some of Core's validity predicates (for example tune excludes rarity C). In that case Core still falls back by its own rule, and the last-pick caption shows Core's forecast of exactly that request. This was not changed.

### Scope of changes
- No source, data, value, Core, AP/RNG/retarget/save or scene change.
- New local client codes only: `PICKER_OPEN`, `PICK_INVALID` and `PICK_STALE`, each with Korean `LocalErrors` text.

## 3. Existing-test change
`ExpeditionTurnFlowPlayModeTests.APickSheetPreparedBeforeAnEndDoesNotSurviveBeneathTheSummary` (S1).

- **Why it changed.** The old test assumed `EndTurn` succeeds while a sheet is open and retires it. Under the newly required lock, that call is now intentionally refused.
- **What it now asserts:**
  - The end is refused with `PICKER_OPEN`.
  - The sheet stays open and the Run is unchanged.
  - A real tap at the EndTurn point hits the sheet, not EndTurn.
  - The available path is an explicit cancel. The selection is kept and nothing is spent.
  - After cancel, the actual end commits and matches the mirror.
- **Preserved assertions:**
  - opening spends nothing;
  - `PickerOpen` is false after the committed end;
  - `HasTurnSummary`;
  - no `BattlePicks` beneath the summary;
  - after `Dismiss` the sheet is still closed.
- **Added assertions:**
  - the cancelled sheet's old buttons and its `PickCard`/`AutoPick` are inert under the summary (`TURN_SUMMARY_OPEN`) and after continue;
  - the sheet does not come back after continue.
- **Where the stale-cleanup proof went.** The direct proof that "a committed change retires a prepared sheet" moved to the new file, as the stale tests.
- **Other existing tests.** `ExpeditionClientPlayModeTests`, the turn-barrier list test and `FailureTests` needed no change:
  - the drivers already use `ConfirmCard` → `AutoPick`;
  - the barrier and recovery codes come first.

## 4. New tests (`ExpeditionPickerGuardPlayModeTests.cs`)

### Original content
The first seven tests come from the original unit, unchanged in this pass. Each reaches the reference kit's real `nia_tune` (one hand pick) through actual `Create` and public commands, with an independent mirror Run.

1. **Shared list:** the drawn buttons, `PickerOptions` and `PickCandidates` agree.
   - The following are refused with `PICK_INVALID`: the source card, out-of-zone cards (draw/discard/exhaust/reservations), enemy uids and unknown ids.
   - The full lock list and every selection are refused with `PICKER_OPEN`.
   - The private `Do` is refused, and covered real taps are refused.
   - Reads are allowed.
   - Every step is pure: Run, AP, RNG, log/events, store/receipts, request and version.
   - A real tap on a candidate equals exactly one mirror play (V+1).
   - Old buttons and ids stay inert.
2. **Stale sheet, tap:** after a change beneath the sheet, a real tap on a still-drawn candidate gives `PICK_STALE`. The sheet closes, nothing is sent, the target is not replaced and old input stays inert.
3. **Resume and stale auto:** resume and disable/enable keep a fresh sheet. After a change, auto gives `PICK_STALE` with no send.
4. **Stale resume:** resume drops a stale sheet without sending.
5. **Cancel, auto and recovery:**
   - cancel restores the selection;
   - the reopened Auto label is Core's forecast, and Auto is exactly one play;
   - after a private `Do` fault, recovery comes first for `PickCard`/`AutoPick`/`ConfirmCard`/`CancelPick`, and `DiscardFailedPractice` departs.
6. **No-pick card:** a card without picks plays at once, and the end is not locked.
7. **`[Test]` helper over real-card `Battle.Create` states:**
   - the growth filter offers exactly the deck's growth cards across two hands;
   - `copyPick` uses the same filter;
   - the source card is excluded;
   - `sion_forget`'s second discard pick cannot repeat the first;
   - zone exclusion holds;
   - `pull` offers exactly the reserved card.

### This pass
8. **TEST-LOCAL branch fixture**, explicitly labelled and not authored-game or source-oracle evidence.
   - After the real drive, `nia_tune.Effects` is replaced by `{op: discard, n: 2}`, an existing Core effect. This is done in the client's own parsed `RunData` (reflection read of `data`) and, separately, in the mirror's `RunData`. Both are restored in `finally`.
   - Public input then shows:
     - the first valid pick is accepted and stays unsent (pure; the list no longer offers it);
     - the duplicate first pick is refused with `PICK_INVALID`, not appended and pure, and is not drawn again;
     - a real tap on a second distinct pick sends exactly one play with `[first, second]`, equal to the mirror (V+1);
     - later picks and auto are inert.
   - Resources and source content are untouched.

### Seams (documented in the file header)
These are test-only reflection seams; none is a production hook:
1. A real Core command issued on the client's private live Run plus the mirror. This is the only way left to make a sheet go stale.
2. The private store read and the private `Do` call, as in the failure tests.
3. The private parsed content read, used only for the test-local fixture.

## 5. Evidence and limits
- **Root evidence (Root's own executions):**
  - the Unity code compiles with 0 errors and 0 warnings;
  - invalid uid c15: refused, sheet open, V1, full-state pure;
  - valid c7: exactly one Core command (V2); repeated picks are inert;
  - Core end beneath the sheet: `AutoPick` returns false with `PICK_STALE`, the sheet is retired and the whole Run is unchanged;
  - the adapted existing suite passed 119/119 with 0 skipped.
  - Receipts: `s3_invalid_picker_green`, `s3_valid_picker_mirror`, `s3_stale_picker_pure`, `s3_adapted_119_result.json`.
- **Not yet run:** the seven original new tests and the new test-local test are NOT_RUN by me, and Root has not yet reported them.
- **Assumptions to watch when Root runs them:**
  - the reached hand holds at least 2 other cards (for the fixture's two picks);
  - Battle/Run read card definitions by reference at query time, so swapping `Effects` after the drive takes effect in both Runs;
  - test 7's preconditions: 5 drawn and 5 left; the end discards the hand in order.
  - A failing precondition reports its cause; nothing is skipped.
- **Coverage limits:**
  - Duplicate and growth handling in the *actual client* is proven only by the test-local fixture (duplicate) and by the helper over real `Battle.Create` states (growth/res/discard). The original reference party cannot reach multi-pick or growth requests.
  - A stale context is reachable only through seam 1, because the public gate refuses every Run command under a sheet.
  - Recovery while a sheet is open is unreachable by design: quarantine needs `Do`, which the lock refuses, and quarantine clears the sheet.
  - This is not Android/device, server or account acceptance.
