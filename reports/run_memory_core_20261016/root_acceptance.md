# Actual terminal memory port and complete Run replay

Original v0.8.1 §§7–8, 14.3, 16.2 and 20.9; data0.7.0. This supersedes the earlier R1a/R1b **prefix-only** counts, without rewriting those historical receipts. Requirement: execute the original terminal memory queries/commit and derive the next fresh departure from the actual returned memory. No unfinished-Run deserialization, account/disk write or server operation is claimed.

## Implementation and independent review

Official subscription-authenticated Opus Max authored `Run.Memory.cs` and the exact numeric formatter repair in two separate sessions. Opus xhigh authored the public-memory JS oracle and its independent C# consumer. Codex reviewed the actual source, callers, original JS ordering/costs/capacities/receipts, all returned-copy boundaries and test execution. Root's completion host and stricter Unity transcript/number tests received opposite Opus review and concrete rechecks. See `author_report.md`, `host_recheck_approved.md`, `numeric_fix_author.md`, `numeric_tests_recheck.md`, `../run_memory_queries_20261016/host_author_report.md`, and the scoped task results in `ops/ai/tasks`.

All calls used the fixed official `claude-opus-5-5` CLI with included-subscription authentication and account-matched UI credits/reload-OFF attestation. No API/Console credit/token extraction, paid fallback, extra usage or other model was used. Raw billing/credentials/reasoning stay outside Git. Image work: none.

## Fresh verification

`final_build.txt`: .NET10 build, **0 errors/0 warnings**. `final_core_checks.txt`:

- **RunProgressionChecks21902/21902**, including public-memory checks873/873.
- Five complete JS source cases: **2684 actual attempted commands**, 1370 commits/1314 rejections, 1074 pure queries, 2694 full-state comparisons including log/events/RNG, 1608 detached-result/snapshot probes. **5/5 actual DONE and 5/5 save-derived fresh departures**.
- Original1000 legacy runs: **273504/273504 actual C# attempts**, 182528 commits/90976 rejections; 6657 battles and275 archives; **1000/1000 actual DONE**. Actual returned ForgeOffer ownership567/567 and saved-memory ownership1000/1000. Their canonical final snapshots omit log/events by the original corpus contract; the separate five cases compare full graphs.
- Public-memory oracle:15 scenarios, **85 executed public operations** (26 returns/59 rejections),3 native error categories and3 explicitly **NOT_COVERED** planned probes. Native diagnostic message text is not counted as parity. Recorded expected stores are comparison inputs only; actual C# M0/commit returns drive execution.
- Numeric checks **2189/2189** over2176 exact binary64 patterns and a real public caller-supplied receipt/replay witness with two nearby/body conflicts. Actual Unity Mono had41/2088 mismatches before the exact formatter; saved REDs remain. Runtime double formatting/parsing is no longer used to choose the shortest decimal digits.
- All previous Core202/canonical87/2000 hashes/opening2221/turn29232/player queries128244/player actions87991/pure-memory5451/create215677 remain passing.

`node22_320.txt`: complete current Node22 suite **320/320**, no skips. `python57_final.txt`: **57/57**. Earlier318-case Node22/25 and numeric2176-targeted outputs are historical intermediate evidence, not a second current320-case full run.

Actual Unity PlayMode **86/86** and EditMode **3/3** are recorded in `../expedition_client_20261016/playmode86_pass.json` and `editmode3_pass.json`; this includes the still separately integrated expedition UI. Core-only final full-flow evidence is `unity_full_flow_final_pass.json`:592 actual commands,4 memory queries,3 commits and3 fresh save-derived departures over three original legacy/v2 clear/defeat cases. It checks every step's original log/events, actual public returns and source pins; reference bytes regenerate in `unity_full_run_reference_generator.mjs`. No stored expected Run state is injected.

## Defects exposed during verification

The first completion host passed the wrong v2 commit arity and compared an unnormalized canonical-only final graph; both failed loudly, were repaired in the host and independently rechecked. The strengthened Unity transcript helper indexed a JSON-null scalar as an object; its exact `as JObject` null guard passed the actual re-run and opposite recheck. These were checker errors, not reasons to change game data or expected results.

The numeric production defect was separately demonstrated through an otherwise matching JS public receipt: Mono produced a different decimal body and falsely returned REQUEST_CONFLICT. Exact midpoint intervals and closest/even tie handling now match original JS while the genuine conflicting bodies still reject. Original public validation and error order are retained.

## Preservation and limits

`preservation47.json` compares all47 protected original/design/data/PDF/golden paths to prior head2dba813: **all unchanged**. Input manifests423 entries are not regenerated; their29 historical development differences remain explicit. No price, probability, capacity, card effect, enemy stat or balance target was retuned; the prior same-condition32 misses remain unresolved.

Run memory is an actual pure legacy/v2 port. Persistence of unfinished runs, raw-byte backup, account/DB concurrency/auth/economy, Android/IL2CPP/physical devices, final art and whole-game acceptance remain separate. The new expedition view deliberately keeps its practice store in-session only. Existing earlier checker lines that say its own scope has zero Run commands describe those older isolated suites; the actual complete execution is the RunProgression section above.
