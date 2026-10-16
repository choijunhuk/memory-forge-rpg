# Opposite-provider UI review

Official Opus5.5 High authored the optional view. Codex reviewed data binding and ran the Editor/HTTP checks; an independent native Sol6.1 Max review covered async session identity, selection/query generations, exact target UIDs, consumed cards and the separation of unsent preparation, durable pending and presentation.

The initial source (`1af79c90fbfc…`) passed that bounded source review. Actual Editor execution then exposed a gap: SelectCard returned true immediately after EndTurnAsync began. The server still committed only one command, but the input lock did not meet §14.7. `actual_flow_red.json` preserves13 successful assertions and that failure.

Opus patched the existing predicates and callers in one narrow High call. `Ready()` shares `session.State.CanMutate` plus this view's busy state. Selection and confirmation refuse in-flight/pending/stale/disconnected states; picker confirmation refuses before discarding its sheet; End cannot overtake a prepared play between skip and Update. Approved ordinary presentation still allows selection and one unsent preparation.

The targeted second Sol6.1 Max review APPROVED exact SHA `bac1214aa00e4d2b084e1bc7627df20b09969f8f57c9c059e3779ef77f885a29`, including normal picker and prepared dispatch preservation. Reviewer runtime is NOT_RUN. Root then verified that same source in actual Editor Play:24 assertions across ordinary/prepared/lifecycle, real503/reconnect/exact-retry/gates, and a fresh no-row account's explicit map→battle flow, plus full Play248/Edit3.

Test-harness corrections are separate: pointer operations need separate layout/query frames, and lifecycle intentionally clears the unsent target/card. The first interrupted final-source scenario's7 successful assertions are retained as a segment; a subsequent explicit-selection recovery scenario passed11 and the fresh map scenario passed6. We do not call the interrupted full attempt a successful run. No extra UI styling or whole-engine rewrite was required.
