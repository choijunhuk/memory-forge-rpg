# Correct the same-expedition AP re-earning regression, preserving every check

Official Claude Code Opus only, included subscription only, High for this small, fully reproduced test interpretation correction. No Bash/MCP/agents/fallback. You are not alone. B worktree owns ONLY engine/tests/run_progression_vectors.test.mjs and reports/run_progression_20261016/author_report.md. Generator/engine/data/fixture are READ ONLY. All initial source/test/report/fixture bytes and raw test failures are preserved privately by Root. No changes to expected gameplay values or other tests.

Root actually ran your generator: PASS,5cases/2684attempted/1370committed/1314rejected/1074queries,2000612gzipbytes/29905373uncompressed bytes,4.84seconds,~720MiB RSS. Root ran --check: PASS all exact source/state/return/query/coverage records. Initial tests4/5PASS; only the wrong multi-seed assumption fails. Root independently reviewed the full generator/test code. No source defect or parser failure is being hidden.

Exact failure: `AP bonus must be earned in seed 2654 and independently in another seed: 2654`.
The actual requested requirement is TWO separately earned bonuses WITHIN THE SAME seed2654/full Run, as explicitly provided in the earlier task. Actual captured coverage:
- earned: seed2654-full-legacy at78(+2),1519(+2).
- consumed: same case at80(AP6),1524(AP6).
- between: real battle entries91/157/228/282/1078/1230/1335/1410 all AP4.
- first-turn Run/Battle copied bonus/apNext zero, no chrono_crown/cracked_hourglass on these observed starts.
The five other generator configurations need not independently acquire ev_rift, so earnedSeeds.size>=2 is a wrong requirement. Replace it with stronger exact same-case event/consumption counts/order, the two fresh gains separated by eight ordinary battles, and the independently derived original AP4/+2 values. Preserve the existing full-corpus AP-change location and first-turn formula checks; do not remove the check or merely accept one gain. Reuse existing observed.coverage/walk helpers and literal source requirement. No new generic harness.

Keep all five tests and corruption/query/atomic-output assertions. Update the report to the correct same-Run interpretation and label Root's actual executions as Root evidence, not your execution. Distinguish your static unverified assumptions from measured facts. Also correct historical worktree-only statements: gen_run_create_vectors is already tracked in Root at8c6d764; do not claim it must be newly committed. Root's actual capture/import/--check succeeded.

Return exact edits and NOT_RUN. Root will read the narrow diff, rerun targeted5 and cross-runtime checks, then integrate after opposite-author review. Full C#Run replay remains0. Source guard commit8d17999 is separately pushed with CI running; this new oracle is not yet integrated. No SourceEngine or generator SHA changes are needed for this test-only correction.
