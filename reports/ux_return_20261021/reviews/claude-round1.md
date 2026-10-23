**Verdict: REQUEST_CHANGES.** There are no P1 issues. Two P2 issues block integration.

**What I could and couldn't check**
- **Images:** I could not open the review JPGs. Reading `ops/ai/local/ux-ui-review` was denied by my permission settings, so I have not seen the before/after captures or the uncentered-portrait state. For the figure's position only, I read the committed HEAD captures `reports/home_hub_20261021/home_touch144.jpg` and `reports/home_hub_20261021/companions.jpg`. I make no visual claim about the new UI.
- **Diff:** I had no shell, so I could not diff against 58ab499. I reviewed the current source as it stands. Where an issue might predate this change, I say so.
- **Files read:** `MemoryForgeLobbyClient.cs` (all), `MemoryForgePracticeSession.cs`, `LobbyOverviewPlayModeTests.cs`, the related parts of `MemoryForgeLobbyRecruitment.cs`, `ExpeditionClient` (return and departure), `MomentPlayer`/`RecruitmentUi` tests, and `DESIGN.md`. I re-ran no tests or rendering.

## P2 (blocking)

**P2-1 — The companion detail screen breaks at 144 touch units, and no test covers it.** DESIGN.md requires that all 144-unit targets stay in view. The current geometry at 144 (`MemoryForgeLobbyClient.cs`):
- **`:573` `UseAsLobby`:** it sits at y 784 and grows to 144 tall, so it ends at 928, past the 900 frame.
- **`:552-554` tabs and scroll area:** the 스킬/시작 고유 카드 tabs cover sheet y 156–300. The scroll area starts at sheet y 288. It is added later, so it sits on top and its raycast image swallows taps on the bottom 12 px of both tabs.
- **`:542-547` header buttons:** `CharacterArt` and `CharacterSheet` start at y 152 and are created after `BackToRoster` and `CloseDetails`, so they draw over the bottom 16 px of those buttons.
- **Repro:** set `TouchOverride = 144`, open 동료, tap any companion, then run `AssertControlsFit(CompanionDetails)`.
- **Minimal fix:**
  - Set `float b = Math.Max(112, TouchUnits())`.
  - Start the art and sheet at `24 + b + 16`.
  - Start the details scroll at `156 + tabH + 16`, with height `620 - y - 16`.
  - Place `UseAsLobby` at `884 - b`.
  - Extend `LargeTouchControlsStayInsideTheFrame…` with `Click("Companion:sera")`, `AssertControlsFit(CompanionDetails)`, and a check that the tabs don't overlap `MenuScroll`.

This may be older than this change, but the new acceptance claims 144 coverage, so it has to be fixed or the claim narrowed.

**P2-2 — The home side of the return has no automated check.** These lines are new and untested:
- `:69` consumes the notice once.
- `:70` + `:648` preselect `HeldLength` through `Mark()`.
- `:71` skips the entry scene on return.
- `:74-79` render `HomeReturnNotice`.

`ExpeditionReturnPlayModeTests` only calls `ConsumeReturnNotice()` directly (`:116`, `:201`), and `Hold` is internal with no `InternalsVisibleTo`. The logic reads correctly to me. Without a test, though, a regression in the one-time read or in the preselection would ship silently.
- **Minimal fix:** in the existing held-practice test (around `:201-211`), before the test consumes the notice, add a `MemoryForgeLobbyClient` host. Assert that `HomeReturnNotice` shows the expected line, a second lobby shows none, `Length:full` wears the brass sprite when the held length is `"full"`, `Player.Playing` is false after Start, and `HasHeldPractice` is still true.

## Verified OK (from source)

- **`CenterSprite` (`:370-376`):** it moves the anchored position by `(0.5 - pivot) × size` before switching the pivot to the center. The rectangle stays where it was, and children are positioned from the parent's corners, so the crests and labels inside a portrait don't move.
- **Home layout at 144 units:**
  - The top account button and the 종료/설정 buttons sit side by side without overlapping.
  - The destination box starts at y 208, just below the top buttons (they end at 204).
  - The three party slots end at 350 inside the 356-tall box.
  - 출격 spans 580–724, and the dock starts at 740.
  - `NextCompanion` (564–708) stays inside the identity box.
  - The left route tiles end at 536.
- **Roster at 144:** the filters span 144–288, below the header at 130. The grid starts at 304 and the footer at 838. The empty-state button ends at 764.
- **Preparation at 144:** 출발 spans 720–864.
- **Missing content (`ShowLoadError`):** a null `content` is handled at `:193`, `:199`, `:633-643` and in `PartyNames`.
- **Ownership:** the 보유 filter, badges and 공명 come only from `recruitmentSession.Recruitment.OwnedChars`. The practice party is labelled 편성, never 보유. When the account isn't connected, the empty state offers 계정 연결 and goes to recruitment with an empty credential field.
- **Routes:**
  - Both detail entry points work: a grid tile and the preview's 스킬과 카드 보기 button.
  - BackToRoster, Escape, the length choice and Depart → `NextLength` all work.
  - The home never calls `Take()`, so it never creates a run, and a notice left unread after a load error survives the reload.

## P3 (optional)

1. **`:518-520` unlabelled crest in the roster preview:** when the preview character has no art, it shows a bare crest with no 준비 중 label. That breaks the "labelled crest" rule and can happen under the 보유 filter (for example if Rien is first). Add the same label the tiles use.
2. **`:512` the preview is static.** It always shows the first character in the list. Under 보유 with no results it shows 세라's passport. Consider having it follow the last-opened `detailId` when that character is in the filtered list, and hiding it when the list is empty.
3. **`:516` the preview art shrinks with touch size.** Its height is `392 − 2T`: 104 px at 144, negative above 196. Clamp it, or drop the art at large touch sizes.
4. **`:509`, `:578` resonance can go negative.** `Math.Min(6, copies − 1)` lacks Core's `Math.Max(0, …)` floor.
5. **`:477` stale ownership is shown as current.** The roster shows 보유 from a snapshot that needs a refresh with no "마지막 확인" qualifier, while the recruitment panel and the account button do distinguish it.
6. **Duplicate routes:** the home now has three routes to recruitment (account button, left tile, dock) and two to companions. That is the "stack of equal buttons" DESIGN.md warns against. Consider giving the left tiles distinct real information or removing one of each pair.
7. **Representative resets on return:** after an expedition round trip the home always shows `companions[0]`, losing the chosen representative.
8. **`:199` error screen text:** "전체 0명의 동료" shows behind the error modal. Use neutral text while content is unavailable.
9. **Older layout nits:**
   - Scene-list rows (`:770`) are 144 tall on a 132 step, so their hit areas overlap inside the scroll area.
   - `Kit()` images that preserve aspect still use the top-left pivot. That is harmless if the ring sprites are square.

**Composition:** the HEAD capture puts the figure at about x 490–960. The new home elements (left tiles ending at x 336, the right column from x 1040) clear it, except the identity box and 교체 button, which overlap the lower coat as before. Whether the screens are actually less empty can only be judged from the captures I couldn't open. The 3-column catalogue shows about 4.5 tiles where HEAD showed about 7.5, which only pays off if the passport becomes dynamic (P3-2).

The known missing art for three characters is still labelled on the tiles and the detail screen, but not in the preview (P3-1). This review does not cover feature completeness or the separate P2 Resume Load/Create repair.
