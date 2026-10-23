# Opposite-author patch review — P1-EXPEDITION-PATCH-REVIEW

- Reviewer: Claude Code (Opus 5.5, High), included subscription only. Read-only review: no Bash, no MCP, no agents, no build or test runs. My CLI execution evidence: NOT_RUN (Root holds the session metadata).
- Inputs: `MemoryForgeExpeditionClient.cs.root.diff`, `ExpeditionClientPlayModeTests.cs.root.diff`, the current A copies of `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs` and `Assets/Tests/PlayMode/ExpeditionClientPlayModeTests.cs`, `core/MemoryForge.Core/{MemoryV2.cs,Run.Memory.cs,Run.Progression.cs,Run.Services.cs}`, `prototype/index.html` viewForge, and `docs/design_v0.8.1.md` (lines 767, 1038, 1094, 1270).

## Verdict: CHANGES_REQUESTED (1 concrete bug). The 4 code changes are approved once that bug is fixed.

### BUG-1 (blocking): the test still asserts the user-facing text that change 4 removed
- File and line: `client/MemoryForgeUnity/Assets/Tests/PlayMode/ExpeditionClientPlayModeTests.cs:357`
  `AssertReadable(result, t => t.StartsWith(savedLine, ...) && t.Contains("기억 버전 1"), "saved result");`
- Cause: change 4 removed `" · 기억 버전 " + Integer(Get(lastSave, "version"))` from the result memory line (`MemoryForgeExpeditionClient.cs:~764`). No label in the result view contains "기억 버전" any more. The test diff only touched line 174–175.
- Repro: run `ActualCommandsReachTheTerminalMemoryChoiceCommitAndDepartFromTheSavedVersion`. Line 357 fails with "saved result" not readable. The current rerun will not reach 6/6 PASS.
- Fix: keep the save-line check and drop only the removed text. The version is still checked through real state:
  - `SessionMemoryVersion == 1` (line 351)
  - `next.MemoryVersion == 1` (line 363)
  - the next `Create` fingerprint (line 364)

  Suggested replacement:
  `AssertReadable(result, t => t.StartsWith(savedLine, StringComparison.Ordinal), "saved result");`
  Optionally add a check that no visible text contains "기억 버전", to pin down the intended removal. The test is not weakened: the version remains asserted through state, and design §14 (line 1270) does not require showing the version to players. Lines 1270–1272 treat it as a server-side lock value.

### Change 1 — `yield return null` before the overlay raycast: APPROVE
- The positive control (line 172), the non-empty hits and non-Button top hit (179–180), the `ExecuteHierarchy` click and the no-spend fingerprint (181–182) are all kept. One real frame between the rebuild and the second pointer matches Root's repro (same frame: 0 hits; next frame: blocker on top). This fixes timing and does not loosen the assertions.
- Side effects: none. `before` is a Run fingerprint, and rendering does not touch the Run (already checked at line 163).

### Change 2 — `StartPractice` uses `MemoryV2.Migrate(data.Memory, {}, {}).ToJsonGraph()`: APPROVE
- With an empty legacy, `Migrate` (MemoryV2.cs:325–357) never enters the loop and never reads capacities. `ToJsonGraph` (112–114) gives the same 7 fields: schema, version, memory, quarantine, legacyBackup, history, receipts. `legacyBackup` is a copy of `{}`.
- One difference: `version` is `int 0`, not the old `0L`. This is harmless:
  - `Integer()` (client:977) accepts int.
  - `CommitMemoryV2` (Run.Memory.cs:251, 272) compares through `MemoryJson.Integer` and `Number`.
  - After a commit, the stored version is `long next` (287).
  - Tests compare only `memory` hashes (352) and `SessionMemoryVersion` values.
- The session-only policy is unchanged: no disk, account or server write was added.
- Non-blocking note: the test's mirror store (lines 337–342) is still built by hand. It could use the same `Migrate` call for symmetry, but it is not required.

### Change 3 — DeckSheet `FoundParts` scrolling viewport: APPROVE
- All `PartsFound` items are now rendered, so the 7th and later parts can be reached. The old `i < 6` cap was a real reachability bug.
- Layout: the viewport spans y 558–698 inside the 720-high `DeckView` panel. Nothing overlaps: the label ends at y 550 and the deck list ends at y 506. Two 66-px rows are visible (132 ≤ 140), and the rest scroll with `Clamped`.
- Buttons are children of the ScrollRect content, so the existing click paths and `interactable = !Finished(s)` stay the same. No Core state or inventory is touched.

### Change 4a — Forge offers "변주하지 않고 떠나기" button: APPROVE (with a note)
- Core: `Proceed` accepts FORGE (Run.Progression.cs:226) and clears `Pending`, so the offers go too. `ForgeOffer` does not spend a use (Run.Services.cs:56). Leaving therefore spends nothing, refunds nothing and grants nothing, and the offer cannot be rerolled because the node is left. No numbers change.
- Layout: the button sits at y 330–394 in the 420-high Forge panel, below the part cards (y 120–280). The two `Proceed` buttons are in exclusive branches, so their names never clash. The test driver calls `client.Proceed()` directly and is not affected.
- Note (not a blocker): the HTML reference `prototype/index.html:3358–3361` has no leave button in the offers view, so the reference forces a pick. This is a UI addition beyond the reference. It is allowed by Core and by the "3택 1" design text (767, 1038), and it does not change numbers. One more case to know about: after the paid event "변주를 산다" (60 coins, design 1094, fromEvent FORGE), leaving gives up the paid variation with no refund. The current label is honest enough. If wanted later, a short hint such as "이번 변주 기회는 사라져요" could be added. That is a copy decision, not a bug.

### Change 4b — removal of user-facing "기억 버전" strings: APPROVE
- The design does not require a player-visible memory version. The API `expectedMemoryVersion` (client:244), the `SessionMemoryVersion` property, the preview and the capacity text (`used`) are all kept. The session-only wording on the result, save and abandon screens is shorter but still correct: it is session-only and disappears when the app closes. The phrase "서버·계정 저장 아님" was dropped from two labels. Its meaning is still covered by "이 세션에만 있고 앱을 닫으면 사라져요", so this is acceptable.
- BUG-1 is the only side effect.

## Original requirements not lost
- These are unchanged by the 4 edits and remain PARTIAL, not removed: card-text detail, battle forecast, lobby, and the durable store (account or disk). None of them were reduced by this patch.

## Verification status
- Build/PlayMode by this reviewer: NOT_RUN. Root's rerun is in progress with no PASS yet. With BUG-1 unfixed, I expect the terminal-save test to FAIL at line 357.
- After the BUG-1 fix: Root should rerun all 6 PlayMode tests and the .NET build, and record actual PASS/FAIL. This review grants no PASS by itself.
