# Read-only Codex review: first-run onboarding coach (§14.5 / §3.4) over the Unity expedition practice

You are an independent read-only reviewer. Do not edit, create or delete files, do not commit, do not call other agents and do not generate images. Answer in Korean or English.

Caller: Claude Opus root-delegated session (claude-opus-5-5) in worktree `.claude/worktrees/onboarding-c82499`, branch `claude/onboarding-c82499`.

## What changed (uncommitted, new files only; no existing file was edited)

- `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeOnboardingSteps.cs` — step order, wording, show/done predicates (`OnboardingSteps.All`), the read-only `OnboardingContext`.
- `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeOnboarding.cs` — the coach component: attaches only to the client of scene `MemoryForgeExpedition` via `RuntimeInitializeOnLoadMethod` + `SceneManager.sceneLoaded`, draws a non-raycast panel over the header text and outlines around the controls to press, persists done steps / skip in PlayerPrefs.
- `client/MemoryForgeUnity/Assets/Tests/PlayMode/OnboardingPlayModeTests.cs` — new Play Mode tests.

The client it observes is `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs` (unchanged). Requirements: `docs/source_v0.8.1/14_chapter.md` §14.5 (and §14.2/§14.4 map "되돌리기 없음 안내 1회"), `docs/source_v0.8.1/03_chapter.md` §3.4, break/reservation rules `docs/source_v0.8.1/05_chapter.md` §5.5–5.7 (the step texts make claims about these rules).

Task constraints given to the author: no scene/prefab edits; guide shows only on first run and is skippable, but §14.5 says the memory-save guide cannot be skipped; existing button behaviour and numbers unchanged; no rewards/currency/numbers that the source lacks; local PlayerPrefs only; existing Play Mode tests must keep passing.

## Please check

1. Does any coach graphic or button intercept a raycast for a practice control in any view (map, battle, reward, rest/forge/shop/event, save, result, recovery, every overlay sheet)? Does the panel rect (frame x 20–920, y 2–74) overlap any interactive control?
2. Step predicates vs the client's real state: can any step get stuck forever, show at the wrong time, mark done without the player doing it, or block later steps? Lesson gating (`Needs`, `finishedIn`, `BattleKey`) and `last`/`mark` handling in `LateUpdate`.
3. Are the Korean step texts accurate to the rules (AP paid at reservation, intent '이번/다음', weakness/break −action/+25%, boss timing) and to the drawn captions? Anything invented (numbers, rewards, grants)?
4. Unskippable save guide: is it really unskippable and does it finish only on a successful commit? Interaction with "저장하지 않고 닫기".
5. Persistence: PlayerPrefs keys, reload on OnEnable, skip semantics, first-run-only behaviour.
6. Test adequacy and anything that could make the new tests flaky or break existing tests (static `sceneLoaded` hook, additive scene load in a test, PlayerPrefs pollution).

Return findings classified as `implementation_defect`, `design_mismatch`, `real_constraint` or `optional_opinion`, each with file:line and a concrete failure scenario, then a one-line verdict (APPROVE / CHANGES_REQUESTED).

## Round 2 (after fixes for round-1 thread 01a14a69-23d8-7422-a519-0470e4d2c1af)

Round 1 returned CHANGES_REQUESTED with six findings. The author changed, in the same three files:

1. `reserve.confirm` / `boss.after` finish only when a reservation uid that was not on the timeline at the mark appears (`boss.after`: of kind `after`); `battle.confirm` only when the marked card left the hand with its own action display open; ending the turn or an unsent intent no longer counts.
2. `Pick` completes a step whose action was already done when it first becomes eligible; `battle.mode`, `battle.confirm`, `reserve.next`, `reserve.confirm`, `boss.after` now show whenever a card is selected, with Alt text/targets guiding back when the mode is reset.
3. The ultimate step marks every actor whose ultimate was usable and also accepts an open action display with an UltimateDamage row.
4. The mark is re-captured on every new showing (`shown != previous`).
5. Lesson 4 (`boss.*`) now needs lesson 3. The save text states that closing without saving keeps no change; "저장하지 않고 닫기" keeps its existing meaning and the save guide (never skippable) returns at the next save screen until a real commit (design decision, not a forced save).
6. The additive scene load is unloaded in `UnityTearDown`; tests now cover the lesson-2 reservation through the real buttons, a wrong end-turn at `reserve.confirm`, close-without-save then a second save screen, and disable/enable.

Batchmode Unity runs (this worktree): onboarding class 5/5 PASS (explicit screenshot test separate). Please re-check the six findings and anything new; same classification and verdict format.

## Round 3 (after fixes for round-2 thread 01a14a74-1d7b-7192-9a0c-aa04ef18ff6a)

Round 2 returned CHANGES_REQUESTED with three P2 findings. Changes since (same three files; commits 7b99a77, 67cf8b1 and the uncommitted working tree):

1. `battle.confirm` / `battle.ult` finish on a command committed after the mark: an open action display that is a different instance (`ReferenceEquals`) from the one at the mark, with a CardPlayed/ReservationPlayed (or UltimateDamage) row — so switching cards counts any real play, and an older display never counts.
2. `reserve.confirm` finishes only on a new `next` reservation; its Alt shows unless the selected mode is `next`; `reserve.timeline` shows only with a `next` reservation on the timeline.
3. `battle.ult` is Core, so lesson 3 needs the ultimate. Boss steps have `OwnBattle = true`: they may follow lesson 3 inside the boss fight (the boss is lesson 4's only battle); otherwise lessons still start in a later battle.
4. Highlights skip the mode button that is already selected (so the next control is outlined).
5. New per root request: stable object names — root `Guide`, `Guide:Panel`, `Guide:Text`, `Guide:Next` (acknowledge), `Guide:Skip`, and one `GuideHighlight:<target name>` group per outlined control with `Top`/`Bottom`/`Left`/`Right` edges. The edges pulse (1.2 s) unless the client's pace is `Reduced` (동작 줄이기).
6. Test 1 now follows the guide through a whole short expedition (seed 1234): cleared, 6 battles, steps after lesson 2 were battle.break, battle.ult, boss.timing, boss.after, save — so §14.5 ①–⑤ are all reached with the original numbers. Test clicks call `Canvas.ForceUpdateCanvases()` first (a view rebuilt in the same frame's Update was raycast before layout).

Batchmode Unity: onboarding class 5/5 PASS. Context: the root branch adds `MemoryForgeUiSkin.cs`, which decorates `ExpeditionFrame`, `Enemy:<uid>`, `Party/Ult:<id>` and the "파티 체력" text by name and adds `UiSkin*` images (first sibling, non-raycast); the guide renames none of the client's objects. Please re-check the three findings and anything new (blocking = implementation_defect or design_mismatch that breaks the brief); same format and verdict.

## Round 4 (after fixes for round-3 thread 01a14bb9-89f7-7890-b9f0-f7d6dc0a54b6)

Round 3: CHANGES_REQUESTED, one P2 and one P3.

1. P2 fixed: `OnboardingMark.Battle` records the battle key; `battle.ult`'s gauge comparison holds only when `c.BattleKey == m.Battle`. New regression test `UltimateStepComparesGaugesOnlyWithinTheBattleItWasMarkedIn` (a full gauge marked in another battle is not done in a fresh battle; positive control in the same battle).
2. P3 adopted: the full-run test now asserts outcome `clear` and the boss steps unconditionally.

Batchmode Unity: onboarding class 6/6 PASS (explicit capture skipped); full PlayMode before this fix 320/322 (pre-existing batchmode failure `PracticeServerSessionPlayModeTests.CardDetails…` that also fails without these files; explicit capture skipped), EditMode 3/3. Please re-check; verdict APPROVE if no blocking defect remains.

## Round 5 (after fixes for round-4 thread 01a14bbc-c335-7c01-bad1-7da92af18f74)

Round 4: CHANGES_REQUESTED, one P2 (`battle.hand` finished by the next turn's auto-selected card). Fixed: `battle.hand` is done only by a real hand press (`HandClicked`) or a chosen mode; the selected-card-change condition is removed. New regression test `HandStepNeedsARealHandPressNotTheNextTurnsAutoSelection` (end turn without a press keeps the step open and unrecorded; a real hand press finishes it). Onboarding class 7/7 PASS. Please re-check; APPROVE if no blocking defect remains.
