# Visible server practice acceptance — 2026-10-17 KST

Requirements: v0.8.1§14.2/14.3/14.7/17.2/17.4. `MemoryForgeServerPracticeClient` and the separate `MemoryForgeServerPractice.unity` connect the accepted transport to a visible optional Development view. The original full local expedition client and scene are retained. The new scene contains no credential, endpoint, persisted snapshot, party override or server-start operation; its factory is supplied only at runtime.

The view displays actual full-public-state-derived map, HP/AP, public enemy intents, pile counts, slot occupancy/due turns, reservations and hand. Card modes/costs/forecast/manual candidates come from the server. Chosen live target UIDs are passed unchanged. A picker is unsent; one prepared play during approved ordinary presentation is also unsent. An uncertain POST remains a durable pending request; only its explicit exact retry can resolve it. Turn/reservation presentation is a barrier and skip/continue sends no command.

| Fresh verification on exact final hash | Result |
|---|---|
| Actual Editor compile | 0 compiler errors |
| Actual Editor uGUI/Core HTTP | 24 assertions in three explicitly recorded segments |
| Ordinary action/preparation/lifecycle | select/query spends nothing; consumed UID refused; one unsent preparation/second refused; disable drops unsent only; re-enable refreshes without dispatch/replay |
| Real committed200 replaced by controlled503 | pending preserved; old-hand selection refused; reconnect shows committed full state without auto retry; explicit retry leaves the whole server envelope unchanged and replays no events |
| Corrected turn gate | selection refused while end is in-flight; full end barrier; target refused under barrier; skip/continue cosmetic |
| Fresh actual server account | connect creates no run/row; actual CreateRun and available-node uGUI raycasts create one map then enter one real battle; AP4 and actual hand visible |
| Full Unity6000.0.46f1 Play Mode | 248/248,failed0,skipped0 |
| Unity Edit Mode | 3/3,failed0,skipped0 |
| Visual comparison | initial91/pass, final92/pass; original palette/Korean font, readable separate panels |

`actual_ui_acceptance.json` states the segmentation and exact source hash. The first full final-source attempt stopped because the harness had not reselected a target after lifecycle; it is not reported as a passing full run. Subsequent recovery and map executions passed. Native automation coordinates are not accepted as physical-mouse proof; the input evidence is actual Editor uGUI raycast/Pointer events plus public lifecycle/actions. No new native build or physical-device result is claimed.

Opus High authored and corrected the source through the official CLI; Codex performed opposite-provider review/integration/runtime work. The bounded independent reviews are in `root_review.md`. Subscription authentication/account match and creditsOFF/autoreloadOFF evidence preceded both calls; no other model, paid API, credit path or image-generation service was used. Screenshots are actual Editor captures.

Existing design/data/numbers, Core/server/transport hashes, original scenes/prefabs and manifests are preserved. The separate scene is a Development test view. Original settings are restored after test options, before commit; the final live preview uses normal settings. Preceding transport01eaf8c's exact push/PR CI is independently verified in `../practice_transport_20261017/remote_ci_verified.json`; this UI's new commit CI is checked after push.

Remaining: network reward/service/archive/memory-save screens, ultimate controls, card/deck/log detail and pace controls, full application-process recovery/startup authentication, standalone/Android/IL2CPP/device/performance acceptance and production. These are not deleted from the approved design or the original local practice. This optional map/battle/recovery slice is not the whole network game.
