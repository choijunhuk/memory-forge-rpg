# P1-NATIVE-INPUT-RECEIPT-RECHECK — recheck of the retained native event records

- **Reviewer:** Claude Code (Opus 5.5, High), using only the included subscription. Read-only: no Bash, MCP, agents, credentials or images.
- **Read:** `native_input_probe.txt` (49 `MF_INPUT` lines). This continues `native_observation_review.md`.
- **NOT_RUN by the reviewer:** the build, the app, the raw-log extraction and the SHA check. The private raw `Player-prev.log` was not visible to me.
- **Not a PASS:** this review grants nothing for the toast change or for native acceptance.

## Verdict: APPROVE

The evidence gap I raised earlier is closed. With one wording precision about the drag (below), the retained lines support Root's narrow conclusion.

## What the file now shows

**One continuous run.**
- It has a single `focus=True`, a single `start` at frame 1, and heartbeats every ~300 frames (about 5 s at 60 fps), from frame 1 to 12598.
- Every line reads `focused=True`, `screen=4112x2580`, `event=ExpeditionInput` and `module=StandaloneInputModule`, except the frame-1 `module=none`.
- The heartbeat frames (276/577/878) differ from the earlier file's (278/579/879). That fits Root's account that the earlier file came from a relaunched player and this one from the original run.

**Click.**
- `down frame=2193 held=True` and `up frame=2218 held=False`, with heartbeats at 2080 and 2381 on either side.
- 25 frames is about 0.42 s at the observed rate, which matches the 400 ms hold.
- Both lines read `mouse=(869.00, 1031.00, 0.00) hits=0 top=none button=none`.

**Drag.**
- `down frame=5015` and `up frame=5015`, the same frame, both with `held=True`, at the same `mouse` and `hits=0`. Heartbeats at 4786 and 5086 surround it.

**Stale position.** `mouse=(869.00, 1031.00)` is the launch value, and it is identical on all 49 lines. That includes both button events and every heartbeat over about 210 s.

## Wording precision (not a blocker)

- Unity received the drag's press and release **in a single frame**. `held=True` on the up line is what `GetMouseButton(0)` returns on a frame that also has a press.
- The record therefore shows one same-frame down/up at the stale position. It does **not** show the position staying frozen across frames while the button was held during a drag.
- The cross-frame evidence comes from the click (frames 2193→2218) and the heartbeats. Describe the drag as "a same-frame down/up at the same stale position", not as a held drag.

## The limited interpretation is still within the evidence

Supported:
- With the player focused and running, the view's own EventSystem and `StandaloneInputModule` were active.
- The legacy Input reported button down/up.
- `Input.mousePosition` stayed at its launch value (869,1031).
- `RaycastAll` at that value hit nothing, so no node could receive the click.

Not covered by this file:
- The probe does not log the Run phase. "No native battle transition" rests on Root's screenshots, so **native battle transition UNCONFIRMED** is the right status.
- Who owns the problem (the CUA tool, macOS or Unity) and how a physical mouse behaves are not asserted, which is correct.

## Remaining limits

- The match between this export and the private raw log (Root's SHA) was not checked by me.
- Physical-mouse behavior is still unobserved.
