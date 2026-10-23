# Read-only Codex review: card details measured at canvas scale 1 (batchmode PlayMode failure)

You are an independent read-only reviewer. Do not edit, create or delete files, do not commit, do not call other agents and do not generate images. Answer in Korean or English.

Caller: Claude Opus root-delegated session (claude-opus-5-5) in worktree `.claude/worktrees/jolly-murdock-26c48b`, branch `claude/jolly-murdock-26c48b`, base `305289b`.

## Change (uncommitted, see `git diff`)

`client/MemoryForgeUnity/Assets/Scripts/MemoryForgeServerPracticeClient.cs` `Render()`: after configuring the freshly created `CanvasScaler`
(ScaleWithScreenSize, 1600×900, match 0.5), add `scaler.enabled = false; scaler.enabled = true;` with a comment.

Report: `reports/card_details_scale_20261018/README.md` (cause, before/after test XML).

## Claimed cause

`PracticeServerSessionPlayModeTests.CardDetailsShowTheCurrentServerTextWhollyScrollableReadOnlyAndCloseWhenStale`
(`Assets/Tests/PlayMode/PracticeServerBattleControlsPlayModeTests.cs:335`) failed in `-batchmode` with
"the label is as tall as its wrapped text — Expected >= 1508.049, But was 1482.0". `CanvasScaler.OnEnable` → `Handle()` runs inside
`new GameObject(..., typeof(CanvasScaler), ...)` with the default ConstantPixelSize (scaleFactor 1); the setters do not re-run `Handle()`; the
next `Handle()` is on `Canvas.preWillRenderCanvases`, after `CardDetailsView()`/`Flow()` measured `Text.preferredHeight` (which depends on
`canvas.scaleFactor` for a dynamic font). So blocks are sized at scale 1 and drawn at the screen scale (≈0.46 in batchmode). The UGUI source is
under `client/MemoryForgeUnity/Library/PackageCache/com.unity.ugui@*/Runtime/UGUI/UI/Core/Layout/CanvasScaler.cs` if present.

Evidence: before fix the single test fails (1482 vs 1508.049); after fix `PracticeServerSessionPlayModeTests` 134/134 and the full batchmode
PlayMode suite 337/338 passed, 0 failed, 1 Explicit skipped.

## Please check

1. Is the cause right, and does the re-enable really make `canvas.scaleFactor` final before any `Text` is created/measured in `Render()`
   (including `Canvas.renderingDisplaySize` being valid at that moment for a ScreenSpaceOverlay root canvas)?
2. Side effects of toggling the scaler (OnDisable resets scale 1 / ref PPU 100, event unsubscribe/resubscribe), on `FitView()` (which reads
   `canvasRoot.rect.size` every Update), on raycasts or any other view built in `Render()`.
3. Is the test expectation itself correct (not loosened), and is anything else in this client still measured at a scale other than the drawn one?
4. Anything the report states that the code or results do not support.

Return findings classified as `implementation_defect`, `design_mismatch`, `real_constraint` or `optional_opinion`, then a verdict line
`VERDICT: NO_BLOCKING_DEFECT` or `VERDICT: BLOCKING_DEFECT`.

## Round 2 (2026-10-18)

Round 1 (session `01a14da7-2038-7ae3-8d57-61e2a9e9c2ca`) returned `NO_BLOCKING_DEFECT` with one optional opinion: narrow the
report's claims about the scale value, Editor runs and visible clipping. `reports/card_details_scale_20261018/README.md` now
states only what the XML shows. The code diff is unchanged. Round 1 is re-run here in the audited invocation form; check the
same four points and whether the README now matches the evidence.
