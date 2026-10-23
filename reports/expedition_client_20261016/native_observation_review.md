# P1-NATIVE-INPUT-OBSERVATION-REVIEW — opposite-provider check of Root's native input probe

- **Reviewer:** Claude Code (Opus 5.5, High), using only the included subscription.
- **How it was done:** read-only. No Bash, MCP, agents, credentials or images were used.
- **Not run by the reviewer:** the build, the app, the log capture and the `sourceSha256` check.
- **What was read:**
  - `native_input_probe_source.cs.txt`
  - `native_input_probe.txt`
  - `native_input_probe_build.json`
  - the current view source, for the layout
- **Not a PASS:** this review grants no PASS for the toast change or for native acceptance.

## Verdict: probe code APPROVE; the conclusion needs one correction (evidence gap) before it is approved

### The probe reports what the listed APIs return

**Value sources.** Every value it logs comes directly from a public Unity call made at that moment:

| Logged value | Source |
|---|---|
| `mouse` | `Input.mousePosition` |
| `held` | `Input.GetMouseButton(0)` |
| down/up triggers | `GetMouseButtonDown/Up(0)` in `Update` |
| `focused` | `Application.isFocused` |
| `screen` | `Screen.width/height` |
| `event` | `EventSystem.current.name` |
| `module` | `currentInputModule` type |
| `hits` / `top` / `button` | `RaycastAll` at that same `mouse` |
| `frame` | `Time.frameCount` |

**Read-only.** The probe sends no events and sets no selection, cursor or state.
- `RaycastAll` only reads.
- Its own GameObject creates no EventSystem.
- It is gated out of the Editor and release builds.

**The retained lines are internally consistent:**
- **Frame rate:** heartbeats are 5 s apart (`Time.unscaledTime`) at frames 1 → 278 → 579 → 879, which is about 60 fps. The player loop was running and focused (`focus=True`, `focused=True`).
- **Start line:** `module=none` at frame 1, then `StandaloneInputModule` from the first heartbeat. This matches uGUI activating the module on its first update.
- **EventSystem:** `event=ExpeditionInput` is the view-owned EventSystem (`MemoryForgeExpeditionClient.cs`). It means the built scene adds no other EventSystem.
- **`hits=0` at (869,1031) is the correct result for that position. It is not a raycaster fault.**
  - At 4112×2580 the frame scale is 2.57 px/unit with a 133.5 px letterbox.
  - (869,1031), measured from the bottom-left, is frame (338, 551).
  - That is inside the floor-2 column (x 240–400), but outside any node rect in the y direction: with 3 nodes they are centred at 343 ± 33; with 2 nodes, at 485.5 ± 33.
  - Every map panel, label and path there has `raycastTarget = false`.
  - So the meaningful evidence is the **position value**, not the hit count.

**Minor limits of the probe.** These are not errors, because they don't change the claim:
- It logs position only at start, at down/up and at 5 s heartbeats, so it cannot show whether `mousePosition` moved briefly in between.
- It records no `Screen.safeArea`/`Display` values.
- Neither matters to a claim about the value at the down/up frames.

### Correction required: the down/up and drag lines are not in the retained log

- `native_input_probe.txt` contains only `focus=True`, `start` and heartbeats up to **frame 879** (about 15 s).
- It does **not** contain:
  - the stated `down` at frame 2193 or `up` at frame 2218;
  - their `mouse=(869.00, 1031.00, 0.00) held=True hits=0` values;
  - the 330,970 → 340,970 drag lines.
- So the central claim cannot be checked from the receipts: button events arrived while `Input.mousePosition` stayed frozen at the launch value. What the retained file does support is narrower: the value was (869,1031) from frame 1 to 879 with no click.

**Fix:** re-export the filtered `MF_INPUT` lines (they need no private OS data) so they include:
- the click down/up (frames 2193/2218);
- the drag down/up;
- the heartbeats on either side.

The alternative is to state in the conclusion that those lines exist only in the private raw log and were not independently reviewed. After that re-export, I approve this limited conclusion with no other change:

> "Under CUA input, the legacy Input reported button down/up while `Input.mousePosition` stayed at its launch value. The raycast at that value hit nothing, and there was no battle transition."

### What the conclusion may and may not say (within evidence)

**May say:**
- At the down/up frames, Unity's legacy `Input.mousePosition` did not reflect the cursor shown in the screenshot.
- The UI therefore raycast at the stale position and no click reached the node.
- This is consistent with H3 from `native_input_review.md` and rules out H1 (focused, running loop) and H4 (the view's own EventSystem and StandaloneInputModule).

**May not say:**
- Who owns the gap: CUA event synthesis (for example, a button event with no preceding move event), macOS, or Unity's runtime.
- Whether a physical mouse works.
- That the product is broken.

Root's stated scope already keeps to this.

**Cheapest discriminating next observation** (optional, no code change, needs a person): the same probe build, with a human moving and clicking the physical mouse over the node. If `mouse=` tracks the hand movement and the battle opens, the frozen position is specific to the automation path.
