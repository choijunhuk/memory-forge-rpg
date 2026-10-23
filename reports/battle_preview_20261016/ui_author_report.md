# P1-EXPEDITION-FORECAST-UI — author report (Claude, Opus 5.5 xhigh)

Requirement: original v0.8.1 §14.3/§14.7 (confirmation with meaningful cost/outcome information) and §6 card effects.

The expedition view lacked a damage/block/HP forecast; this change fills it from Core's pure `Run.BattleForecast`. It adds no rule,
formula, data or policy and does not touch Core, fixtures, scenes, settings or the separate battle practice. The subscription was used
with no Bash/MCP/agents/images.

**Build, PlayMode tests, Unity Editor and screenshots: all NOT_RUN by Claude.** Root's earlier Core results are Root's evidence, not mine.

## Changed files

- `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs`
- `client/MemoryForgeUnity/Assets/Tests/PlayMode/ExpeditionClientPlayModeTests.cs`
- this report

## New view APIs (pure reads; none reaches the Run)

- `BattlePlayForecast? PlayForecast(uid, mode, picks = null)`: `run.BattleForecast(uid, mode, target, picks)`. This is exactly the
  request `PlayCard` would send, with the current target; empty picks mean Core's automatic choice.
- `string ForecastLine(f, autoPicks)`: `"예상"` (or `"자동 선택 기준 예상"`) + ` · ` + mode + ` · ` + `ForecastText(f)`.
- `string ForecastText(f)`: `ErrorText(error)`; otherwise `"행동력 {Cost} · "` + `ForecastOutcome(f)`. Reservations add
  `" · 위력은 그때 상태로 정해져요"`.
- `string ForecastOutcome(f)`: only the DTO's own values.
  - Error: the error text.
  - Reservation: `"{Due}턴 시작(다음 턴|다다음 턴)에 해결"` (the absolute turn; no future damage).
  - Hidden: `"드로우·무작위 결과는 사용한 뒤에 알 수 있어요"`. Every number is hidden, including defeat.
  - Visible: `"적 체력 -N"`, `"방어 ±N"`, `"파티 체력 ±N"`, `"이 행동으로 패배해요"`, or `"체력·방어 변화 없음"`.
- Errors and defeat are coloured Danger; other forecasts Memory.

## Text paths

1. **Action hint line** (the existing TrackNotice label in `CardActions`).
   - With a selected card + mode in PLAYER phase, the fallback hint becomes `ForecastLine`. It is computed once per Render, never per frame.
   - The 2.6 s toast stays in place and expires back to this forecast line.
   - When the card has effect picks, the basis says Core's automatic choice; ConfirmCard opens the picker, which promises per-pick forecasts.
2. **Pick sheet.**
   - The PickList is 336 tall (was 380). A new line at y 448 reads `"자동으로 고르고 사용하면 · " + ForecastText(...)`. This is the forecast of
     exactly what `AutoPick` sends: the picks made so far, with Core choosing the rest.
   - When the next candidate click completes the picks (`PickCard` then plays at once), each candidate's second line is `ForecastOutcome`
     of the current picks + that candidate. The note line then reads `"마지막 선택이에요. 카드를 누르면 바로 사용해요…"`.
   - Earlier, non-final picks show no per-candidate outcome, because the result still depends on later picks.
   - All input behaviour is unchanged.
3. **Hand-card detail note.** It still separates definition text from the actual outcome, and now points to the action-line forecast. It
   also states that draw/random results and a reservation's later power are not previewed.
4. **Header comment** updated to describe the forecast.

## PlayMode tests (9 existing kept, 4 new)

Existing:
- One assertion changed: the old note check `"피해 예측은 아직"` now checks the updated note, which keeps the definition-vs-forecast wording.
- All other assertions are unchanged, including the strict shop seed 1235, the toast regression and the nullability annotations.

New:
1. `ActionLineShowsCoresForecastWhileSelectingAndOnlyTheConfirmedPlayMatchesItOnce`
   - Seed 1234, first battle. Covers every hand card × allowed mode × living target.
   - The view's `PlayForecast` equals the mirror's `BattleForecast`, and the drawn action line is exactly `ForecastLine`.
   - Checked semantics:
     - error defaults;
     - reservation due = turn+1/+2 with no numbers;
     - hidden suffix;
     - the reference party's turn-1 original card values (for example `sera_round` "적 체력 -8", `harin_shield` "방어 +8", `sera_cover` 4/+5),
       with at least one asserted.
   - The fingerprint is unchanged throughout.
   - Real rays on enemy/card/mode, then confirm: exactly one actual `BattleCommand`, and the real HP loss, block, HP and AP equal the shown
     forecast.
2. `DrawCardForecastHidesEveryNumberAndOnlyTheConfirmedPlayReachesTheRun`
   - The bounded existing driver reaches the reference kit's `nia_scan` (it draws).
   - The exact hidden line appears, and no forecast number is drawn anywhere.
   - The next-turn reservation line shows the absolute due turn.
   - Nothing is spent, and the confirmed play executes exactly once.
3. `PickSheetForecastsMatchTheAutoPromiseAndEachConcreteFinalCandidate`
   - Uses `nia_tune` (one pick). The action line carries the auto basis.
   - Opening the sheet spends nothing.
   - Each candidate caption equals the forecast for exactly that pick, and the caller's picks are not consumed.
   - The auto line equals the forecast with no picks.
   - The clicked candidate is exactly one actual play with that pick.
4. `ForecastLabelsOfAuthoredCoreBattlesWarnDefeatAndHideDrawResults`
   - A supplement, not the client path: actual Mono `Battle.Forecast` on the oracle's authored reflect-defeat and hidden-definition setups,
     run through the view formatter.
   - Lethal reflect: `"행동력 1 · 적 체력 -4 · 파티 체력 -3 · 이 행동으로 패배해요"`.
   - Draw/random/nested-draw cards are hidden, the reservation shows its due turn, `sion_cut` shows "적 체력 -8", and an error shows its text.
   - Battle states are unchanged.

Tests 2 and 3 wait for the last driver toast on the component's real timer before reading the action line.

## Limits (reported, not hidden)

- **Effect picks:** `nia_tune` is the reference kit's only pick card, and its candidates produce no HP/block change. The test therefore
  proves the per-candidate plumbing, not that outcomes differ across picks. No multi-pick (non-final) card is in the reference kit.
- **Defeat and negative party HP:** the client path in the reference opening never reaches them. They are covered only by the Mono
  supplement (test 4).
- **Hand-dependent tests:** the turn-1 literals, `nia_scan` and `nia_tune` depend on seed 1234's actual hands. If Root sees a "must reach"
  or "at least one" failure, it means the hand content, not a weakened check.
- **Out of scope:** reserved card damage is never previewed (by design). Mode-button captions are unchanged (cost + timing/error); the
  forecast is on the action line. Visual fit at 130% text, Android/device, server and native physical input are separate and not claimed.

## Root validation

1. Compile the Unity project (Editor import).
2. Run PlayMode: all 13 tests; confirm the 9 existing ones still pass.
3. EditMode tests.
4. Console errors/warnings.
5. Screenshots of:
   - the action line on now, reservation, hidden and error forecasts;
   - the pick sheet (auto line and final-candidate captions);
   - the card-detail note.
6. Independent diff review and the opposite-AI review of any Root fixes, then commit/push to PR#2.
