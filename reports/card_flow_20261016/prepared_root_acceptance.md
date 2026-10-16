# S4B: one locally unsent card intent — independent Root acceptance

## Requirement and boundary

Original v0.8.1 §14.2/14.3/14.7 and the user's supplemental card-flow request govern this unit. Its role is to prepare one explicit next card while a committed player action is displayed. This is a local UI bound, not reference K=2 or a measured external-game rule. Next/after time reservations remain real Core commands. Server transport, ACK wait and reconnect are **NOT_IMPLEMENTED**; no network confirmation is simulated.

Official Opus5.5 authored the scoped implementation at actual **xhigh**. Codex independently inspected the complete production diff, caller changes and new tests, and alone operated Unity. Author execution remains NOT_RUN. A fresh independent Opus **Max** final source review returned **CHANGES_REQUESTED: P2-1**, a same-coordinate Confirm/Cancel toggle. Root independently agrees with the concrete source reproduction and requested a two-rect repair plus actual double-tap regressions. Opus repaired it and Codex independently re-reviewed the complete repair, reproduced the defect against the old Main, and validated the final source. **Acceptance: PASS within the stated local scope; P2-1 resolved.** The original Max verdict remains preserved rather than rewritten. Actual model/effort evidence is recorded in `actual_cli_metadata.json`.

## Actual behavior

- Confirm during a nonterminal action display freezes Run reference, node, turn, version, source UID, mode, target and a copy of ordered picks. It returns false because no Core command has committed. The screen says `미전송 사용 대기 1/1` and provides Cancel; no AP, source card, RNG, receipt or reservation slot is spent yet.
- Capacity is one, with no silent overwrite. Selection changes require Cancel. Choice cards use the existing guarded picker and only its final Pick/explicit Auto prepares the request. An unfinished picker never executes merely because the display ends.
- Display completion/Skip only closes presentation. A subsequent Update takes and clears the slot, checks the frozen context/current hand/living target/choice requests/ordered picks and `BattleCheck`, then uses the existing command path exactly once. Stale requests are dropped with `INTENT_STALE`; no replacement target, Core fallback, retry, refund or partial benefit is introduced.
- Recovery, turn summary, picker and terminal presentation keep ownership of their locks. Direct extra Play/End/Ultimate and unrelated commands remain blocked. Focus loss, pause, disable, new departure and quarantine drop the unsent request. Resume redraws actual current state and never executes a hidden pending action.
- The implementation reuses existing presentation, command and picker methods. Core, game data, Scenes/Prefabs, hidden RNG projection, save meanings and time-reservation rules are unchanged. No new dependency or generic engine was added.

## Fresh actual Unity execution

| Check | Result | Receipt |
|---|---|---|
| Existing suite against draft B | 135/135 PASS; failed0/skipped0 | `prepared_draft_old135_result.json` |
| New prepared-intent cases | 10/10 PASS; failed0/skipped0; 14.4619s | `prepared_initial10_result.json` |
| Pre-review complete PlayMode | 145/145 PASS; failed0/skipped0; 50.5626383s; missed the same-point defect | `prepared_full145_result.json` |
| Final complete PlayMode after P2 repair | **147/147 PASS; failed0/skipped0; 54.604809s** | `prepared_full147_result.json` |
| Initial EditMode | 3/3 PASS; failed0/skipped0 | `prepared_edit_result.json` |
| Final fresh EditMode after repair | **3/3 PASS; failed0/skipped0** | `prepared_final_edit_result.json` |
| Final Console query | 0 error/warning entries returned after final build; controlled fault logs remain in test receipts, and transient manual-reload transport warnings are separately disclosed below | `prepared_safe_console.json` |
| Initial standalone macOS build | Succeeded; 0 errors/0 warnings; 255.11MB; 32.619921s | `prepared_build.json` |
| Final repaired standalone macOS build | **Succeeded; 0 errors/0 warnings; 255.11MB; 9.889073s** | `prepared_safe_build.json` |

The new cases exercise actual Create/public commands with a separate mirror: pure pending input, natural exactly-once dispatch, normal/2x/reduced/Skip/switch equivalence, capacity/cancel/reuse/inactive callbacks, spent/dead selection refusal, next-turn reservation only on real dispatch, picker final/Auto behavior, stale version after an actual public Core end, lifecycle cancellation, held Cancel, mandatory finishing-card witness, summary/recovery priority and complete session-store preservation. No skipped or optional witnesses are accepted. Full comparisons include log/events separately; CanonicalState alone is not treated as a whole-graph comparison (`comparison_scope.md`).

Two existing action tests deliberately adapt Confirm to the new prepare/picker path, explicitly cancel it, and retain their substantive state/lock assertions. Root additionally split a local declaration and annotated nullable RunId as `string?` without changing execution/assertions. The final independent reviewer explicitly APPROVED this Root edit and identified the identical nullable declaration in the new test file for the same repair. Draft warning receipts remain preserved rather than rewritten as a clean first attempt.

## Actual scene and native scope

`prepared_live_before_send.json` records a real first shield action, then confirmed unsent c1/now/e19: **AP3, block8, V2, turn1, reservation0** remain unchanged, including complete JSON log/events and whole session store. No expected state is injected into the live Run.

Without another Play call, actual Update then dispatches exactly one frozen request: **AP2, V3, turn1**, no pending slot, full JSON equal to the independent two-command mirror, store unchanged (`prepared_live_after_send.json`). Temporary comparison objects were removed from the QA AppDomain afterward.

`prepared_boundary_probes.json` separately records real capacity/Cancel/focus purity and stale-context rejection after an actual public Core end. Its direct private-dispatch invocation is an explicitly documented reflection seam; natural automatic Update is proven by the preceding scene receipt, not inferred from that seam. NUnit lifecycle method invocation is component behavior, not a claim of real OS suspension/reconnect.

The post-repair scene was freshly repeated: `prepared_final_live_before_send.json` (raw `confirmReturned:false`, AP3/V2 pure) and `prepared_final_live_after_send.json` (natural exactly-once AP2/V3, complete mirror/store equality, comparison objects cleared). `prepared_final_live_pending.png` and `prepared_visual_verdict.json` show the repaired Cancel/info positions, frozen card/target and separate timeline at actual 1180×665. Both initial Prepared and final PreparedSafe native apps were launched at their exact built paths and their Korean initial maps observed (`prepared_native_launch.json`, `prepared_safe_native_launch.json`). New native battle/queue/physical mouse flow is **NOT_RUN**. The user's earlier Final-app physical map→battle report remains separate. Android, IL2CPP, real-device and server acceptance are not claimed.

## Final-review findings, repair and independent revalidation

`final_independent_review.md` found that Prepare and Cancel synchronously redraw opposite consequential controls at the same point. Cancel twice could reprepare or immediately play the cancelled request, and Confirm twice could cancel it. Existing 145 passing tests did not cover that coordinate transition. The repair swaps Cancel with the pure card-info button. Two mandatory EventSystem coordinate regressions failed against the old layout for precisely those state errors (0/2), then passed after the repair (2/2). The first repaired attempt exposed a test observation-timing error on a retired info button; both the failed receipt and the narrow pre-callback assertion fix are retained. All consequential state/ownership/exactly-once assertions remain. `prepared_fix_root_review.md` records Codex's opposite-provider re-review, followed by final147/147 and3/3 actual execution. No Core or gameplay rule change is needed.

Reviewer P3-2 correctly notes the original author prose overstates effect-semantic pick guarantees; the narrower boundary below governs acceptance. P3-3: raw `queuedReturnMeansNoCommit:false` is the raw ConfirmCard return (false means no commit), not a failed purity assertion. Original receipts are retained unchanged; explicit `preparedFullJsonPure:true` and the AP3/V2 values establish the pending state.

## Preservation and remaining work

`prepared_preservation.json` rechecks all 47 protected files and all 423 original manifest entries: identical protected hashes and the same 29 historical differences as the accepted head. No manifest regeneration, balance retuning or candidate activation occurred. The original 1,320-run/32 misses and expanded 5,280-run/21 misses retain different populations and both remain failed balance targets. `prepared_settings_restoration.json` records the earlier idle restoration; `prepared_final_settings_restoration.json` records final exact accepted bytes for all five settings and future None/0 options, with no pending restoration while the actual Editor is left in Play.

Existing prototype effect-specific candidate lists can still be broader than Core's tune/recall/cost/growth predicates. This unit revalidates its existing shared UI policy and preserves explicit Auto/Core fallback semantics; it does **not** claim that independent boundary is fixed. `P1-EFFECT-CANDIDATE-FOLLOWUP.md` is an undispatched bounded follow-up, not completed work. Next-mode pending has an actual witness; after-mode uses the same production branch but has no separate public witness. Same-version defensive hand/target branches are source-reviewed while the stale witness exercises a real version change.

Global turn-summary 2x pacing, final VFX/audio/art, worst-density/130% text/48dp accessibility, disk/account persistence, mid-Run restoration and backend reconnect remain outside this local unit. Existing ordinary turn-summary 0.3s/Skip behavior is preserved. Source and tests are bound to `prepared_final_source_hashes.json` and were unchanged after final147/Edit3/build. Supporting checks freshly passed: Node25.9.0 333, proposals14, Python57, docs9 and handoff13 (`prepared_supporting_checks.json`). Earlier Node22/cross-version evidence remains separate; exact pushed-head CI is recorded in the PR.


## Manual Editor transport recovery

Normal domain reload during the optional final manual Play transition tore down MCP's WebSocket, producing two observed transport-warning kinds and generic unsuccessful/timeout responses. Those attempts are retained as `prepared_manual_connection_retry1_before_send.json` / `prepared_manual_connection_retry1_after_send.json`; they prove no scene success. Root stopped Play through the actual Editor, opened its existing MCP panel, and verified a real successful RPC (`prepared_mcp_reconnected.json`). No server reinstall, asset deletion, production edit or paid provider was used.

For the successful manual scene repeat only, Root temporarily used the same DisableDomainReload/1 option as the installed MCP test runner to retain the bridge, then restored None/0 while idle (`prepared_manual_play_options.json`, `prepared_final_editor_restoration.json`). Actual live Create/commands and scene results above were re-run successfully. Final settings bytes are restored separately. The final Console query's empty result does not erase those earlier transport warnings or controlled test faults, and does not mean the whole session had zero diagnostics.


## Editor left ready

After build/acceptance, Root left the actual Expedition Scene in Play at its first battle, with c7/next selected and its original current-state reservation forecast visible. AP4/V1 and full JSON are unchanged by selection; no pending input or action display remains (`prepared_editor_left_ready.json`, `prepared_editor_ready.png`). Temporary manual entry again used DisableDomainReload to retain the known bridge, then restored the **future** Editor options to None/0 while the current Play remains open. All five serialized settings are exact accepted bytes (`prepared_final_settings_restoration.json`); no Scene/Prefab was saved or replaced. This visible practice state is session-only and is not another native/device acceptance claim.
