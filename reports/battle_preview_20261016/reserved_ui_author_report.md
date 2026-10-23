# Current-state reservation forecast view — P1-RESERVED-FORECAST-UI (Claude, Opus 5.5 xhigh)

Requirement: original v0.8.1 §14.2/§14.3 (preview with every modifier before committing; next/after damage and break), §5.4–5.6 (real
resolution later), §6. Role: the expedition action line labels the accepted Core `BattlePlayForecast` v2 (`EnemyDamage`, `NewlyBroken`,
next/after current-state estimate), as the original HTML `preview()` (prototype/index.html 3274+) does with " (현재 상태 기준 예상)".

**Compile, PlayMode/EditMode tests, Editor, screenshots, native build and device: all NOT_RUN by Claude.** The source was written in two
sessions (the host restarted mid-task); both are reflected here. Root runs everything.

## Files

| File | Change |
|---|---|
| `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs` | `ForecastText` / `ForecastOutcome` labels, the hand-card note and the header comment. No layout, input, Render, notice or overlay change. |
| `client/MemoryForgeUnity/Assets/Tests/PlayMode/ExpeditionClientPlayModeTests.cs` | Updated assertions in 5 existing tests; 11-field `Project`; 2 new tests; small helpers. 14 → 16 tests. |
| this report | — |

## Labels (Core DTO values only; no formula or future-turn prediction)

`ForecastText(f)` = `ErrorText(code)` on error, else `"행동력 {Cost} · " + ForecastOutcome(f)`. The old reservation suffix
" · 예약의 실제 해결 결과는 그때 확인해요" is removed. `ForecastLine` (the "예상 · {mode} · " basis prefix, or "자동 선택 기준 예상" for pick
cards) is unchanged.

`ForecastOutcome(f)`:
1. **Error:** the error text, as before.
2. **Reservation (next/after) prefix:** `"{Due}턴 시작({다음 턴|다다음 턴})에 해결 · "`, with the actual cost and absolute due turn kept.
3. **Hidden:**
   - now: "드로우·무작위 결과는 사용한 뒤에 알 수 있어요" (unchanged);
   - reservation: prefix + "드로우·무작위 결과는 해결된 뒤에 알 수 있어요".
   - No number, break or defeat is shown.
4. **Visible outcome parts, joined by " · ":**
   - Enemy damage, which includes enemy block (`EnemyDamage`). The HP-only number (`EnemyHpLoss`) is named only when it differs:
     - `적 피해 8` when HP loss equals damage;
     - `적 피해 8 (체력 -2)` when they differ;
     - `적 피해 4 (체력 그대로)` when block absorbed all of it;
     - `적 체력 -N` only if damage is 0 but HP loss is not.
   - `방어 ±N`, `파티 체력 ±N` (unchanged).
   - `새 균열 N체` (`NewlyBroken`).
   - Defeat: "이 행동으로 패배해요" (now) or "지금 해결되면 패배해요" (an estimate). The estimate wording never says that committing the reservation defeats the party.
   - No parts: "체력·방어 변화 없음".
5. **Visible reservation:** prefix + "현재 상태 기준 예상: " + the parts. Example: `행동력 2 · 2턴 시작(다음 턴)에 해결 · 현재 상태 기준 예상: 적 피해 22 · 새 균열 1체`.

Hand-card note:
- **Now:** 적힌 수치는 카드 정의의 값이에요. 약점·상태·유물까지 반영한 예상은 방식을 고르면 행동 안내 줄에 보여요. 적 피해에는 적 방어에 막힌 몫도 들어가요. 예약의 예상은 지금 상태에서 바로 해결한다고 가정한 값이라, 실제 결과는 해결되는 턴의 상태로 정해져요. 드로우·무작위 결과는 미리 보여 주지 않아요.
- The source-text vs effective-outcome distinction is kept.
- "예약 카드의 나중 위력은 미리 보여 주지 않아요" is removed.

Unchanged seams:
- The action line recomputes `run.BattleForecast` on Render only.
- The pick sheet's per-final-candidate caption (`ForecastOutcome` with that candidate's picks) and the auto label (`ForecastText` of the picks made so far) are unchanged. Pick requests exist only for now, so reserved resolution never uses manual picks, matching Core.
- `ForecastColor` still marks errors and defeat, including estimate defeat, with Danger.

Text fit (estimated, not measured):
- **Action line** (1498×30, size 23, best fit down to 16): a typical visible reservation line is about 35 Hangul plus about 45 narrow glyphs, so it fits at 23. The worst all-parts line shrinks to about 19 on one line.
- **Pick captions** (308×54, size 21, best fit down to 14) are no longer than the already accepted hidden caption.
- `AssertReadable` checks for truncation in every view-path test below.

## Tests (14 existing kept, 2 added)

**View-path acceptance** (the actual expedition Run, real rays, mirror Run plus fingerprints):
- `ActionLineShowsCoresForecast…` (updated):
  - Rejected requests: all 9 outcome/cost fields must be default, including the 2 new ones.
  - Reservations: absolute due turn. A hidden estimate has every outcome field default and ends with the reserved hidden text. A visible estimate contains "{due}턴 시작(...)에 해결 · 현재 상태 기준 예상: ", the damage/block/break parts, and "지금 해결되면 패배해요" exactly when Defeat. Neither the old note nor "이 행동으로 패배해요" may appear.
  - harin_shield, if it is in the opening hand: rv11 block estimate.
  - Now literals: `EnemyDamage == EnemyHpLoss` (the enemy has no block), and the label contains "적 피해 N".
  - The actual confirmed play must equal the shown forecast in HP loss, damage including block, newly broken enemies, block, HP and AP.
- `DrawCardForecastHides…` (updated): the nia_scan reservation is hidden (all 7 outcome fields default). Its line is exactly "예상 · 다음 턴 · 행동력 {c} · {turn+1}턴 시작(다음 턴)에 해결 · 드로우·무작위 결과는 해결된 뒤에 알 수 있어요". The new `AssertNoForecastNumbers` finds no damage/HP/block/break/defeat/estimate wording anywhere in the view, for both the now and the reserved selection.
- `HandCardInfo…` (updated): the note contains the block and current-state sentences and no "나중 위력".
- `NextIntent…` and `PickSheet…`: unchanged code, now with the 11-field `Project`.
- **New: `ReservationShowsItsCurrentStateEstimateAndOnlyTheConfirmSchedulesItWithoutApplyingIt`:**
  - Selection: real rays pick target, card and the first visible next/after request of the seed-1234 opening hand. The label must start with "예상 · {mode} · 행동력 {cost} · {due}턴 시작({mode})에 해결 · 현재 상태 기준 예상: " and render untruncated. Fingerprints stay unchanged.
  - Confirm: the ConfirmCard click must equal exactly one mirror command. Version +1, AP − cost, and the reservation is scheduled at `Due`.
  - The estimate is not applied now: party HP/block and each enemy's HP/block/brokenUntil are unchanged.
  - This assumes the opening offers a visible reservation, as the existing test already requires a reservation. If the hand only had draw-card reservations, it fails loudly.

**Supplement, standalone Core through the view's formatter** (labelled in code; separate from view-path acceptance). Each battle is a fresh public `Battle.Create` plus the oracle's recorded commands, with literals taken from the accepted oracle/C# checker:
- `ForecastLabelsOfAuthoredCoreBattles…` (updated):
  - reflect lethal: `적 피해 4 · 파티 체력 -3 · 이 행동으로 패배해요`.
  - New reflect reservation estimate: due 3, 6/−3/defeat, `… 현재 상태 기준 예상: 적 피해 6 · 파티 체력 -3 · 지금 해결되면 패배해요`.
  - Nested-draw reservation: hidden text.
  - `적 피해 8`.
- **New: `ReservationEstimateLabelsOfAuthoredCoreBattlesKeepCostDueAndCurrentStateSemantics`:**
  - **harin_shield next:** Cost 1, Due 2, `방어 +11`. The real reservation then leaves block 0, AP 3, and one reservation due 2.
  - **sera_timed** (reserved-estimates, seed 19):
    - next 22 / after 30 at the same point;
    - sera_aim next: zero estimate, "체력·방어 변화 없음";
    - sera_cover next: `적 피해 6 · 방어 +7`.
    - After c1: timed next `적 피해 22 · 새 균열 1체`.
    - After c2: cover now `적 피해 4 · 방어 +5 · 새 균열 1체`.
    - After c3 and end, the keeper is already broken: cover next has `NewlyBroken` 0, `적 피해 9 · 방어 +7`; round next against cog block `적 피해 12 (체력 -6)`; round now `적 피해 8 (체력 -2)`.
  - **Block absorption** (damage-block-targets, seed 11, turn 2): sera_cover `적 피해 4 (체력 그대로) · 방어 +5` (damage 4, HP loss 0).
  - **Hidden reservations** (seed 20, void_shard): nia_scan next, yuri_root_hunt after and sera_last after (the RNG hook) keep cost/due and print only the hidden text. The sera_round next control prints `적 피해 12`.
  - Every estimate leaves `BattlePrint` (state, log, events) unchanged.

Preserved:
- `ClearIntentNotice`, the 2.6 s held-press test, the overlay ray-blocking checks and every other existing assertion except the truthfully updated labels.
- The reference party (`ReferenceParty`, `PracticeSetup`).
- Root's three runtime-guarded test nullability annotations: lines not edited.

## Limits

- **Estimates, not promises:** reservation numbers are current-state estimates from Core; the later real resolution can differ, and the UI says so.
- **Conditional checks:** the harin_shield-in-opening check runs only if that card is drawn. The deterministic 11 is in the standalone supplement.
- **Not claimed:** whole §14.3, accessibility (130% text), device, account and server acceptance.

## Root validation steps

1. Compile the Unity project (client and PlayMode test assemblies); expect no new warnings.
2. Run the PlayMode suite `ExpeditionClientPlayModeTests` in the actual Editor: 16 tests, including the 14 existing ones. Also run the other Unity suites.
3. Visually check the live action line for a visible reservation (with damage, block or break), a hidden reservation (nia_scan next), an absorbed hit (`(체력 -N)` / `(체력 그대로)`) and a defeat estimate if reachable. Check the pick-sheet captions at the real resolution and take screenshots.
4. Independent review, then build and PR.
