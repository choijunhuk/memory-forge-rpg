# P1-DIAGNOSTIC-INPUT-PRESERVATION-RECHECK: APPROVE

Reviewer: official Claude Code Opus5.5 (High), included subscription only. I only read code and docs, and I edited only this file. I did **not** run anything (no Bash, no hashing, no node or dotnet). Scope: restoring `tools/run_all_checks.sh`. This is not a review of the whole project.

## Restored script vs original manifest

- **Manifest rows still declare the original.** `MANIFEST.sha256:422`, `MANIFEST_AIO.json:2113-2115` (bytes 3372) and `MANIFEST_v0.8.1.json:1896-1898` all give `34f4b54447d4fdf6a1ced5cc3cb9076b505e81d4b292c78151ac91f79d4f7257`. That matches the archived original at `docs/archive/launch_before_all_in_one/MANIFEST.sha256.original:379`. None of these manifest rows was changed to accept the edit.
- **The text matches the pre-edit version.** `tools/run_all_checks.sh:37` is exactly the `-` line in `diagnostic_labels.diff:61`, the original 9-entry `not_done` list with `"Unity client"` and `"C# battle/run port"`. The `+` wording does not appear anywhere in the file. The diff had one hunk only (blob `8501481`→`68466b5`), so removing it should give the original blob `8501481`.
- **Line endings stay LF.** `.gitattributes` has `* text=auto eol=lf` and no `.sh` exception, so checkout cannot turn the file into CRLF bytes.
- **I could not confirm the bytes myself.** I had no hashing tool, so the byte-for-byte match with SHA `34f4b5…` is **NOT_RUN by reviewer**. It relies on Root's reproduction, the red/green receipts and the CI rerun.

## Red/green evidence

- `diagnostic_manifest_red.json`: 423/423 entries, 30 changed vs 29 expected. The only unexpected path is `tools/run_all_checks.sh`, and nothing historical is missing. This matches the reported failure.
- `diagnostic_manifest_green.json`: 423 entries, 29 historical, `inputAuditMatchesOriginalExpected: true`, `manifestsRegenerated: false`, `fixtureExpectedChanged: false`. This agrees with the restoration, but it is Root's receipt and I did not run it.

## No weakening, scope labels kept

- `RunCreateChecks.ManifestAudit` (`RunCreateChecks.cs:674-699`) still checks size and SHA256 for every entry, the manifest pair hash, duplicates, unsafe paths and the index count. It still reports `regenerated: false`. `run_create_vectors.test.mjs:171-177` still checks for 423 entries, `manifestMatch: false`, `regenerated: false`, and the 4 `-text` CSV exclusions. I saw no added allowlist entry for `run_all_checks.sh`.
- All 4 checker-only labels are still the new `NOT_RUN_IN_THIS_SUITE` / `RunCreateScope … in this suite` text: `TraceHashChecks.cs:52`, `BattleTurnChecks.cs:109`, `BattlePlayerActionChecks.cs:144` and `RunCreateChecks.cs:113`. Those files are not original-manifest inputs, so keeping them is correct.
- The ledger rows in `docs/IMPLEMENTATION_STATUS.md` were not touched here. The earlier `recheck.md` approval of those rows still applies.

## Notes (non-blocking)

- `recheck.md` and `diagnostic_labels.diff` still describe the two-string edit to `run_all_checks.sh`. That is now history. The final state is the original script, so do not cite them as the current `not_done` wording. The live `reports/delivery_status.json` will print the old 9 labels, which are now less precise. A future change to that wording would need its own approval for a 30th changed input. This restoration does not grant that.
- C# runtime and CI `engine_tests`/`csharp_checks` are re-running and **not yet PASS**. Do not merge until both are green.
