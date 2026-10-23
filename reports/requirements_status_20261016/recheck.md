# P1-REQUIREMENT-STATUS-RECHECK — APPROVE

Reviewer: official Claude Code Opus5.5 (High), included subscription only. Read-only on code and docs; only this file edited. Execution: **NOT_RUN** by reviewer (no .NET build, `bash -n`, docs or handoff run). Scope: the two required fixes, the optional precision edits and `diagnostic_labels.diff`. No whole-project re-review.

Note: the copied `current_rows.diff` is still the pre-fix snapshot. I checked the current rows directly in this worktree's `docs/IMPLEMENTATION_STATUS.md`.

## Ledger rows (current file)

- **§7.2** (l.132): now says `Editor포인터 도달입력`, links the expedition receipt, and lists native physical input as separate. Required fix 2 is resolved.
- **§16.4** (l.283): `네이티브 물리입력(NOT_CONFIRMED)` is now the first remaining condition. This matches `actual_interaction.json`. Required fix 1 is resolved.
- **P1 trace** (l.13): `legacy JS1000`. **§7.7/§7.8** (l.137–138): per-event and 32-relic hook coverage are listed as remaining. **§8.4** (l.151): `3용량초과 원문프로브는 NOT_COVERED`. **§7.3** (l.133): `원정87Play`. All optional notes were applied correctly.
- No status value changed. §8.13 is still `NOT_IMPLEMENTED`, the server rows are unchanged, and the forecast is still described as in development and not counted.

## Diagnostic-label diff

- All 4 edits in `.cs` files change only literal `Console.WriteLine` strings. The diff has no interpolated values, counters, `Check(...)` calls, conditions, return values or method changes. The surrounding count lines (`BattleTurnChecks`, `BattlePlayerActionChecks`, `RunCreateChecks`, `TraceFixtureValidationChecks`) are untouched context. Nothing is weakened.
- The new labels point to `RunProgressionChecks`, and that pointer is accurate: both the `RunProgressionChecks:` and `RunTracePrefixReplay:` lines are printed from `RunProgressionChecks.cs`. Unresolved items are still stated: generic base0 UNRESOLVED (player actions) and unfinished restoration/server/device (turn, create).
- `tools/run_all_checks.sh` changes only two strings in the `not_done` list, and the list still has 9 entries. Server, zones 3–5, art/audio, Android device, playtests, load tests and payments are unchanged. No file under `tests/` or `tools/` matches the old strings, so no consumer is broken. The old `NOT_IMPLEMENTED` mentions in `ops/ai/tasks/*` and `review/runtime_changes.patch` are historical task text and patch records. Their condition ("until full replay exists") is now met by RunProgression.

## Optional nit (non-blocking, no re-review needed)

- In `RunCreateScope`, `complete progression/saving` could be read as durable persistence. RunProgression covers the terminal `commitMemory` and the save-derived departure, not disk or account storage. `complete progression/terminal memory commit` would be more exact. The rest of the line already says unfinished restoration, server and device are separate, so this does not block.

The row count was not used as a basis for completion. No game policy, quantities or gates were changed.
