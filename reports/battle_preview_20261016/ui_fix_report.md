# P1-FORECAST-VISIBILITY-FIX — author report (Claude, Opus 5.5 High)

This change applies only the three review findings on the expedition forecast view, plus one regression test. It does not touch Core,
data, fixtures, scenes, settings, layout or the battle practice. The subscription was used with no Bash/MCP/agents/images.

**Compile, PlayMode, Unity Editor and screenshots: all NOT_RUN by Claude.** Root first runs the new test against the old view to get an
actual RED, then runs it against the fixed view.

## Changes

1. **Forecast visibility** (`MemoryForgeExpeditionClient.cs`)
   - `SelectCard`, `SelectMode` and `SelectEnemy` now call `ClearIntentNotice()`. It sets `noticeUntil = 0`, `LastError = null` and
     `notice = ""` before the existing `Render()`.
   - A new card/mode/target intent therefore retires the previous command's toast (or error) at once. The action line immediately shows
     the current request's forecast or error.
   - Only view text changes, and no Run command is issued.
   - The 2.6 s timer itself, `ExpireNotice` in-place expiry, the held-press map regression and every command path are unchanged.
   - Toasts raised by commands, including the success/rejection of the confirm that follows, still show for their full 2.6 s.
2. **Reservation wording** (`ForecastText`)
   - Old: `" · 위력은 그때 상태로 정해져요"`. New: `" · 예약의 실제 해결 결과는 그때 확인해요"`.
   - The new wording only says that the query withholds the future outcome. It makes no claim about power capture or delay rules.
   - All three exact test expectations are updated. No reservation rule or value changed.
3. **Nullability** (NoticeExpiry test)
   - The last runtime assertion now reads `view!.GetComponentsInChildren<Text>().Any(t => t.text == departed)`.
   - It sits alongside Root's existing `noticeLabel!` and `AssertReadable(view!, …)`. No condition was removed.

## New regression test (14 tests total; all 13 earlier tests and assertions kept, apart from the reworded reservation labels)

`NextIntentShowsItsForecastImmediatelyWhileThePreviousToastWouldStillBeActive`:

1. **Setup**
   - Seed 1234, first battle.
   - One actual immediate play goes through `ConfirmCard`. The mirror applies the same command, and the fingerprints are equal.
2. **Positive control**
   - The success toast is non-empty and drawn on the action line.
3. **Next intent, no wait**
   - Without `AwaitNoticeExpiry`, real rays select the next legal no-pick card's target, then the card, then the mode `now`.
   - Elapsed time since the play is asserted < 2.6 s, so the old toast's timer would still be running.
4. **Checks at the read point**
   - `Notice` is empty and `LastError` is null.
   - `PlayForecast` equals the mirror's `BattleForecast`.
   - Exactly `ForecastLine(...)` is drawn and readable.
   - The old toast text is gone.
   - The full Run/RNG/log/events/AP fingerprint is unchanged by the new selection, for both the client and the mirror.
5. **Confirm**
   - A real-ray confirm is then exactly one actual `BattleCommand`, at version +1.

Against the old view this test fails on `Notice` being empty, and on the forecast label, which the toast hid.

## Remaining limits

- The earlier draw-card and pick-sheet tests still wait for the driver's last toast. That wait is now unnecessary but harmless; the new
  regression covers the immediate path.
- A rejection toast (for example a refused ult) is also retired by the next selection. Its code stays visible until the player changes
  intent, and the confirm button and the forecast line still show any current error.
- Visual check at 130% text size, Android/device, server and native physical input remain separate.

## Root validation

1. Old-view RED for the new test.
2. Compile the fixed view.
3. Full PlayMode (14) and EditMode.
4. Console check.
5. Screenshots of the immediate post-play forecast and the reservation wording.
6. Final independent review, then commit/push to PR#2.
