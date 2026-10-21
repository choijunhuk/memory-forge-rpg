**Verdict: APPROVE.** I found no P1 or P2 issues. Both of last round's P2s are fixed, and the Codex resume-failure repair holds up under source review. This is a source and evidence review only: I edited nothing, ran no tests or builds, and used no terminal or MCP. The JUnit XML files and the acceptance JSON are the authors' runs, not mine.

**What I reviewed**
- **Patch:** `final-changes.patch` in full: the lobby, expedition client, practice session, the resume-failure, home-return, overview and navigation tests, and the docs.
- **Source:** I spot-checked the current source against the patch (for example `ReturnCompanion`, `pendingResume` and the `NextLength` lines), and they match.
- **Images I actually viewed:**
  - `before/home.jpg`
  - `before/companions.jpg`
  - `after4/home.jpg`
  - `after4/home_touch144.jpg`
  - `after4/companions.jpg`
  - `after4/companion-skills.jpg`
  - `after4/home_sortie.jpg`
  - `actual-scene/actual-home-abandon-return-notice.jpg`
  - `actual-scene/actual-expedition-result-saved.png`
- **Run records:**
  - `actual-scenes-acceptance.json`: four scene requests, in order expedition → lobby → expedition → lobby.
  - `actual-scenes-green.xml`: 10/10 passed, including the explicit test that loads the shipped scenes.
  - `resume-related-green.xml`: 63/63 passed.

## Last round's blockers
- **Details at 144 units: fixed.** The header buttons, body, tabs and scroll area now take their sizes from `TouchUnits`. At 144, the body starts at y 184 and the footer at 740. The tabs end at 300 and the scroll area starts at 316. The `LargeTouch…` test now opens a real detail screen and checks containment, overlap with the scroll viewport, and that the header buttons don't cover the art or sheet. The scene-list row spacing now scales with touch size too.
- **No coverage of the home side of the return: fixed.** `HomeReturnAcceptance` checks:
  - the notice appears once and is gone on the next home;
  - the held full length is drawn selected (brass) before any tap;
  - nothing plays, departs or gets queued automatically;
  - the hand-off is still held.

  The explicit test that loads the shipped scenes also checks memory v1 survives, Harin stays the representative, and repeated taps each open one scene.
- **The optional items from last round:** all addressed. The preview's missing-art label, the preview following the last opened companion, the empty-state preview text, the 0–6 clamp on 공명, the stale-ownership caption, the scrolling preview, the account box (no longer a duplicate recruitment button), the neutral missing-data text and keeping the representative across a return are all in.

## The resume-failure repair (Codex lane)
I traced each failure path and found no data loss and no duplicate hand-off:
- **Held until success.** `OnEnable` only uses `Peek()`. `Take()` and clearing `NextLength` happen only after `Depart()` succeeds (`ExpeditionClient:378`).
- **Rollback.** `Resume` installs the trusted store and counters before `Load`. Any failure (load throws, rule rejection, Create throws) restores the data, run, close state, store and counters. It then sets a visible `DEPARTURE_FAILED` recovery with no Run.
- **Lifecycle.** The new first line of `OnEnable` (returning, recovery or an existing run → just redraw) stops a re-enable from quietly retrying or starting fresh. `Depart`/`StartPractice` stay blocked while in recovery. Only the explicit 다시 출발 retries, once per press.
- **Return-home failure.** If the scene load throws, `ReturnToWorkshop` puts back the previous hand-off and notice. `returning` blocks a second press. The held copy is detached; the test changes the old store's version and the held version is unchanged.
- **Fresh instances.** Without `NextLength`, a client still starts a fresh practice and leaves any held state alone.
- **Ownership.** Ownership shown in the UI comes only from the server snapshot. The practice party is labelled 편성 and the preview asserts it never says 보유.

## Visual check
The screens are materially less empty, and the authored figures are untouched:
- **Home:** two real route tiles on the left. On the right, the destination and three party SD portraits, now correctly centered.
- **Catalogue:** three columns plus the ability preview. Missing art shows a labelled crest.
- **Preparation:** a full-screen layout with art, length, party, element, weapon and starting-card count.
- **Result:** compact, with each deck card sized to its rows. The way home is the brass primary button.

## P3 (optional, not blocking)
1. **Low-contrast return notice.** The notice text (`ShowHomeNotice`, `Lobby:~268`) is teal at 20 px on the bright lamp area, mostly outside `IdentityShade` (y 24–174), and part of it is hard to read in `actual-home-abandon-return-notice.jpg`. It is the only confirmation of the memory result. Fix: put a `Shade` behind the line, or grow `IdentityShade` to about 230 while a notice is showing.
2. **`NextLength` stays set after a failed resume and a return home.** The resume-failure test asserts it stays `"full"` during recovery, and `ReturnToWorkshop` never clears it. Nothing reads it in the real app, because the lobby's 출발 overwrites it. It does break the rule "opening the home never queues a departure". Fix: set `MemoryForgeLobbyClient.NextLength = null` after `Hold(...)` in `ReturnToWorkshop`, since the held state already carries the length.
3. **Escape from details doesn't refresh the preview.** `Lobby:87` calls `ShowPanel(rosterPanel)` without `RenderRoster()`, so the preview doesn't follow the last opened companion. The BackToRoster button does refresh it.
4. **Recovery wording for a failed departure.** `[RecoveryCode]` says "이 탐사를 버리고 새로 출발해 주세요". A public command blocked during a failed departure would show that, which doesn't fit. The UI only offers the two recovery buttons, so this only shows through the API.
5. **Empty band on the result card.** There is roughly 160 px of empty space between the deck cards and the footnote. Optional: let the cards grow, or move the footnote up.
6. **Font missing during resume.** If the font is missing, `Load` throws, recovery is set, but `Render` returns early, so no screen draws. Pre-existing and very unlikely.

The three missing character originals are still clearly labelled and not claimed as filled. Local practice memory stays app-session only. `PROGRESSION_DIRECTION_20261021.md` stays a proposal. This approval covers these UI and return changes only, not the overall feature.
