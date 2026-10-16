# P1-PRACTICE-TRANSPORT-FIX-TEST-BATCH-20261017 — tests author report (Claude Code, Opus 5.5, requested effort high)

**AUTHOR RUNTIME: NOT_RUN.** Nothing was compiled or executed by the author: no Bash/Unity in this call, and none of .NET, Unity Mono
PlayMode or Android was run. Root owns the build, the actual run, the real host/fault-proxy/process proofs and the review.

session_id: the same Root-invoked session (`cfcd60b9-d701-4abf-874b-b6915e50765e`, per Root's prompt); not independently visible to the
author.

## What these tests are

`client/MemoryForgeUnity/Assets/Tests/PlayMode/PracticeServerSessionPlayModeTests.cs` contains **protocol fixture tests** (namespace
`MemoryForge.Client.Tests`).

- **Dependencies:** BCL plus NUnit 3.5 and Newtonsoft only. No UnityEngine, TestTools, reflection or source hook. Every test is a
  synchronous `[Test]`/`[TestCase]`; async work is waited with a 20 s bound, and there are no sleeps.
- **Fixture server:** a test-owned HTTP/1.1 responder (`TcpListener` on 127.0.0.1, ephemeral port, one request per connection,
  `Connection: close`). It records every request (method, path, Authorization, Content-Type, exact body bytes).
- **Fixture replies:** fixed public JSON written from the documented protocol (PublicView.Account / Run / BattleQuery shapes and
  `{code, message}` errors). The responder can also:
  - hold a reply on a test-released gate;
  - drop the connection after reading the request;
  - declare a longer `Content-Length` than it sends (truncation);
  - omit the length;
  - use `text/plain`.
- **Records:** a fresh temp folder per test with real outbox files. Listener, connections and folder are cleaned up in TearDown; held
  replies are released in `finally`. A handler exception is recorded and fails the test in TearDown.
- **Scope:** these are **not** Core or server acceptance. No real Run, receipt store, restart, fault proxy or second process is involved.

## Coverage (grouped)

| Test | Requirement |
|---|---|
| `RefreshReadsTheOwnerFirstThenTheCurrentRunAsDetachedCompletePublicJson` | owner-first reads, one credential, complete public JSON kept (unknown member, `state.zones`, reward `cards`, token, `order`, "seed" inside log text), separate server version 3 vs battle.v 7, both memory versions, detached copies |
| `PreflightFailuresStageNothingAndPostNothing` | callback throw/unusable → CREDENTIAL_UNAVAILABLE with no request; 401 / WRONG_ACCOUNT handshake → NotSent; moved server context → REFRESH_REQUIRED + NeedsRefresh, and the stale screen does not even reach the handshake; no record or POST anywhere; a wrong-owner refresh keeps the last good snapshot, marked stale |
| `OneOperationKeepsTheCredentialItCapturedWhileTheCallbackRotates` | handshake → POST → re-read all carry the one captured credential; callback called once; the next operation uses the rotated one |
| `StagesBeforeTheHeldPostSendsTheCopiedOrderedArgsAndRefusesASecondCall` | record exists while the POST is held; POST bytes == record bytes; Guid requestId, member order, ordered picks unaffected by mutating the caller's args; InFlight; second command/refresh → SESSION_BUSY; receipt → Committed, record cleared, fresh view at 4, events presentable |
| `AnUnreadCommittedReplyIsRetriedByteForByteByANewSessionWithoutReplayOrRollback` | dropped reply → Pending NETWORK_FAILED; a new command is blocked (PENDING_UNRESOLVED); Dispose keeps the record; a new session retries with identical path and bytes; the old receipt (v4) confirms without rolling back from v6 and without replaying events; then NOTHING_PENDING |
| `AFailedReReadAfterACommitKeepsTheLastGoodSnapshotButBlocksNewMutations` | 200 confirms even when the re-read fails (RefreshCode); last good snapshot kept with NeedsRefresh; a new mutation is refused before any request; a later refresh clears it |
| `UncertainEndsKeepTheExactRequestPending` ×14 | drop, truncation, no length, text/plain, 503 commit-unknown / busy, 500, 401, 409 IDEMPOTENCY_CONFLICT, binding 409, unsafe error code, wrong-version 200, other-run 200, hidden-battle 200 → Pending with the exact record; a new command is blocked; a refresh is no acknowledgement |
| `CancellationTimeoutAndDisposeDuringAHeldPostKeepItPending` | caller cancel → CANCELED, a 1 s session → TIMEOUT, Dispose mid-POST → CANCELED; the record is unchanged throughout; three identical POSTs; a disposed session → SESSION_DISPOSED |
| `OnlyProvenVersionedRefusalsClearTheRequest` ×20 | clears: 409 STALE_VERSION / 422 on /commands and /memory-commit with a same-run handshake version ≥ expected. Retains: future version, other or no run, 400, 404, 507, IDEMPOTENCY_CONFLICT, STORE_FIXTURE_MISMATCH, 403, unknown 418, and every create refusal (400 / RUN_ACTIVE / 422 / 507) |
| `AFutureVersionRefusalStaysPendingEvenAfterALaterReadCatchesUp` | the later GET at v10 does not resolve the earlier refusal; only a new attempt whose own handshake shows ≥ 9 clears |
| `ADoneRunSaveSendsTheContextMemoryVersionAndACoreRefusalClearsIt` | memory-commit on a DONE run (no phase gate): exact members/order, server version 3, context memory version 2, ordered IDs; a 422 clears |
| `ConfirmNotCommittedClearsOnlyTheExactCurrentRequest` | outbox helper: another request → OUTBOX_NOT_CURRENT with the record kept; the exact current one is cleared; again → NOT_CURRENT. The existing 17 outbox tests are untouched |
| `SnapshotsAreCoherentMonotonicAndAdoptOnlyANewerRun` | null run (account read only), adoption, REFRESH_RACED (kept and stale), ROLLBACK_REFUSED for a lower version, a lower memory version, or another run without `finishedRuns`+1; adoption with +1 |
| `HiddenBattleContainersPrivateNamesAndMissingContextAreRefused` ×11 | **new DTO blocker**: `state.battle.zones`, `state.battle.cards` → PRIVATE_FIELD; a list pile / missing piles → INVALID; enemy `pattern`, nested `RNG`, `state.seed` → PRIVATE_FIELD; missing position / text `v` → INVALID; state version / other run → MISMATCH |
| `NonStrictReplyTextIsRefused` ×8 | duplicate member, trailing value, comment, BOM, NaN, escaped lone surrogate, array root, empty |
| `PublicLookalikesParseAndTheCompleteReplyIsKept` | positives: the full reply DeepEquals the fixture, events copy detached, account parse, wrong owner |
| `TheBattleQueryIsPureAndBoundToItsContextAndExactRequest` | exact body with no requestId, no record file, the snapshot instance unchanged; a default `blocked.next = PULL_DESTINATION_FULL` does not pre-block an explicit pull query (forecast error null); a reversed-picks echo → MISMATCH; STALE_VERSION → NeedsRefresh; after a refresh the old answer is not `IsCurrent` and the old context → CONTEXT_STALE with no POST |

## Source delta in this call (for review)

- **`PracticeServerSession.cs`:**
  - `KnownNotCommitted(pending, handshake, status, code)` now has the narrow rule (see author_report). `InputRefusals` was removed.
  - `NewMutationAsync` captures the credential and runs the owner handshake before the requestId and the stage, checks the summary against
    the context (REFRESH_REQUIRED + NeedsRefresh), re-checks cancellation, then stages and calls the new `PostAsync` with the same
    handshake and credential.
  - `AttemptAsync` (retry) = its own handshake + `PostAsync`.
  - `RefreshWithAsync` (and a refresh without a credential) marks NeedsRefresh on failure.
  - Added `NotSentWith(code, status)`.
  - No public API was added or removed.
- **`PracticePublicSnapshot.cs`:** `PracticeRunView.Parse` refuses `state.battle.zones`/`cards` (PRIVATE_FIELD) and requires integer
  `battle.piles.{draw,hand,discard,exhaust}`. Internal helpers added: `HiddenBattleMembers`, `PileCounts`, `Private()`.
- The outbox and the existing 17 tests were not touched.

## Risks to check when Root runs them

- **Unity Mono `HttpClient` differences:**
  - a dropped connection may surface as another exception type (still a transport failure, so NETWORK_FAILED is expected);
  - `text/plain` and no-length handling;
  - whether Mono retries a POST whose connection was dropped. The fixture uses `Connection: close`, and the test asserts exactly one POST.
- **The 1 s timeout case** relies on the per-exchange deadline; the bound is 20 s.
- **If a case fails, do not weaken it.** Report it as a source gap; nothing here was tuned to pass.
