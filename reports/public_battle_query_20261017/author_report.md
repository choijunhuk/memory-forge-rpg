# P1-PUBLIC-BATTLE-QUERY-20261017 — author report (Claude, Opus 5.5, MAX requested)

Base `47502b797ebc2de262704968f85b9ce30ed7665f`, worktree `claude-public-battle`, branch `codex/public-battle-query-20261017`.
Author runtime: **NOT_RUN** (no Bash/dotnet/Unity in this job). Root owns build, tests, Editor import and integration.

## 1. Bounded plan (written before the edits)

Requirement anchors: §17.4 (public DTO: computed slots, no hidden state), §14.7 (reservation board must not shrink or hide slots),
§14.3 (pick sheet = only the reached Core effect's candidates; forecast of the exact request).

| # | Unit | Files | Acceptance |
|---|---|---|---|
| 1 | Core slot query | `Battle.PresentationQueries.cs`, `Run.BattleQueries.cs` | `Battle.ReservationSlots()` detached: per column base/temp/max (Core-held), `Cap` = `SlotCapacity` (→ `ReservationBook.CalculateCapacity`), `Used` = `SlotCapacity − FreeSlots` (exact `Due == (long)Turn+1/+2`), `Sealed` = `SealedUntil >= Turn` (turn 0 included), every reservation copied with its own Kind. Run wrapper through `LiveBattle()`. |
| 2 | Shared pick policy | `BattleChoicePolicy.cs`, Unity `MemoryForgeExpeditionClient.cs` | `Choices/ChoiceRequests/ChoiceRules/PickCandidates/Narrowed/PickBlocked/EffectEligible` moved verbatim into BCL-only Core; the sheet progression of `PicksHold` moved with them. Unity keeps every public signature and `PickGrowthCap` as forwarding wrappers; no second implementation. |
| 3 | Core hand-source query | `Battle.PresentationQueries.cs`, `Run.BattleQueries.cs` | `Battle.HandChoices(uid, mode, picks)`: `NOT_IN_HAND` first (before `EffectiveCard`/price), then effective definition + Core `CostOf`/`CostOfWithoutNextFree`; returns requests metadata, accepted pick prefix, current request candidates (uids) and `PICK_GROWTH_CAP`. |
| 4 | Server | `Contracts.cs`, `PublicView.cs`, `Program.cs` | Computed `cap/used/sealed/due` added beside the unchanged raw slot members. `POST /dev/practice/runs/{runId}/battle-query` `{expectedVersion, uid, mode?, target?, picks?}` under auth/ownership/`Store.Read`, stale → 409, no battle → 422, hand gate → 422 `NOT_IN_HAND`, digest guard; reply = context + source + slots + Core forecast + pick sheet, explicit allowlist. |
| 5 | Checks | `BattlePresentationQueryChecks.cs` + CoreChecks `Program.cs`; Unity `BattlePresentationQueryPlayModeTests.cs`; `BattleQueryChecks.cs` + ServerChecks `Program.cs` | Literal slot values reached by real commands (turn 0 seal, temp, collateral and enemy seal expiry, relic base/max, delayRes kind preservation and T+3), policy purity/FreeNext 0/1/2/curse/growth/evolution/unknown/order, hidden-uid gate, manual pull; new mandatory file-suite scenario `battle-query` over real HTTP with a check-side Core oracle and byte/SHA-256 envelope purity. |

Out of scope (unchanged): CI/scripts/SQL/Npgsql/`PracticeRunStore`, auth/economy/migration, Unity scenes/prefabs/metas, transport/process
recovery, numbers and content.

## 2. What was actually written

Job history (same session `bc4c8bd8-684e-4e74-be0f-e49277bc4530`): the first job `P1-PUBLIC-BATTLE-QUERY-20261017` (MAX) **timed out at
30 minutes** (not a quota stop) after the source edits below and before any test file was filled. The follow-up job
`P1-PUBLIC-QUERY-SERVER-TESTS-20261017` (XHIGH, edit rights: the two ServerChecks files, `PublicView.cs`, this report) added the HTTP checks
and the candidate zone. Nothing was executed by the author in either job.

| File | State | Content |
|---|---|---|
| `core/MemoryForge.Core/BattleChoicePolicy.cs` | written (job 1) | `Choices/ChoiceRequests/ChoiceRules/PickCandidates/Narrowed/PickBlocked/EffectEligible` moved verbatim from the Unity view; `PickGrowthCap`, `PickInvalid`; `WalkPicks` (the sheet progression formerly inside the view's `PicksHold`) and `PicksHold`. |
| `core/MemoryForge.Core/Battle.PresentationQueries.cs` | written (job 1) | `BattleSlotColumn/BattleSlotView`, `Battle.ReservationSlots()` (Cap = `SlotCapacity`, Used = `SlotCapacity − FreeSlots`, Sealed = `SealedUntil >= Turn`, Due as long, reservations copied with their own Kind); `BattleChoiceRequest/BattleChoiceView`, `Battle.HandChoices(uid, mode, picks)` (NOT_IN_HAND first, INVALID_MODE, effective definition, Core `CostOf`/`CostOfWithoutNextFree` price, accepted pick prefix, PICK_INVALID, current request Blocked/Candidates). |
| `core/MemoryForge.Core/Run.BattleQueries.cs` | edited (job 1) | `BattleReservationSlots()`, `BattleHandChoices(...)` through `LiveBattle()` (NO_BATTLE outside battle). |
| `client/.../MemoryForgeExpeditionClient.cs` | edited (job 1) | `ChoiceRequests/ChoiceRules/PickCandidates/PickBlocked/EffectEligible` and `PickGrowthCap` are thin forwarders to `BattleChoicePolicy`; private `PicksHold` forwards to `BattleChoicePolicy.PicksHold` with the view's `cardDefs`/`Price`. Public signatures unchanged. |
| `server/MemoryForge.Server/Contracts.cs` | edited (job 1) | `BattleQueryRequest`, `ParseBattleQuery` (allowlist `expectedVersion, uid, mode?, target?, picks?`; no requestId; mode `now|next|after`); `CheckArgs` body moved unchanged into a shared `Normalize`. |
| `server/MemoryForge.Server/Program.cs` | edited (job 1) | `POST /dev/practice/runs/{runId}/battle-query` under the existing auth/`RunId`/`Store.Read`: owner/run → `STALE_VERSION` (journal version) → `NO_BATTLE` → `NOT_IN_HAND` gate → `Run.BattleForecast` → projection → `RequireUnchanged`. |
| `server/MemoryForge.Server/PublicView.cs` | edited (jobs 1+2) | Run view slots keep raw `base/temp/max/sealedUntil` and add Core `cap/used/sealed/due`; `BattleQuery` reply (`runId, version, phase, context{node, turn, battleVersion, battlePhase}, source{uid, id, name, text (BattleText), mode, target, picks}, slots, forecast, choices`); job 2 added `zone` (the current request's `From`) to each candidate. |
| `server/MemoryForge.ServerChecks/BattleQueryChecks.cs` | written (job 2) | Mandatory file-suite scenario `battle-query` (below). |
| `server/MemoryForge.ServerChecks/Program.cs` | edited (job 2) | `"battle-query"` added to `RequiredScenarios`; `RunAll` runs it on the main host after write-failure, before the host is killed; the existing check-side oracle setup moved unchanged from `IndependentReplay` into `CheckSideRun(run)`, which both use. |
| `core/MemoryForge.CoreChecks/BattlePresentationQueryChecks.cs`, CoreChecks `Program.cs`, Unity `BattlePresentationQueryPlayModeTests.cs` | **not written by this author** | Still empty / unwired at the job-1 timeout; per Root, a separate Opus job (C1 worktree) owns the Core/Mono tests. |

### `battle-query` scenario (real child host, real HTTP, check-side Core oracle)

- On the map (alpha's fresh departure, version 0): anonymous and unknown credentials 401; another account's run, an account without a run,
  an unknown run and a malformed run ID 404 with gamma's file still absent; 422 `NO_BATTLE`; 409 `STALE_VERSION`; 21 strict-body refusals
  (malformed/non-object JSON, requestId/seed/state/v/kind members, duplicate member, missing/fractional/negative/string version, missing/
  numeric/empty uid, unknown mode, boolean target, nested/object/null picks, 65 picks) 400 with their codes; 415; GET 405. Envelope bytes
  unchanged.
- After a real `enter` (version 1), one query round: the oracle (`CheckSideRun` of the stored inputs + journal) must equal the served battle;
  run-view slots and every reply's slots are checked against `core.BattleReservationSlots()` and the exact-due count; every hand card in
  `now/next/after` with its needed target, and each pick card with an explicit first candidate and with its own uid (literal
  `PICK_INVALID`, accepted 0), are compared member by member with `core.BattleForecast`/`core.BattleHandChoices`, `BattleText.Card`,
  context (journal version, node, turn, battle.v) and an exact top-level member allowlist; candidates must be Core's, in order, public
  hand/discard/reservation cards carrying their request's zone. A repeated query returns the same bytes. Every draw/discard/exhaust/
  reservation/enemy uid (with target and picks) returns 422 `NOT_IN_HAND` byte-identical to the reply for `c9999`. Stale (±1) 409, another
  account 404, no account file, stored envelope byte-identical with the same full SHA-256, run read byte-identical.
- Then up to two legitimate plays chosen from the oracle's own forecast (a reservation, then an immediate play), each committing exactly
  the next version, and a second full round at the new version (reservation and discard/exhaust uids now probed); if the play ended the
  battle, the query must answer 422 `NO_BATTLE`.
- All replies stay in the final complete-capture privacy scan; no PostgreSQL list or privacy rule was changed.

## 3. Verification status (truthful)

- Author runtime: **NOT_RUN** for everything (no Bash/dotnet/Unity in either job): Core build, CoreChecks, Unity Mono/Play, server build,
  ServerChecks file suite and PostgreSQL mode.
- Root-reported only (not re-observed by this author): a draft of the current host compiled with 0 warnings/0 errors, and Root's own
  independent HTTP check (44 checks: authentication, ownership, stale, bad fields, hand-source guard, repeat, exact envelope purity) PASSED;
  native Sol Max Core review found no blocker. The `battle-query` scenario written in job 2 has **not** been compiled or run.
- Full acceptance: **NOT complete**. Missing: compile + run of the new ServerChecks scenario (file mode), the PostgreSQL suite with a fresh
  dedicated test database, the Core/Mono tests owned by the other job, Unity import (new Core `.meta` files) and Editor Play runs.

## 4. Known limits

- `HandChoices.Error == null` only says the pick sheet was computed; whether the play is legal (phase, AP, target, slot, pull destination)
  is still Core's `Check`/`Forecast`, and the reply's `forecast.error` carries it. A hand card's `blocked[mode]` stays the default
  omitted-picks check and is never applied to the query.
- ~~In a run's first battle the server journal version and battle.v coincide~~ and ~~PostgreSQL mode not wired~~: both closed in job 3
  (§5), source only.

## 5. Job 3 delta — `P1-PUBLIC-QUERY-BACKEND-BOUNDARY-20261017` (XHIGH, same session)

Edit rights: `BattleQueryChecks.cs`, `PostgresChecks.cs`, this report. No Core/UI/server-host change. Author runtime **NOT_RUN**.
Root-reported only: the job-2 code built with 0 errors and the full file mode passed 664 assertions including the new queries.

- **PostgreSQL:** `"pg-battle-query"` was added to `PostgresScenarios`. `RunPostgres` runs the same `BattleQueryScenario` after
  `PostgresOwnership` and before `PostgresRowLock`. It uses alpha's credential and current run, `Stored(alpha)` bytea as `stored()`,
  and `Oracle.Rows(gamma) == 0` as the no-row oracle. Later scenarios checked: `PostgresRowLock` uses beta; `PostgresPartialMutation`
  uses gamma; `PostgresCorruptRows` tampers `alphaDone`, sends refused commands that fail before any run lookup, and restores alpha's
  latest bytes. None of them assumes alpha is still on the map at version 0, so nothing needed adapting.
- **Journal version ≠ battle.v:**
  - The suggested "starting common card" assign route is not available in v2 by Core's own rules. `Run.Create` builds a v2 deck from
    `MemoryBefore`, and `MemoryV2.Baseline` adds the starting common card with a saved `base:<cid>:<i>` key. `Run.Assign` refuses any
    `MemoryBefore` key with `REASSIGNMENT_REQUIRES_SEPARATE_FLOW`, so only a common card gained during the run is assignable.
  - The scenario now checks this for real, without skipping it. `AssignProbe` tries `Run.Assign` for every common deck key on its own
    fresh check-side oracle (never on the host). It asserts every key is either accepted or refused with exactly that code. The host must
    refuse one saved key with the same 422 and write nothing.
  - If a key is assignable, one HTTP `assign` is sent inside the first battle. `AssignInBattle` asserts the journal version +1 with
    battle.v and the whole served battle view unchanged.
  - Otherwise the existing greedy bot `Choose` finishes the battle through legal commands (`LaterBattle`). It takes the first reward card
    or proceeds, enters the next battle node, then assigns a common card gained on the way if one exists.
  - A lost battle is saved unchanged and the account departs again. Each new seed is added to the privacy scan, and the departures are
    bounded like the expedition's existing bot limit, failing explicitly.
  - A full query round then runs there. It asserts battle.v < journal version and checks every reply member against its own source. A
    final `Check(distinct)` makes the separation mandatory.
- **Oracle for assign entries:** the check-side `CoreExecute` (in `Program.cs`, not editable here) has no `assign`. `QueryOracle` reuses
  `CheckSideRun` with an empty journal, then dispatches the stored entries itself: `assign` through `Run.Assign`, everything else
  through `CoreExecute`.
- **Unchanged:** all earlier refusal, strictness, purity, gate and privacy checks are kept; the map refusals still run at version 0 before
  any assignment.
- **Runtime risk:** the later-battle route adds dozens of real commands, each replaying a growing journal, to both suites. It depends on the
  bot winning within four departures, like the existing expedition scenario.
- The new Core build changes the strict `coreModule` binding: existing dev envelopes/rows are refused (`STORE_IMPLEMENTATION_MISMATCH`), not
  reset or migrated; use a fresh store / dedicated test database.
- (Known limits carried from §4, still valid:) the new Core build changes the strict `coreModule` binding as above; candidates expose public
  instance members (uid, id, zone, plus, mods, temp, growth) of hand/discard/reservation cards only, never draw order or the card map,
  which is new discard-zone exposure relative to the counts-only piles, limited to cards a pick offers.
- ~~`server/README.md` still describes slots as raw-only and does not list the new route~~: closed in job 4 (§6). The original production
  `server/openapi.yaml` is intentionally unchanged.

## 6. Job 4 delta — `P1-PUBLIC-QUERY-APP-DOCS-20261017` (High, same session)

Edit rights: `server/MemoryForge.Server/README.md` and this report only; no code, `server/openapi.yaml`, schema or SQL change.
`MemoryForge.Server/README.md` now states that only Core's pure presentation queries were added (data, numbers, SQL and the production
OpenAPI unchanged); documents the backwards-compatible slot board (raw `base/temp/max/sealedUntil` + Core `cap/used/sealed/due`), the
scoped pick candidates with `zone`, and the strict read-only `battle-query` route (body, owner/stale/`NO_BATTLE`/fixed `NOT_IN_HAND`
order, server journal version vs `battle.v` context, forecast/choices members, `blocked[mode]` as the default omitted-picks check only,
`choices.error: null` not meaning playable, no write/requestId/draw order/RNG/transport/recovery); the mandatory `battle-query` /
`pg-battle-query` scenarios; and that the rebuilt Core binding refuses old dev envelopes/rows (`STORE_IMPLEMENTATION_MISMATCH`) without
reset or migration. No sample credentials were added.

Verification: author **NOT_RUN** (documentation only; no tests executed in any of the four jobs). Root reports its own independent
acceptance of the final source separately (Core, legacy replay, file and PostgreSQL server suites, Unity Play/Edit); this author did not
run or observe those runs, and the dated history above (job-1 timeout, earlier missing coverage) stays as recorded.
