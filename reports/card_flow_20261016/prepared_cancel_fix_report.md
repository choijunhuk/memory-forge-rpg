# P1-CARD-FLOW-CANCEL-TAP-FIX — author report

- Author: Claude Opus 5.5 (`claude-opus-5-5`), requested effort high, continuing the B author session. Codex collects the session_id from the CLI metadata.
- **Author execution: NOT_RUN.** I had no Bash, Unity or Editor access, so nothing was compiled or run. Root's actual RED (new tests against the old Main) and GREEN runs are the evidence.
- Edited files (allowed list only):
  - `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs`
  - `client/MemoryForgeUnity/Assets/Tests/PlayMode/PreparedIntentPlayModeTests.cs`
  - this report
- Unchanged: Core, the other tests, assets, settings, dependencies, legacy `EventText`, and the earlier source/acceptance reports and receipts (P3-3 raw bytes untouched).

## Requirement
- Source: original v0.8.1 §14.7 (safe input) and the latest reference request.
- Final review P2-1: Confirm and Cancel share one rect, (1360,38,158,52). Both redraw synchronously, so a repeated tap at the same point flips the action:
  - confirm → cancel
  - cancel → confirm again

## Production change (`PreparedControls` only): 2 rects swapped
| Button (name unchanged) | Before | After |
|---|---|---|
| `CancelPreparedIntent` | (1360, 38, 158, 52) | (388, 38, 148, 52) — the ordinary `CardInfoButton` rect |
| prepared `CardInfoButton` | (388, 38, 148, 52) | (1360, 38, 158, 52) — the ordinary `ConfirmCard` rect |

- Only these two button lines and the method comment changed. Captions, colors, button names, the waiting label and the card info's hand-only condition are unchanged.
- Draw order changed: Cancel is now drawn before the waiting label, and card info after it.
- Result for a repeated tap at the same point:
  - after Cancel, it reaches the ordinary pure card info at 388
  - after a confirm, it reaches the prepared pure card info at 1360

  Either way a read-only info sheet opens; nothing is silently cancelled or played again.
- No debounce or time-based suppression was added. Dispatch, locks, selection, normal confirm/cancel and the timing rules are untouched.

## Tests (`PreparedIntentPlayModeTests`)
Both new tests use the existing real Create, public commands, mirror, `PlayThenFind`, `Fingerprint` (AP, V, RNG, log, events) and whole-store helpers.
- **New helper `TapAt(Vector2)`:** a real top-ray click at a fixed screen point. It returns the owning Button, read before the click.
- **Common pattern:**
  - Both taps use the first button's actual center.
  - One frame is yielded before each tap so the layout is stable.
  - Ray ownership is asserted: the first tap must hit the intended button; the second must hit an active `CardInfoButton` with card info open.
  - The state assertion comes before the ownership assertion, so on the old Main the failure message names the actual regression.

1. **`DoubleTapCancelNeverPreparesOrPlaysAgain`**
   - Tap 1 cancels the waiting intent.
   - After tap 2: no intent, no picker, fingerprint and store unchanged, mirror unchanged.
   - Card info is closed, the test waits past the display's end plus 3 Updates, and asserts no dispatch.
   - **Old-Main RED:** tap 2 lands on the redrawn `ConfirmCard` and prepares again, so `HasPreparedIntent` is true.
2. **`DoubleTapConfirmKeepsOneUnsentIntent`**
   - Tap 1 prepares exactly (card, now, target).
   - The display is asserted still open before tap 2, so no dispatch can happen between the taps.
   - After tap 2: the same single frozen intent, no picks, no picker, no commit.
   - Card info is closed; the natural end plus one Update sends exactly one mirror-equal play. No duplicate follows over 5 frames.
   - **Old-Main RED:** tap 2 lands on the redrawn `CancelPreparedIntent` and cancels, so `HasPreparedIntent` is false.

- **P3-1:** in `LeavingTheAppOrDisablingDropsTheWaitingIntentWithoutUnseenExecution`, the declaration is split: `string committed, store;` and `string? runId`. Runtime behavior is identical and no assertion changed.
- **Existing tests:** held-Cancel, choice, stale, finisher, pace and the others are unchanged.
  - The held-Cancel test presses the cancel's own center, wherever it is.
  - Test 1's "no ConfirmCard/Mode while waiting" assertion still holds.

## Remaining limits
- The prepared card info now sits where Confirm was, so a repeated tap opens a full-frame info sheet that must be closed. This is acceptable per the task.
- Not verified: 48 dp touch targets, Android/device input, worst-case density and 130% text. P3-2 (broader prototype candidate predicates) remains a separate follow-up.

## Addendum — P1-CARD-FLOW-TAP-ASSERT-FIX (test observation timing only)
- Author: same Claude Opus 5.5 session, effort high. **Author execution: NOT_RUN.** Root's actual receipts are preserved as reported:
  - **Old Main:** 0 PASS / 2 FAIL at the intended semantic assertions. The second Cancel tap left `prepared=true`; the second Confirm tap left `prepared=false`.
  - **Two-rect Main:** both tests passed every intent, no-Core, full-state and store assertion. Both then failed only the post-tap observation `(second.name, second.IsActive(), CardInfoOpen)`, which read `(CardInfoButton, false, true)`.
- **Cause:** a test instrumentation error, not a production fault. `ShowHandCardInfo` redraws synchronously, just as Prepare and Cancel do, so the tapped owner is already retired when `TapAt` returns.
- **Repair (test only; Main unchanged):**
  - `TapAt` now asserts the hit owner is non-null and `IsActive()` **before** the click is dispatched. It returns a non-null `Button`.
  - The two post-tap checks now assert only `(second.name, CardInfoOpen) == ("CardInfoButton", true)`.
  - The first-tap `Is.SameAs(cancel/confirm)` checks are unchanged.
- **Nothing removed or relaxed:** every intent, mirror, fingerprint, store, timing and remaining-Update assertion stays as it was, and no skip or inconclusive was added.
- **Expected results:**
  - On the old Main, the second tap's owner (`ConfirmCard` / `CancelPreparedIntent`) is live when tapped, so the pre-click check passes. Both tests should still fail first at their original semantic state assertion.
  - On the repaired Main they should pass.
