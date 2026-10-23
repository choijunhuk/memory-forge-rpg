# Read-only independent fact check

Native GPT-6.1 Sol Max examined the supplemental HTML technical claims against accepted dec7688, with current uncommitted candidate changes distinguished. No tests/Unity/CLI/edits by reviewer.

| Claim | Assessment / concrete source at dec7688 |
|---|---|
| Core restore / receipts / atomicity | Partly confirmed missing server features: Battle.cs15–23,118–146; Run.Progression.cs18–26,60–77. Battle commands already atomic. Run.Memory.cs259–269,282–294 persists v2 memory receipts in returned store. No current C# restoration API, so post-restoration failure is a future risk rather than reproduced bug. Core is not the only possible transaction boundary. |
| OpenAPI v2 | Confirmed gap: server/openapi.yaml270–274 uses integer indices; Run.Memory.cs243–249 and Client582–585 use IDs/expected version. Preserve legacy and define v2 separately. |
| Candidates | Confirmed at accepted head1094–1102, corrected draft not yet accepted; one-charge/conditional costs show that small diff does not imply trivial logic. |
| Provenance churn | Exact byte hash in gen_battle_turn_vectors.mjs24–29. Three cited gzip artifacts change only metadata after provenance exclusion. Preserve source binding; floating intermediates alone do not prove wrong gameplay. |
| UI | Render1112–1122 rebuilds when called; timer/pace/skip880–896,914–926 update in place. Direct coordinates2011–2019 are debt. Cross-state tap defect already repaired1346–1357; current performance failure unmeasured. |
| CI | ci.yml15–34 and run_all_checks.sh10–27 do not run actual Unity or test:handoff. Local147 Play/3 Edit is separate. MCP is not proven the only possible runner. |
| Android | ProjectSettings265,581–582 Mono/ARMv7 differs from original16_chapter41 IL2CPP/ARM64 target. Not a reproduced APK/store failure. |
| JSON dependency | manifest2–14 lacks direct JSON dependency; packages-lock209–214 resolves3.2.1 only through MCP; Client.asmdef8–10 requires DLL. Concrete dependency risk; no actual removal build performed. |
| Small hygiene | Reports imports/writes and stale CI comment confirmed; exact external counts not remeasured. Original manifest development delta is not a product defect and must not be blindly regenerated. |
| Git | main one commit vs accepted33, but fixed commits/hash-bound vectors/acceptance artifacts are regression baselines. No automatic merge/rebase instruction follows. |
| Docs | Stale README/AGENTS/policy confirmed. CURRENT explicitly labels older statements historical. Current matrix and readable summary needed while preserving original records. |
| Handoff pin | test_handoff.py17,34 pins package false/historical words. Ordinary semantic documentation repair needs no new user permission. Also never turn old auth booleans into permanent true credentials. |

Root synthesis and actual actions: ../../docs/PROJECT_REVIEW_20261016.md. Original archive preserved separately.
