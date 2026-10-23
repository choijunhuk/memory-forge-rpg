# P1-CARD-FLOW-ONE-UNSENT-INTENT — author report

- Author: Claude Opus 5.5 (`claude-opus-5-5`), requested effort xhigh, fresh official CLI session in the reused C worktree. Codex collects the session_id from the CLI metadata.
- **Author execution: NOT_RUN.** I had no Bash, Unity, MCP or Editor access, so nothing was compiled or run. Every claim below comes from reading the source. Root's actual Unity run and view inspection are the acceptance evidence.
- Edited files (all within the allowed list):
  - `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs`
  - `client/MemoryForgeUnity/Assets/Tests/PlayMode/PreparedIntentPlayModeTests.cs` — the empty placeholder, now holding 10 tests
  - `client/MemoryForgeUnity/Assets/Tests/PlayMode/ActionPresentationPlayModeTests.cs` — 2 tests adapted
  - this report
- Read-only and unchanged: Core, digest, content, resources, scenes, settings, dependencies and the other UI tests. The private legacy `EventText` mapper is untouched. No write outside scope was needed.

## 1. Requirement, role and done condition
- **Source:** original v0.8.1 §14.2/§14.3/§14.7 and the latest supplemental-reference request.
- **Role:** while the last actual play or ultimate is being shown, the player may confirm **at most one** next card. It stays local and unsent until the display ends.
  - The limit of one is a conservative local UI choice. It is not a measured K2 value and not a next/after slot count.
- **Done condition:**
  - The pending request is pure.
  - Exactly one real command is sent after the display.
  - Locks, freshness, cancel and lifecycle behave correctly.
  - Every display pace produces the same command sequence.
  - AP, fuse, RNG, retarget, fizzle, undo, save and data rules are unchanged.
  - No server ACK, wait or reconnect state is faked. Server transport remains NOT_IMPLEMENTED.

## 2. Behavior (`MemoryForgeExpeditionClient.cs`)

### Public API (read-only status plus one explicit action)
- `PreparedLimit = 1`
- `HasPreparedIntent`
- `PreparedCard`, `PreparedMode`, `PreparedTarget`
- `PreparedPicks` — returns a copy
- `CancelPreparedIntent()`

The bool contract is unchanged: `true` still means an actual Core commit. These calls return `false` with `HasPreparedIntent == true` when they only prepare:
- `ConfirmCard` during a display
- the sheet's final `PickCard` or `AutoPick` during a display

### Gate order
- **`Blocked()`** (all commands), checked in this order:
  1. recovery
  2. turn summary
  3. sheet (`PICKER_OPEN`)
  4. any display (`ACTION_SHOWING`)
  5. waiting intent (`INTENT_PENDING`)
- **`Locked()`** (selections and `ConfirmCard`), checked in this order:
  1. recovery
  2. turn summary
  3. sheet
  4. terminal display
  5. waiting intent
- Consequences:
  - A second confirm is refused with `INTENT_PENDING`, so the full slot is never overwritten.
  - Selections are refused while an intent waits. I read "changed selections require explicit Cancel first" as "selection is locked until Cancel".
  - Direct `PlayCard`, end, ultimate, service, reward, save, depart and `Do` stay refused. During the display they get `ACTION_SHOWING`; in the frame between the display closing and the dispatch they get `INTENT_PENDING`.
  - The private Do boundary therefore cannot run anything ahead of the intent.

### Prepare
- Entry points:
  - `ConfirmCard` during a non-terminal display, for a card with no picks.
  - `SendPicks` (the sheet's final pick or explicit Auto) while a display is open.
- For a choice card, `ConfirmCard` opens the existing S3 sheet even during a display. The sheet keeps its own gate.
- If the display ends while the sheet is still open, nothing happens automatically. The later explicit final pick or Auto plays at once through the normal path.
- Frozen into an immutable `PreparedPlay`:
  - the Run reference, node, battle turn and version
  - source uid, mode and target
  - a copy of the request list
  - a copy of the ordered picks
- Nothing reaches the Run, so AP, cards, RNG, receipts and time-reservation slots are unchanged. A next/after request takes its slot only when it is sent.
- Prepare runs the same check as dispatch (`Unsendable`). A request Core refuses now, a card no longer in hand or a dead target never becomes an intent; the refusal uses Core's own code.

### Dispatch ordering (`Update` → `DispatchPrepared`)
1. **Update N:** the display closes, on its timer or because of a skip. It closes in place with no Render. Dispatch never runs in the same Update (`else if`).
2. **Update N+1:** `DispatchPrepared`:
   - **(a)** Takes the intent and clears the slot first, so reentry, a later Update or a repeated skip finds nothing to send.
   - **(b)** Runs `Unsendable` on the frozen request. It checks, in order:
     - same Run, node, turn and version
     - player phase
     - card in hand
     - target alive
     - the same `ChoiceRequests`
     - every frozen pick, in order, on the shared `PickCandidates` list given the earlier picks
     - finally, Core's `BattleCheck(uid, mode, target, picks)`

     Because the client list check runs first, an invalid pick is never hidden by Core's automatic remaining-picks rule. Fewer picks than requested means explicit Auto, and the rest stay Core's choice. Core's effect predicates are not duplicated or changed.
   - **(c)** If the check fails, the intent is dropped with `INTENT_STALE` and the reason, with no fallback, refund, retarget or retry.
   - **(d)** Otherwise the new `Play(uid, mode, aim, picks)` sends the frozen target and picks, not the current selection, through the existing `SendBattle` → `Do` → exactly one public `Run.BattleCommand`. That play opens its own display.
- `PlayCard(uid, mode, picks)` is now `Play(uid, mode, target, picks)`, so behavior is unchanged.
- `SkipActionPresentation` is still display-only. It never calls Core; it only lets the next Update dispatch.

### Lifecycle
| Event | Effect on a waiting intent |
|---|---|
| Focus loss or pause (`Leave`) | Dropped first (`INTENT_DROPPED`), then the display is dropped |
| Disable | Dropped |
| Resume | Any intent still waiting is dropped, never executed, and the actual view is redrawn |
| Quarantine or new departure | Cleared |

- No restart, replay or fake server fetch happens on any of these.
- A drop notice has no timer, so a player who comes back still sees it.
- The session store, exact-comparison and failed-restart fixes are untouched.

### UI (current layout reused; no new framework)
- **While an intent waits**, the `CardActions` second row shows:
  - the frozen card and target
  - Card info
  - a brass label in the mode-button area: `미전송 사용 대기 1/1 · 즉시 사용` (next mode: `· 다음 턴 예약 요청 · 시간표 칸은 보낼 때 차지해요`), plus picks or auto when present, and on a second line `결과 표시가 끝나면 이 요청 하나만 보내요 · 바꾸려면 먼저 대기 취소`
  - `CancelPreparedIntent` (`대기 취소`) in the confirm's place
- The action line shows Core's forecast of the frozen request, with picks; unmade picks are labeled as automatic.
- The timeline is still Core's `TimelineCells` only.
- `Gate` and the in-place refresh also turn EndTurn and Ult off while an intent waits.
- `ConfirmCard` is no longer gated, because during a display it prepares. Its plain caption changes in place between `사용 대기` (display open) and `사용 확정`, so a held press survives.
- The sheet's last-pick hint uses a wording that is true whether or not the display ends while the sheet is open.
- There is no timer-driven Render. Prepare, cancel and dispatch render because they are explicit actions or command boundaries.

## 3. ActionPresentation test adaptations (deliberate behavior changes only)
- **Test 1:**
  - The confirm is now interactable during the display.
  - The old `ConfirmCard` refusal is now a prepare, which is explicitly cancelled, with purity and selection checked.
  - It was removed from the refusal loop, and the loop also asserts that no intent waits.
  - Every other refusal, purity, barrier, in-place and timing assertion is kept.
- **Test 7:**
  - The `nia_tune` confirm during the display now opens a pure sheet.
  - The sheet's lock comes before the display's: `EndTurn` → `PICKER_OPEN`.
  - Then `CancelPick`, and the old pick/auto-reach-nothing and fingerprint checks.
  - The rest of the test (sheet after display, summary barrier) is unchanged.
- Other drivers already finish displays explicitly and needed no edits.

## 4. New tests (`PreparedIntentPlayModeTests`, 10 UnityTests, NOT_RUN)
Common to all tests:
- Real Create and public commands or rays, with an independent mirror Run.
- Full `Fingerprint` (Run graph plus Run log plus battle log and events) and the whole `StorePrint`.
- Witnesses come from a bounded actual driver (`PlayThenFind`) and are asserted, never skipped.

1. **Natural end (rays):**
   - pending leaves AP, V, slots, RNG and receipts unchanged
   - waiting label and cancel are visible; no confirm or mode controls; timeline unchanged
   - second confirm, selections, `PlayCard`, End, Ult, Depart and `Do` are refused
   - the display closes in place and does not dispatch; the frame before dispatch refuses with `INTENT_PENDING`
   - the next Update sends exactly the frozen play and opens its own display
   - later Updates and skips send nothing
2. **Normal / 2x / reduced / skip / switch:**
   - Run, store, battle log and both displays' rows are identical
   - skip never calls Core synchronously
3. **Capacity:**
   - no overwrite; changed selection refused; the replaced view's old confirm and old cancel reach nothing
   - cancel keeps the selection; the freed slot can be reused; a cancelled intent is never sent
   - the ordinary play then works once
4. **Spent card, refusal and next mode:**
   - a spent card is not drawn and cannot stay selected
   - a non-living target cannot stay selected
   - an unknown mode is refused with Core's own `BattleCheck` code and no intent
   - a pending next request takes no slot; its label is checked; after dispatch the reservation appears on the timeline
5. **Choice card:**
   - final pick freezes a copy of the picks
   - explicit Auto freezes no picks
   - the display ending mid-choice triggers no automatic action; the later explicit pick plays immediately
6. **Seam: actual end beneath (plain and choice):**
   - dispatch drops with `INTENT_STALE`
   - no command, no display, nothing later
7. **Focus / pause / disable / skip-then-focus gap:**
   - dropped at once; nothing is sent while away for longer than the display
   - resume redraws and explains the drop; RunId and log are unchanged
   - the next End is accepted
8. **Held Cancel:**
   - press held across completion keeps the same view, button and EventSystem
   - release before the dispatch Update is exactly one cancel; nothing is ever sent
9. **Finisher:**
   - the dispatched play ends the battle; no intent survives
   - the terminal sheet refuses confirm, pick, auto, cancel and commands
   - the next screen's command works afterwards
10. **Summary and recovery:**
    - confirm, pick, auto and cancel are refused with `TURN_SUMMARY_OPEN` / `RECOVERY_REQUIRED`
    - nothing waits; the store is kept
    - discard recovers

## 5. Risks Root should watch
- **Mandatory witnesses that may FAIL instead of skipping:**
  - a plain play followed by another plain card (A test 1 suggests the opening has one)
  - a next-legal card after a plain play (TurnFlow says the opening offers one)
  - `nia_tune` with a pick Core's precheck accepts, after a plain play
  - a finishing card: exact forecast, one living enemy, after a plain play, within 4000 commands
- **Timing:**
  - Tests 1 and 8 need the display (at least 0.8 s at normal pace) to outlast about 5 frames.
  - Tests 1 and 8 rely on the coroutine resuming between the closing Update and the dispatch Update. `yield return null` resumes after the Update phase, and dispatch is strictly the next Update.
  - An Editor focus event mid-test drops the intent by design, the same risk A noted for displays.
- **Layout:** the 796×52 waiting label (font 19, best fit down to 14) and the longer idle strip sentence. `AssertReadable` will catch truncation.
- **Reading of "selection change":** I chose to lock selection until Cancel. If Root prefers allowing selection changes while only refusing a second confirm, that is a small change in `Locked()`.

## 6. Coverage limits and remaining real limits
- **Defense-in-depth branches with no direct public witness:**
  - With the version unchanged, hand, target and picks hold by construction. The dead-target and invalid-pick branches of `Unsendable` are defense in depth; the stale test reaches the version branch.
  - Quarantine with a waiting intent cannot be reached publicly, because every command is refused while one waits. `Quarantine` still clears it.
  - An `after`-mode pending request is handled by the same code as next mode but is not separately exercised.
- **Not implemented here:** no server transport, ACK, confirmation wait, reconnect or request id. The intent is local practice only.
- **Not verified:**
  - Android, IL2CPP and real-device input
  - the 48 dp touch target, 130% text and worst-case density
- **Not built:** no final VFX, audio or card motion.
- **No measured timing:** the 0.2/0.3/0.6 s pacing is prototype timing, not a measured target.
