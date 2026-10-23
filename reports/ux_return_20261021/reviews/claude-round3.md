**Verdict: APPROVE.** None of the three changes introduces a P1 or P2 regression. I read `final-polish.patch` in full and checked the current source (`MemoryForgeLobbyClient.cs:78, 204, 263, 268-273, 436`; `MemoryForgeExpeditionClient.cs:700`). I made no edits, ran no tests, did no rendering, and read no images this round. I also didn't look at the RED XML.

1. **Notice background (`Lobby:268-273`)**
   - The new `HomeNotice` panel at (46,164,544,66) is created by `Box`, which sets `raycastTarget = false`, so it can't block input. The new test asserts this, along with alpha ≥ 0.9.
   - The label sits at (12,6) inside the panel, which puts it at the same position as before (58,170).
   - `Find("HomeNotice")` still removes the previous notice, because the panel is a direct child of `homeControls`. The 출발 failure path through `ShowHomeNotice` therefore still replaces the notice instead of stacking a second one.
   - The panel lives in `homeControls`, so it fades with the rest of the controls during moment scenes. It doesn't affect the figure or its framing.

2. **`NextLength` cleared on return (`Expedition:700`)**
   - It is cleared only after the scene loader call returns without throwing. If the call throws, the `catch` restores the previous held state and notice and leaves `NextLength` set. The held state carries its own `Length`, so the home's preselected length is unaffected.
   - The tests cover the failure case (`"full"` kept), the success case (null), and the held memory staying at v1.
   - On a normal, healthy return `NextLength` is already null, so nothing changes there.

3. **Shared `BackToRoster()` (`Lobby:436`)**
   - The Escape key on the detail screen and the visible 동료 목록 button now both call it, so the preview always re-renders from `detailId`.
   - The test reaches it with `SendMessage("BackToRoster")`, which can call private methods and finds the lobby component on the host. It then checks that the preview shows 니아. The `Input.GetKeyDown` branch itself is not exercised, but it calls the same method.

**P3 (not blocking).** Your summary says there is a gap before the left tiles at y 244. In the current source the first route tile starts at y 232 (`Lobby:204`), and the notice panel ends at y 230 (164 + 66). The gap is 2 px, so the two may look like they touch. If you want breathing room, shrink the panel to about 56 high or start the tiles at about 244. Your final recapture will show whether it matters.
