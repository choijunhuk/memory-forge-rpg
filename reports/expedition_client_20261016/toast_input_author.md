# P1-EXPEDITION-TOAST-INPUT-SAFETY — author report

- Author: Claude Code (Opus 5.5, requested effort High), included subscription only. No Bash, MCP, agents, credentials or images were used.
- session_id: not visible to this worker. Root records it from the CLI metadata.
- **Compile and every test are NOT_RUN.** I could not execute anything.
- **Scope:** only W1 from `native_input_review.md`. This change is **not** claimed to explain the repeated native miss, and native pointer acceptance is still unconfirmed.

## Requirement and completion criterion

- **Requirement:** design §14.2 says every refusal shows a 2.6 s toast, and §14.3 needs a usable screen. The toast timer must therefore never disturb reading or pressing buttons.
- **Completion criterion:** when the toast expires while a pointer is held, the same canvas, button and EventSystem stay alive. The Run does not change. The label falls back to the screen's existing hint text and color. The later release is exactly one real command.

## Changed files

### 1. `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs`

**New fields:**
- `noticeLabel` (`Text?`), `noticeFallback`, `noticeFallbackColor`.

**New methods:**
- `ExpireNotice()` resets the same state as before: `noticeUntil = 0`, `LastError = null`, `notice = ""`. It then rewrites only the tracked label, using that screen's fallback text and color. It does **not** call `Render`.
- `TrackNotice(label, fallback, color)` records the label that is currently showing `notice`.

**Changed methods:**
- `Update`: on expiry it calls `ExpireNotice()` instead of clearing the state and calling `Render()`.
- `NoticeLine` (map, reward, rest, forge, shop, event and archive screens): tracks its label. The fallback is the same `hint` in `Muted`, as before.
- `BattleView` action line: tracks its label. The fallback is the same instruction text in `Muted`. That text was a literal before and is now a local `hint`, with an identical string.
- `SaveView` bottom line: tracks its label. The fallback is exactly the old non-error branch:
  - text: the reasons text, or "고른 변화는 확정하기 전까지 아무것도 바꾸지 않아요.";
  - color: `Danger` if there are reasons, otherwise `Muted`.

  The text and color on first display are unchanged: `LastError != null ? notice : fallback`, and `Danger` when there is an error or reasons.
- `ResultView` notice: tracks the label that exists only while a notice is shown. The fallback is `""`. The old Render left that label out, which is the same as showing empty text.
- `RemoveView`: also sets `noticeLabel = null`. Every explicit `Render` (and `OnDisable`) therefore drops the stale reference before building a new view, and expiry never writes into a replaced view.

**Why this matches the old rendering:** those four labels are the only places `notice` or `LastError` affect the drawn view. No button's interactable state, caption or overlay depends on them. Rewriting the one tracked label therefore gives the same pixels that the old full rebuild would have drawn, but without destroying anything.

**What is unchanged:**
- All text, numbers, colors, layout and data.
- Every command still calls `Render` exactly as before.
- No input-system change, raw polling, new dependency, config change or scene change.

### 2. `client/MemoryForgeUnity/Assets/Tests/PlayMode/ExpeditionClientPlayModeTests.cs`

**New test:** `NoticeExpiryDuringAHeldPressKeepsTheViewAndTheReleaseEntersTheBattleOnce`, plus a `Hex` helper for the muted color. The 8 existing tests and the SHOP seed 1235 are untouched.

1. Does a real fresh `Create` through `Open`, then waits one frame for raycastable graphics. It asserts the actual departure toast is showing. It captures the active view, `EventSystem.current`, the toast `Text` and the actual reachable `Node:` button (from `mirror.Available()`). It checks that the view's fingerprint equals the mirror's.
2. **Press:** a real `RaycastAll`, with the node as the top hit. It sends `pointerDownHandler` and records `pointerPress`, the way the input module does, and asserts the press is the button. The fingerprint is unchanged.
3. **Hold:** it waits on the component's **real** timer, for at most 6 s and until `Notice` is empty, then one more frame so a replaced view would show up. No private field is set and the timer is not shortened. It asserts at least 2.6 s of real time have passed since before `Open`.
4. **Asserts after expiry:**
   - `OwnedViews == [view]`;
   - the same canvas is active;
   - the same live, interactable button;
   - `EventSystem.current` is the same object;
   - the same toast `Text` now reads the map fallback "밝게 표시된 칸만 갈 수 있어요. 칸을 누르면 바로 들어가요." in `#98a5c0` and is readable;
   - the departure text is gone;
   - `LastError` is null;
   - the full fingerprint (Run state, log, battle log/events and RNG) equals both the pre-press value and the mirror.
5. **Release:** a real `RaycastAll` again, then `pointerUpHandler` on the press target. It asserts that the click handler under the release is that same press target and that the pointer is still eligible for a click. It then sends `pointerClickHandler`, followed by `mirror.Enter(first)`. It asserts BATTLE at `first` with no error, and that the fingerprint equals the mirror's after exactly one `Enter`.

**Expected results:**
- **RED against the old component:** expiry rebuilds the view, so the `OwnedViews == [view]` assertion fails first. The same-EventSystem assertion would also fail.
- **GREEN with this change.**

## Remaining limits

- This test dispatches events the way the module does; like the other tests, it does not use OS mouse input. It proves the view lifecycle, not native delivery.
- Native pointer acceptance (H1–H6 in `native_input_review.md`) is still with Root.
- Explicit command renders still rebuild the canvas and its child EventSystem, as the task allows.
- The battle practice (`MemoryForgeBattleClient`) has the same toast-rebuild pattern. It is outside my ownership and unchanged.
- Run time: the new test waits about 2.6 s of real time.
