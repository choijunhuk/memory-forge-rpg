# P1-CARD-FLOW-FINAL-REVIEW — independent final review of the one-unsent-intent unit (B)

- **Reviewer:** Claude Opus 5.5 (`claude-opus-5-5`).
  - Requested effort: max.
  - Fresh session; not the author's history.
  - The applied effort and the session_id are in Codex's CLI metadata. I can't see them.
- **Scope:** a read-only static review of this worktree's files at review time.
  - I edited only this file.
  - I used no Bash, MCP, Unity, agents or images.
- **Reviewer execution: NOT_RUN.** Every PASS cited here is Root's receipt, not my run.
- **Source identity:** I had no shell, so I made no hash or diff, and file scope is not diff-verified.
  - Main: the stack lines in `prepared_draft_old135_result.json` are `MemoryForgeExpeditionClient.cs` :299, :328, :511, :717 and :805. All five match the reviewed Main exactly, and all lie after the B insertions.
  - Test files: no such witness exists for them.
  - Root should bind this review to the source hash used for FULL145.

## Verdict

**CHANGES REQUESTED — one P2 (P2-1).**
- No P1 found.
- Items 1–5 otherwise hold on source.
- Root's edit (item 5): **APPROVE**.
- The three P3 items are non-blocking.

## P2-1 — Confirm and Cancel share one rect, so a repeated tap re-sends a cancelled request

**Where.** Both buttons are drawn in `CardActions` at the same rect, (1360, 38, 158, 52):
- `ConfirmCard`: `MemoryForgeExpeditionClient.cs:1316`
- `CancelPreparedIntent`: `:1355`

**Why the point toggles.**
- `Prepare` (`:433`) and `CancelPreparedIntent` (`:442`) each Render synchronously.
- Cancel keeps the card, mode and target (`:437-443`). So the redrawn `ConfirmCard` at that same point is interactable (`:1317`).
- Its caption is "사용 대기", or "사용 확정" once the display has closed.
- `PreparedIntentPlayModeTests.cs:354` already asserts that this confirm is back and interactable.

**Reproduction** (derived from source; NOT_RUN by me):
1. Set up: mirror session, a non-terminal display, a plain next card confirmed.
2. Tap the Cancel center with a real EventSystem tap, as `ExpeditionTurnFlowPlayModeTests.TapAt` does.
3. `yield return null`, then tap the same point again.
4. What source says happens:
   - The second tap hits the new `ConfirmCard`.
   - `HasPreparedIntent` is true again, with the same request.
   - The Update after the display closes sends it, so the mirror needs one more `play`.
   - If the cancel landed in the close→dispatch gap, the second tap is an immediate `PlayCard` instead.
5. The reverse case: two taps on "사용 대기" prepare and then cancel. That fails safe, but it silently undoes the confirm.

**Why it blocks.**
- Accepted tests already require two taps at one point to be one action:
  - `TwoTapsOnEndTurnAtOnePoint…`
  - `WinningEnd…RepeatedTapCannotProceed`
  - `LosingEnd…CannotCloseWithoutSaving`
- S4A placed its terminal sheet so that a repeated tap "reaches no reward or save".
- After an actual play, `selectedMode = null` (`:515`) redraws the confirm as a disabled "방식 선택", so a repeated confirm is inert there.
- B adds a same-point path that commits a card play the player explicitly cancelled. Battles have no undo.

**Minimal repair** (client UI only; no Core, rule or value change):
- Rules while an intent waits:
  - Only a pure control may sit on the selection row's `ConfirmCard` rect.
  - Cancel must not sit where the redrawn selection row has a consequential control.
- Option: in `PreparedControls` (`:1349-1356`), swap Cancel and the waiting row's card-info button.
  - Cancel moves to (388, 38, 148, 52). That is the selection row's pure `CardInfoButton` rect (`:1302`).
  - Card info moves to (1360, 38, 158, 52).
  - After an actual dispatch, `:515` disables the confirm again, so the swap opens no new path.
- Add ray tests:
  - Two taps at Cancel's point give exactly one cancel. After the display plus one Update, the mirror is unchanged.
  - Two taps at Confirm's point during the display leave one waiting intent.
- Rerun FULL.

## Items 1–5 (source review)

### 1. Frozen unsent card — holds
- **Frozen state:** `PreparedPlay` (`:130-138`, built at `:426-427`) holds:
  - the Run reference, node, turn and version;
  - uid, mode and target;
  - copies of the requests and picks.
- **Pure reads:** `Prepare` and `Unsendable` only read:
  - `Run.State`, which is a copy (`Run.cs:19`);
  - `BattleEffectiveCard`;
  - the client's `PickCandidates`;
  - `BattleCheck` with picks: `Run.BattleQueries.cs:26-27` → `Battle.Check`/`Precheck`, which only read and only peek the overload cost (`Battle.PlayerActions.cs:53-88,111,117-122`).
- Render's forecast of the waiting request is the detached `BattleForecast`.
- **No mutation before dispatch:**
  - Nothing changes AP, cards, RNG, receipts or slots.
  - A next/after request takes its Core slot only when it is sent.
- **The limit of 1:** `PreparedLimit = 1` is documented as a local UI cap. It is not a measured K2 and not a slot (`:104-105`; label `:1362`).

### 2. Ownership and dispatch — holds, apart from P2-1
- **Gates** (`:829-857`):

  | Gate | Order |
  |---|---|
  | `Blocked` | recovery → summary → picker → any display → waiting intent |
  | `Locked` | recovery → summary → picker → terminal display only → waiting intent |

- **Refused commands:** direct Play, End, Ult, Do, Depart, StartPractice and services. The code is `ACTION_SHOWING` during the display and `INTENT_PENDING` in the gap after it.
- **What only prepares:** Confirm (`:359`) and sheet completion (`:412`), and only during a non-terminal display. Both return false.
  - A terminal display refuses the confirm (`:838`).
  - A sheet cannot be fresh under a terminal display.
- **A sheet and an intent can't coexist.**
  - A sheet opens only from `ConfirmCard`, after `Waiting` has passed.
  - `Prepare` is reached only from `ConfirmCard`, which a sheet refuses, or from `SendPicks`, after `picker = null`.
- **`DispatchPrepared`** (`:449-458`):
  1. It takes the intent and clears the slot first.
  2. It rechecks the frozen request.
  3. If it still holds, it sends exactly one `Play` with the frozen uid, mode, target and picks: `SendBattle` → `Do` → `Run.BattleCommand`.
  4. Otherwise it drops the intent as `INTENT_STALE` with the reason. There is no fallback, refund, retarget or retry.
- **Timing:**
  - Update's `else if` (`:238-239`) keeps the natural close and the dispatch in separate Updates.
  - Skip and the timer only close the display in place (`:914-918`, `:980-984`).
- **Core rules, not client retargets:**
  - A null frozen target with one living enemy uses Core's existing precheck default (`Battle.PlayerActions.cs:65`).
  - A reserved card whose target has died follows Core's existing resolution rule.
- **Explicit Auto** sends only the picks made so far.

### 3. Lifecycle — holds
- **Drops:**
  - `Leave` drops the intent before the display (`:244`), so closing the display cannot release it.
  - Disable drops it (`:232`).
  - Resume drops any intent still waiting and redraws. It never sends (`:248-256`).
  - Depart clears it (`:303`), and Depart is gated anyway.
  - Quarantine clears it (`:782`).
  - An intent cannot exist alongside a terminal display.
- **In place vs Render:**
  - Close, skip, pace and notice expiry rewrite in place (`:987-1012`, `:259-263`).
  - Render happens only at explicit or command boundaries: prepare, cancel and dispatch.
  - The held-Cancel test documents that a release after the dispatch reaches nothing.
- **Earlier units unchanged:**
  - S2: `Do`, `SameToken`, `Quarantine` and `DiscardFailedPractice`.
  - S3: `Fresh`, `PickCandidates`, `RetirePicker` and cancel.
  - The only change is the documented `SendPicks` branch.
- **Presentation reads** only public zones, plus Core's forecast, which hides draws.
- No value, rule, monetization or deployment change.

### 4. Tests — acceptable
- **Setup and input:**
  - A real `OnEnable` → `Run.Create`, plus an independent mirror from `CoreRun.Create`.
  - Commands go through public handlers or EventSystem rays.
- **Comparison:**
  - the complete `Fingerprint`: Run graph, Run log, battle log and events;
  - the whole `StorePrint`.
- **Witnesses and skips:**
  - Witnesses are mandatory; `PlayThenFind` asserts them.
  - There is no `Assume`, `Ignore`, `Inconclusive` or `yield break`.
  - The conditional skips at `:403/:453/:465/:529` are each followed by an assert that the intent still waits.
- **Seams** are documented reflection only (`:8-12`, `:942-953`):
  - one actual Core `end` on the private Run, mirrored on the mirror Run;
  - the private `Do`, `store` and `data`. The content fault is restored in `finally`.
  - There is no expected-state injection.
- **Lifecycle simulation:** focus and pause use `SendMessage`; disable uses `SetActive`.
  - These are Editor component-lifecycle invocations.
  - They do not test OS focus, Android activity pause order, multi-window or physical touch.

### 5. Root edit at `ActionPresentationPlayModeTests.cs:436` — APPROVE
- The edit splits the declaration into `string committed = …; string? runId = client.Snapshot!.RunId;`.
- Evaluation order is unchanged: committed → runId → version.
- `RunState.RunId` is `string?` (`RunState.cs:58`).
- The nullable annotation has no runtime effect.
- The assertion at `:455` is unchanged.

## P3 (non-blocking)

- **P3-1 Twin CS8600.**
  - `PreparedIntentPlayModeTests.cs:567` has the same declarator, `string … runId = client.Snapshot!.RunId;`, under `#nullable enable` (`:13`).
  - The compile that reported the ActionPresentation CS8600 reports this one too.
  - The 10/10 run shows it is at most a warning.
  - If acceptance claims 0 warnings (as S3 did), apply the same split there. It is runtime-identical, but it is a B-file edit and needs the same opposite review.
- **P3-2 Claim to narrow.**
  - Author report §2(b) says "an invalid pick is never hidden by Core's automatic remaining-picks rule". That overstates it.
  - `PicksHold` rejects only frozen picks that are no longer on the shared prototype list.
  - Effect predicates stay Core's. For example, tune excludes rarity C and cards that already carry the part (`Battle.Effects.cs:286`); costdown, recall and growPick are similar. `TakePick` (`Battle.PlayerActions.cs:260-274`) can still substitute a different card at send.
  - This is the unchanged boundary S3 already disclosed. The acceptance text should keep that disclosure.
  - The code comment at `:459-463` is accurate.
- **P3-3 Receipt wording.**
  - In `prepared_live_before_send.json`, `queuedReturnMeansNoCommit:false` reads as the opposite of the claim. Name it as ConfirmCard's raw return value.
  - The live stale probe used reflected private dispatcher validation. The real-Update stale path is the NUnit test `ACommittedChange…`.
- **Observation (no change requested).** An intent confirmed before the player opens the header's Deck, Log or Abandon sheet is still sent beneath that overlay. Its rows still reach the battle log. This is not a lifecycle case.

## Evidence status

- **Root's actual runs:**
  - Old 135 Play with B: 135/135 PASS, 0 skip (`prepared_draft_old135_result.json`; the file is named "draft").
  - New 10: 10/10 PASS, 0 skip, 14.46 s (`prepared_initial10_result.json`).
  - Live scene (`prepared_live_before_send.json`, `prepared_live_after_send.json`):
    - pending at AP3/V2, then the real Update dispatched it to AP2/V3;
    - the complete JSON equals exactly two mirror commands;
    - the store is unchanged.
  - Boundary probes: Root-computed booleans. The probe source is not in the receipts.
- **Pending or not supplied to me:**
  - FULL145: the first run with final B and the Root edit together.
  - An EditMode result for this source.
  - A screenshot of the waiting row.
- **Not verified:**
  - a new native build or physical input;
  - Android, IL2CPP or a device;
  - 48 dp targets, 130% text, worst-case density;
  - OS-level focus and pause.
- **NOT_IMPLEMENTED:** server, ACK, reconnect and request id.
- The timing is prototype timing, not a measured value.
