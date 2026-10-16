# Local authoritative practice server / persistent memory — Root acceptance

Accepted scope: original §17.2 receipt-first transactions, §17.4 public projection, §17.5 existing v2 memory, §14.7 recovery and the first local §20.9 P1-2 slice. The game role is to preserve a real expedition, saved cards and exact successful responses across server-process restart without trusting client state or retaining partial benefits from failed commands.

Latest narrow correction: [hosting URL preference acceptance](hosting_url_pin_acceptance.md), after parent7e38544. Root's final startup probe found and fixed a separate PreferHostingUrls override. Corrected source passed a fresh246-assertion suite, HTTP24 and independent CLI/environment socket/auth probes2/2. The first unit's source hashes/results below remain historical evidence; use `source_hashes_after_hosting_url_pin.json` for the corrected source.

## Implemented and independently reviewed

- `server/MemoryForge.Server`: an actual ASP.NET Core net10 Development-only HTTP host at `/dev/practice`. It binds one approved loopback endpoint, requires a private explicitly labelled development account/party fixture and bearer credential, rejects client seeds/state/derived stats and uses existing Core public APIs. The production SQL/OpenAPI, Core and game data are unchanged.
- Each private account envelope keeps the original v2 memory store, current server-created Run inputs, version-pinned full-digest command journal, complete finished-run records and exact response receipts. A permanent separate OS lock serializes writers across processes. Fresh real `Run.Create` replay isolates each candidate; failures leave the committed envelope/versions/memory/receipts unchanged. Data flush and same-directory rename precede the response.
- Public responses use explicit allowlists and real Core cost/mode/target queries. They exclude seed/RNG/draw order/enemy patterns/private journals/receipts/credentials. No generic Restore API or expected-state injection was added.
- `server/MemoryForge.ServerChecks` runs actual child host processes with private temporary test fixtures. Its CI step was independently reviewed by official Opus; no new package or paid service was added and the protected runner was not edited.

Claude authored the ten owned source/test/report files in a separate worktree. Codex reviewed the host/store/contracts and executed the checks. A separate Sol Max reviewer examined the projection/request identities and the checker, then verified the concrete fixes against frozen source. Actual model/effort receipts are in `actual_cli_metadata.json`; author execution remains **NOT_RUN**, separate from Root execution. All imported files match the final author hashes in `import_receipt.json`.

## Defects found, fixed and reverified

`root_review_round1.md` preserves the failed first review. The actual second-loopback endpoint opened after an appsettings reload; the corrected host replaces the default reloadable Kestrel loader. The original independent probe failed that exact regression, then passed it. Read/write envelope-size guards now agree before file creation (512 MiB dev boundary, 507 without truncation). Overstated power-loss wording was corrected.

The initial BCL run reached 221 assertions and scanned 190 responses, then failed an arbitrary `>200` threshold. That was not a leak. Required scenarios plus complete request/response capture now determine coverage. Raw failure values are confined to a 0600 diagnostics file in a private temporary directory; planted-marker checks cover safe console formatting, string seed detection and structured RNG logs. A real earned-card save now always undergoes SIGKILL/new-process/deck/original-receipt checks. The independent re-review found the prior defects resolved and the important existing assertions retained.

## Fresh Root execution

| Check | Result and evidence |
|---|---|
| Root server build | PASS, 0 warnings / 0 errors; `root_server_build.txt` |
| Exact new CI command, `dotnet run --project server/MemoryForge.ServerChecks -- "$PWD"` | PASS **241 assertions**; `root_server_checks.txt` |
| Separate Root HTTP/process probe | PASS **24**; `root_http_boundary.json` |
| Earlier fixed author-worktree preview | PASS 246 assertions / HTTP24, distinct source location; not substituted for Root execution |
| Unchanged Core full regression | PASS all existing suites, including 1000 legacy expeditions / 273504 commands; `core_regression.txt` |
| Original PostgreSQL schema | PASS PostgreSQL16.15 in a network-disabled cached postgres:16 container, only the three original files streamed in; `postgres_validation.json` |
| Current docs / handoff / Python | PASS 9 /13 /57; `docs.txt`, `handoff.txt`, `python_tests.txt` |
| Actual Unity MCP read-only check | Reinitialized expired HTTP session, actual expedition scene stopped/not compiling/options0, Console0 errors/warnings; `unity_scope_state.json`. No server Play Mode claim. |

Assertion/response counts can vary with server-chosen seeds and the legitimate check bot's route. No threshold, game stat or balance target was changed to obtain a pass. The tests fail explicitly if the bot cannot obtain an earned card within its declared bound; that is a check-bot limit rather than evidence about balance.

The process suite includes authentication/ownership/strict-input refusals; real create/enter/combat/progression/terminal v2 save; exact receipt-before-stale-version retry after SIGKILL; one-process and two-process duplicate/conflicting-version races; read-only-directory write failure; a genuine partially mutating Core Enter failure on test-local bad content; query/preview immutability; corruption/content/Core/fixture mismatch refusals without overwriting; endpoint reload and response/log privacy.

## Preserved and not yet accepted

- Original v0.8.1/0.7.0 numbers, content, source documents, art ownership, protected47 hashes, input423 historical29 changed paths and original manifests remain unchanged. No late candidate activation or balance acceptance: 1320/32 and the expanded 5280/21 misses remain separate historical measurements.
- This is a trusted **local development** fixture/server. Real account ownership/authentication/collection, PostgreSQL app persistence and migration, economy/payment/gacha ledgers, production API/deployment/rate limits and 100-client/device acceptance remain unimplemented here. Passing the unchanged SQL baseline is not database app integration.
- Unity transport/network recovery is not connected. This unit changes no Core/Unity sources, scene, prefab or game UI. The JSON/CI unit's actual Editor160 Play /3 Edit/new native map verification remains distinct; no server Play Mode or physical-device acceptance is claimed.
- Raw slot fields are not complete calculated cap/used/sealed DTOs. Pile counts are not manual discard/exhaust candidates; default `blocked[mode]` is not a verdict on every manual pick. Event display requires matching content. Service commands are wired to Core but not all are exercised by the server suite.
- The independent check-side replay compares selected public values and identities, not the entire private state/RNG/log/events against another engine. Runtime replay validates full stored digests, a different claim. Leak scanning covers the exercised responses and known values/patterns, not a proof about every future field.
- Every request replays the current journal and writes the whole account envelope. History/receipts are not pruned; reviewed DB/migration work is needed before the dev size/performance boundary. The size-guard fix was statically reviewed; no >512MiB allocation test was performed.
- Process-crash/retry tests are not power/storage-failure tests. Parent-directory fsync/macOS F_FULLFSYNC and recovery from power-loss ambiguity are not implemented or claimed. No exhaustive kill-at-every-write-instruction campaign was run.

Local doc/handoff checks and the exact commit's remote CI are recorded separately; a pre-push local PASS is not a remote-head CI result. GitHub remains private and PR2 remains draft; no merge/deployment is authorized by this record.
