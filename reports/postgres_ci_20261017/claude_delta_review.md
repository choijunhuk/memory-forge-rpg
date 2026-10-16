# P1-POSTGRES-CI-DELTA-20261017 — delta review (Claude)

- **Reviewer:** Claude Opus 5.5 (`claude-opus-5-5`), effort high.
- **Session:** `a1d7a222-69d8-4b5b-8137-fdcd8d5e2b39`. Included subscription only.
- **Not used:** Bash, MCP, agents, images, recursive calls, source edits.
- **Write scope:** this file only.

**Verdict: APPROVE.** No blocker found. **Reviewer execution: NOT_RUN** (static comparison only).

The earlier P1-POSTGRES-TRANSACTIONAL-PRACTICE result stays **FAILED (session limit)**. The full-CI APPROVE in `claude_review.md` is unchanged.

## Compared

Current `tools/check_practice_postgres.sh` against `review_inputs/postgres-ci-base/check_practice_postgres-reviewed.sh`. In the table, "current" and "reviewed" give line numbers in each file.

| Change | current | reviewed | Assessment |
|---|---|---|---|
| ERR trap added | L21 | — | See "ERR trap" below. |
| Global `export PGPASSFILE` removed | — | L47 | `initdb`, `pg_ctl`, the oracle calls and ServerChecks/hosts no longer inherit the admin passfile. |
| Inline `PGPASSFILE="$MF_TMP/pgpass"` on the initial `CREATE DATABASE` psql | L51 | L51 | This psql is the only call that relied on the export. The oracle wrapper still exports its own passfile (current L42), so L52-L55 and every check-side oracle call keep working. |

Apart from these three changes, every other line is identical; current L22-L46 equal reviewed L21-L45 (shifted by the trap line).

## ERR trap (current L21)

- **Output content.** It prints only fixed text plus numeric `$LINENO`. The single quotes delay expansion to trap time. There is no `BASH_COMMAND`, SQL, password, path or log content.
- **When it fires.**
  - It fires only for top-level failures under `set -e`, for example initdb at L48, pg_ctl at L50, psql at L51-L55, and dotnet at L61.
  - It does not fire inside `if` conditions (L8, L9), `|| true`, or `mf_cleanup`, because errtrace (`set -E`) is not set.
  - The exit status is preserved, so a failure is still a failure.
- **Paths still printed.** The only path in output is the existing EXIT retained-directory line (L17), which was pre-existing and is unchanged.

No workflow, source, dependency, SDK, pinning, service or timeout change is present in the compared script.

Root's repeat of the native harness and the planted-secret setup-failure probe is still pending.
