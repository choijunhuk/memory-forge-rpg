# P1-EXPEDITION-NATIVE-INPUT-DIAGNOSIS — native pointer input, source review

- Reviewer: Claude Code (Opus 5.5, requested effort xhigh), included subscription only. This was a read-only review: no Bash, MCP, agents, credentials or images were used. No code, scene, settings, test or data file was changed. This report is the only file written.
- Everything here comes from reading source and project settings. **Nothing was executed (NOT_RUN).** No Unity, native player, screenshot or log was observed by this reviewer. The built Expedition scene is not in this worktree (`Assets/Scenes` holds only `SampleScene` and `MemoryForgeMcpSmoke`; `EditorBuildSettings` lists only `SampleScene`), so I could not read its contents.
- session_id: not visible to this worker. Root records it from the CLI metadata.

## Verdict: INCONCLUSIVE — no source defect found that explains the native miss

- I found nothing in the source that would stop a native click from working on the idle MAP. The path from mouse button to node is ordinary uGUI, and every object in it is built correctly (facts F1–F4).
- I found one real but narrow robustness weakness, W1: a click is lost if a full view rebuild happens while the button is held down. The battle practice has the same weakness. On an idle map, the only automatic rebuild is the departure toast expiring once, at 2.6 s. So W1 cannot explain repeated 180/400 ms clicks made later on that map.
- I am **not** claiming the product is broken, and I am **not** claiming the CUA is at fault. Problems with focus, delivery, coordinates or timing are all still possible. Each one leaves a different trace, which the checks below are designed to separate.

## Facts from source

**F1 — Native input path.**
- `Render` creates the EventSystem only when `Application.isPlaying && EventSystem.current == null`. It is a new `ExpeditionInput` GameObject with `EventSystem` and the legacy `StandaloneInputModule`, parented under the view's own canvas (`MemoryForgeExpeditionClient.cs:427-428`).
- The clean test scene asserts that exactly one active EventSystem exists and that the view owns it (`ExpeditionClientPlayModeTests.cs:85-87`).
- **Unknown for the built scene:** if it already contains an EventSystem, the view creates none, and that object's module is the one in use. It might be an `InputSystemUIInputModule`. Root should check this in the Editor, or read it from the probe's `module=` field.
- The `MemoryForge.Client` asmdef references only `MemoryForge.Core` and `UnityEngine.UI` (`MemoryForge.Client.asmdef:3-6`). The client code itself never reads the new Input System.

**F2 — Render lifecycle on MAP.**
- Every `Render` calls `RemoveView` first (`:391`). That does `SetActive(false)` and then a deferred `Destroy` of the whole owned canvas (`:1138-1139`), including the child EventSystem.
- `SetActive(false)` disables the old EventSystem immediately, so `current` becomes null and `:427` creates a fresh EventSystem and module on every Render.
- On MAP, Render runs in only two cases:
  - in `OnEnable` → `StartPractice` → `Depart` (`:88-92`, `:133`);
  - once more from `Update`, when the departure toast set at `:132` (`realtimeSinceStartup + 2.6f`) expires (`:97`).
- After that, nothing redraws MAP until a command runs. `Update` only calls `FitView` (`:1126-1133`), which changes `frame.localScale`/`anchoredPosition` and nothing related to input.

**F3 — The node button** (`:476-478`, helpers `:1142-1164`).
- The button's own `Image` has `raycastTarget = true`. Every Box, Label and Connector under it or nearby has `raycastTarget = false` (`:1146`, `:1152`, `:1124`). The top raycast hit at the node is therefore the `Node:<id>` GameObject itself.
- `interactable = reachable`, and the button is drawn in Brass with "갈 수 있음" only when it is reachable. `onClick` → `EnterNode` → `Run.Enter`.
- The map panel is a plain Box, not a `Scroll`. No `IDragHandler` (ScrollRect) sits above a node, so the drag threshold cannot cancel a click on MAP. The 65% input blocker exists only while a sheet is open (`:1102-1109`).
- Tints: `highlightedColor` is (1.1,1.1,1.1), which clamps to no visible hover change. `pressedColor` is 0.78, clearly darker while held. `disabledColor` is 0.45. `selectedColor` is left at the uGUI default of about 0.96, slightly darker after a press selects the node (`:1160`).

**F4 — What the passing tests cover.**
- `PointerAt`/`Click` build a `PointerEventData` at the button's center, run `EventSystem.current.RaycastAll`, and call `ExecuteEvents.ExecuteHierarchy(..., pointerClickHandler)` directly (`ExpeditionClientPlayModeTests.cs:115-119`, `:680-700`). That is the same as Root's Editor click.
- These tests prove the raycast target and the onClick → Core wiring. They **never** exercise:
  - `StandaloneInputModule.Process`;
  - `Input.GetMouseButtonDown/Up` and `Input.mousePosition`;
  - the module's focus check;
  - the pairing of press and release into a click.

  The native miss sits entirely in that uncovered layer.

**F5 — Settings.**
- `runInBackground: 0` (`ProjectSettings.asset:85`), `activeInputHandler: 2` (`:675`), `fullscreenMode: 1` (FullScreenWindow, `:109`), `macRetinaSupport: 1` (`:84`).
- `companyName: DefaultCompany` and `productName: 2D_URP` (`:15-16`) mean the Player.log is at `~/Library/Logs/DefaultCompany/2D_URP/Player.log`.
- The InputManager has the default axes. `Submit` = `return` / `enter` / `space` (`InputManager.asset:249-271`). That matters because a missing axis would spam exceptions in Player.log, and Root reports the log as clean.

**F6 — Keyboard.**
- No `firstSelectedGameObject` is set, and the view never selects anything. Arrow keys, Return and Space therefore reach no button until a pointer press has selected one.
- A keyboard-only check cannot show whether pointer input arrives. It is useful only *after* a click (check 3).

**F7 — The coordinates fit the layout.**
- The map always has 7 floors plus the boss, so there are 8 columns, and floor 1 always has 3 nodes (`Run.cs:162-165`).
- Assuming Unity's `Screen` is 4112×2580 with no safe-area inset:
  - scale is 2.57 px per frame unit, with a 133.5 px letterbox at top and bottom;
  - node `z1f0n0` spans x 123–535 and y 879–1048 from the top, with its center at (329, 964).
- Screenshot point (330, 970) is therefore about 80 px from the node's nearest edge, and the earlier point (160, 490) lands on the HP bar of `RunStatus`, as Root saw.
- So a small coordinate error cannot explain the miss. Only a gross mismatch inside Unity could, such as a factor of 2, a flipped y axis or a menu/notch offset.
- In Unity's bottom-left screen coordinates, a correct click should read as `Input.mousePosition ≈ (330, 1610)`. If Unity's Screen were in points (2056×1290), it should read about (165, 805). If the safe area is inset, use the rects the probe logs rather than these numbers.

**F8 — The battle practice uses the identical pattern.**
- It has the same toast-expiry Render, the same canvas-owned `BattleInput` EventSystem with `StandaloneInputModule`, and the same `SetActive(false)`+`Destroy` (`MemoryForgeBattleClient.cs:41-46`, `:179-180`, `:324-331`). Nothing about input is specific to the Expedition client.
- If a native battle-practice build responds to the same CUA clicks, that is a useful control.

## uGUI package behavior this relies on (verify in `Library/PackageCache/com.unity.ugui*/…/EventSystem.cs`, `InputModules/StandaloneInputModule.cs`, `PointerInputModule.cs`)

1. `StandaloneInputModule.Process()` returns immediately when `!eventSystem.isFocused` in a player (`ShouldIgnoreEventsOnNoFocus` is true outside the Editor). `isFocused` is changed only by `OnApplicationFocus`.
2. `EventSystem.Update` runs only on `EventSystem.current`. On the frame it first activates a module, it does **not** call `Process()`.
3. A click requires the press and the release on the same module's pointer data, and the release must be over the same click handler.
4. A graphic has `depth == -1`, and so cannot be raycast, until its canvas has been rendered once. Root's repro matches this: 0 hits in the same frame as a Render, hits on the next frame.

Separately, Unity documents that with `runInBackground` off, a standalone player pauses its loop while unfocused.

## W1 — confirmed in source, narrow, not shown to be the cause

A full rebuild (F2) destroys the pressed Button and the module that recorded the press. A press already in progress therefore never becomes a click.

For one frame after a rebuild:
- the new module only activates and does not process (package behavior 2);
- the new graphics cannot be raycast (package behavior 4).

On an idle MAP this only affects:
- a click that overlaps the toast expiry at 2.6 s of realtime after `OnEnable`; or
- the first resumed frame, if the player was paused before 2.6 s had passed. In that case the first activating click can be lost.

In battle, every command starts a new 2.6 s toast, so the window comes back more often. This is a robustness fix for a separate, owned and reviewed change; it is not made here. Options include keeping the EventSystem outside the rebuilt canvas, or updating the toast label without rebuilding. It does not explain repeated misses on an idle map.

## Hypotheses and what tells them apart

| # | Hypothesis | Observation that confirms it | Observation that rules it out |
|---|---|---|---|
| H1 | The player loop is paused or unfocused when the click arrives (`runInBackground` 0; AXRaise is a window action and may not activate the app) | The notice line still shows the departure toast more than 3 s after launch; the probe shows a heartbeat gap, `appFocused=False` or `OnApplicationFocus False` around the click | Steady heartbeats and `focused=True` at the click |
| H2 | The OS click never reaches Unity's input | Heartbeat running and focused, but no `down=True` at the click; cursor moves also missing from `mouse=` | `down=True`/`up=True` logged at the click time |
| H3 | Unity sees a different position | `down=True`, but `node=none` and `mouse` far from the logged node rect (for example a factor of 2, flipped y or an offset) | `mouse` inside `Node:z1f0n0*` |
| H4 | Something else is on top in the built scene, or a different or unfocused EventSystem/module is in use | First `hits` entry is not the node; `es=` is not `ExpeditionInput`; `module=` is unexpected; or `over=False` while `hits` contains the node | First hit is `Node:…`, `over=True`, `selected=Node:…` after the down frame |
| H5 | The press arrives but the click is lost (for example W1) | `selected=Node:…` after down, `phase=MAP` after up, and `view=` changed between down and up | The phase becomes BATTLE after up |
| H6 | The click worked but the screenshot is stale | Probe shows `phase=BATTLE` while the screenshot still shows MAP | Phase and screenshot agree |

## Cheapest checks first (no code; all owned by Root)

1. **Read the existing screenshots' bottom notice line** (`:450-454`, `:480`).
   - More than 3 s after launch, the teal line "출발했어요. 밝게 표시된 칸을 골라 나아가세요." means the loop had not reached the toast expiry, which points to H1.
   - The muted "밝게 표시된 칸만 갈 수 있어요. 칸을 누르면 바로 들어가요." means the loop had run.
2. **Press and hold:** make the app frontmost, then mouse-down at (330,970), wait about 400 ms, take a screenshot, then mouse-up.
   - If the gold node darkens (pressed tint 0.78), the module received the press and the node is the hit. Look at the release next (H5/H6).
   - If it does not darken, look at H1–H4.
   - Hover shows no change by design (F3), so do not use hover as evidence.
3. **Click, then press Return or Space once.** If the battle opens, the press reached the module and selected the node, but the click was lost (H5). If nothing happens, the result is ambiguous and needs the probe.
4. **Same tool on a native AppKit control.** Reveal the fullscreen menu bar and click the app menu. This separates "the tool's clicks reach this process" from Unity's input layer.
5. **Windowed launch:** `<App>.app/Contents/MacOS/<executable> -screen-fullscreen 0 -screen-width 1600 -screen-height 900`. This takes the fullscreen Space and Retina scaling out of the picture.
6. Optionally, one hand-made click by the operator. It decides between "product path" and "automation" without telling you which layer failed.
7. In the Editor, check whether the built scene has its own EventSystem or any extra Canvas.

## Smallest reversible diagnostic (for Root to write; not applied here)

This is one new file, for example `client/MemoryForgeUnity/Assets/Scripts/Diagnostics/MemoryForgeNativeInputProbe.cs`, inside the existing `MemoryForge.Client` asmdef.
- Compile gate: `DEVELOPMENT_BUILD && !UNITY_EDITOR`, so it is absent from Editor PlayMode tests and from release builds.
- Run gate: the `-mfInputProbe` argument.
- No scene, prefab, setting, package or asmdef changes.
- It is read-only. It never sends events, selects anything or calls the client. It only creates its own DontDestroyOnLoad GameObject and creates no EventSystem.
- It uses only legacy `Input`, because that is what `StandaloneInputModule` reads, so no Input System reference is needed.
- To remove it, delete the file and its `.meta`. Keep it on a diagnostic commit and do not merge it.

```csharp
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    // DIAGNOSTIC ONLY (P1-EXPEDITION-NATIVE-INPUT-DIAGNOSIS): read-only native pointer probe, Development player + -mfInputProbe.
    sealed class MemoryForgeNativeInputProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Environment.GetCommandLineArgs().Contains("-mfInputProbe")) return;
            var probe = new GameObject("MFNativeInputProbe"); DontDestroyOnLoad(probe); probe.AddComponent<MemoryForgeNativeInputProbe>();
            Log("start screen=" + Screen.width + "x" + Screen.height + " safe=" + Screen.safeArea + " mode=" + Screen.fullScreenMode
                + " display=" + Display.main.systemWidth + "x" + Display.main.systemHeight + " render=" + Display.main.renderingWidth + "x" + Display.main.renderingHeight
                + " dpi=" + Screen.dpi + " runInBackground=" + Application.runInBackground + " mousePresent=" + Input.mousePresent);
        }

        MemoryForgeExpeditionClient? client;
        string lastState = "";
        int lastView;
        float lastBeat, lastMove;
        Vector3 lastMouse;

        void OnApplicationFocus(bool focus) { Log("OnApplicationFocus " + focus); }
        void OnApplicationPause(bool pause) { Log("OnApplicationPause " + pause); }

        // LateUpdate runs after this frame's EventSystem.Update, so selection and phase show what this frame's input did.
        void LateUpdate()
        {
            bool down = Input.GetMouseButtonDown(0), held = Input.GetMouseButton(0), up = Input.GetMouseButtonUp(0);
            Vector3 mouse = Input.mousePosition;
            var es = EventSystem.current;
            if (client == null) client = FindObjectsByType<MemoryForgeExpeditionClient>(FindObjectsSortMode.None).FirstOrDefault();
            var view = client == null ? null : ActiveView(client.transform);
            int viewId = view == null ? 0 : view.GetInstanceID();
            string state = "es=" + (es == null ? "null" : es.name + "#" + es.GetInstanceID() + " focused=" + es.isFocused
                    + " module=" + (es.currentInputModule == null ? "null" : es.currentInputModule.GetType().Name)
                    + " selected=" + (es.currentSelectedGameObject == null ? "null" : es.currentSelectedGameObject.name))
                + " appFocused=" + Application.isFocused + " view=" + viewId;
            bool moved = mouse != lastMouse && Time.unscaledTime - lastMove >= 0.2f, beat = Time.unscaledTime - lastBeat >= 1f;
            if (!(down || up || moved || beat || state != lastState)) return;
            if (moved) { lastMove = Time.unscaledTime; lastMouse = mouse; }
            if (beat) lastBeat = Time.unscaledTime;
            string line = "f=" + Time.frameCount + " t=" + Time.unscaledTime.ToString("F2") + " mouse=" + mouse + " down=" + down + " held=" + held + " up=" + up
                + " over=" + (es != null && es.IsPointerOverGameObject()) + " " + state + " phase=" + (client == null ? "-" : client.Snapshot?.Phase ?? "-");
            if (view != null && (down || up || viewId != lastView)) line += " node=" + NodeUnder(view, mouse, viewId != lastView);
            if (es != null && (down || up)) line += " hits=" + TopHits(es, mouse);
            Log(line);
            lastState = state; lastView = viewId;
        }

        static GameObject? ActiveView(Transform host)
        {
            foreach (Transform child in host) if (child.name == MemoryForgeExpeditionClient.OwnedCanvas && child.gameObject.activeInHierarchy) return child.gameObject;
            return null;
        }
        // Overlay canvas: world corners are screen pixels (origin bottom-left), the same space as Input.mousePosition.
        static string NodeUnder(GameObject view, Vector3 mouse, bool listReachable)
        {
            var corners = new Vector3[4]; var found = new List<string>();
            foreach (var button in view.GetComponentsInChildren<Button>())
            {
                if (!button.name.StartsWith("Node:", StringComparison.Ordinal)) continue;
                ((RectTransform)button.transform).GetWorldCorners(corners);
                bool inside = mouse.x >= corners[0].x && mouse.x <= corners[2].x && mouse.y >= corners[0].y && mouse.y <= corners[2].y;
                if (inside || (listReachable && button.interactable))
                    found.Add(button.name + (button.interactable ? "" : "(off)") + (inside ? "*" : "") + "[" + corners[0].x.ToString("F0") + "," + corners[0].y.ToString("F0")
                        + "-" + corners[2].x.ToString("F0") + "," + corners[2].y.ToString("F0") + "]");
            }
            return found.Count == 0 ? "none" : string.Join(" ", found);
        }
        static string TopHits(EventSystem es, Vector3 mouse)
        {
            var hits = new List<RaycastResult>();
            es.RaycastAll(new PointerEventData(es) { position = mouse }, hits);
            return hits.Count == 0 ? "none" : string.Join(" > ", hits.Take(3).Select(h => h.gameObject.name));
        }
        static void Log(string line) { Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "[MFProbe] {0}", line); }
    }
}
#endif
```

Notes on the sketch:
- It was not compiled by me. It only appears in a Development player build, so the first compile check is that build.
- `client.Snapshot` copies the Run state. That copy happens only on lines that are actually logged: at most about 5 per second, plus click frames.

**Run:**
1. Use the same Development build plus this file.
2. Launch with `<App>.app/Contents/MacOS/<executable> -mfInputProbe`, optionally adding `-logFile <path>`.
3. Make the app frontmost and wait 5 s. You should see 1 Hz heartbeats and one `view=` change at about 2.6 s, which is the toast rebuild.
4. CUA: move to (330,970), wait 1 s, mouse-down, hold 400 ms, mouse-up, wait 1 s. Take a screenshot, then press Return once and take another.
5. Quit and `grep "\[MFProbe\]"` the Player.log.

**Expected for a working path:**
- The `node=` list at start shows `Node:z1f0n0..n2` with rects near F7's values.
- The down line shows `down=True`, `mouse` inside `Node:z1f0n0*`, `hits=Node:z1f0n0 > …`, `over=True`, `focused=True`, `module=StandaloneInputModule`, `es=ExpeditionInput#…`, and after that frame `selected=Node:z1f0n0`.
- The up line shows `up=True`, and either the same frame or the next one shows `phase=BATTLE` with a new `view=`.

Whichever field first breaks from this sequence names the layer: H1 (focus/heartbeat), H2 (`down`), H3 (`mouse` vs. rect), H4 (`hits`/`es`/`module`/`over`), H5 (`selected` set, phase not changed) or H6 (log says BATTLE, screenshot shows MAP).

## Not done / status

| Item | Status |
|---|---|
| Source and settings review (this report) | Done (read-only) |
| Any code, scene, settings, test or InputSystem/dependency change | Not done, by scope |
| Native or Editor run, probe build, screenshots, Player.log | NOT_RUN (Root owns all Editor writes and native checks) |
| Built scene composition (extra EventSystem/Canvas) | NOT_RUN (not in this worktree) |
| A fix for W1 | Not proposed for merge here; it needs separate ownership and review |
