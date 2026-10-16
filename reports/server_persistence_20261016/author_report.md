# P1-LOCAL-AUTHORITATIVE-PERSISTENCE — author report (Claude, official Opus 5.5)

- Tasks: `P1-LOCAL-AUTHORITATIVE-PERSISTENCE` (Max: analysis, project) → `P1-LOCAL-PERSISTENCE-IMPLEMENTATION-FINISH` (xhigh: remaining code),
  same session `a1d7a222-69d8-4b5b-8137-fdcd8d5e2b39`, included subscription only. No Bash/MCP/agents/images/commit were used.
- **Author execution: NOT_RUN.** This session had no shell, so nothing was compiled, launched or tested by the author. Every behaviour
  below is the design as written, not an observed result. Root compiles, runs and reviews independently.

## Requirements → role

§17.2 receipt-first transaction and atomicity, §17.4 public-only DTO, §17.5 existing v2 store semantics, §14.7 retry/recovery, §20.9 P1-2
(first local slice only). Game role: an expedition and its saved memory survive a process restart; the client controls neither RNG nor
state, and a failed command never leaves partial Run benefits.

## Chosen boundaries

- **Development-only loopback host**, explicit `/dev/practice` routes (not the legacy production API). Exits 2 unless Development, required
  absolute config, an owner-only fixture, an owner-only store outside the repository, and a port; refuses injected `Kestrel:Endpoints`;
  stops if bound anywhere but `127.0.0.1`. No CORS, anonymous route, static files or query strings. Windows refused.
- **Trusted fixture** (`mf-dev-practice-fixture-1`, `developmentOnly: true`, label): accounts with SHA-256 credential digests (no secret on
  disk, no built-in account/key), the reference party (growth inputs only; Core derives stats) and run length. Each account is a trial
  `Run.Create` before listening. Credentials are matched in constant time against every digest; the account comes only from the match.
- **Version-pinned journal replay**: real `Run.Create` from recorded server-chosen inputs (CSPRNG seed and runId, start memory/version,
  useMemory), then a small fixed dispatcher over existing public `Run` methods; full-state SHA-256 after Create and after each command.
  No restore API, no state injection, no Core change. Envelope binding = replay format + rules version + content SHA-256 + Core module ID
  + fixture digest.
- **Store**: one private envelope per account (`{schema, sha256, body}`), permanent `FileShare.None` lock file (flock) plus an in-process
  gate, bounded waits, receipts checked before the version check, isolated candidate per request, temp file + `fsync` + same-directory
  rename. Corrupt/tampered/mismatched envelopes are refused and preserved byte for byte.
- **Public DTO**: explicit allowlist of real Core state; costs/modes/targets/usability from Core queries; piles as counts; current and next
  intent only; events allowlisted; no seed/RNG/draw order/pattern/receipts/journal/digests/run flags. Query purity is enforced at runtime
  (state digest before = after, otherwise 500 and nothing written).

## Files (only the ten allowed)

| File | Content |
|---|---|
| `server/MemoryForge.Server/MemoryForge.Server.csproj` | net10.0 Sdk.Web, Core reference, compile links to the 4 reviewed loaders + `JsonGraphComparison.cs` (needed by `MemoryV2DataLoader`) |
| `server/MemoryForge.Server/Contracts.cs` | strict request allowlists, `CheckArgs` (live + replay), identities, `GraphJson`, `ApiError`/`Reply` |
| `server/MemoryForge.Server/Content.cs` | content load/binding, fixture parsing/validation, constant-time credential match, `Run.Create` setup |
| `server/MemoryForge.Server/PracticeRunStore.cs` | envelope format, replay verification, lock, atomic persist, fixed `RunCommands` dispatcher |
| `server/MemoryForge.Server/PublicView.cs` | explicit run/battle/memory/account projections |
| `server/MemoryForge.Server/Program.cs` | host startup refusals, loopback binding, auth, routing, handlers |
| `server/MemoryForge.Server/README.md` | usage, routes, boundary, limits |
| `server/MemoryForge.ServerChecks/MemoryForge.ServerChecks.csproj` | BCL console checks; Core + loaders only for the check-side oracle |
| `server/MemoryForge.ServerChecks/Program.cs` | process/HTTP checks |
| `reports/server_persistence_20261016/author_report.md` | this report |

## Supported commands

`enter`, `play`, `ult`, `end`, `takeCard`, `skipCard`, `takeRelic`, `takePart`, `evolve`, `proceed`, `archive`, `rest`, `forgeOffer`,
`forgeApply`, `forgeRemove`, `applyPart`, `buy`, `event`, `assign`; queries: account, run, v2 memory preview; mutation: v2 memory commit.
Anything else is `400 UNSUPPORTED_COMMAND`. Only the commands driven by the checks (create, enter, play, ult when ready, end, takeCard,
proceed, archive if reached, preview, commit) have check coverage; the service commands are wired to Core but have no server check yet.

## Commands for Root

```sh
dotnet build server/MemoryForge.Server/MemoryForge.Server.csproj
dotnet run --project server/MemoryForge.ServerChecks -- "$PWD"            # builds the host into a temp dir itself
dotnet run --project server/MemoryForge.ServerChecks -- "$PWD" --server-dll <path>/MemoryForge.Server.dll
```

Run as a normal user on macOS/Linux (the write-failure check needs enforced directory permissions; it fails, not skips, as root).

## What the checks assert (all NOT_RUN by the author)

Startup refusals (Production, missing/absent fixture, missing credential digest, derived stat in the party, group-readable fixture,
store inside the repository and not created, injected Kestrel endpoint, missing port); 401s and strict 400/404/405/415 inputs with no file
written; create/enter/play with retries returning original bytes, receipt before stale version, different payload 409, stale 409,
RUN_ACTIVE, Core rejections leaving the envelope byte-identical and the requestId unconsumed; SIGKILL + restart serving the same view and
receipts; a greedy check bot to a real defeat, independent check-side Core replay of the stored journal vs the served view, pure previews,
rejected saves (VERSION_CONFLICT/INVALID_SELECTION/STALE_VERSION) unchanged, the v2 save (version 1, history, store receipt), restart,
the next departure starting from the saved deck; 16-way duplicate and stale races in one process and across two processes on one store;
ownership 404s; a real read-only-directory write failure (500, unchanged, retry commits once); a genuine partial `Run.Enter` mutation
(test-local unknown encounter enemy, confirmed by a direct Core probe) rolled back, then that envelope refused by real content; eight
tampered/unknown/mismatched envelopes refused and preserved; private file modes; a final scan of every response and process log for
credentials, seeds, forbidden members and exception text.

Known check-side assumption: the simple greedy check bot plays stochastic, server-seeded runs and must win a first battle and save a
reward card within four departures; otherwise the suite fails with an explicit bounded-bot message (a check-bot limit, not an observed
server result). No design-era bot statistic is used as evidence for it, and no game value or seed is adjusted. Defeat by only ending
turns is bounded at 3000 commands.

## Not implemented / follow-ups (not claimed)

PostgreSQL/schema migration, real auth (Play Games/guest/JWT), account/collection/gacha/growth/purchase/mail APIs, OpenAPI contract tests,
rate limits, production/staging, network UI recovery flows, Unity network transport, Android. Durability is claimed only for a host
process crash (flushed temp file + same-directory atomic rename before the reply); with no directory `fsync` or macOS `F_FULLFSYNC`,
power/storage failure has no tested guarantee and needs inspection/recovery, and a retry is not guaranteed safe then (a lost create receipt
would pick a new seed/runId). Replay and rewrite cost grow with history up to the 512 MiB dev-store boundary (no pruning/snapshots: needs
an approved retention/database/migration policy). Strict Core-module pinning refuses envelopes after any Core rebuild. The DTO shows raw
slot base/temp/max/seal (not a computed §17.4 cap/used/sealed), pile counts (not manual-choice candidates) and `blocked[mode]` for the
default request without manual picks; event rendering needs matching client content. Service commands lack server checks. Loader location
coupling to `core/MemoryForge.CoreChecks` remains.

## Review fix — P1-LOCAL-PERSISTENCE-REVIEW-FIX (same session, xhigh; author runtime NOT_RUN)

Root's results quoted below are Root's own runs, not author execution; nothing here was compiled or run by the author.

| # | Finding (source) | Change |
|---|---|---|
| 1 | Root repro: a runtime appsettings `Kestrel:Endpoints` change opened a second loopback socket | `Program.cs` replaces the default reloadable Kestrel loader with `Configure(<empty>, reloadOnChange: false)` before `Listen`; startup refusal and the start-time address check stay. New check `KestrelReloadCannotOpenEndpoints`: host in a temp working directory with `{}` appsettings, then an endpoint on a free 127.0.0.1 port is written; the port must stay closed for 8 s of probing while the authenticated route still returns 200. |
| 2 | Root run: 221 assertions, all 190 responses scanned clean, then `scanned > 200` failed | The incidental count is gone. The scan now requires every started request's response to be captured (`scanned == started`, nonzero), every required scenario to have completed, and every launched process's output to be scanned. No key/value/token/seed/exception/log assertion was removed. This was not an observed leak. |
| 3 | Sol review: `Status`/`Same`/`Finish`/`Main` echoed raw runtime values | Console failure text carries only check names, statuses, codes matching the uppercase constant shape, and HMAC fingerprints (per-run random key, not brute-forceable). Raw bodies, host/build output and exception text go to `diagnostics.log` (0600) in the owner-only temp directory (deleted on success), and known credentials/seeds are additionally redacted from console lines. `DiagnosticSafety` plants a credential, a seed and an error body and asserts that none of the failure formatters echo them and that the raw material is only in the 0600 file. Original bodies remain in memory for the leak scan. |
| 4 | Load refused >512 MiB but Persist could write more | One shared `MaxEnvelopeBytes` guard (`RequireWithinLimit`) on load and before the temp file is created (`507 STORE_TOO_LARGE`); no truncation, receipt or version change. Static-inspection item, no >512 MiB test allocation. |
| 5 | Durability wording promised too much | Header, README and this report claim only the process-crash scope (see above). |
| 6 | Conflict test depended on the starting hand | The post-restart conflict now changes only `expectedVersion`, so the identity always differs. Not a server defect. |
| 7 | DTO/doc limits and bot evidence | README documents raw slots vs computed cap, pile counts vs candidates, `blocked[mode]` without manual picks, event content, and the missing PostgreSQL/Unity transport/network recovery; the design-era bot statistic is no longer cited. |
| + | Leak-helper coverage | The response scan now flags known seeds/credentials inside strings; the log scan also flags raw private JSON members (`"rng":`, `"seed":`, …). Covered by the planted-marker assertions. |
| + | Earned-card save restart | After the first nonempty earned-card save (run 1 or a later bounded departure), the host is SIGKILLed and a new process must serve the saved card and return that save's original receipt bytes before the next departure is checked. |

## Follow-up — P1-LOCAL-HOSTING-URL-PIN (same session, High; author runtime NOT_RUN)

Root's safe loopback-only repro on the committed `7e38544` host: `MemoryForge:PracticePort=53122` with `--preferHostingUrls=true
--urls=http://127.0.0.1:53123` made the host report and listen on 53123. The endpoint-section refusal and the non-reloading Kestrel loader
do not govern `PreferHostingUrls`. Change in `Program.cs`: `builder.WebHost.PreferHostingUrls(false)` (official `IWebHostBuilder`
extension, applied at the builder after the command line/environment are read), and the post-start check now requires the single address
to be `http://127.0.0.1` on the configured port (any port only when it is 0). New required scenario `hosting-url-pin` in ServerChecks: two
free loopback ports, an explicit `PracticePort`, conflicting `urls`+`preferHostingUrls=true` once as command-line configuration and once as
`ASPNETCORE_URLS`/`ASPNETCORE_PREFERHOSTINGURLS`; the host must listen on the configured port, serve the authenticated route there and leave
the other port closed. No wildcard/public address is used. All earlier fixes and checks are kept. Not compiled or run by the author.

CI (read-only review of `.github/workflows/ci.yml`): `ubuntu-latest`, `setup-dotnet` 10.0.x on PATH, the unprivileged `runner` user (the
write-failure check needs non-root), Linux flock/inotify/chmod, `/tmp` outside the repository. The step
`dotnet run --project server/MemoryForge.ServerChecks -- "$PWD"` is compatible as written: no concrete incompatibility found, and no new
package or service is needed. Not executed by the author.
