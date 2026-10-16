# MemoryForge.Server — local development practice host (P1-LOCAL-AUTHORITATIVE-PERSISTENCE)

This is a **local development slice**, not the production API of `server/openapi.yaml` and not P1-2 as a whole. There is no PostgreSQL,
real account/auth, gacha, collection, purchase, Unity transport or deployment here. `server/schema.sql`, `server/openapi.yaml`, Core and
`data/` are unchanged.

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
    manual target/pick alternative; a client must still send its real choice and accept the command result.
  - `slots` are Core's raw values (`base`, `temp`, `max` per kind, `sealedUntil`). They are not the §17.4 computed `cap`/`used`/`sealed`:
    Core has no public capacity query yet, and the server does not re-derive it. Per-card `SLOT_FULL` still comes from `Check`.
  - `piles.discard`/`piles.exhaust` are counts only; they do not enumerate candidates for recall/discard/exhaust choices.
  - A pending event shows its event ID, `done` and `choice`; rendering its text and options needs the client's matching content.

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

Command kinds and arguments: `enter{nodeId}`, `play{uid, mode?, target?, picks?}`, `ult{actor, target?, picks?}`, `end`, `takeCard{id}`,
`skipCard`, `takeRelic{id}`, `takePart`, `evolve{key, branchId}`, `proceed`, `archive{choice: return|continue}`,
`rest{choice: heal|upgrade, key?}`, `forgeOffer{key}`, `forgeApply{partId}`, `forgeRemove{key}`, `applyPart{partId, key}`,
`buy{kind: cards|parts|relics|remove, id?, key?}`, `event{index, keys?}`, `assign{key, charId}`.

## Checks

```sh
dotnet run --project server/MemoryForge.ServerChecks -- "$PWD" [--server-dll <built MemoryForge.Server.dll>]
```

They build the host, launch real processes with a temp test-only fixture/credentials/stores, and exercise startup refusals, auth,
strict input, restart/retry, duplicate/conflict/stale, rule and write-failure rollback, a runtime appsettings Kestrel endpoint that must
never open, conflicting loopback hosting URLs (command line and environment) that must not move the configured port, a genuine partial Core mutation, ownership, two processes on one store, tampered stores and a complete-capture leak scan.
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
- Not in this slice: PostgreSQL and its migrations, real account auth, network UI recovery flows and Unity transport.
- Performance: every request replays the whole current journal and rewrites the whole envelope; receipts and finished runs are never
  pruned, so the 512 MiB dev-store boundary is eventually reached. Snapshots/compaction need an approved retention and migration policy.
- Binding is strict: rebuilding Core (another module ID), changing `data/game_data.json` or the fixture refuses existing envelopes.
- The host reuses the reviewed loaders from `core/MemoryForge.CoreChecks` as compile links (plus `JsonGraphComparison.cs`, which
  `MemoryV2DataLoader` references); moving them to a shared host library is a separate change.
- Windows is refused (POSIX permissions and flock semantics are part of the contract).
