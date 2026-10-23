# Check restoration of the original manifest-audited diagnostic script

Official Opus5.5 High, included subscription only. No API/credits/extra/fallback/Bash/MCP/agents/images. Separate A worktree; all source/receipts readonly. Own only reports/requirements_status_20261016/input_preservation_recheck.md.

Root's new a114e30 status/label commit failed CI engine_tests and csharp_checks. Root independently reproduced the exact preservation mismatch: original423entries/29historical changedpaths vs30, the sole unexpected path tools/run_all_checks.sh. That file is an ORIGINALMANIFEST input: SHA34f4b54447d4fdf6a1ced5cc3cb9076b505e81d4b292c78151ac91f79d4f7257. Changing two static not_done descriptions was enough to trigger the shared audit. It was not a game-number/rule change.

Root restored ONLY that script to exact acceptedbcf28e4 bytes/originalmanifestSHA. No manifest/golden/expectedlist/case/count/check was changed or regenerated. Actual original JS factory audit/checkFixture is re-run; corrected diagnostics and current22ledger rows remain. C#runtime is re-running, not claimed PASS before completion. The prior review/diff of two description changes remains historical and must not be presented as finalscript state.

Compare restored script's bytes/SHA declaration in the copiedmanifest row and red/green evidence. Verify no weakening of tests/audit and no accidental loss of the other4checker-only scope labels/currentledger. Give APPROVE or precise issue in a short report; executionNOT_RUN by reviewer. No broad project re-review. This is a conservative restoration of originalinput, not permission to accept a30th changedinput.
