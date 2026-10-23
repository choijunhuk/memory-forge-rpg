# P1-POSTGRES-TRANSACTIONAL-PRACTICE — author report (Claude, official Opus 5.5 Max)

Session `a1d7a222-69d8-4b5b-8137-fdcd8d5e2b39`, included subscription only. No Bash/MCP/agents/images/AI API/commit/push were used.
**Author execution: NOT_RUN.** No shell was available: nothing was restored, compiled, launched or tested by the author. Root compiles,
runs and reviews. The package (`Npgsql` 10.0.3, human-approved) was added by Root to the server csproj; the author did not edit it.

## Refactor plan (written before editing the existing store)

1. Keep the request-facing facade `PracticeRunStore` with its `Read`/`Mutate` signatures (so `PracticeApi` is untouched) and its one
   global in-process Core gate. Keep `Replay`, `RunCommands`, `Envelopes`, `GraphJson`, receipts and the size guard exactly once in it.
2. Move only the file-specific parts (path checks, permanent lock file, load, flushed temp + rename) into `FileEnvelopePersistence`,
   unchanged in behaviour and order. The shared decision runs inside the persistence's exclusive section as one callback:
   decode + binding → receipt/identity (before any version check) → fresh replay → isolated command → receipt + revision → encode →
   size guard. It returns a tiny immutable `Transition` (reply + optional new envelope bytes; no bytes = stored receipt, write nothing).
3. Add `PostgresEnvelopePersistence` around the same callback: one `NpgsqlDataSource`; per mutation one READ COMMITTED transaction,
   bounded `lock_timeout`/`statement_timeout`/`idle_in_transaction_session_timeout`, `INSERT … ON CONFLICT DO NOTHING` of a fresh valid
   envelope, `SELECT … FOR UPDATE` of that one row, the callback (it receives `null` when this transaction inserted the row, exactly like
   the file backend's "no file yet"), `UPDATE`, `COMMIT`, then the reply. A failure before COMMIT rolls back (the inserted row too); a
   failure during COMMIT is reported as `503 STORE_COMMIT_UNKNOWN`, never as a rollback. Reads are one `SELECT` with no lock and no insert.
4. Storage is the exact `Envelopes.Encode` bytes as one opaque `bytea` row per authenticated fixture alias in the Development-only
   `dev_practice.account_state`; `server/schema.sql` and the original UUID tables are untouched. No JSONB, no second data model.
5. `PracticeBackend` absent or `file` keeps every existing command line; `postgres` is explicit and never falls back to the file store.

## What was implemented (source only; NOT_RUN)

| File | Change |
|---|---|
| `server/MemoryForge.Server/PracticeRunStore.cs` | `PracticeRunStore` is now the facade (gate, decode/binding, receipts, replay, encode, shared `RequireWithinLimit`, `IsInside`); new `Transition` and `IEnvelopePersistence`; the unchanged file logic moved to `FileEnvelopePersistence` (same lock file, load, size guard, flushed temp + rename, error codes). `Envelopes`, `RunCommands`, `Replay` and records are untouched. |
| `server/MemoryForge.Server/PracticePostgresStore.cs` (new) | `PostgresSettings` (connection-file validation: absolute, owner-only, non-symlink, outside the repo, ≤ 4 KiB, key allowlist, one loopback-IP or Unix-socket host, explicit database/username/password; forces `IncludeErrorDetail`/`LogParameters`/`Enlist` off, fixed timeouts) and `PostgresEnvelopePersistence` (one `NpgsqlDataSource`; startup verification of PostgreSQL 16, `fsync`/`full_page_writes`, `synchronous_commit` ≠ off, schema marker and privileges; positional-parameter SQL; the transaction in the plan; `55P03` → `503 ACCOUNT_BUSY`, other DB failures → `503 STORE_UNAVAILABLE`, COMMIT failure → `503 STORE_COMMIT_UNKNOWN`; logs only operation, exception type and SQLSTATE). |
| `server/MemoryForge.Server/Program.cs` | `PracticeHost.ConfigureAsync`: explicit backend selection and mixed-config refusals; the host owns and disposes the data source; logger attached after build. HTTP boundary unchanged. |
| `server/MemoryForge.Server/Contracts.cs` | Fixed messages for `STORE_COMMIT_UNKNOWN` (does not claim nothing changed) and `STORE_UNAVAILABLE`. |
| `server/dev_practice_schema.sql` (new) | `dev_practice.schema_marker` + `dev_practice.account_state(account_alias PK CHECK alias rule, envelope bytea CHECK 1..536870912)`. No FK, no original table touched, no delete, grants documented for the harness. |
| `server/MemoryForge.ServerChecks/Program.cs` | `--postgres-file`/`--psql` mode selection (both or neither); `partial` class; `FixtureJson` takes optional ids; `LeakScan` takes the invocation's required scenarios; `Finish` stops oracle sessions. File-suite scenarios and assertions unchanged. |
| `server/MemoryForge.ServerChecks/PostgresChecks.cs` (new) | `PostgresChecks` psql oracle (hex read, count, exact replace, bounded lock-holder session, password read only for scanning) and the PostgreSQL scenarios as a `ServerChecks` partial reusing its helpers (launch, client, status/same, bot, leak scan, safe diagnostics). |
| `server/MemoryForge.Server/README.md` | PostgreSQL backend section, checks usage, limits. |

`server/schema.sql`, OpenAPI, Core, data, loaders, CI, Unity and the Root-owned csproj/lock file were not edited.

## Commands for Root (not run by the author)

```sh
dotnet build server/MemoryForge.Server/MemoryForge.Server.csproj
dotnet run --project server/MemoryForge.ServerChecks -- "$PWD"                       # file suite, unchanged; PostgreSQL mode NOT_RUN
<wrapper> -X -q -v ON_ERROR_STOP=1 < server/dev_practice_schema.sql                     # admin/test DB only, after server/schema.sql; then grant the app role
dotnet run --project server/MemoryForge.ServerChecks -- "$PWD" --postgres-file <0600 conn file> --psql <wrapper>
```

## PostgreSQL-mode checks written (assertions only; results NOT_RUN)

Required scenarios `pg-refusals`, `pg-auth-input`, `pg-expedition`, `pg-concurrency`, `pg-ownership`, `pg-row-lock`, `pg-partial-mutation`,
`pg-corrupt` must all complete before the complete-capture privacy scan (responses and every host log, credentials incl. the DB
password, seeds, private members, exception text). Fresh run-tagged aliases (`t<hex>-alpha/beta/gamma`); no earlier row is reused or
deleted.
- Refusals: postgres + file store, postgres without file, file backend with a connection file, unknown backend, non-loopback host,
  several hosts, a host name, no password, an unsupported option, session `Options`, a group-readable file, and the real credentials
  against a non-existent database.
- Auth/input: 401s, strict 400s, unknown run 404, and a refused first mutation leave **no row** (placeholder insert rolled back).
- Expedition (alpha): create revision 1 with its receipt; retries return original bytes; conflict/RUN_ACTIVE/stale/unknown arg leave the
  bytea identical; enter; receipt before stale; rejected play does not consume its requestId; **SIGKILL + new host** serves the same view
  and original receipts; bot to a real defeat; pure previews and rejected saves leave the bytea identical; the v2 save puts memory,
  history, both receipts and exactly the committed journal in one row; bounded legitimate departures until an **earned card** is saved;
  **SIGKILL + new host** serves that card and its original save receipt; the next departure starts from it.
- Concurrency (beta): 16 same-ID and 16 same-version requests in one host, then across two hosts; the one stored row must show revision 6,
  six receipts and journal `[enter, end, end, end, end]`.
- Ownership: beta/gamma on alpha's run → 404, alpha's bytea unchanged, gamma still has no row.
- Row lock: a psql session holds `FOR UPDATE`; the mutation returns `503 ACCOUNT_BUSY` within the lock timeout, a read still serves the
  committed version, nothing is committed; after the holder ends, the same requestId commits once.
- Partial mutation (gamma, explicit bad content): Core's `UNKNOWN_ENEMY` Enter leaves the bytea identical (the check-side Core probe shows
  position/RNG really moved inside Core); repeated same-ID failures are not receipts; a real-content host refuses the row
  (`STORE_CONTENT_MISMATCH`) and leaves it byte-identical.
- Corrupt rows: eight tampered/unknown/mismatched variants of alpha's saved envelope are refused on read, create and command and stay
  byte-identical; the host's own last committed bytes are restored and served again.

**Not covered (NOT_RUN, for Root's independent runs):** database outage/reconnect and revoked permissions during requests, the COMMIT-
unknown path (no deterministic way to lose a COMMIT reply without a test seam), and a database lacking the schema marker.

## Limitations (stated, not hidden)

- Whole-envelope replay and rewrite per mutation (now a full `bytea` UPDATE), so cost grows with history up to the 512 MiB dev boundary
  shared by both backends; nothing is pruned. A row-per-receipt/journal design would need its own reviewed migration.
- Core work runs while the transaction holds the row lock; a replay longer than `idle_in_transaction_session_timeout` (60 s) would fail
  as `STORE_UNAVAILABLE` (nothing committed).
- No production schema/API mapping, auth, migration, Unity transport, load test or power-loss acceptance. Durability rests on PostgreSQL
  with the verified settings; the slice makes no hardware claim.
- Compile-time API assumptions Root should confirm against the restored Npgsql 10.0.3: `NpgsqlConnectionStringBuilder.LogParameters`,
  `IncludeErrorDetail`, `Enlist`, `NpgsqlDataSource.Create`, `BeginTransactionAsync(IsolationLevel)`, `PostgresException.SqlState`.
