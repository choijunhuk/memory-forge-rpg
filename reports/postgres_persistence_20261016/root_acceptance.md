# PostgreSQL development persistence — Root acceptance

Scope: original §17.2 transaction/receipt atomicity, §17.4 privacy, §17.5 existing memory v2 and §14.7 recovery, within the local §20.9 P1-2 slice. A real existing Core expedition and earned saved card must survive host/DB restart; two hosts must not duplicate benefits; failed commands must not retain partial benefits. This is a Development-only practice backend, not production account/authentication, an economy ledger or Unity network recovery.

## Implementation and review

The human explicitly approved free Npgsql **10.0.3** and its required Logging.Abstractions dependency in this chat. Actual net10 restore locks Npgsql10.0.3 only; logging abstractions are provided/pruned by the ASP.NET shared framework. Official TLS download equals the cached signed archive, and `dotnet nuget verify --all` verifies the NuGet.org repository signature. The signed-archive hash and NuGet content fingerprint are different hash layers and were not rewritten to agree. See `approved_dependency.json` and `package_signature_verify.txt`.

Official Claude Code **Opus5.5 Max** authored nine scoped source/test/report files in its separate worktree. Root added the approved project reference/lock configuration. The actual session has81 positive-output records with the sole Opus model and per-turn Max. The CLI then hit the included **session limit** and exited1; its original `FAILED` result is preserved, author execution is **NOT_RUN**, and no paid/API/new-session/model fallback was attempted. Completed source is independently accepted here; the failed CLI job is not relabelled successful. See `actual_cli_metadata.json`, `claude_limit_checkpoint.json` and the original task result.

Codex reviewed the shared facade, HTTP configuration, binding, receipts and file-behaviour delta. An additional independent Sol6.1 Max reviewer examined frozen transaction/settings/schema and final test sources, found no new blocking defect or weakening of the file suite, and did not run the application or read credentials. `independent_source_review.json` records exact hashes and limits. `import.json` proves all11 imported files match the author checkpoint or approved package configuration.

- Existing `PracticeRunStore` makes the same single decision: decode/binding → receipt identity before versions → full-state replay → isolated Core command → receipt/revision → encode/size guard. The original file lock, load, flush and rename path remains the default.
- Explicit `PracticeBackend=postgres` stores the **same opaque envelope bytes** in one additive `dev_practice.account_state` row. Original `server/schema.sql` and its UUID tables remain unchanged and separate. No JSONB conversion, automatic migration, backfill, pruning or fallback.
- One data source per host; parameterized READ COMMITTED transaction; insert-if-absent and row lock serialize even the first creation. The transaction commits the whole memory/history/journal/receipt envelope before returning success. Reads never create rows.
- Connection credentials live in a private outside-repository file. Only an explicit loopback IP/Unix socket and allowed settings are accepted. Host startup verifies PG16, schema marker, required DML privileges and non-disabled durability settings. This is startup verification, not protection against later administrator changes or a hardware durability guarantee.
- Errors before COMMIT retain no new benefit/ID/version. A COMMIT acknowledgement failure reports `STORE_COMMIT_UNKNOWN`; the same request ID resolves the original result. The generic interface comment about write failures is overbroad for an in-flight COMMIT: the actual PostgreSQL code, response contract and this acceptance require the explicit unknown-outcome rule.

## Fresh Root execution

Root used a fresh owned PostgreSQL16.15 database, separate from the author-worktree preview DB; no existing user database/volume was used. Original SQL and the new additive schema were applied explicitly by the test administrator. Runtime credentials have DML access to the development envelope and no original account-table access. Docker publishes only127.0.0.1. Private credentials, SQL oracle output and raw account/seed data are not committed.

| Check | Result / evidence |
|---|---|
| Locked dependency restore; host/checks builds | PASS; both builds0 warnings/errors. `root_locked_restore.txt`, `root_server_build.txt`, `root_checks_build.txt` |
| Existing default file suite | PASS **251 assertions**, `root_file_checks.txt`; PG mode explicitly NOT_RUN in that invocation |
| Explicit real PostgreSQL suite | PASS **202 assertions**, `root_postgres_checks.txt`; file suite explicitly NOT_RUN in that invocation |
| Independent actual PostgreSQL/HTTP fault probes | PASS **27**, `root_independent_faults.json` |
| Independent existing HTTP/process boundaries | PASS **24**, `root_http_boundaries.json` |
| Existing Node / proposals / Python | PASS **333 /14 /57**, corresponding test logs |
| Docs / handoff | PASS **9 /13**, `docs.txt` and `handoff.txt`. Handoff uses the existing Memory Forge Python environment with the already declared PDF requirements. System Python and the generic bundled Python lacked PDF modules; both initial failed logs are preserved, no dependency/test was removed or added to fix this environment selection. |
| Actual Unity MCP Play smoke | Existing expedition map rendered and inspected (`unity_play_map.png`); Play/stop tool receipts, Console0 errors/2 depth-surface warnings in `unity_play_smoke.json`. Editor-state cache remained stale during Play and is not used as a readiness proof. No new Unity automated suite, server transport or device acceptance. |

Counts vary with legitimate server CSPRNG/check-bot routes; no count threshold, game statistic or balance goal changed. The author-worktree preview separately passed file262/PG201 and the27 fault probes; it is not substituted for Root results.

The authored PG suite requires real create/enter/combat, actual earned-card v2 save, SIGKILL/new process, original receipt and next-departure preservation; same-ID/conflicting-version races across two hosts; ownership/strictness; a real held row lock; genuine partially mutating Core Enter failure; corrupt/content/fixture/Core-binding refusal; full exercised-response/log privacy scans.

The independent27 fault probes additionally prove initially absent-row create races, live UPDATE privilege revocation and restoration, actual database stop/start on the same volume, no file fallback, missing schema marker without automatic DDL, and **lost successful COMMIT acknowledgement**. A private loopback relay observes PostgreSQL's successful `CommandComplete COMMIT`, drops its acknowledgement, and SQL independently confirms revision1/one receipt. The host returns `STORE_COMMIT_UNKNOWN`; identical retry returns the stored response bytes with the entire row unchanged. No product fault seam, seed or expected-state injection was added.

The first independent probe incorrectly expected an unused host's stale pooled connection to succeed on its first request after DB restart. It instead safely returned503. `independent_faults_initial_probe_failure.json` preserves that failure. The corrected probe requires that fixed unavailable error, exact row immutability and successful same-ID reconnect/retry; this did not change application code or weaken atomicity.

## Preserved, deferred and remaining risks

- All47 accepted protected hashes, original423 manifest entries/same29 historical changed paths, original SQL/OpenAPI, Core/Unity/JS/data and protected runner are preserved; manifests are not regenerated. Original1320/32 and expanded5280/21 balance misses remain separate and unchanged.
- A new Root-authored native PG CI harness was prepared but **not integrated**, because Claude's quota prevents its required opposite-AI review. It is preserved privately for that review. Existing CI still runs the file suite; remote CI does **not** prove the PG suite ran. Exact pushed-head CI is recorded separately after push.
- No production account/collection authorization, ledger/payment/gacha, original API/SQL migration, Unity transport/full public-state UI recovery, native/device,100-client load or power-loss acceptance. The original SQL tables remain empty in the acceptance DB.
- Whole-envelope replay/rewrite grows with history, bounded at512MiB without pruning. There is no separate PG normal-path whole-state/RNG/log/events oracle against another engine; runtime replay checks every recorded full digest, and the unchanged Core regression is a different claim. No512MiB allocation campaign or kill-at-every-instruction test.
- Scope remains local and explicit; no paid service, fallback provider, public repository switch, merge or production deployment. GitHub PR2 stays private/draft.

Next dependency: complete calculated public DTO/manual-candidate coverage and then explicit Unity transport/recovery; its input must distinguish unsent intent, server acknowledgement and approved presentation. New meaningful Codex implementation and the deferred PG CI harness require Claude review after included allowance returns. Read-only contract mapping and current verification can proceed independently.
