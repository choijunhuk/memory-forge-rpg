# Read-only Codex review: expedition screens in a subculture mobile-game style (EXPEDITION-RESTYLE-20261018)

You are an independent read-only reviewer. Do not edit, create or delete files, do not commit, do not call other agents and do not generate images. Answer in Korean or English.

Caller: Claude Opus root-delegated session (claude-opus-5-5) in worktree `.claude/worktrees/expedition-ui-40eff0`, branch `claude/expedition-ui-40eff0` (merged `codex/late-actions-balance` 377b080 first). The diff against that merge is attached on stdin; screenshots are attached as images.

## Why

The user judged the redesigned expedition screens (377b080) "still look like a website, not a mobile game". `reports/expedition_ui_20261018/subculture_ui_reference.md` records the grammar taken from real subculture mobile screens. This pass applies it to the existing layout with code-built shapes; Codex's UI kit is not made yet (`art/requests/ui-skin-kit-20261018.md`, needs list `art/requests/expedition-restyle-needs-20261018.md`).

## What changed

- `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs`, view section only:
  - New procedural shapes (one texture each per session): pill (half-height ends, 9-sliced), disc, diamond, soft halo, vertical fade, an ornate ring with corner diamonds, a shaded gem face. Ring widths quantised to half pixels.
  - Helpers: `Frame` (glass panel + sheen + ornate edge), `Gloss` (primary buttons), `Chip`/`Chips` (pills sized by `TextWidth`, wrapping), `Gem`, `Strong` (drop shadow on titles/numbers), `Clock`, `OwnerColor`/`AttributeColor` (card frame = owner's attribute colour from `BattleCharacterDef.Attr`).
  - `Button`: one-line buttons up to 64 tall are pills; primary faces get a gloss; when the hit area is grown (phones) the face sinks to 95 % about its centre while pressed (`UiPress.Bind`, off with reduced motion or 동작 줄이기). The hit root never changes size.
  - Battle: HUD = party HP plate with gem + bar (label text unchanged: `파티 체력  X / Y`), the turn/block/counter/debt/echo line split into chips, piles + coins + Core's party statuses (`PartyStatusText` split on ` · `) as chips, AP on a brass gem plate (`행동력  N` unchanged). Party stands on the stage without a panel (SD, name pill with attribute icon, gauge, `Ult:<id>` pill; same touch bands). Enemies: painting with tier aura and floor shadow (teal ring when targeted) + nameplate `Info` (crest line in tier colour, tier/selection pills, HP bar, diamond crack pips, attribute-coloured weak icons, status chips from `StatusText` split on ` · `, intent badge with the exact `이번  …\n다음  …` text). Timeline pill with a clock mark; slots keep `ReservationSlot` and their exact first text. Action strip and pace group as pills. Card actions on a framed panel; the ready `ConfirmCard` breathes. Hand cards: frame in the owner's colour, art window, cost gem with the number only, name plate, effect text (same measuring), mode chip. `EndTurn`: a round brass dial with ticks over the whole old bottom-right touch area (caption unchanged).
  - Map: medal nodes (`Node:<id>` keeps its rect; medal + name ribbon + 갈 수 있음 / 현재 위치; boss ring red), brass route connectors, framed map panel, pill legend.
  - Reward: card tiles framed in the owner's colour with a taller art window and a cost gem; coin banner; relic tiles with a mark. Places: framed place card with a big medal and title ribbon; rows with a chevron. Shop wares, save ledger (portrait medals), result, every overlay sheet use the framed panel.
  - Backdrop: lighter scrims plus a header shade and a bottom shade.
- `MemoryForgeUiMotion.cs`: `UiPress.Bind(face, calm)` (scale a face about its centre and restore its exact position; existing lobby use unchanged).
- `ExpeditionUiCapturePlayModeTests.cs`: `Timeout(900000)` on the Explicit capture (it walks whole expeditions on 14 seeds; each redraw is now ~8 ms slower).
- `art/requests/expedition-restyle-needs-20261018.md`: needs-only list for Codex.

Measured redraw cost (batchmode editor, `ActionPresentationPlayModeTests`, 503 battle redraws): before 19.3 ms average, after 27.3 ms.

## Constraints given to the author

Keep every object name other code and the onboarding coach rely on (ExpeditionFrame, Enemy:<uid>, Party, Ult:<id>, Hand, Hand:<uid>, Node:<id>, EndTurn, Mode:*, ConfirmCard, Pace:*, Timeline, ReservationSlot, RewardCards, RewardCard:*, SkipCard, CardInfo:*, Proceed, CommitMemory, CloseWithoutSave, the `파티 체력` and `행동력  N` labels). No rule, number or text-meaning change; never cut text. Art content is Codex's; new frames/icons go to a request, not drawn here. Respect `MemoryForgeUiMotion.Reduced`. 1600×900 reference, phone 2340×1080 and 48dp.

## Please check

1. Behaviour preservation: commands, gates, confirm/cancel swap, repeated-tap safety, notices, captions, object names, onboarding targets. Does any decoration take a raycast or cover a control? Can `UiPress` leave a face displaced or scaled?
2. Text completeness: can any chip, nameplate, card, ribbon or dial text be cut (chip widths from `TextWidth`, the 갈 수 있음 line, the dial caption, the cost gem)? Is any rule information lost by the chip split or the cost gem showing only the number?
3. Performance: the redraw is slower; anything wasteful, and does anything regenerate textures per redraw?
4. Visual check of `after/*.png` against `before/*.png` and the reference notes: does it now read as a mobile game screen; anything broken, unreadable or inconsistent?

Return findings classified as `implementation_defect`, `design_mismatch`, `real_constraint` or `optional_opinion`, each with file:line (or screenshot name) and a concrete failure scenario, then a one-line verdict: `NO_BLOCKING_DEFECT` or `BLOCKING_DEFECT`.

## Round 2 (after fixes for round-1 session 01a14e0c-96d0-71b0-afa9-d7aa402a53f9)

Round 1: BLOCKING_DEFECT. Findings and what changed:

1. **P1 implementation_defect — party status chips could wrap into the stage (y ≥ 162).** The HUD chips now measure first (`ChipsWidth`): the stats take one row and the piles + coins + party statuses the next only when each fits between x 636 and 1222; otherwise all chips flow over three compact rows (22 tall, 3 apart, y 87–159), so no chip reaches the stage.
2. **P2 design_mismatch — on a five-enemy fight the nameplates crowd out the paintings (the elite's art vanished).** On a narrow slot the tier and selection badges moved out of the name row onto the slot's top edge over the painting (so the name takes the whole width and no longer wraps), the nameplate uses compact sizes (name 20, body 17, intent 17, padding 10) and never rises into the badge row (it scrolls inside the nameplate if it ever needs more), and the painting is drawn from 40 units of room (was 48). Every enemy's intent text stays visible on the stage without a tap (it is what the turn is planned on), so details-on-selection was not adopted.
3. **real_constraint — redraw cost.** Recorded in the README; no device measurement yet.
4. `UiPress` now resets scale and position on `OnDisable` (a press cut short by a disabled control or a hidden view).

Attached: the new `after/battle_five.png`, `after/battle_five_phone_2340x1080.png`, `after/boss_b_bell.png`, `after/battle_two.png`.

## Round 3 (after fixes for round-2 session 01a14e15-bd92-7fc1-a418-c779f5822434)

Round 2: BLOCKING_DEFECT. Findings and what changed:

1. **P2 design_mismatch (blocking) — a status chip row (e.g. 취약 after 구조 분석) grew the elite's nameplate until the painting was dropped.** A narrow slot now always keeps the painting's room at its top: at least 76 units, and when there are badges or statuses, 34 (badge row) + the status chips' measured height + 8 + 40 of visible painting. On a narrow slot Core's statuses are no longer in the nameplate: they are brass chips over the painting just above the nameplate (like a unit's buff icons), measured with `ChipsHeight`. The nameplate (name, HP, crack, weak, intent) takes the rest and scrolls inside itself only if it ever needs more. `Standing` is always called on a narrow living slot.
2. **P3 implementation_defect — reduced motion switched on mid-press let the press ease back over a few frames.** `UiPress.Update` now snaps scale and position at once while `Reduced` or the caller's calm predicate holds.
3. **real_constraint (performance) / optional_opinion (still an information-table impression on five-enemy fights)** — recorded in the README; the five-enemy fight keeps every enemy's intent on the stage by design.

Attached: `after/battle_five.png`, `after/battle_five_phone_2340x1080.png`, `after/boss_b_bell.png`, `after/boss_b_garden.png`.

## Round 4 (after fixes for round-3 session 01a14e21-24d3-77f2-93e6-45fa57f30354)

Round 3: BLOCKING_DEFECT. Finding and what changed:

1. **P2 implementation_defect (blocking) — with a status row the narrow nameplate scrolled and hid "다음 …" of the intent.** The intent badge is now pinned: whenever a nameplate (narrow or wide) needs more than its room, the badge stays at the nameplate's bottom, always in view, and only the details above it (name, HP, crack, weak, wide-slot statuses) move into the `InfoScroll`, whose height ends above the badge. `Overflow(info, needed, height, keep)` takes the scroll height and the child it must leave in place.
2. **Test gap (Codex noted `AssertWhole` cannot see a parent `RectMask2D`).** `ExpeditionUiLayoutPlayModeTests` now calls `AssertIntentsInView` on every battle frame its walk settles on (every check and every 16th step, all four densities, statuses coming and going): each living enemy's `이번  …` label must lie wholly inside every `RectMask2D` above it (the stage band `Enemies`, which scrolls whole slots past six enemies, excepted). A mutation run with the intent scrolling again is recorded in the README.
3. real_constraint (device performance) and optional_opinion (five-enemy fights still read as information-heavy) — unchanged, recorded.

## Round 4 result

Session `01a14e29-280a-7f32-8c00-02329b45c5ff`: **NO_BLOCKING_DEFECT**. Codex confirmed the pinned intent, the mask check and the mutation XML. Remaining: real_constraint — device redraw cost unmeasured (19.3 → 27.3 ms in the batchmode editor); optional_opinion — the five-enemy fight still reads as five information plates under small paintings, the trade-off of keeping every intent always visible. Full PlayMode suite on this code: 342 total, 340 passed, 0 failed, 2 Explicit skipped.
