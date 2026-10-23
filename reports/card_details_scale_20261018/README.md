# Card details label shorter than its wrapped text under batchmode (2026-10-18)

## Symptom

`PracticeServerSessionPlayModeTests.CardDetailsShowTheCurrentServerTextWhollyScrollableReadOnlyAndCloseWhenStale`
failed under `Unity -batchmode -runTests -testPlatform PlayMode` (6000.0.46f1):
"the label is as tall as its wrapped text — Expected: greater than or equal to 1508.04907f, But was: 1482.0f".
Reproduced here on Burst 1.8.19 (`before_fix_single.xml`). The android-16kb session reports the same numbers on Burst 1.8.29.
Earlier PlayMode runs used the live Editor through Unity MCP; its Game view size was not recorded.

## Cause: a real UI defect, not a test artifact

- `MemoryForgeServerPracticeClient.Render()` builds a new canvas with `new GameObject(..., typeof(CanvasScaler), ...)`.
  `CanvasScaler.OnEnable` runs `Handle()` inside that constructor, while the scaler still has its default ConstantPixelSize
  mode, so `canvas.scaleFactor = 1`. The `uiScaleMode` / `referenceResolution` setters do not run `Handle()` again
  (com.unity.ugui `CanvasScaler.cs`). The next run is `Canvas.preWillRenderCanvases`, which comes after the whole view is built.
- `Flow()` sets each card-details block's height from `Text.preferredHeight`. For a dynamic font that value depends on
  `canvas.scaleFactor`, because glyphs are rasterized at `fontSize * scale`. So the heights were measured at scale 1 and the
  text was drawn at the screen's ScaleWithScreenSize scale. The batchmode screen gives a scale other than 1 (not measured).
  Line heights do not scale linearly, so the wrapped text came out about 27.05 units taller than its label (`preferredHeight` 1509.05 vs 1482).
- The label uses `VerticalWrapMode.Overflow`, so the extra lines draw past the label into the next block and can pass the
  end of the scroll content. The failing XML proves only the missing height. Overlap and clipping were not checked visually.
- Any screen whose canvas scale is not 1 can show the defect, phones included. Whether it does depends on rounding and on
  the 6-unit margin. The test expectation (current `preferredHeight - 1`) is correct and stays unchanged.

## Fix

In `Render()`, after the scaler is configured, `scaler.enabled = false; scaler.enabled = true;` runs `OnEnable` → `Handle()`
again, so the 1600×900 ScaleWithScreenSize scale is in place before any text is measured. This is one line plus a comment.

Known ceiling: if the screen is resized after a build, the canvas scale changes again, and the blocks are measured again only
on the next `Render()`.

The same build-at-scale-1 construction exists in `MemoryForgeExpeditionClient.cs`, `MemoryForgeLobbyClient.cs` and
`MemoryForgeBattleClient.cs`. Of these, only the expedition client measures `preferredHeight` while building; the expedition-ui
work measures across several scales instead. Those files are not changed here.

## Tests (Unity 6000.0.46f1 -batchmode, separate worktree, Library cloned from a worktree at the same HEAD 305289b)

| Run | Result | File |
|---|---|---|
| Before fix, the one test | FAIL (1482.0 vs ≥1508.049) | `before_fix_single.xml` |
| After fix, `PracticeServerSessionPlayModeTests` class | PASS 134/134 | `after_fix_class134.xml` |
| After fix, full PlayMode suite | PASS 337/338, 0 failed, 1 skipped (`OnboardingPlayModeTests.CaptureCoachScreens`, Explicit screenshot writer) | `after_fix_full_playmode.xml` |

Codex read-only review (`gpt-6.1-sol`, high): round 1 session `01a14da7-2038-7ae3-8d57-61e2a9e9c2ca` and round 2 session
`01a14daa-63b4-7a13-9e77-5c5389aa8d7f`, both `VERDICT: NO_BLOCKING_DEFECT` (`codex_review.md`; request
`ops/ai/tasks/claude-card-details-scale-review-20261018.md`). Round 1's one optional opinion asked for
narrower claims about scale, Editor runs and visible clipping. Round 2 asked for the exact height gap. This README applies both.

This HEAD has 338 PlayMode tests. The 316 figure came from the older `claude/android-16kb-44bb0e` base, which predates the
onboarding tests. The ProjectSettings and texture `.meta` files that batchmode re-serialized were reverted before commit.
