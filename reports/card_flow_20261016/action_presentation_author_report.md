# P1-CARD-FLOW-ACTION-PRESENTATION — author report

- Author: Claude Opus 5.5 (`claude-opus-5-5`), requested effort high, fresh official CLI session in the reused C worktree. Codex collects the session_id from the CLI metadata.
- **Author execution: NOT_RUN.** I had no Bash, Unity, MCP or Editor access, so nothing was compiled or run. Every claim below is a source reading. Root's actual Unity run and visual check of the new strip and sheet are the acceptance evidence.
- Edited files (only the allowed ones):
  - `Assets/Scripts/MemoryForgeExpeditionClient.cs`
  - `Assets/Tests/PlayMode/ActionPresentationPlayModeTests.cs` (the empty placeholder, filled in)
  - `ExpeditionClientPlayModeTests.cs`, `ExpeditionTurnFlowPlayModeTests.cs`, `ExpeditionPickerGuardPlayModeTests.cs` (driver sequencing only)
  - this report
- `ExpeditionFailurePlayModeTests.cs` did not need a change: none of its tests makes a successful play.
- Read-only and unchanged: Core, `BattleTurnDigest.cs`, content, resources, scenes, settings and dependencies. The private legacy `EventText` mapper is kept as it was.

## 1. Requirement, role and done condition
- **Requirements:** §14.2 (reduce motion: no card-move or damage-number animation), §14.3 (action and reservation presentation; reservation resolution 0.3 s per card, skippable), §14.7 (safe input; selection and confirm are separate states).
- **Role:** show the actual committed play or ultimate events. While they are shown, allow only pure preparation of the next card, mode and target.
- **Done condition:**
  - The presentation is real and visible.
  - Normal, 2x, reduced and skip all give the same Run, logs, events, RNG and store.
  - No command depends on the display.
  - The next selection stays pure and its forecast stays visible.
  - The S1 turn barrier, S2 recovery and S3 picker gates are unchanged.
- **Not in this unit:** no confirmed-intent queue (that is the next unit), and no server ACK, pending or reconnect state.

## 2. Behavior (`MemoryForgeExpeditionClient.cs`)

### Source of the rows
- `SendBattle` already builds `Digest(before, response.Events, after, live)` for every successful command and appends it to the 40-line log. That code is unchanged.
- For `play` and `ult`, the same rows now also open the display. For `end`, the turn summary opens as before.
- The display drops only the `Summary` and `PreviousIntent` framing rows. It keeps one row per actual Core event, in Core's order, followed by the `EnemyState` results.
- No new projection exists. Nothing reads the draw order or RNG, and no forecast is computed per frame.

### Kind labels (`ActionRowText`, public static)
| Rows | Label |
|---|---|
| `CardPlayed` | `[사용]` |
| `ReservationPlayed`, `ReservationRegistered` | `[예약]` |
| `CardResolved`, `ReservationResolved` | `[해결]` |
| `Copy` | `[메아리]` |
| `CounterDamage` | `[반격]` |
| `UltimateDamage` | `[필살기]` |
| any row with provenance `유물` | `[유물]` |
| everything else | `·` |

- The status line names the display: `카드 사용 결과 k/n`, `예약 결과 k/n` or `필살기 결과 k/n`.
- Labels are text, so meaning does not depend on color alone (§14.2).

### Where it is shown
- **Battle continues:** a new 44 px inline strip `ActionPresentation` at frame y 544, between the timeline and the action panel.
  - To make room, the timeline moves from 466/100 to 458/78 (slots y 20→9, still 60 px tall), and `CardActions` moves from 582/98 to 596/94. The hand, end-turn, party and enemy panels did not move.
  - The strip shows the status line, the two latest shown rows, three pace buttons and `건너뛰기`.
  - The action line (forecast or toast) stays a separate label and is never masked.
  - When no display is open, the strip shows a muted idle sentence.
- **Battle ended (REWARD/DEFEAT/VICTORY):** a full-frame `ActionPresentationSheet` over the already drawn next screen. It lists every shown row.
  - Its pace and skip controls sit at frame y 104–152, x 620–1280. On the reward screen that is the label-only `RunStatus`; on the save screen it is the label area of `SaveHeader`, left of `AssignButton` (x ≥ 1332). Neither area has a button.
  - A repeated tap after the sheet closes therefore reaches no reward or save control. The terminal test checks this.

### Pacing
This is **local prototype timing**. It is not K2/250/180 ms and was not measured from another game.

| Pace | Ordinary row | Time-reservation resolution row | Final hold |
|---|---|---|---|
| Normal (default) | 0.2 s | 0.3 s (original §14.3 value) | 0.6 s |
| 2x | 0.1 s | 0.15 s | 0.3 s |
| Reduced | all rows at once | all rows at once | 0.6 s |

- The first row shows at once.
- Steps use unscaled `Time.realtimeSinceStartup`. `Time.timeScale` is never read or written.
- Changing the pace mid-display affects only the remaining rows. Switching to Reduced shows the rest at once.
- Normal turn-summary reservation rows keep their original 0.3 s (that code is unchanged).
- `SetActionPace` is a session-local preference. It is not saved, does not reach the Run and is never gated.

### Gate
- `Blocked()` = `Locked()` and then the display (`ACTION_SHOWING`).
- `Locked()` = `Barred()` (recovery, then the summary), then the picker sheet (`PICKER_OPEN`), then a **terminal** display.
- `SelectCard`, `SelectMode` and `SelectEnemy` now check `Locked()`. They pass a non-terminal display and are refused under a terminal one.
- Everything else still checks `Blocked()` or `Do`, and is refused during any display with `ACTION_SHOWING`. That covers: `ConfirmCard` (including opening a pick sheet, which is a command path), `PlayCard`, `EndTurn`, `UseUltimate`, `EnterNode`, every reward, service and event command, `Assign`, `Abandon`, `ToggleChange`, `CommitMemory`, `CloseWithoutSave`, `Depart`, `StartPractice`, and the private `Do`.
- Recovery, summary and picker codes still come first.
- `PickCard`, `AutoPick` and `CancelPick` still pass `Barred()` only. A picker and a display can never coexist: a display opens only after a sheet's own send has closed it, and `ConfirmCard` cannot open a sheet during a display.
- Bool meaning is unchanged: `true` means an actual Core commit, and `ConfirmCard` that opens a sheet still returns `false`.

### In-place updates
- `ConfirmCard`, `EndTurn` and `Ult:*` are drawn through `Gate(control, allowed)`. During a display they are non-interactable.
- Each step, natural expiry, skip, pace change and focus drop calls `RefreshActionPresentation()`, which rewrites only the strip or sheet labels, those controls' `interactable`, the pace captions and colors, the skip availability, and (once closed) `SetActive(false)` on the terminal sheet.
- Completion never calls `Render`, so the canvas, the `EventSystem` and a held next-card press survive (the notice-expiry rule).

### Lifecycle
- **Disable:** drops the display, as it already did for the summary.
- **Focus loss or pause:** `DropActionPresentation()` closes the display in place and marks the view for a redraw. `ResumeView` then redraws the actual Run. No command is replayed or restarted, and no log row is repeated.
- **New departure and `Quarantine`:** both close the display. Recovery keeps precedence, and no display survives or comes back.

## 3. Old-driver changes (sequencing only; every old assertion is kept)
Each change inserts an explicit finish before the next actual command. The finish asserts that the display is open (or, in one place, may have ended), calls skip, checks that a second skip is inert, and checks that the full fingerprint is unchanged.

- **`ExpeditionClientPlayModeTests`**
  - New helper `FinishActionDisplay`.
  - `BattleStep` finishes after each successful play, auto-pick or ultimate.
  - `NextIntentShowsItsForecastImmediately…` finishes before its final confirm. It uses `open: false` because the display may already have ended on its timer.
- **`ExpeditionTurnFlowPlayModeTests`**
  - New helper `FinishAction`.
  - Finishes in `ReserveAndEnd`, in the `WinningEnd…` plan loop, after both reservations in `TimeReservationSlots…`, and in `BattleStep`.
- **`ExpeditionPickerGuardPlayModeTests`**
  - New helper `FinishAction`.
  - Finishes in `BattleStep`.
  - `CancelRestores…` finishes before the reflected `Do` recovery seam. Otherwise the display would correctly refuse `Do`.
  - `ACardWithoutPicks…` finishes before its end.

## 4. New tests (`ActionPresentationPlayModeTests`, 8 Unity tests, NOT_RUN)
Every command goes through public handlers or real raycasts and is checked against an independent mirror Run. Expected row kinds and labels are computed independently from the mirror's events.

1. **`ACommittedPlayIsShownInlineWhileOnlyThePureNextSelectionIsAccepted`**
   - Rows match the events, and the same rows appear in the battle log once.
   - The spent card and dead enemies are not selectable at once.
   - Strip text and status are readable.
   - Next target, card and mode are chosen by rays during the display; the forecast stays on its own line; the state stays pure.
   - 27 public commands, the private `Do` and `StartPractice` are refused with `ACTION_SHOWING`.
   - Natural expiry happens in place (same view, `EndTurn`/`ConfirmCard` enabled, skip disabled), takes at least the documented duration, and leaves `timeScale` unchanged.
   - The prepared intent is then the next actual play.
2. **`EveryPaceAndSkipLeaveTheSameCommittedStateAndChangeOnlyTheDisplay`**
   - Variants: normal, 2x, reduced, skip, and a mid-display Reduced→2x switch.
   - Repeated pace or skip calls are inert. The chosen pace is marked by text.
   - Lower-bound durations are checked. After the same actual end, the Run, logs, events, RNG, store and rows are identical across variants.
3. **`TheBattlesLastActionCoversTheNextScreenUntilItEndsInPlace`**
   - The driver continues until a play or ultimate ends a battle.
   - The sheet and its rows are shown; there is exactly one `BattleEnd` row and no invented enemy result.
   - Reward and save commands, next selections and real taps beneath are refused; the pending rewards are unchanged.
   - The sheet closes in place on its timer, and a repeated tap at the skip point is safe.
   - Positive control: the screen's own command is then accepted.
4. **`AnActualUltimateIsPresentedAsItsOwnCommittedEvents`**
   - A real `Ult:` click; the first row is `(Other, "필살기 사용")` and the title is `필살기 결과`.
5. **`FocusLossPauseAndDisableDropTheDisplayAndResumeRedrawsWithoutReplay`**
   - Covers all three triggers.
   - The view is redrawn; the fingerprint, RunId, battle `V` and log are unchanged; the next end is accepted.
6. **`AHeldNextCardPressSurvivesTheDisplayCompletionAndItsReleaseSelectsOnce`**
   - The same button, canvas, `EventSystem`, end button and status label survive completion. The release is exactly one pure selection.
7. **`PickSheetAndTurnSummaryKeepTheirOwnLocksAroundTheDisplay`**
   - During a display, `ConfirmCard` on `nia_tune` is refused with `ACTION_SHOWING` and no sheet opens.
   - Afterwards the sheet opens and keeps `PICKER_OPEN`.
   - The summary still refuses with `TURN_SUMMARY_OPEN`, with no display beneath it.
8. **`RecoveryQuarantineOutranksAndOutlivesTheDisplay`**
   - Uses a test-local enemy-move fault, restored in `finally`.
   - The display refuses the faulty end before the Run. After skip, the end quarantines.
   - The recovery code comes first, the store is kept, and focus, pause and disable/enable keep only `RecoveryDiscard`. Discard departs `practice-2-1235`.

## 5. Risks Root should watch when running
- **Witnesses that are asserted, not skipped.** If they fail, they FAIL.
  - Test 3 needs a battle that ends on a play or ultimate within 4000 commands.
  - Test 4 needs an actor's gauge to fill.
  - Test 7 needs `nia_tune` together with an affordable immediate card.
  - I expect all three from the existing drivers' behavior, but this is not verified.
- **Editor focus.** In the Editor, a real focus change during a run now drops an open display by design. An unfocused-Editor event mid-test could perturb the pacing tests. The turn summary has no such behavior.
- **Layout readability.** The strip's two-row label (764×42, font 16, best fit down to 14) and the timeline panel, now 22 px shorter, should be checked visually (worst case: 5 reservation slots, 5 enemies, 10 hand cards). `AssertReadable` will flag truncation of the strip rows.

## 6. Remaining real limits
- This is provisional text presentation. There is no final VFX, audio, card motion, damage-number popups or cut-ins, and no art was added or requested.
- Touch size and safe-area behavior (the strip buttons are 36 px tall in the 1600×900 reference) are not verified against §14.2's 48 dp, and Android device acceptance is NOT_RUN.
- There is no server, ACK, pending, reconnect or request-ID flow. The display is local practice only.
- The confirmed next-intent queue belongs to the next unit. Today a valid next request waits with `ConfirmCard` disabled until the display ends or is skipped.
