# Read-only Codex review: expedition practice UI/UX redesign (EXPEDITION-UI-20261018)

You are an independent read-only reviewer. Do not edit, create or delete files, do not commit, do not call other agents and do not generate images. Answer in Korean or English.

Caller: Claude Opus root-delegated session (claude-opus-5-5) in worktree `.claude/worktrees/expedition-ui-40eff0`, branch `claude/expedition-ui-40eff0` (based on `codex/late-actions-balance` 305289b). The diff is attached on stdin.

## What changed

- `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs` — view section only (`Render` … `Launch`, the uGUI helpers, `Play`/`UseUltimate`/`SendBattle` gained a decoration-only "flight" origin). Every map, battle, reward, rest/forge/shop/event/archive, memory-save and result screen is redrawn:
  - Codex's paintings full-bleed under a navy scrim (battle: `bg_zone1_battle` = `bg_zone1_battle_street_v02`, map: new copy `bg_zone1_map` = `bg_zone1_map_clockcity_v02`, all other screens: `bg_workshop` = `bg_zone1_event_workshop_v02` as a thematic backdrop); every rule text on opaque rounded panels (procedural 9-sliced sprite, edge = a ring child named `Edge`).
  - Battle: party rows with the approved SDs and `Ult:<id>`; enemy slots in Core order (`Enemy:<uid>` root, sprite by definition id `enemy_<Id>`, measured data panel: name, HP/block, HP bar, crack + pips, weakness words (+ Codex attribute icons on wide slots), statuses, the unchanged "이번/다음" intent label; a defeated enemy keeps a narrow slot with name, HP 0 and 처치; slots scroll when narrower than 196); timeline slots with in-cell next/after labels and capacity counts; hand cards with the owner's cut-in band (or SD / plain band), cost pill, name, Core's `BattleText.Card` effect text (card widens 184/236/300 to fit, otherwise points to 카드 설명), mode tag.
  - Map: Codex node icons + type word, reachable brass edge (breathing), current teal, passed floors dimmed, legend.
  - Services: a place card (Codex glyph, title, intro) + rows that grow to their text; shop wares as tiles, card removal on the place card.
  - Save: per-member ledger with portrait, capacity meter (danger + 용량 초과 only when over), scrolling lists; result with portraits and scrolling lists.
  - Motion (decoration only, `MemoryForgeUiMotion.Reduced` or the practice's 동작 줄이기 makes it instant): fade on screen change and on a newly opened sheet; in battle, from the public state difference since the last uncovered view: hit numbers, art shake + red tint, party flash, HP bar drain, new hand cards dealing in, the played card's ghost flying to its target / timeline / party. Controls' hit rectangles never move: hand cards animate a child `Face`, enemies their `Art` child.
- `MemoryForgeUiMotion.cs` — new `Shake`, `Rise`, `Resize`, `Fly` coroutines and a `UiPulse` component.
- `MemoryForgeUiSkin.cs` (+meta) deleted: its background, enemy art, party SD, hit shake/flash and press/hover/fade decoration now live in the client (it found objects by name and would double-decorate).
- Byte copies (verified by `python3 tools/check_art_copies.py`): `Resources/Art/Backgrounds/bg_zone1_map.png`, `Resources/Art/UI/ico_node_*.png` (8), `ico_attribute_*.png` (4).
- `Assets/Tests/PlayMode/ExpeditionUiCapturePlayModeTests.cs` — Explicit capture test (public commands only; a seed-summoning fight is passed turn by turn until five enemies stand).
- Screenshots: `reports/expedition_ui_20261018/before/*.png` (old code) and `after/*.png` (new code), 1600×900 plus one 2340×1080 aspect-fit capture.

## Constraints given to the author

Keep every object name other code relies on (ExpeditionFrame, Enemy:<uid>, Party and Ult:<id>, Hand and Hand:<uid>, Node:<id>, EndTurn, Mode:now|next|after, ConfirmCard, Pace:*, the "파티 체력" label, RewardCard:*, SkipCard, CardInfo:*, Proceed, CommitMemory, CloseWithoutSave, the onboarding targets in `MemoryForgeOnboardingSteps.cs`). No rule, number or text-meaning change; long text gets layout fixes, never cuts. Art content is Codex's and used unchanged. Respect `MemoryForgeUiMotion.Reduced`. 1600×900 reference, phone readability (2340×1080, 48dp). Codex's `layout.json` / `design-spec.md` (art/candidates/20261018/codex) are a proposal, not a contract.

## Please check

1. Behaviour preservation: does any command, gate (`Gate`, `Blocked`, `Locked`), confirm/cancel swap (`InfoRect`/`ConfirmRect`), repeated-tap safety (EndTurn bottom vs Proceed/CloseWithoutSave, action-sheet skip point), notice handling or caption change? Does any decoration take a raycast or cover a control (including the onboarding `Guide` drawn last in `ExpeditionFrame`)?
2. Text completeness: can any rule text (enemy name, intent, status, weakness, card effect, reward/relic/part/shop/event text, memory lists) be cut or hidden? Are the measured heights (`Grow`, `TextHeight`) consistent with how Unity draws them?
3. Motion: can a coroutine touch a destroyed object, move a control's hit rect, fire on a refused command (the flight is only set after a committed play/ult), or run with reduced motion on? Does the diff logic read anything but public state?
4. Art: correct ids/paths, byte copies only, no art edited, Codex's art direction respected (scrim, opaque text panels, no stretched silhouettes, cut-in as decorative band only).
5. Visual check of `after/*.png` against `before/*.png` and Codex's `design-spec.md` intent: readability, hierarchy, anything that looks broken.

Return findings classified as `implementation_defect`, `design_mismatch`, `real_constraint` or `optional_opinion`, each with file:line (or screenshot name) and a concrete failure scenario, then a one-line verdict: `NO_BLOCKING_DEFECT` or `BLOCKING_DEFECT`.

## Round 2 (after fixes for round-1 session 01a14d23-efd0-7e92-ba75-84fb1654d676)

Round 1: BLOCKING_DEFECT, five implementation_defect findings. Changes since (same files plus one new test file):

1. **48dp (P1).** Every button's hit area grows to 48dp around its drawn face on a phone: `minTouch = 48 × Screen.dpi / 160 ÷ (frame pixels per unit)` when `Application.isMobilePlatform` and dpi is known (≈105 units on a 2340×1080, 420 dpi phone; 0 on desktop/batchmode, so desktop layouts are unchanged). The root becomes a transparent hit area, the drawn button its child `Face` (`targetGraphic`, ColorTint, caption). Where a grown area would cover another control, the hit rectangle is explicit: `Proceed` (and the save screen's `CommitMemory`/`CloseWithoutSave`) take y 790–900 only, below the scrolling panels, which now end at y 788; lists (pick sheet, deck pick, deck, found parts, assign, memory changes) keep exact hit rects and grow their rows to 48dp instead. New test `ExpeditionUiLayoutPlayModeTests.PhoneTouchTargetsAreFortyEightDpAndEachFaceTakesItsOwnPress` sets `TouchOverride = 105` and walks real short expeditions (seeds from 1234) until it has seen map, battle with 2 and with 5 enemies, all three bosses, pick sheet, turn summary, a battle's last-action sheet, reward, elite reward, rest, deck-pick sheet, forge, shop, event, save and result, plus the deck, log, abandon and card-info sheets; on each it asserts every control of the topmost layer is ≥105 units both ways and that its own face centre and four points 20% in from the corners take the press for it (EventSystem raycast).
2. **Tier chip over the name (P2).** Tier (Core's `Tier`) and 선택 chips are now inside the data panel's name row, whose label width reserves their room; they move with the content if the panel scrolls. The same test asserts no chip overlaps an enemy's name text in every battle it checks.
3. **Survivors moving after a defeat (P2).** All enemy slots have one width (`(band − gaps) / count`, max 560, scroll below 196); a defeated enemy keeps its full slot (faded art, name, HP 0, 처치). Only a summon (a new enemy) re-lays the band. New test `DefeatingAnEnemyLeavesEverySurvivorsSlotWhereItWas` compares every slot rectangle before and after a real defeat.
4. **Flying card over usable controls (P2).** The ghost is inserted just above the stage scrim, under every panel and control, so it shows over the painted stage only. The party hit number and flash moved from the party panel (which holds the Ult buttons) to the party HP strip, which holds no control.
5. **동작 줄이기 mid-motion (P2).** Every motion helper (`Fade`, `SlideIn`, `Shake`, `Rise`, `Resize`, `Fly`) takes a `stop` predicate checked every frame (plus `Reduced`) and settles at once in its final pose; the client passes `Still() => Calm` (global Reduced or the practice's 동작 줄이기). `Deal` and `Tint` check it too; `UiPulse` already did. New test `ReducedMotionSettlesRunningDecorationAtOnce`: after a damaging play the flying card exists (positive control); switching to 동작 줄이기 removes the flight and drained-bar part within two frames and the hit number stops moving.

Other changes: shop wares fit without scrolling (tile height = text + controls) and card removal sits on the shop's place card. Batchmode Unity: the three new tests PASS; full suite result in the README. Screenshots in `after/` are recaptured from this code.

Please re-check the five round-1 findings and anything new (the diff below is the whole change against 305289b); same classification, then `NO_BLOCKING_DEFECT` or `BLOCKING_DEFECT`.

## Round 3 (after fixes for round-2 session 01a14d3b-4510-71e0-a95b-21e417e92eef)

Round 2: BLOCKING_DEFECT. Findings 2–5 of round 1 confirmed fixed; new: (1) grown touch areas overlapping (pace vs mode row), (2) the reward card's info button area covering the card's bottom, (3) the onboarding guide's buttons still 48 units. Changes since:

1. **No overlapping touch areas.** Where centred growth would meet a neighbour, the control gets an explicit band (`Band(...)`, parent coordinates, only on a phone): the strip's pace/skip buttons take frame y 470–575 (over the timeline, which holds no control) and the mode/카드 설명/확정/대기 취소 row takes 575–680, both 105 units; the sheet closers (deck, card text, log) grow upward away from their lists, the deck-pick and assign sheets' buttons grow downward below their lists, the evolution info button keeps clear of its evolve button, the save screen's 공용 카드 배정 takes 96–201. The party panel is now three columns (SD, name, attribute + gauge, `Ult:<id>`), so each ultimate has a 107×130 area of its own instead of rows 96 apart.
2. **Reward info button.** On a phone the 카드 설명 보기 button itself is 48dp tall below the card (the card's own area is untouched); the panel grows with it.
3. **Guide buttons.** `MemoryForgeOnboarding.CoachButton` grows `Guide:Next`/`Guide:Skip` to the client's `TouchUnits()` (transparent root, drawn `Face`); the area reaches only over header text (x ≤ 920, above y 91), never a practice control. `TouchUnits` is now public.
4. **Stricter test.** `PhoneTouchTargetsAreFortyEightDpAndEachFaceTakesItsOwnPress` now runs with the guide attached (fresh test-only PlayerPrefs key, deleted at teardown) and, on every screen and sheet it checks, also asserts that no two touch areas of the topmost layer (plus the guide's buttons) overlap — an area inside a scroll counted only where its viewport shows it. Mutation check: with every band disabled (centred growth only) the test fails at the deck sheet (`CloseDeck` vs the first `DeckInfo` row), so the overlap check does detect overlaps. Same walk coverage as round 2 (map; battles with 2 and 5 enemies; all three bosses; pick sheet; turn summary; last-action sheet; reward; elite reward; rest; deck-pick; forge; shop; event; save; result; deck/log/abandon/card-text sheets).

Visual: the party columns put the SDs side by side above their ultimates (see `after/battle_two.png`, `after/boss_b_bell.png`).

Please re-check round 2's findings and anything new; same classification; `NO_BLOCKING_DEFECT` or `BLOCKING_DEFECT`.

## Round 4 (after fixes for round-3 session 01a14d50-84b2-77c2-a6b9-fee0a3e5117d)

Round 3: BLOCKING_DEFECT — (1) the fixed bands stayed 105 units when the real `minTouch` is larger (e.g. 120 at 480 dpi), (2) the guide's and header buttons' areas reached above the frame's top edge. Changes since:

1. **Every band follows `minTouch`.** Battle: `BandSplit = max(568, 460 + minTouch)`; pace/skip take `[BandSplit − minTouch, BandSplit]`, the card row `[BandSplit, BandSplit + minTouch]`, and the hand dock and 턴 종료 start at `DockTop = max(684, BandSplit + minTouch + 2)` (hand cards and the text area shrink to the rest; the effect text still widens the card or points to 카드 설명, never cut). The strip's pace/skip buttons are laid out from the right, each ≥ max(design width, minTouch). The party panel widens to `16 + members × (minTouch + 4)` so each ultimate column is ≥ minTouch wide (the enemy band takes the rest); the ultimate's band is max(130, minTouch) tall. The exits' row starts at `ExitTop = 900 − max(108, minTouch)` and every scrolling panel (reward, services, shop list, event choices, save columns) ends above it. Sheet closers, the deck-pick and assign buttons, the save screen's 배정 and 기록's width use minTouch-sized bands; flowing panels draw their buttons at ≥ minTouch (service rows, shop read/buy, relic tiles, 가져가기, evolution choices with their info buttons, map nodes) so nothing needs to grow over a neighbour. `Button` always unions a given hit rectangle with its own face.
2. **On the frame.** Header buttons' bands run from the frame's top edge (y 0) down to max(minTouch, 72); the guide's buttons (`CoachButton`) likewise start at y 0, never above. The turn summary's skip/continue reach down to the frame's bottom edge (y 900), not past it.
3. **Test at three densities.** `PhoneTouchTargetsAreFortyEightDpAndEachFaceTakesItsOwnPress` now runs the whole walk at 105, 120 and 130 units (420/480/520 dpi at 1.2 px per unit) over seeds 1234, 1235, 1237, 1245 (which together reach every listed screen and sheet), and measures each touch area clipped to the frame (and to its scroll viewport) before the ≥ minTouch check; the no-overlap and own-face checks are unchanged. All three densities pass, each listing all 18 screen/sheet kinds.

Please re-check round 3's findings and anything new; same classification; `NO_BLOCKING_DEFECT` or `BLOCKING_DEFECT`.

## Round 5 (after fixes for round-4 session 01a14d7c-4105-7c40-a277-b8833b9fd282)

Round 4: BLOCKING_DEFECT, two P2 — (1) at 130 units the hand card's pointer text ("효과가 길어요 …") was cut in a 28-unit text area; (2) at 130 units the save column's result/dropped lists sat at fixed y and their scroll areas reached 2–4 units into the exits' touch row. Changes since:

1. **Hand card at high density.** The card's art band thins (46 → 30) when the card is shorter than 180, the name, text and mode tag follow it (`nameTop`, `textTop`, `tagTop`), so at 130 units the text area is 44 (2 lines). The pointer is shorter (`LongEffect` = "전체 효과는 '카드 설명'에서") and is itself measured: the card takes the first width at which it fits (else the widest). Off phones the card is unchanged (46/56/92/160/66).
2. **Save columns from their own bottom.** The result preview and the dropped-cards list now stack up from the column's bottom (`column.sizeDelta.y − 8`), and the changes list takes what is left above them; the column already ends 6 above `ExitTop`. Off phones the positions are the same as before (changes list 196 tall).
3. **Two new guards in the phone walk (all three densities, every checked screen and sheet).** (a) No control's touch area lies over a scroll region it is not part of (`ScrollRect` viewports vs every button outside them) — this covers round 4's finding 2. (b) Every text of the checked layer (and the guide) shows all its characters, generated with the label's own settings (labels scrolled out of view included); rich-text guide text is skipped. Mutation check: making the hand card's effect label 16 units tall fails the walk at `battle_two` ('방어 8 (예약 11).' is cut).

Please re-check round 4's findings and anything new; same classification; `NO_BLOCKING_DEFECT` or `BLOCKING_DEFECT`.

## Round 6 (after fixes for round-5 session 01a14d8c-0dca-7e31-ae60-37f0d940fd02)

Round 5: BLOCKING_DEFECT, one P2 — in the 1600×900 capture the hand card 「미세 조정」 lost its last line ("「증폭」 부착."), and the text check compared non-whitespace glyphs with visible characters and skipped the desktop layout. Cause: line breaks follow the canvas scale factor the glyphs are generated at; the card was measured at the test canvas's scale and drawn by the capture camera at 1.0. Changes since:

1. **Measured at several canvas scales.** `TextHeight` (and `Grow`, which now uses it) takes the tallest preferred height over this canvas's own scale and `MeasureScales` = 1.0 (1600×900), 1.2 (1920×1080), 1.3248 (2340×1080 phone), 1.6875 (2560×1600 tablet), still measured 6% narrower plus a margin. So the hand card widens (or points to 카드 설명) when any of those screens would need another line, and every grown label is tall enough on each.
2. **Exact cut check, all scales, desktop too.** For every text of each checked layer, the walk now generates it with the label's own settings at each of those scales plus the current one, and asserts its visible characters equal those of the same text with no height limit (exact, whitespace included). The walk runs at densities 0 (desktop layout), 105, 120 and 130. Mutation check: measuring at the current scale only fails the walk at `battle_two`, desktop density, scale 1.0: '[즉시 전용·전투당 2회] 손의 카드 1장에 임시 회로 「증폭」 부착.' shows 30 of 39 characters — round 5's case.

`after/` screenshots are recaptured from this code.

Please re-check round 5's finding and anything new; same classification; `NO_BLOCKING_DEFECT` or `BLOCKING_DEFECT`.

## Round 7 (after round 6 NO_BLOCKING_DEFECT, session 01a14d9b-80fe-7942-9353-55d4a2042869)

Round 6 found no blocking defect; its P3 opinion (the boss's 다음 intent pushed below the info scroll) was taken. Two later changes need a re-check:

1. **P3 taken.** The measurement margin was overly conservative; the crack row is wider (108) and a single-enemy slot gives the info column 62% (38% art).
2. **Measure at the drawn scale, redraw when it changes** (replaces round 6's multi-scale measure). Measuring every label at five canvas scales made a battle `Render` ~30 ms, which broke the 0.3 s reveal timing in `ExpeditionTurnFlowPlayModeTests.ReservationRowsRevealOnTheApprovedTimer…` (0.2725 < 0.28). It also rasterises CJK glyphs into the dynamic font atlas at five extra sizes. Now:
   - `TextHeight` measures once, at the canvas's own scale factor (3% narrower, +4), and caches per scale/size/style/width/text.
   - `Update` redraws the view whenever the canvas scale factor changes. A redraw keeps the previous canvas's render mode and camera.
   - So a label is always measured at the scale it is drawn at, including a resized window, another screen, or a camera that draws the canvas.
   - Redrawing is view-only: no command, no fade, no hit number, no deal (the seen state is unchanged).
3. **Tests.**
   - The phone walk's cut check now generates each label at its canvas's own scale.
   - At desktop density, every key screen is also drawn by a camera at 1600×900 (scale 1.0) and 2340×1080 (scale 1.3248). The test waits for the client to redraw, asserts the camera's scale applied, and checks no text is cut there.
   - The capture test now captures the redrawn canvas, so screenshots are laid out at their own scale; the phone shot uses the phone scale.
   - Mutation check: with the redraw-on-scale-change line removed, the walk fails at `battle_two` 1600×900, scale 1: 「미세 조정」 shows 30 of 39 characters (round 5's case).
   - Layout + turn-flow classes: 12/12 PASS.

Please review these changes (and anything else); same classification; `NO_BLOCKING_DEFECT` or `BLOCKING_DEFECT`.
