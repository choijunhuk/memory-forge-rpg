# PostgreSQL CI gate — Root acceptance, 2026-10-17 KST

Original requirements §17.2/§17.5 and P1-2: PostgreSQL persistence must be checked by actual HTTP/child-server/DB transactions in CI, separately from the existing file backend. This completes the previously deferred CI orchestration; it does not add game rules, production authentication, migration or Unity network recovery.

## Change and independent review

- `.github/workflows/ci.yml` adds exactly the server's `dotnet restore --locked-mode` and `bash tools/check_practice_postgres.sh`. The existing default file suite and protected `tools/run_all_checks.sh` remain unchanged.
- The new script creates its own PG16 cluster, socket and database in a private temporary directory; generated credentials stay private. It runs the existing explicit PostgreSQL mode as an unprivileged user and stops only its own cluster. Missing PG16/root execution fails instead of skipping. It neither uses nor changes the service/default/previous development database.
- Root authored the orchestration. Official Opus5.5 Max independently approved it; High independently approved the narrow hardening delta. Actual CLI exit0, sole Opus usage and per-turn Max61/High6 positive records are in `actual_cli_metadata.json`. Reviewer execution is **NOT_RUN**, distinct from Root execution. Earlier session-limit FAILED results remain unchanged. Subscription authentication/account match and the existing valid24h creditsOFF/autoreloadOFF UI attestation passed; these are not server-side billing enforcement.
- Root accepted two useful optional findings: the admin `PGPASSFILE` is now local to the initial psql command, and an ERR trap reports only a line number/fixed text. The oracle retains its own passfile setting. Broader optional SDK/runner pinning, service removal, timeouts and privilege redesign were not mixed into this change. Assertion counts are observations, not equality targets despite the review's comparison suggestion.

## Actual execution

Root used a new labelled disposable **Linux arm64** container with cached PostgreSQL16.15 native binaries and official .NET SDK10.0.402 verified against Microsoft's SHA-512 metadata. Only that container received Python and the standard CA certificate runtime prerequisites; no host/global or product dependency changed. The repository input was the exact accepted47502b7 archive plus the two candidate files, with the approved public Npgsql cache. No prior DB or volume was used.

| Check | Result |
|---|---|
| `bash -n` | PASS |
| Initial exact native script | PASS190 PostgreSQL assertions; `linux_native_verification.json` |
| Hardened exact script, unprivileged user | PASS188 assertions, all mandatory PG scenarios/privacy; `linux_hardened_verification.json` |
| Application environment probe | Admin passfile absent; the probe then executes the actual .NET CLI, not a simulated check |
| Real setup-failure diagnostic/privacy | Injected psql failure exits23; only fixed text/line number reaches public output; planted secret reaches private setup log only; own cluster stops |
| Exact new locked restore, Linux | PASS; original package lock byte-identical, `linux_locked_restore.json` |
| Docs/handoff | Fresh results recorded separately before commit |
| Exact new remote CI | Pending until the new commit is pushed and checked; the old head's success is not proof of the new PG step |

The first Linux attempt failed before application checks because the minimal validation image had **no CA certificate bundle**. Installing the standard bundle only in that container and checking the real NuGet TLS chain resolved it. The source was unchanged and TLS/audit/signature checks were not disabled; the initial failure/private logs remain local. Neither raw setup/PostgreSQL logs nor credential directories are uploaded. The negative test deliberately reaches the new error branch; it is not counted as a passing gameplay scenario.

The script's final source identity, workflow identity and actual test results are recorded independently. Counts188/190 vary with real CSPRNG/legal check-bot routes; no stat, balance target, scenario or pass threshold was modified.

## Scope retained

Existing original SQL/OpenAPI, Core/Unity/JS/content/data, protected47 source hashes and423 input manifest entries/same29 historical changed paths are preserved. No manifest regeneration, asset migration, late-candidate activation, public-repository change, merge or deployment. This unit changes no Unity game source, so no new Play/native/device result is claimed. The prior real map Play is separate evidence.

Public combat DTO/shared candidate queries are now being implemented in a different scoped worktree; they are not complete because this CI gate works. Real account/collection authorization, production migration, full public-state Unity transport/recovery, hardware power-loss and100-client/device acceptance remain separate work.

References for the isolated verification SDK: [official manual Linux install and checksum verification](https://learn.microsoft.com/en-us/dotnet/core/install/linux-scripted-manual). Verification tooling is not a new game dependency or AI service.
