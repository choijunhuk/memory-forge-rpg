# A1 native patch source review — second pass

Date: 2026-10-15. Reviewer: the same Codex native GPT-6.1 Sol source-review child.

**Verdict: APPROVE the two corrections at the hashes below, SOURCE ONLY.** Initial findings P1 (stale exhaust zone) and P2 (eager picks/error precedence) are closed for this patch. No new actionable source defect was found in the changed code and its affected paths. This is not full A1 parity, official Opus approval, Unity acceptance or integration approval.

## Exact reviewed identity

Read-only patch workspace: `/Users/choi/.codex/worktrees/csharp-battle-opening/Memory_forge`. Compared actual bytes with the unchanged initial root copies, rather than the worktree index. All five hashes were checked; the two patch hashes were confirmed again at the end of inspection.

| Source | Reviewed SHA-256 |
|---|---|
| `Battle.PlayerActions.cs` | `28e8bbdd8e149bc2eed1924ea3c1a8cc8403a2317c235dfaf3e0769acd688e41` |
| `Battle.Effects.cs` | `b2a59ace543623b62de2020466ec56c4db8cb4e85954de23d19649ddfc9373bb` |
| `Battle.cs` — unchanged | `4fdd520997b1965796ae6218f8c280d550109d8ce7ebacf1f39756b16fd5cd2e` |
| `BattleState.cs` — unchanged | `bdc58054e3edcccf5fcc5f6ad6f43adb8548ad32c6d97491f50920627ec033c0` |
| `ReservationBook.cs` — unchanged | `49ca7cec199e4951ac7ecc9075f8238a64977c7e9131e9784c764530f0a4e23e` |

The initial review is preserved: `native_source_review.md`, 82 lines, SHA-256 `6d4bcb2e892d75635193e0ff4cbdfb730b29a3683c6cc9bca1164e5ac4e7a51e`. That document describes and rejects the initial hashes; this follow-up does not rewrite its historical verdict.

## P1 correction

`Battle.Effects.cs:277-280` now obtains the current `s.Zones.Discard` or `s.Zones.Hand` inside each exhaust iteration. A marked-kill draw can move/replace a list between iterations without redirecting the next discard exhaust into the draw pile. This follows `engine/rules-core.js:1355-1363` and its reshuffle at `:1453-1463`.

`exhaustHand` retains its explicit hand snapshot, matching source `:1366`; it is intentionally different from the live-zone selection loop. The patch does not alter other snapshot loops, exhaustion hooks, target/RNG selection or terminal cleanup.

Parent-owned runtime evidence was read: `exhaust_live_zone_red.txt` reports the original fifth request's false victory/extra exhaust; `exhaust_live_zone_green.txt` reports **5/5 full-state/response matches**, with PLAYER, block10, draw `[c6]`, exhaust `[c3]`, killedMarked1. The unchanged `exhaust_live_zone_repro.json` contains actual setup/commands only. This child did not execute that C# harness.

## P2 correction

- `Battle.PlayerActions.cs:49-51` passes the original immutable picks value through command decoding. `ReservationPicks` at `:42-47` performs a non-throwing first-pick lookup for precheck/pull; it does not consume choices. Loan/defer/phase/mode/hand/AP/target checks retain their source ordering and the public typed `Check`/`Precheck` paths remain unchanged.
- Immediate play reaches `ArgumentPicks` only at `:141-142`, after precheck and candidate commit work, where JS enters `resolveCard` and spreads picks (`engine/rules-core.js:737-765`, `:829-835`). An error still discards the entire candidate; the original receipt identity is unchanged.
- Reserved play never decodes unused picks or saves them into a reservation. Due resolution continues to receive an empty list, matching source `:524-529`.
- Ult keeps raw picks in its ephemeral context at `:163`. Unused picks do not reject an otherwise legal ultimate. If a choice is reached, `TakePick` at `:260-270` materializes an array once and consumes the resulting local list across subsequent choices; raw first-pick lookup for pull remains non-consuming. This matches source context/choice timing at `:803-819`, `:787-793`, `:1438-1451` within the existing documented transport/error boundary.
- `RawPicks` belongs only to the ephemeral effect context. It adds no serialized state, alternate queue, mutation of the request argument tree or receipt normalization. `Battle.Command`, receipt-before-stale ordering, state copying and success-only publication were not changed.

Parent-owned runtime evidence was read: `picks_priority_red.txt` reports **9 mismatches in 21 requests**; `picks_priority_green.txt` reports **21/21 full-state/response matches**. The eight source cases cover created play/ult, invalid mode, missing card, unknown actor, gauge-not-ready, unused reserved picks with actual due resolution, unused ult picks and failed-rid reuse. This child did not run or broaden that harness.

## Verification limit

This pass reviewed only the two-file diff and affected paths. No product file was changed; only this new review document was written. No build/C# run, CLI/model call, delegation or Unity operation was performed. The initial full source review was not repeated. Full A1 oracle/host checks, official Opus review, additional Unity regressions and integration remain the parent's separate required gates; earlier-unit PASS counts do not establish them.
