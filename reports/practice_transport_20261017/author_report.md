# P1-PRACTICE-TRANSPORT-SOURCE-20261017 — author report (Claude Code, Opus 5.5, requested effort xhigh)

Requirements: design v0.8.1 §14.7 / §17.2 / §17.4, approved boundary step 4. Scope: the thin BCL transport plus the public read model
only. This is **not** the UI/Scene work, the NUnit tests or the actual .NET/Mono HTTP runs; Root does those next.

**AUTHOR RUNTIME: NOT_RUN.** Nothing was compiled, run or tested by the author (no Bash/MCP/Unity in this call). The existing 17 outbox
PlayMode tests were not run, and neither were the new-method tests, the HTTP checks against a real host, the Mono check or the Android
check. Root owns the build, the actual tests, the opposite-AI review and the integration.

session_id: not visible from inside the session; Root records it from the CLI metadata.

## Plan (bounded)

1. `PracticePublicSnapshot.cs`: a strict reply reader that reuses the outbox lexical pass and Newtonsoft (duplicates, depth, trailing
   content), with a private-name denylist. Typed views (account, run/receipt, snapshot, UI context, battle query) keep the complete detached
   public JSON and bind only the critical context.
2. `PracticeServerSession.cs`: one operation at a time over a private `HttpClient`, a credential captured once per operation for both the
   handshake and the request, monotonic snapshot apply, outbox-staged mutations, an exact retry and a pure battle query.
3. `PracticeCommandOutbox.cs`: add only `ConfirmNotCommitted`, sharing Confirm's lock, identity check and clear.
4. This report covers the API, the refusal whitelist (taken from the actual server codes), the limits and NOT_RUN.

## Files

| File | Change |
|---|---|
| `client/MemoryForgeUnity/Assets/Scripts/PracticeServerSession.cs` | new: transport/session, results, state, codes |
| `client/MemoryForgeUnity/Assets/Scripts/PracticePublicSnapshot.cs` | new: strict reader + public views |
| `client/MemoryForgeUnity/Assets/Scripts/PracticeCommandOutbox.cs` | `ConfirmNotCommitted` added; `Confirm` and it share the private `ClearCurrent` (same lock, `Current()`, `SameAs`, `File.Delete`, same `NOT_CURRENT`/`CLEAR_FAILED` codes; only the CLEAR_FAILED message text differs); `CheckLexemes` changed from private to **internal** (no behaviour change) so replies reuse the one lexical checker; header comment updated |

Nothing else changed: no server, Core, scene, prefab, test, data, package or dependency. The existing local expedition UI was not touched.
The record format, permissions, locking and body validation are unchanged.

**For review:** making `CheckLexemes` internal is a visibility-only change outside the "one new method". The task asked me to reuse the
lexical checker instead of repeating it, and this was the way to do that. If the reviewer rejects it, the other choice is a second
lexical pass in the snapshot file.

## API (namespace `MemoryForge.Client`, no UnityEngine)

- `new PracticeServerSession(endpoint, account, Func<string> credentialProvider, PracticeCommandOutbox outbox, TimeSpan? timeout = null)`.
  The endpoint and account must be exactly the outbox's (the outbox already validated `http://127.0.0.1:<port>` and the alias). Timeout per
  exchange is 1–120 s (default 30 s).
  - Handler: `AllowAutoRedirect=false`, `UseProxy=false`, `Proxy=null`, `UseCookies=false`, `UseDefaultCredentials=false`,
    `Credentials=null`, `PreAuthenticate=false`. `HttpClient.Timeout` is only a backstop (timeout + 1 s).
- `State` → immutable `PracticeSessionState { Snapshot, Pending, OutboxCode, NeedsRefresh, InFlight, CanMutate }`. The UI can read it at
  any time, including while mutations are blocked.
- `RefreshAsync()` → `PracticeRefreshResult { Code, HttpStatus, Snapshot }`.
- `CreateRunAsync(context, useMemory)`, `CommandAsync(context, kind, JObject? args)`, `CommitMemoryAsync(context, selectedChangeIds)`,
  `RetryPendingAsync()` → `PracticeMutationResult`:
  - Fields: `Status` (NotSent / Pending / Committed / NotCommitted), `Code`, `HttpStatus`, `Request`, `Receipt`, `Snapshot`,
    `OutboxCode`, `RefreshCode`, `PresentableEvents()`.
  - `CommitMemory` takes `expectedMemoryVersion` from `context.MemoryVersion` (the saved store version the save screen showed). This is
    deliberate: a separate parameter could disagree with the context.
- `BattleQueryAsync(context, uid, mode?, target?, picks?)` → `PracticeBattleQueryResult { Code, HttpStatus, Context, View }`.
  `IsCurrent(result)` tells whether the answer still belongs to the current snapshot.
- Views: `PracticeAccountView`, `PracticeRunSummary`, `PracticeRunView` (also used for create/command/commit replies), `PracticeSnapshot`,
  `PracticeUiContext`, `PracticeReceipt`, `PracticeBattleQueryView`. Each one keeps its complete reply and returns deep copies
  (`ToJObject()`, `StateJObject()`, `EventsJArray()`, `ResultJToken()`).
  - Typed context: owner, runId, **server journal version** (`ServerVersion`), node (`state.position`), turn, `battle.v`
    (`BattleVersion`), and two memory versions with different meanings: `AccountMemoryVersion` (the run reply's top-level) and
    `RunMemoryVersion` (`state.memoryVersion`, the version the run started from). `battle.v` is never compared as the server version.
  - The public parsers (`PracticeAccountView.Parse`, `PracticeRunView.Parse`, `PracticeReceipt.Parse`, `PracticeBattleQueryView.Parse`,
    `PracticePublicJson.ParseObject`) throw `PracticeProtocolException` with RESPONSE_INVALID, RESPONSE_PRIVATE_FIELD, RESPONSE_MISMATCH or
    WRONG_ACCOUNT. The next test call can use them directly.
  - Snapshot, context and session-state constructors are internal, so state cannot be injected.

## Boundaries as implemented

- **Strict replies:**
  - Size and framing: `application/json` (charset absent or utf-8). The declared `Content-Length` must be ≤ 4 MiB; the body is read into a
    buffer of exactly that size and then probed for extra bytes. A missing length or extra bytes → RESPONSE_INVALID; a short body →
    RESPONSE_TRUNCATED; a declared length over the cap → RESPONSE_TOO_LARGE without reading.
  - Parsing: strict UTF-8 (no BOM), the outbox's `CheckLexemes`, then Newtonsoft with duplicate members refused, `MaxDepth` 32 and no
    trailing content. The root must be an object, and non-finite numbers are refused.
  - Private names: a member *name* such as seed, rng, journal, receipt(s), binding, requestId, identity, digest/sha256s, credential*,
    password, secret, authorization, pattern, pi, passive, runFlags, turnFlags, offered, startMemory, memoryBaseline, envelope, revision,
    archive or seq* refuses the whole reply. Names are compared case-insensitively; string values are never scanned.
  - A missing or mistyped required context member is RESPONSE_INVALID. It never becomes 0 or an empty game.
- **Owner and credential:** every operation calls the credential callback once and validates the result (16–512 visible ASCII
  characters).
  - That credential is used for the `GET /dev/practice/account` handshake (`accountId` must equal the configured alias, otherwise
    WRONG_ACCOUNT) and then for every request of the same operation, including the re-read after a resolution.
  - The credential is never stored in a field, logged or returned. A callback exception becomes CREDENTIAL_UNAVAILABLE.
- **Refresh:** reads the account first, then `GET /runs/{runId}` only when the account names a run. A null run is a snapshot with no run.
  - The run must match the account summary's runId, version and phase, and the account's memory version. Otherwise → REFRESH_RACED and
    nothing is applied. A 404 on the run read is also REFRESH_RACED.
  - Monotonic apply (ROLLBACK_REFUSED, the shown snapshot is kept and NeedsRefresh is set): the same run never moves to an older server
    version, and the memory version and `finishedRuns` never go back. A different or absent current run is adopted only from a fresh
    authenticated account read *and* only if `finishedRuns` increased, which is how the server archives a run.
  - A current-state GET never resolves the outbox.
- **New mutation** (corrected by P1-PRACTICE-TRANSPORT-FIX-TEST-BATCH):
  - Checks, in this order: a snapshot exists, no refresh is due, the context equals the current snapshot context, a run exists (run
    routes), `outbox.Load()` returns null, and the call is not cancelled or disposed.
  - Then the credential is captured once and the owner handshake runs. A credential or handshake failure (CREDENTIAL_UNAVAILABLE, 401,
    WRONG_ACCOUNT, transport) is NotSent: no requestId, no record, no POST.
  - The handshake's account summary must still show the acting context (run ID, server version, memory version). If it does not, the
    result is NotSent REFRESH_REQUIRED with NeedsRefresh set and nothing staged.
  - Cancellation and Dispose are checked again. Only then come the Guid requestId, the body built **once** (args deep-cloned in order;
    picks/IDs copied in order), `outbox.Stage` immediately before the POST, and the POST of `pending.BodyUtf8()` with the **same**
    handshake and credential. There is no second callback and no second GET.
  - Only the existing command kinds are accepted (Contracts.CommandArgs minus memoryCommit). Argument content is the server's to judge.
- **Retry:** sends exactly what `outbox.Load()` returns (route, body, requestId and version are never rebuilt), whatever the UI shows.
  It runs its own credential capture and handshake first; a preflight failure keeps the request pending.
- **Failed refresh** (any reason, including after a resolution): the last good snapshot stays readable but NeedsRefresh is set, so a
  stale screen cannot start a mutation.
- **Resolution** (only after the same credential's handshake immediately before this POST, and that attempt's own validated reply):
  - 200 → `PracticeReceipt.Parse` checks the route's runId, a version of exactly `expectedVersion + 1` (0 for a new run) and a result
    object for memory-commit. Then `Confirm`, NeedsRefresh set, and a re-read. If Confirm fails, the result is Committed with OutboxCode
    set and the request stays pending (the next retry gets the stored receipt). If the re-read fails, the result is Committed with
    RefreshCode set and NeedsRefresh stays. The receipt's own state is never applied.
  - Known not-committed refusal (whitelist below) → `ConfirmNotCommitted`, then a re-read. If the clear fails, the result is Pending
    with OutboxCode set.
  - Anything else (the cases listed in the next section) → Pending: the request stays pending and new mutations stay blocked.
- **Events:** `PresentableEvents()` is non-null only when the snapshot shown before the call was the same run at exactly
  `receipt.version − 1` and the re-read snapshot is that run at exactly `receipt.version`.
  - An old or replayed receipt therefore offers nothing, a recovery never replays events that are already represented, and a new run has
    no events.
  - Presenting, skipping or speeding events sends nothing.
- **Battle query:** pure. No requestId, no outbox, allowed while a mutation is pending.
  - Needs a fresh context and a battle in the snapshot.
  - Sends `{expectedVersion: server version, uid, mode (default now), target?, picks: [ordered]}`, at most 64 KiB, after the handshake.
  - The reply must echo the context (runId, server version, node, turn, battleVersion) and the exact request (uid, mode, target, ordered
    picks); otherwise RESPONSE_MISMATCH.
  - 409 STALE_VERSION or 404 RUN_NOT_FOUND set NeedsRefresh.
  - The answer never touches the snapshot. `blocked[mode]` stays documented as the default request only.
  - The client applies no price, capacity or legality rule.
- **Lifecycle:**
  - One operation at a time: a second call gets SESSION_BUSY immediately, with no queue.
  - `Dispose` cancels the in-flight exchange (it ends as CANCELED) and disposes the client. It never touches the outbox.
  - There is no automatic retry, re-ID or background work.
  - Cancellation and timeout are separate codes (CANCELED / TIMEOUT), and neither clears a pending request.

## Not-committed whitelist (narrowed by P1-PRACTICE-TRANSPORT-REFUSAL-FIX; supersedes the first version)

A pending request is cleared by a refusal **only** when all of these hold:

- The route is versioned: `/commands` or `/memory-commit`, never a new run.
- The reply is 409 `STALE_VERSION` or 422 (a Core rule refusal or `COMMAND_REJECTED`).
- The same credential's handshake immediately before **this** POST named the request's run as the current run, at a server journal version
  `>=` the request's `expectedVersion`.

Why this is safe: `Program.Execute` checks `Current(runId)`, then the version, then Core, and all of that happens after
`PracticeRunStore.Mutate`'s receipt lookup and before any write. Journal versions only grow. `STALE_VERSION` only says the versions
differ, so a future-version request (expected above the current version) could still execute later, and the handshake condition excludes
it. A later GET never legitimizes an earlier refusal.

Everything else keeps the request pending:

- every new-run refusal (409 RUN_ACTIVE and 422 included: a delayed earlier copy can still execute once the active run is DONE);
- every 400 (parsed before the receipt lookup);
- every 404;
- every 507 (it can come from loading before the lookup);
- 401 and 403;
- 409 IDEMPOTENCY_CONFLICT and the STORE_* binding refusals;
- every 500 and every 503;
- unknown statuses, unparseable bodies and unsafe codes.

The old `InputRefusals` table and the earlier "400 depends on the server build" limit are removed, since 400 no longer clears anything.
The phase is not checked, because a memory-commit runs on a DONE run.

## Known limits / follow-ups (not production policy decisions)

- **Some requests stay pending for good.** IDEMPOTENCY_CONFLICT, STORE_* binding refusals, a corrupt outbox record or a malformed 200 keep
  the request pending and block new mutations. There is deliberately no Drop/Reset/re-ID; recovery would need a separate approved flow.
- **A wiped store blocks the session.** If the dev store is wiped while the client runs, the next refresh is refused (ROLLBACK_REFUSED)
  until a new session object is created (session state is in memory only).
- **Server reply framing is assumed.** The client requires `Content-Length` (the host always sets it) and refuses chunked replies.
- **The private-name check is name-based**, plus one location rule: `state.battle.zones` / `state.battle.cards` (the private ordered piles
  and the card map) are refused at that location only, and `battle.piles.draw/hand/discard/exhaust` must be integer counts. `state.zones`,
  reward/shop `cards` and the pile counts stay public, and unknown public members are kept. It is a client-side tripwire; the server's
  `PublicView` allowlist remains the authority.
- **Each operation adds one GET.** The account handshake runs once per operation.
- **Results complete on the thread pool** (`ConfigureAwait(false)`). Unity callers await from the main thread, so their own
  continuation resumes there; the next UI call should verify this.
- **Android is untested.** The `HttpClientHandler` property support on Unity Mono/IL2CPP Android is unverified. On a phone,
  `127.0.0.1` is the phone itself; an approved non-loopback Development endpoint would be a separate decision, and this code adds no
  endpoint switch.
- **Monotonic apply assumes server behaviour.** It assumes the memory version and `finishedRuns` never decrease, which holds for the
  current server; a deliberate store reset breaks that assumption, as above.

## Next (Root)

- **NUnit tests:** written in `PracticeServerSessionPlayModeTests.cs` (see `tests_author_report.md`); NOT_RUN by the author.
- **Actual Host plus client HTTP on .NET and Mono:**
  - wrong owner, no row, and a pure query;
  - a successful but unread reply, then a server restart and a retry with the same ID and bytes (one consumption only);
  - a known rejection that clears the request while 503, 401 and identity conflicts keep it pending;
  - a stale or different-run receipt that does not roll anything back;
  - a corrupt record that is preserved;
  - cancel and focus semantics kept separate.
- **The existing 17 outbox tests**, unchanged.
- **Unity UI wiring.**
