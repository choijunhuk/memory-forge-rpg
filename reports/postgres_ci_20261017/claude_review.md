# P1-POSTGRES-CI-REVIEW-20261017 — independent CI review (Claude)

Reviewer: Claude Opus 5.5 (`claude-opus-5-5`), effort max. Official Claude Code CLI invoked by Root (Codex), session
`a1d7a222-69d8-4b5b-8137-fdcd8d5e2b39`, included subscription only. No Bash, MCP, agents, images, AI API, commit or push.
Write scope: this file only. No source, script, CI, csproj or lock file was changed.

**Verdict: APPROVE.** No blocker found. This is a static review; **reviewer execution is NOT_RUN** (see the last section).

**Status preserved:** the earlier Claude call for P1-POSTGRES-TRANSACTIONAL-PRACTICE ended **FAILED (session limit)**. This review
does not change or relabel that result. Its author report (`reports/postgres_persistence_20261016/author_report.md`) stays
author-NOT_RUN.

## Scope

Reviewed read-only:
- `.github/workflows/ci.yml` against `review_inputs/postgres-ci-base/.github/workflows/ci.yml`.
- `tools/check_practice_postgres.sh`.

Cross-checked against what the harness must satisfy:
- `server/MemoryForge.Server/PracticePostgresStore.cs`: connection-file validation and startup verification.
- `server/dev_practice_schema.sql`, and `server/schema.sql` (grep for role/owner/extension/search_path statements).
- The server csproj and `packages.lock.json`; the Core csproj and its lock file.
- `server/MemoryForge.ServerChecks/{Program.cs, PostgresChecks.cs, .csproj}`.
- `tools/run_all_checks.sh` and `tools/check_schema.sh`.
- For CI coupling to the changed files: `package.json`, `tools/ai/verify_manifest.py`, `tools/ai/test_handoff.py`, `tests/test_delivery_files.py`.

Not reviewed: game content, internals of unrelated CI steps, Unity/Editor, Root's runtime.

## Diff

The current file adds exactly two steps. Every other line is identical to the base.

| current | step | base |
|---|---|---|
| L28 | `bash tools/run_all_checks.sh` (protected; unchanged) | L28 |
| **L29** | `dotnet restore server/MemoryForge.Server/MemoryForge.Server.csproj --locked-mode` (new) | — |
| L30 | `dotnet run --project server/MemoryForge.ServerChecks -- "$PWD"` (default file suite; unchanged) | L29 |
| **L31** | `bash tools/check_practice_postgres.sh` (new) | — |

Neither new step has `if:` or `continue-on-error`, so both gate push and PR. `permissions: contents: read` is unchanged and no secret
is referenced.

## Contract checks (static evidence)

Unprefixed L-numbers refer to `tools/check_practice_postgres.sh`.

1. **The real PostgreSQL suite runs, and it gates the job.**
   - Harness L61 runs ServerChecks with `--postgres-file` and `--psql`.
   - ServerChecks `Program.cs` L53, L59-L65 and L80: `Main` returns 0 only after `RunPostgres` completes.
   - `RunPostgres` ends with `LeakScan(PostgresScenarios)`, which requires all eight `pg-*` scenarios (`PostgresChecks.cs` L150, L180-L190).
   - Any failure exits 1, and `set -e` (L4) then fails the step.
   - Running as root (L8) or without PG16 binaries (L9-L11) exits 2. That fails the step; it is not a skip.
   - So a green step means the full PostgreSQL mode ran. Compare N in the CI line `SERVER CHECKS PASS [postgres]: N assertions` with Root's reference count.
2. **A fresh disposable PG16 is used; no existing cluster or volume is touched.**
   - The harness uses a private `mktemp -d` (L12, set to 0700 at L25) and its own `initdb` data directory (L48).
   - It uses its own Unix socket directory (L49-L50 `-k`), so it never touches `/var/run/postgresql`.
   - It listens on TCP only, on `127.0.0.1`, on an ephemeral port (L26-L28; L50 `-h`).
   - The stop is scoped to `-D "$MF_TMP/data"` (L15).
   - `rm -rf` runs only on the mktemp path, only after success (L16), and only once the trap exists (L20, after L12 succeeded).
   - It never uses the `services.postgres` container (5432), apt's default cluster, or `pg_ctlcluster`.
   - The version guard requires `16.` (L9).
   - `check_schema.sh`, which runs inside `run_all_checks.sh`, uses its own socket-only cluster on 55432 and stops it. The two do not interact.
3. **The host accepts this database.**
   - `runtime.txt` (L34) uses only allowlisted keys (`PracticePostgresStore.cs` L37-L40, which include `ssl mode`).
   - Host `127.0.0.1` passes L54-L58. The explicit database, username and password pass L59-L60.
   - The file is 0600 (L39, plus `umask` at L5), outside the repository (L72) and under 4 KiB (L78). It is not group/other-accessible (L79).
   - Startup verification is met by the PG16 binaries and by explicit `fsync=on`, `synchronous_commit=on` and `full_page_writes=on` (L50).
   - It is also met by the marker row (`dev_practice_schema.sql` L26) and the grants (L57-L59).
4. **The schema applies in order.**
   - `server/schema.sql` (L53) has no OWNER, GRANT, ROLE, EXTENSION or search_path statements (grep).
   - The same file already applies under `check_schema.sh` in CI.
   - `dev_practice_schema.sql` (L54) is one BEGIN/COMMIT and depends on nothing in `schema.sql`.
   - Every setup psql call uses `ON_ERROR_STOP=1`.
5. **The dev role is minimal.**
   - `mf_runtime` is `LOGIN` only (L33): not superuser, not CREATEDB or CREATEROLE, and it owns nothing.
   - It gets exactly what the host verifies (L57-L59):
     - USAGE on `dev_practice`
     - SELECT on `schema_marker`
     - SELECT, INSERT and UPDATE on `account_state`
   - It has no DELETE, TRUNCATE or DDL. It cannot read `schema.sql` tables, which `mf_admin` owns with no PUBLIC grants.
   - Only PostgreSQL's PUBLIC defaults (CONNECT, TEMP) remain. L56 duplicates the CONNECT default; harmless.
6. **The oracle is the bootstrap superuser `mf_admin`, using its own `PGPASSFILE` (L41-L43).**
   - Superuser is needed for three check operations (`PostgresChecks.cs` L85-L107):
     - the exact-row tamper/replace
     - the `FOR UPDATE` lock holder
     - seeing other sessions in `pg_stat_activity`
   - It is test-only and is never handed to the host.
7. **The Npgsql restore is locked.**
   - `server/MemoryForge.Server/packages.lock.json` pins `Npgsql` 10.0.3 with its contentHash for `net10.0`, plus the Core project.
   - The csproj sets `RestorePackagesWithLockFile` and `Version="10.0.3"`.
   - L29 is the job's first restore of the server, because `run_all_checks.sh` builds only CoreChecks.
   - L30 and the harness's nested `dotnet build … -c Debug -o …` (no RID; ServerChecks `Program.cs` L365) reuse that verified graph.
   - Core has no packages. Its lock file is consistent if present and not required if absent.
   - ServerChecks has no packages.
8. **No secret reaches output.**
   - Generated secrets exist only in 0600 files (L29-L39).
   - All setup output goes to the private `setup.log` (L48-L60), and there is no `set -x`.
   - ServerChecks prints names, codes and fingerprints and redacts known secrets (`Program.cs` L6-L8, L73-L77).
   - It reads the database password only to scan output for it (`PostgresChecks.cs` L121-L128).
   - The harness itself prints only the retained-path line on failure (L17).
   - The workflow references no secrets. `POSTGRES_PASSWORD: ci` is pre-existing and unused.
9. **No paid dependency or service.**
   - Ubuntu `postgresql-16` is already installed by the pre-existing step L24.
   - Npgsql comes from nuget.org, and the checks are BCL-only.
   - The only cost is extra runner time against the repository's existing Actions allowance.
10. **No hidden CI coupling.**
    - CI never runs `verify:manifest`.
    - `test_handoff.py` checks the AI-handoff documents only.
    - `tests/test_delivery_files.py` exercises the manifest builder only in a temporary root.
    - So the new script and the ci.yml change cannot fail a manifest check in CI.

## Non-blocking notes (optional; Root decides; none changes the verdict)

1. **The admin `PGPASSFILE` is exported to everything after L47**, including ServerChecks and every host process.
   - It is not exploitable today. The host refuses any connection file without an explicit password (`PracticePostgresStore.cs` L59-L60), so Npgsql never reads the passfile.
   - The oracle sets its own copy (L41).
   - Minimal hardening: drop the `export` and pass it only to L51 (`PGPASSFILE="$MF_TMP/pgpass" "$MF_PGBIN/psql" …`). Alternatively, `unset PGPASSFILE` before L61.
2. **Setup failures are opaque in the CI log.**
   - L48-L60 send all output to the private `setup.log`. A failed `initdb`, `pg_ctl` or psql step therefore shows only the retained-path line (L17).
   - Do not print `setup.log` or `postgres.log`. A failed L52 can echo `CREATE ROLE … PASSWORD '…'`, and PostgreSQL logs it as `STATEMENT:`.
   - Minimal fix: add `trap 'echo "check_practice_postgres: failed at line $LINENO" >&2' ERR` after L20. It prints a line number only, never a secret.
3. **Triage after a CI failure needs a re-run.**
   - On failure, the stopped cluster, test credentials and both logs stay in `/tmp` on the ephemeral runner and disappear with it. ServerChecks's own `mf-server-checks-*` directory does too.
   - This is the right privacy trade-off. Do not add an artifact upload for these directories.
   - Triage by re-running the harness on Linux.
4. **Port reservation (L26-L28) is check-then-use.**
   - Another process could take the port before `pg_ctl` (L50) binds it.
   - The window is short. The outcome is a loud `pg_ctl` failure, never use of a foreign server, because the cluster and its password are new.
   - Acceptable as is.
5. **The first real CI run is the proof of the locked restore.** The base CI never ran `--locked-mode`.
   - setup-dotnet floats on `10.0.x` (L21), and the lock was generated by Root's SDK. Drift fails loudly with NU1004.
   - If that becomes noisy, pin the SDK with a `global.json` (Root-owned).
   - The nested restores (L30, L61) are not locked, but they follow the verified graph.
   - Optional: add `<RestoreLockedMode Condition="'$(CI)' == 'true'">true</RestoreLockedMode>` to the server csproj (Root-owned).
6. **The runner image can drift.**
   - `runs-on: ubuntu-latest` (L8) with the distro `postgresql-16` (L24, pre-existing) works on the current image, per Root's passing runs with L24.
   - A future `ubuntu-latest` without `postgresql-16` in its archive would fail L24 or the harness (exit 2) loudly. It would not skip.
   - Pinning `ubuntu-24.04` avoids this.
7. **There is no step timeout.**
   - ServerChecks bounds its own waits (psql 120 s, listening 120 s), but the job keeps the 360-minute default.
   - `timeout-minutes` on L31 would cap a hang on the private repository's Actions minutes.
   - Choose the value from a measured run; that is NOT_RUN here.
8. **The service container is unused** (pre-existing, not part of this diff).
   - No step uses `services.postgres` (L9-L13). Grep finds no 5432 or `PG*` environment use outside `ci.yml`.
   - Removing it in a separate change saves the image pull and removes an unused database with password `ci` from the runner.
9. **Cosmetic.**
   - L56 duplicates PUBLIC's default CONNECT grant. A stricter form is `REVOKE ALL ON DATABASE mf_practice_checks FROM PUBLIC` followed by the CONNECT grant.
   - `umask 077` (L5) also applies to the nested build outputs. They belong to the same user, so this is harmless.
   - L61 rebuilds the server once more, which costs only time.

## Execution status

- **Reviewer: NOT_RUN.** No shell was available in this call. The reviewer did not run any of these:
  - `bash -n`
  - the harness
  - `dotnet restore --locked-mode`
  - either ServerChecks mode
  - any CI
- **Root, as reported to the reviewer and not re-verified here:**
  - `bash -n tools/check_practice_postgres.sh` passes on the Mac.
  - The native harness was NOT_RUN on the Mac. It has no PG16 binaries, so the harness exits 2 by design.
  - Push 38902926589 and PR 38902934597 passed for `47502b7` on the base workflow, which has neither L29 nor L31. PostgreSQL CI mode therefore remains NOT_RUN.
- **Evidence needed before P1-2 §17.2/§17.5 counts as CI-verified**, from Root's independent Linux/exact-CI run:
  - L29 green.
  - L31 green, with its `SERVER CHECKS PASS [postgres]: N assertions` line.
  - L28 and L30 green in the same run.
