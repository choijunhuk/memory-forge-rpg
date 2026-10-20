# MemoryForge.Server — local development practice host (P1-LOCAL-AUTHORITATIVE-PERSISTENCE, P1-POSTGRES-TRANSACTIONAL-PRACTICE)

This is a **local development slice**, not the production API of `server/openapi.yaml` and not P1-2 as a whole. There is no production
database or migration, real account/auth, gacha, collection, purchase, Unity transport or deployment here. `server/schema.sql`,
`server/openapi.yaml`, the SQL files and `data/` (all numbers and content) are unchanged. Core gained pure read-only presentation queries
for the battle query below (P1-PUBLIC-BATTLE-QUERY: `Battle.ReservationSlots`/`HandChoices`, the shared `BattleChoicePolicy`, and the
`Run.BattleReservationSlots`/`BattleHandChoices` wrappers); no Core rule or command changed. Storage is a private file per account by
default, or, when selected explicitly, one Development-only PostgreSQL row per account (see "PostgreSQL backend").

## What it does

- Real expeditions with the shared `MemoryForge.Core`: `Run.Create` from a trusted fixture party and a server-chosen CSPRNG seed/runId,
  then only existing public `Run` methods (`Enter`, `BattleCommand` play/ult/end, rewards, rest/forge/shop/event, `Proceed`, `Archive`,
  `Assign`, v2 `MemoryChanges`/`PreviewMemory`/`CommitMemory`). Any other kind is refused with `400 UNSUPPORTED_COMMAND`.
- Durability without client authority: each account is one private envelope `account-<id>.json` holding the binding (replay format, rules
  version, SHA-256 of `data/game_data.json`, the exact Core module ID, fixture digest), the existing `mf-memory-v2` store, the current run
  record (seed, runId, start memory, created-state digest, journal of `{kind, args, state digest}`), finished runs kept whole, and every
  committed request's receipt (identity digest + exact response text). The live Run is never stored: every request replays the journal
  from `Run.Create` and checks the full-state digest after each step.
- §17.2 order under the account lock: receipt (same identity → the stored bytes, other identity → `409 IDEMPOTENCY_CONFLICT`) → replay →
  `expectedVersion` (`409 STALE_VERSION`) → Core on that isolated candidate (`422 <Core code>`) → journal + receipt → size guard →
  private temp file, `Flush(flushToDisk: true)`, same-directory rename → reply. A failure before the rename commits nothing; a failed
  requestId is not consumed.
- Store size: one account file may not exceed 512 MiB (`PracticeRunStore.MaxEnvelopeBytes`). The same guard refuses loading a larger file
  and refuses, before any temp file is created, a write that would exceed it (`507 STORE_TOO_LARGE`): no truncation, no consumed receipt
  or version. Growing beyond this dev-store boundary needs the reviewed database/migration work.
- Locks: an in-process gate plus the permanent `account-<id>.lock` opened with `FileShare.None` (flock) with a 10 s bounded wait
  (`503 ACCOUNT_BUSY`). The lock file is never deleted; the data file is replaced, never locked.
- Refusal, never reset: unreadable/edited envelopes (`500 STORE_CORRUPT`), journals that replay differently (`500 STORE_REPLAY_MISMATCH`),
  unknown formats (`409 STORE_UNSUPPORTED`), other content/Core build/fixture (`409 STORE_CONTENT_MISMATCH`, `STORE_IMPLEMENTATION_MISMATCH`,
  `STORE_FIXTURE_MISMATCH`). The file is left byte-for-byte; migration is a separate, reviewed step.
- Public DTO (`PublicView.cs`): explicit allowlist. Hand cards carry Core `CostOf`/`AllowedModes`/`NeedsTarget` and `Check` (auto target)
  per mode; piles are counts; enemies show only the current and next intent. Never returned: seed, RNG, draw order, enemy pattern/index,
  receipts, journal, digests, run flags. Limits of this first DTO:
  - `blocked[mode]` is Core `Check` for the default request of that mode (auto target, **no manual picks**). It is not a verdict on every
    manual target/pick alternative (an explicit pull of a next-turn reservation can be legal while the default pull is
    `PULL_DESTINATION_FULL`); ask the battle query for the exact request, and a client must still send its real choice and accept the
    command result.
  - `slots.next`/`slots.after` keep the raw Core-held `base`, `temp`, `max` and `slots.sealedUntil` unchanged (backwards compatible) and
    add Core's own `Run.BattleReservationSlots` values: `cap` (the capacity a new reservation meets, the seal included), `used` (the
    reservations due exactly on that column's turn, whatever their kind; it may exceed `cap`), `sealed` (`sealedUntil >= turn`, the
    capacity predicate, next column only) and `due` (turn + 1 / turn + 2). The server does not re-derive them; every reservation stays
    listed in `reservations` with its own `due` and `kind`. Per-card `SLOT_FULL` still comes from `Check`.
  - `piles` are counts only. Pick candidates of one hand card's request (hand, discard pile or reservations) are listed only by the battle
    query below, never as a pile dump.
  - A pending event shows its event ID, `done` and `choice`; rendering its text and options needs the client's matching content.

## Battle query (read-only, P1-PUBLIC-BATTLE-QUERY)

`POST /dev/practice/runs/{runId}/battle-query` with exactly `{expectedVersion, uid, mode?, target?, picks?}` (one card play as the
`play` command would send it). Strict like every body: `mode` is `now|next|after` (default `now`), `target` and each pick are ASCII text,
at most 64 picks, an optional member given as `null` is dropped; a `requestId`, seed, state, battle version, operation or any other
member is `400 UNKNOWN_FIELD`, a duplicate member `400 DUPLICATE_FIELD`.

- Pure: it runs under the same credential/owner check and `Store.Read` as the run read, on the replayed Run with the full-state digest
  guard. It creates no account file/row, journal entry, receipt or revision and runs no command; there is no `requestId` to retry.
- Order: another account's or unknown run `404 RUN_NOT_FOUND` → `expectedVersion` must equal the **server journal version** (the run's
  committed command count, as in commands), else `409 STALE_VERSION` → no battle `422 NO_BATTLE` → hand gate.
- Hand gate: a `uid` that is not in the current hand (draw, discard or exhaust pile, a reservation, an enemy, no card at all) gets the one
  fixed `422 NOT_IN_HAND` before its definition, price or forecast is read, so a hidden card instance cannot be probed.
- Reply: `runId`, `version` (server journal version), `phase`, `context {node, turn, battleVersion, battlePhase}` — `battleVersion` is the
  battle's own `v`, which differs from `version` once the run has other commands (a later battle, an `assign`); `source {uid, id, name,
  text, mode, target, picks}` with the effective name and Core `BattleText`; the same computed `slots` as above; `forecast` = Core
  `Run.BattleForecast` of exactly this request (`error`, `mode`, `cost`, `due`, `hidden`, `enemyHpLoss`, `enemyDamage`, `newlyBroken`,
  `blockDelta`, `hpDelta`, `defeat`; with `hidden` the outcome fields stay default, so no draw order or random result is revealed) and,
  added last, `enemyHpLosses [{uid, hpLoss}]`: Core's own `BattlePlayForecast.EnemyHpLosses`, one entry per enemy that loses HP, in the
  battle's enemy order, summing to `enemyHpLoss`, empty with an error or `hidden` (nothing is recomputed; a client without it ignores it);
  `choices` = Core's shared pick sheet for the effective definition and Core prices: `requests [{label, from, count, growthOnly, op, part,
  leading}]`, `accepted` (leading picks that hold), `index`/`left` (the request the next pick fills), `blocked` (`PICK_GROWTH_CAP`),
  `candidates [{uid, id, zone, plus, mods, temp, growth}]` of that request only (`zone` is its `from`: `hand`, `discard` or `res`), and
  `error` (`PICK_INVALID` when a supplied pick does not hold).
- `choices.error: null` only means the pick sheet was computed. Whether the play is legal (phase, AP, target, slot, pull destination) is
  `forecast.error`, and only the real command commits anything. A forecast for next/after is a current-state estimate, not a promise.
- Not provided: draw order, RNG, the card map, replayable history, client transport, an unconfirmed-request store or process recovery.

## Security boundary (local only)

Development environment only (anything else exits 2), Kestrel bound to `127.0.0.1` only: configured `Kestrel:Endpoints` refuse startup,
the default reloadable Kestrel configuration loader is replaced by an empty non-reloading one (an appsettings/environment change after
start cannot open another socket), `PreferHostingUrls` is pinned to false at the builder (so `--urls`/`ASPNETCORE_URLS` with
`preferHostingUrls=true` cannot replace the configured `PracticePort`), and any bound address other than `127.0.0.1` on the configured
port (or the server-chosen port when it is 0) stops the host. Loopback `Host`/remote address only, no CORS, no
anonymous route, no query strings, 64 KiB bodies.
Every route needs `Authorization: Bearer <credential>`; the server compares SHA-256(credential) with the fixture digests in constant time
and the account comes only from that match. Requests are strict allowlists: a client seed, state, party, HP/power or unknown member is
`400`. Logs carry exception type names only.

## Run it

Fixture (owner-only, `chmod 600`; development/test only, the reference practice party, not an owned collection):

```json
{ "schema": "mf-dev-practice-fixture-1", "developmentOnly": true, "label": "local development fixture",
  "accounts": [ { "accountId": "dev-local", "credentialSha256": "<printf %s \"$SECRET\" | shasum -a 256>", "length": "short",
                  "party": [ {"charId": "sera", "res": 0}, {"charId": "harin", "res": 0}, {"charId": "nia", "res": 0} ] } ] }
```

Party entries accept only `charId`, `weaponId` and the growth inputs `res level asc weaponLevel refine armor accessory skillLv ultLv`
(Core derives power/HP). There is no built-in account or default secret.

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/MemoryForge.Server -- \
  --MemoryForge:ContentRoot="$PWD" --MemoryForge:PracticeFixture=/abs/private/fixture.json \
  --MemoryForge:PracticeStore=/abs/private/store --MemoryForge:PracticePort=5173
```

The store must be outside the repository and owner-only (created `0700` when missing). Routes (all JSON, all authenticated):

| Route | Purpose |
|---|---|
| `GET /dev/practice/account` | saved v2 decks/version/history summary, current run summary (pure) |
| `POST /dev/practice/runs` `{requestId, useMemory?}` | new run (`409 RUN_ACTIVE` until the current run is DONE) |
| `GET /dev/practice/runs/{runId}` | run view (pure) |
| `POST /dev/practice/runs/{runId}/commands` `{requestId, expectedVersion, kind, args}` | one journal command |
| `POST /dev/practice/runs/{runId}/memory-preview` `{selectedChangeIds}` | Core `PreviewMemory` (pure) |
| `POST /dev/practice/runs/{runId}/memory-commit` `{requestId, expectedVersion, expectedMemoryVersion, selectedChangeIds}` | Core v2 save |
| `POST /dev/practice/runs/{runId}/battle-query` `{expectedVersion, uid, mode?, target?, picks?}` | one card play's forecast and pick sheet (pure; see "Battle query") |

Command kinds and arguments: `enter{nodeId}`, `play{uid, mode?, target?, picks?}`, `ult{actor, target?, picks?}`, `end`, `takeCard{id}`,
`skipCard`, `takeRelic{id}`, `takePart`, `evolve{key, branchId}`, `proceed`, `archive{choice: return|continue}`,
`rest{choice: heal|upgrade, key?}`, `forgeOffer{key}`, `forgeApply{partId}`, `forgeRemove{key}`, `applyPart{partId, key}`,
`buy{kind: cards|parts|relics|remove, id?, key?}`, `event{index, keys?}`, `assign{key, charId}`.

## PostgreSQL backend (explicit, Development only)

`--MemoryForge:PracticeBackend=postgres --MemoryForge:PracticePostgresFile=/abs/private/practice.conn` (instead of `PracticeStore`).
`PracticeBackend` absent or `file` keeps the file store above; `postgres` together with `PracticeStore`, a connection file without
`postgres`, or any other backend name refuses startup. There is no fallback between backends and no automatic migration or copy.

- Connection file: absolute, owner-only (`chmod 600`), not a symlink, outside the repository, at most 4 KiB, holding only a connection
  string with `Host`, `Port`, `Database`, `Username`, `Password` and optionally `SSL Mode` (Npgsql aliases accepted). Exactly one host,
  which must be a loopback IP or a local Unix-socket directory (no host names, no host lists); database, username and password are
  required. The host forces `Include Error Detail`, `Log Parameters` and `Enlist` off and fixed timeouts; secrets never go on the command
  line, into defaults, logs or error bodies.
- Schema: `server/dev_practice_schema.sql`, applied explicitly by an administrator/test harness to a dedicated development/test database
  after (and without changing) `server/schema.sql`. One marker table plus `dev_practice.account_state(account_alias, envelope bytea)`: one
  row per authenticated fixture alias holding the exact `Envelopes.Encode` bytes (never JSONB), limited to 512 MiB like the file store.
  The host runs no DDL and needs only `SELECT, INSERT, UPDATE` on that table, `SELECT` on the marker and `USAGE` on the schema; before it
  listens it verifies PostgreSQL 16, `fsync`/`full_page_writes` on, `synchronous_commit` not off, the marker and those privileges.
- Transaction per mutation (after the same in-process Core gate and the same shared decision as the file store): `BEGIN` → `lock_timeout`
  5 s, `statement_timeout` 30 s, `idle_in_transaction_session_timeout` 60 s → `INSERT` a fresh valid envelope `ON CONFLICT DO NOTHING` →
  `SELECT … FOR UPDATE` of that one row → receipt/identity → replay → command → `UPDATE` → `COMMIT` → reply. Anything before `COMMIT`
  rolls back, the placeholder insert included, so refused requests create no row and consume no requestId, version or memory.
- Failures: lock wait over 5 s → `503 ACCOUNT_BUSY`; any other database failure before the commit → `503 STORE_UNAVAILABLE`; a failure
  during `COMMIT` → `503 STORE_COMMIT_UNKNOWN` (the commit may have landed; retry with the same requestId, which returns the stored
  receipt or runs the command once). Error bodies are fixed text; logs carry only the operation, exception type and SQLSTATE.
- Reads are one `SELECT` of one row (an atomic committed envelope), take no row lock and never insert.
- Durability rests on PostgreSQL's commit under the verified settings; this slice adds no power-loss or hardware claim of its own.

## Checks

```sh
dotnet run --project server/MemoryForge.ServerChecks -- "$PWD" [--server-dll <built MemoryForge.Server.dll>]
dotnet run --project server/MemoryForge.ServerChecks -- "$PWD" --postgres-file /abs/private/practice.conn --psql /abs/psql-wrapper
```

The second form is the explicit PostgreSQL mode (the file suite is then NOT_RUN in that invocation; without both arguments the
PostgreSQL mode is NOT_RUN). It needs a dedicated test database with `server/dev_practice_schema.sql` applied, the application role's
connection file, and a psql wrapper already bound to that database (ordinary `-X -q -A -t -v` flags, SQL on stdin, no connection
arguments). It runs real hosts on fresh run-tagged fixture aliases: PostgreSQL startup refusals (including a non-existent database), auth
and strict input with no row created, create/enter/play with exact receipt retries across a SIGKILL, a legitimate defeat and v2 save with
an earned card surviving another SIGKILL, 16-way duplicate/stale races in one and two hosts (checked against the one stored row), a real
row-lock timeout and retry, a genuine partial Core `Enter` rollback, content-binding refusal, tampered rows refused and preserved, the
mandatory `pg-battle-query` scenario, and the complete-capture privacy scan (including the database password). Database outage/reconnect, revoked permissions during requests
and a database without the schema marker are not covered by these checks.

They build the host, launch real processes with a temp test-only fixture/credentials/stores, and exercise startup refusals, auth,
strict input, restart/retry, duplicate/conflict/stale, rule and write-failure rollback, a runtime appsettings Kestrel endpoint that must
never open, conflicting loopback hosting URLs (command line and environment) that must not move the configured port, a genuine partial Core mutation, ownership, two processes on one store, tampered stores and a complete-capture leak scan.

Both modes make the battle query scenario mandatory (`battle-query` / `pg-battle-query`, required before the privacy scan counts): auth,
ownership and no file/row on refusal, `NO_BATTLE` on the map, stale versions, the strict body, every hand card and mode compared member
by member with a check-side Core replay of the stored inputs (the forecast's exact member list and its `enemyHpLosses` entry by entry;
an enemy-target card is also asked at every other living enemy), explicit picks, the fixed `NOT_IN_HAND` for every instance outside the hand
(byte-identical to no card), byte/SHA-256 envelope purity, and at least one query round whose server journal version differs from
`battle.v`, each checked against its own source. The v2 assignment boundary is checked too: a saved starting-memory key (the base common
card included) is refused with `REASSIGNMENT_REQUIRES_SEPARATE_FLOW`, so the separate versions come from a legitimate assignment of a card
gained in the run or from progression to a later battle. This proves the query contract, not client transport or process recovery.
Whether a real round meets a play that hits several enemies for different amounts depends on the server-chosen seed and the hand; such
splits themselves are pinned by Core's `BattlePreviewChecks`, and every real reply here must equal the oracle.
Console failure output carries only check names, statuses, safe codes and keyed fingerprints; raw responses, host/build output and
exception text go to `diagnostics.log` (0600) in the owner-only temp directory, which is kept only on failure. Run as a normal user (the
write-failure check needs enforced directory permissions; as root it fails rather than skips).

The end-to-end path uses a simple greedy check bot over the stochastic, server-seeded runs: it must win a first battle and save a reward
card within four departures. If it cannot, the suite fails with an explicit bounded-bot message; that is a limit of the check bot, not an
observed server result, and no game value or seed is adjusted to make it pass.

## Known limits / follow-ups

- Durability scope: the new envelope is flushed (`Flush(flushToDisk: true)`) and atomically renamed in the same directory before the reply,
  which covers a crash of the host process (the file is the previous or the new complete envelope). The directory entry is not `fsync`ed
  and macOS `F_FULLFSYNC` is not used, so nothing is claimed for power or storage failure: afterwards the store needs inspection and
  recovery, and a client retry is not guaranteed to be safe (for example, a lost create receipt would choose a new seed and runId).
- Not in this slice: the production schema/API mapping (UUID accounts and runs, `command_receipt`, `memory_deck`) and its migrations,
  real account auth, network UI recovery flows and Unity transport. The Development PostgreSQL rows are never a migration source.
- Performance: every request replays the whole current journal and rewrites (file) or updates (PostgreSQL) the whole envelope; receipts
  and finished runs are never pruned, so the 512 MiB dev-store boundary is eventually reached. Snapshots/compaction need an approved
  retention and migration policy.
- Binding is strict: rebuilding Core (another module ID), changing `data/game_data.json` or the fixture refuses existing envelopes. The
  Core build with the battle-query queries is such a rebuild: older dev envelopes/rows answer `409 STORE_IMPLEMENTATION_MISMATCH` and are
  preserved byte for byte, never reset or migrated; use a fresh store or a fresh dedicated test database.
- The host reuses the reviewed loaders from `core/MemoryForge.CoreChecks` as compile links (plus `JsonGraphComparison.cs`, which
  `MemoryV2DataLoader` references); moving them to a shared host library is a separate change.
- Windows is refused (POSIX permissions and flock semantics are part of the contract).
