# Actual server reward UI — 2026-10-17 KST

v0.8.1§7/§14.3/§17.4: the optional server practice now continues from battle victory into the existing public REWARD phase. It renders published card/relic/part/evolution choices and forwards explicit take/skip/evolve/proceed actions through the accepted authoritative transport and readiness guards. No Core, numbers, price/probability, reward RNG, data, scene or persistence contract changed.

Root played the genuine server battle through8 public UI commands (journal1→9) without injecting a seed, snapshot or reward. The server published coins30 and3 card offers. Root then ran11 actual Editor uGUI/CoreHTTP assertions:

- The earned reward and current server context were displayed in the scrollable viewport.
- A raycast/Pointer click added one card, advanced the journal once, and removed the published card choices only after committed-state refresh.
- An immediate duplicate, a claimed-card repeat and absent card/relic/part/evolution choices sent no additional mutation. The entire server envelope and journal stayed unchanged.
- A raycast/Pointer proceed returned to the next real map once (journal9→11). Another proceed from MAP refused.

`actual_reward_ui.json`, `actual_earned_reward.json` and the actual probe sources preserve scope and results. Visual comparison passed92; screenshots show the actual earned/claimed/map states. Final-source full Unity Play248/248 and Edit3/3 passed with failed0/skipped0; document9 and handoff13 passed. This is Editor/uGUI evidence, not a native-mouse, standalone or physical-device result.

The original fixed-height assumption was corrected after Root traced the approved growthCap4 case. Every published evolution row remains reachable through a vertical scroll; actual positive four-row/relic/part/evolution earning was NOT_RUN in this card-only fixture. The wrappers for those paths are source-reviewed and Core-validated, not labelled as fully runtime accepted.

Official Opus5.5 High authored and corrected the source using verified included-subscription CLI calls; Codex performed the opposite-provider review and actual runtime validation. No fallback model, paid API/credits, new dependency or image generator was used. Original design/data/Core/server/transport and original scenes/settings remain protected. Related UI commits are pushed together so unchanged backend CI is not needlessly restarted for each small patch.

Remaining: network rest/forge/shop/event/archive/memory-save, ultimate/detail/log/pace controls, production startup/authentication, Android/IL2CPP/device/performance acceptance. The original complete local practice remains available. Positive relic/part/evolution runtime coverage remains a separate gap.
