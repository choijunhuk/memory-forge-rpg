# Public combat query and shared candidate policy — Root acceptance, 2026-10-17 KST

Original requirements §17.4, §14.3 and §14.7: a client must read accurate public slot occupancy, the already-approved ordered card candidates and safe play forecasts without consuming a command or exposing hidden state. This unit provides that server/Core boundary and preserves the existing Unity local practice. Full network UI and recovery are the next dependent unit.

## Implemented

- `Battle.ReservationSlots` / Run wrapper reuse Core capacity and exact-due occupancy; raw values, seals, both columns and copied reservations retain their meaning. Used may exceed current capacity. Reservation kind/power is not changed by a delay.
- The existing Unity card-pick policy now lives once in `BattleChoicePolicy`; Unity's original public signatures and labels forward to it. `Battle.HandChoices` / Run wrapper gate the source to the actual hand before definition/price lookup and return detached metadata/candidate IDs. Unknown conditions, cost/FreeNext, growth/evolution, curses and ordered choices keep the approved behavior.
- Authenticated read-only `POST /dev/practice/runs/{runId}/battle-query` accepts only expectedVersion,uid,mode?,target?,picks?. It returns separate server journal version and node/turn/battle.v context, effective source text, computed slots, existing safe forecast and explicitly projected candidate metadata/zone. Hidden sources all answer fixed NOT_IN_HAND. It writes no account row/file, journal, version or receipt. Full run views keep the old slot fields and add computed fields.
- App README and both mandatory real-HTTP backend scenarios match the implemented contract. The original production OpenAPI and SQL are preserved.

## Independent review and execution

Official Opus5.5 Max authored the source; the first 30-minute job timed out after source work, before tests were complete. That TIMED_OUT result remains unchanged. Subsequent distinct test/fix jobs completed exit0 at xhigh, and the documentation job at High. `actual_cli_metadata.json` records actual model/per-turn effort evidence: only Opus, with subscription/account-match and the existing valid creditsOFF/autoreloadOFF UI attestation. These are not server-side billing enforcement. No paid fallback, other model or extra credit was used.

Codex independently reviewed the source and ran the checks. The separate native Sol6.1 Max review identified two test proof gaps; Opus fixed them, the second review approved them, and Root verified exact hashes. See `root_review.md`. Author runtime stays NOT_RUN; the results below are Root execution.

| Actual check | Result |
|---|---|
| New Core suite | PASS553/553, 43 actual commands and82 pure queries; full state/RNG/log/events, definitions and entire receipt map unchanged |
| Root full registered C# suites | PASS, including1000 legacy completions and273504 actual command attempts,6657 battles; `core_checks.txt` |
| Root real HTTP / file store | PASS952 assertions, mandatory battle-query and existing corruption/retry/race/privacy scenarios |
| Root real PostgreSQL16.15 / Linux arm64 | PASS859 assertions, mandatory pg-battle-query and existing real transactions/restart/race/rollback/privacy scenarios |
| Actual Unity6000.0.46f1 Editor Mono | Play165/165 and Edit3/3, failure0/skip0; Console0errors/0warnings, original settings restored byte for byte |
| Node / Python / proposals / docs / handoff | PASS333 /57 /14 /9 /13 respectively; adjacent final logs, no Unity/device equivalence inferred |
| Source/preservation | Exact reviewed/imported/tested hashes, original protected47 and protected runner unchanged; original423-entry manifest retains the same29 historical changed paths, never regenerated |
| Exact new remote CI | Check only after this unit's commit/push; preceding d0a3f32 CI success belongs to the preceding PG-CI unit |

Server assertion counts vary with real CSPRNG/legal check-bot routes (the author-worktree previews were file891/PG905); they are observed results, not equality targets. Neither seeds/stats nor the required scenarios were adjusted. PostgreSQL used a newly initialized private native cluster in the owned labelled verification container, not an old/user/production DB.

The first narrow Core rebuild used the host PATH's .NET9 and failed NETSDK1045 before tests. Explicitly using the existing isolated official .NET10 SDK produced a clean build (zero warnings/errors); the original failed log is preserved privately. The default Python and bundled runtime also lacked one or more declared PDF test modules; installing the existing tools/ai/requirements.txt only into a private validation venv produced handoff13/13. Earlier missing-module failures remain private. No product dependency, SDK, test or TLS requirement was weakened.

## Limits preserved

This is not full Unity network transport, an in-flight request store, process-termination/reconnect recovery, replayable public event history, production auth/collection/economy/API migration, a native build or Android/IL2CPP/device acceptance. Existing scene/Prefab assets and GUIDs are unchanged; the new files receive new Unity metas only. The old successful native mouse report is not reused as new input acceptance.

Core's strict module binding changes with this source build: old development envelopes/rows are refused with STORE_IMPLEMENTATION_MISMATCH and preserved, not reset or migrated. Tests use fresh stores/DBs. Live Int32-maximum turns and a player-turn reservation outside both displayed due columns remain NOT_COVERED, with static long bounds and the legal delay cases reported separately. Original rules0.7.0/designv0.8.1, numbers, inactive late candidates, input manifests and balance1320/32 versus5280/21 evidence remain intact.
