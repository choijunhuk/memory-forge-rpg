// P1-PRACTICE-TRANSPORT §14.7/§17.2/§17.4 (approved boundary step 4): the Development practice client's thin HTTP transport to the local
// practice host (server/MemoryForge.Server, the /dev/practice routes). BCL System.Net.Http and Newtonsoft only, no UnityEngine, so the same
// code runs on .NET and Unity Mono. It is not a UI, scheduler, game engine or authority: the server judges every command, and nothing here
// hydrates a Run, seeds anything or computes a price, capacity or legality.
// - Configuration: exactly http://127.0.0.1:<port>, the fixture account alias, a credential callback (the current in-memory credential;
//   never kept in a field, serialized, logged or put into a result) and the PracticeCommandOutbox bound to that endpoint and account. One
//   private HttpClientHandler: no redirect, proxy, cookies, default credentials or pre-authentication; one bounded timeout per exchange.
// - One operation at a time: a call while another runs is SESSION_BUSY at once (no queue, so two commands never race). Each operation
//   captures the credential once and uses it for its account handshake (GET /dev/practice/account; accountId must be the configured
//   owner) and every request of that operation, so a rotated credential never mixes accounts inside one operation.
// - Replies: application/json with a declared Content-Length of at most PracticePublicJson.MaxResponseBytes, read into one buffer of
//   exactly that size (a longer or shorter body is refused, never buffered without bound), then PracticePublicJson. Failures are fixed
//   codes; no body, header, credential or exception text leaves this file and nothing is logged.
// - Mutations (new run, command, memory-commit; existing routes and kinds only): the UI context must be the current snapshot's, nothing
//   may be pending, no refresh may be due and the call must not be cancelled. Then the credential is captured and the owner handshake
//   must succeed and still show exactly that context (else REFRESH_REQUIRED); only then is the exact body built ONCE (a fresh Guid
//   requestId, the snapshot's server version) and staged in the outbox, immediately before its POST with that same handshake and
//   credential. A preflight failure stages and sends nothing. Every uncertain end after staging (timeout, disconnect, cancellation,
//   Dispose, a malformed/oversized/truncated reply, 401, 500, any 503, 409 IDEMPOTENCY_CONFLICT, a store binding refusal, any
//   refusal not proven below) leaves it pending and new mutations blocked; RetryPendingAsync re-sends exactly the stored
//   route/body/requestId (after its own handshake), never a rebuilt one. There is no automatic retry, re-ID or background work.
// - Resolution, only from THAT attempt's validated reply after the same credential's immediately preceding handshake: a 200 passing the
//   receipt checks of that exact request confirms it (outbox Confirm); only the narrowly proven refusals of KnownNotCommitted clear it
//   (outbox ConfirmNotCommitted). Then the account and current run are re-read and applied monotonically; a receipt's own state is never
//   applied, and a 200 still counts as executed when that re-read fails (NeedsRefresh). A later read never legitimizes a refusal.
// - Snapshots: only a fresh authenticated account read (plus its current run read at the same server and memory version) becomes the
//   current snapshot, and never one that moves the same run to an older server version or the account to an older memory version or
//   finished-run count (ROLLBACK_REFUSED). A failed re-read keeps the last good snapshot but marks it NeedsRefresh, so a stale screen
//   cannot start a mutation. A current-state GET never resolves a pending request. A receipt's events are offered for
//   presentation only when the UI showed exactly the state before that command and the re-read state is exactly the one after it;
//   presenting, skipping or speeding them sends nothing.
// - The battle query and the memory preview are pure: no requestId and no outbox, bound to the asking context and its own request, never
//   applied as state. The preview route takes no version, so its owner handshake must still show exactly the asking context (memory
//   version included) and its reply the context's run and server version; anything else marks the shown state due a refresh.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Codes = MemoryForge.Client.PracticeSessionCodes;

namespace MemoryForge.Client
{
    /// <summary>The session's own fixed codes. A server refusal keeps the server's code, an outbox failure the outbox's (OUTBOX_*), a
    /// refused reply PracticeProtocolException's (RESPONSE_INVALID, RESPONSE_PRIVATE_FIELD, RESPONSE_MISMATCH, WRONG_ACCOUNT).</summary>
    public static class PracticeSessionCodes
    {
        public const string Ok = "OK";
        /// <summary>Another operation of this session is running; nothing was done.</summary>
        public const string Busy = "SESSION_BUSY";
        public const string Disposed = "SESSION_DISPOSED";
        /// <summary>The credential callback failed or gave no usable credential (16..512 visible ASCII characters); nothing was sent.</summary>
        public const string CredentialUnavailable = "CREDENTIAL_UNAVAILABLE";
        /// <summary>No snapshot was applied yet; refresh first.</summary>
        public const string NoSnapshot = "NO_SNAPSHOT";
        /// <summary>The shown snapshot may be behind the server (also: a mutation's handshake no longer shows the acting context; nothing
        /// was staged); refresh before acting.</summary>
        public const string RefreshRequired = "REFRESH_REQUIRED";
        /// <summary>The UI context is not the current snapshot's.</summary>
        public const string ContextStale = "CONTEXT_STALE";
        public const string NoRun = "NO_RUN";
        public const string NoBattle = "CONTEXT_NO_BATTLE";
        /// <summary>The context's run shows no memory save (its state.pending.type is not save); no preview was asked.</summary>
        public const string NoSave = "CONTEXT_NO_SAVE";
        /// <summary>A mutation is still pending; retry it exactly (RetryPendingAsync) before anything new.</summary>
        public const string PendingUnresolved = "PENDING_UNRESOLVED";
        public const string NothingPending = "NOTHING_PENDING";
        /// <summary>Not an existing command kind, or a null/unencodable argument; nothing was staged.</summary>
        public const string InvalidCommand = "INVALID_COMMAND";
        public const string Timeout = "TIMEOUT";
        public const string Canceled = "CANCELED";
        public const string NetworkFailed = "NETWORK_FAILED";
        public const string ResponseTooLarge = "RESPONSE_TOO_LARGE";
        public const string ResponseTruncated = "RESPONSE_TRUNCATED";
        /// <summary>The account and its run changed between the two reads of one refresh; nothing was applied. Refresh again.</summary>
        public const string RefreshRaced = "REFRESH_RACED";
        /// <summary>The server's state is older than the shown one (same run at a lower version, older memory/finished runs); kept the shown one.</summary>
        public const string RollbackRefused = "ROLLBACK_REFUSED";
    }

    public enum PracticeMutationStatus
    {
        /// <summary>This call staged nothing and POSTed nothing (a local, credential, owner-handshake or outbox refusal). State.Pending shows
        /// any request still pending.</summary>
        NotSent,
        /// <summary>The request is staged and unresolved (sent or not; the outcome unknown or not acceptable as a resolution). New mutations
        /// stay blocked until RetryPendingAsync resolves exactly this request.</summary>
        Pending,
        /// <summary>A validated 200 receipt of exactly this request: it executed (once). OutboxCode set = the record could not be cleared yet.</summary>
        Committed,
        /// <summary>A validated server refusal decided before anything could commit; the pending request was cleared.</summary>
        NotCommitted
    }

    public sealed class PracticeMutationResult
    {
        readonly JArray? freshEvents;
        internal PracticeMutationResult(PracticeMutationStatus status, string code, int? httpStatus, PendingPracticeCommand? request, PracticeReceipt? receipt,
            PracticeSnapshot? snapshot, string? outboxCode, string? refreshCode, JArray? freshEvents)
        {
            Status = status; Code = code; HttpStatus = httpStatus; Request = request; Receipt = receipt; Snapshot = snapshot;
            OutboxCode = outboxCode; RefreshCode = refreshCode; this.freshEvents = freshEvents;
        }

        public PracticeMutationStatus Status { get; }
        /// <summary>OK (Committed), the server's refusal code, or the session/outbox/protocol code of why nothing was sent or resolved.</summary>
        public string Code { get; }
        /// <summary>The validated status of this attempt's reply (the handshake's when it failed); null without one.</summary>
        public int? HttpStatus { get; }
        /// <summary>The exact request this call staged, retried or found pending; null when there was none.</summary>
        public PendingPracticeCommand? Request { get; }
        public PracticeReceipt? Receipt { get; }
        /// <summary>The snapshot applied after the resolution (re-read from the server); null = not applied, State.NeedsRefresh says why.</summary>
        public PracticeSnapshot? Snapshot { get; }
        /// <summary>Clearing the resolved request failed (still pending until a retry resolves it again); null otherwise.</summary>
        public string? OutboxCode { get; }
        /// <summary>Why the re-read after a resolution was not applied; null when it was (or none was made).</summary>
        public string? RefreshCode { get; }
        /// <summary>The receipt's public events, only when they describe exactly the transition from the shown state to the applied one.
        /// A copy per call; consuming, skipping or speeding them sends nothing.</summary>
        public JArray? PresentableEvents() => freshEvents == null ? null : (JArray)freshEvents.DeepClone();
    }

    public sealed class PracticeRefreshResult
    {
        internal PracticeRefreshResult(string code, int? httpStatus, PracticeSnapshot? snapshot) { Code = code; HttpStatus = httpStatus; Snapshot = snapshot; }
        public string Code { get; }
        public int? HttpStatus { get; }
        /// <summary>The applied snapshot; null when nothing was applied (the shown one stays).</summary>
        public PracticeSnapshot? Snapshot { get; }
    }

    public sealed class PracticeBattleQueryResult
    {
        internal PracticeBattleQueryResult(string code, int? httpStatus, PracticeUiContext context, PracticeBattleQueryView? view)
        {
            Code = code; HttpStatus = httpStatus; Context = context; View = view;
        }
        public string Code { get; }
        public int? HttpStatus { get; }
        /// <summary>The context the query was asked in; use the answer only while it is still the current one (PracticeServerSession.IsCurrent).</summary>
        public PracticeUiContext Context { get; }
        public PracticeBattleQueryView? View { get; }
    }

    public sealed class PracticeMemoryPreviewResult
    {
        internal PracticeMemoryPreviewResult(string code, int? httpStatus, PracticeUiContext context, PracticeMemoryPreviewView? view)
        {
            Code = code; HttpStatus = httpStatus; Context = context; View = view;
        }
        /// <summary>OK with a View; otherwise the server's refusal code (INVALID_SELECTION, WRONG_PHASE, ...) or the session/protocol code.
        /// A failure has no View and decides nothing about a save.</summary>
        public string Code { get; }
        public int? HttpStatus { get; }
        /// <summary>The context the preview was asked in; use the answer only while it is still the current one (PracticeServerSession.IsCurrent)
        /// and only for the selection it carries (View.SelectedChangeIds).</summary>
        public PracticeUiContext Context { get; }
        public PracticeMemoryPreviewView? View { get; }
    }

    /// <summary>What a UI may read at any time, also while mutations are blocked: the latest applied full public state and what blocks a
    /// new mutation. Immutable; PracticeServerSession.State returns the current one.</summary>
    public sealed class PracticeSessionState
    {
        internal PracticeSessionState(PracticeSnapshot? snapshot, PendingPracticeCommand? pending, string? outboxCode, bool needsRefresh, bool inFlight)
        {
            Snapshot = snapshot; Pending = pending; OutboxCode = outboxCode; NeedsRefresh = needsRefresh; InFlight = inFlight;
        }

        public PracticeSnapshot? Snapshot { get; }
        /// <summary>The unresolved mutation last read from the outbox (sent or not). Only an exact retry resolves it.</summary>
        public PendingPracticeCommand? Pending { get; }
        /// <summary>The outbox could not be read last time (busy, corrupt, unsupported, another binding, ...): pending is unknown.</summary>
        public string? OutboxCode { get; }
        /// <summary>The snapshot may be behind (a resolved mutation, a failed or refused re-read, a stale query, a moved server context);
        /// the last good snapshot stays readable, but refresh before acting.</summary>
        public bool NeedsRefresh { get; }
        /// <summary>An operation of this session is running (a network exchange may be in flight).</summary>
        public bool InFlight { get; }
        public bool CanMutate => Snapshot != null && Pending == null && OutboxCode == null && !NeedsRefresh && !InFlight;
    }

    public sealed partial class PracticeServerSession : IDisposable
    {
        public const string AccountRoute = "/dev/practice/account";
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30), MinTimeout = TimeSpan.FromSeconds(1), MaxTimeout = TimeSpan.FromSeconds(120);
        const string RunsPrefix = PracticeCommandOutbox.CreateRunRoute + "/", BattleQuerySuffix = "/battle-query", MemoryPreviewSuffix = "/memory-preview";
        const int MinCredential = 16, MaxCredential = 512;
        // Contracts.CommandArgs: the existing journal kinds of the commands route (memoryCommit has its own route). Arguments are the server's.
        static readonly HashSet<string> CommandKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "enter", "play", "ult", "end", "takeCard", "skipCard", "takeRelic", "takePart", "evolve", "proceed", "archive", "rest",
            "forgeOffer", "forgeApply", "forgeRemove", "applyPart", "buy", "event", "assign"
        };
        static readonly HashSet<string> Modes = new HashSet<string>(StringComparer.Ordinal) { "now", "next", "after" };

        readonly string endpoint, account;
        readonly Func<string> credentials;
        readonly PracticeCommandOutbox outbox;
        readonly TimeSpan timeout;
        readonly HttpClient client;
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly CancellationToken lifetimeToken;
        readonly object sync = new object();
        PracticeSessionState state = new PracticeSessionState(null, null, null, false, false);
        bool disposed;

        /// <summary>endpoint and account must be exactly the outbox's (it validated them: http://127.0.0.1:&lt;port&gt; and the fixture alias).
        /// credentialProvider returns the current in-memory credential; it is called once per operation and its value is never kept.
        /// timeout bounds each exchange (MinTimeout..MaxTimeout, default 30 s). Nothing is read, sent or locked here.</summary>
        public PracticeServerSession(string endpoint, string account, Func<string> credentialProvider, PracticeCommandOutbox outbox, TimeSpan? timeout = null)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (credentialProvider == null) throw new ArgumentNullException(nameof(credentialProvider));
            if (outbox == null) throw new ArgumentNullException(nameof(outbox));
            if (outbox.Endpoint != endpoint || outbox.Account != account) throw new ArgumentException("The outbox must be bound to exactly this endpoint and account.", nameof(outbox));
            var limit = timeout ?? DefaultTimeout;
            if (limit < MinTimeout || limit > MaxTimeout) throw new ArgumentOutOfRangeException(nameof(timeout));
            this.endpoint = endpoint; this.account = account; credentials = credentialProvider; this.outbox = outbox; this.timeout = limit;
            lifetimeToken = lifetime.Token;
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false, UseProxy = false, Proxy = null, UseCookies = false, UseDefaultCredentials = false, Credentials = null, PreAuthenticate = false
            };
            // HttpClient's own timeout is only a backstop behind the per-exchange deadline below.
            client = new HttpClient(handler, true) { Timeout = limit + TimeSpan.FromSeconds(1), MaxResponseContentBufferSize = PracticePublicJson.MaxResponseBytes };
        }

        public string Endpoint => endpoint;
        public string Account => account;
        public PracticeSessionState State { get { lock (sync) return state; } }
        /// <summary>Whether a battle query answer still belongs to the current snapshot; a stale answer must not replace a selection.</summary>
        public bool IsCurrent(PracticeBattleQueryResult result) => result != null && State.Snapshot?.Context.Equals(result.Context) == true;
        /// <summary>Whether a memory preview answer still belongs to the current snapshot. Its selection is the caller's to match: two
        /// answers at the same context may be for different selections.</summary>
        public bool IsCurrent(PracticeMemoryPreviewResult result) => result != null && State.Snapshot?.Context.Equals(result.Context) == true;

        /// <summary>Re-reads the outbox, then the account (owner checked first) and, when it names one, its current run, and applies them
        /// monotonically. Pure on the server; never resolves a pending request.</summary>
        public Task<PracticeRefreshResult> RefreshAsync(CancellationToken cancellation = default) =>
            Exclusive(async () =>
            {
                ReloadPending();
                string? credential = CaptureCredential();
                if (credential == null)
                {
                    SetNeedsRefresh();
                    return new PracticeRefreshResult(Codes.CredentialUnavailable, null, null);
                }
                var read = await RefreshWithAsync(credential, cancellation).ConfigureAwait(false);
                return new PracticeRefreshResult(read.Code, read.Status, read.Value);
            }, code => new PracticeRefreshResult(code, null, null));

        /// <summary>POST /dev/practice/runs {requestId, useMemory}. The server decides whether a new run may start (RUN_ACTIVE).</summary>
        public Task<PracticeMutationResult> CreateRunAsync(PracticeUiContext context, bool useMemory, CancellationToken cancellation = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return Exclusive(() => NewMutationAsync(context, false, (requestId, run) =>
                (PracticeCommandOutbox.CreateRunRoute, Json(new JObject { ["requestId"] = requestId, ["useMemory"] = useMemory })), cancellation), NotSent);
        }

        /// <summary>POST /dev/practice/runs/{runId}/commands {requestId, expectedVersion, kind, args} for an existing kind; args are copied
        /// once in their order and judged by the server.</summary>
        public Task<PracticeMutationResult> CommandAsync(PracticeUiContext context, string kind, JObject? args, CancellationToken cancellation = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            if (!CommandKinds.Contains(kind)) return Task.FromResult(NotSent(Codes.InvalidCommand));
            var copied = args == null ? new JObject() : (JObject)args.DeepClone();
            return Exclusive(() => NewMutationAsync(context, true, (requestId, run) => (PracticeCommandOutbox.CommandRoute(run!.RunId),
                Json(new JObject { ["requestId"] = requestId, ["expectedVersion"] = run!.ServerVersion, ["kind"] = kind, ["args"] = copied })), cancellation), NotSent);
        }

        /// <summary>POST /dev/practice/runs/{runId}/memory-commit {requestId, expectedVersion, expectedMemoryVersion, selectedChangeIds}: the
        /// expected memory version is the context's (the saved store version the save screen showed); the IDs are copied in order.</summary>
        public Task<PracticeMutationResult> CommitMemoryAsync(PracticeUiContext context, IReadOnlyList<string> selectedChangeIds, CancellationToken cancellation = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (selectedChangeIds == null) throw new ArgumentNullException(nameof(selectedChangeIds));
            var ids = new JArray();
            foreach (string id in selectedChangeIds)
            {
                if (id == null) return Task.FromResult(NotSent(Codes.InvalidCommand));
                ids.Add(id);
            }
            return Exclusive(() => NewMutationAsync(context, true, (requestId, run) => (PracticeCommandOutbox.MemoryCommitRoute(run!.RunId),
                Json(new JObject { ["requestId"] = requestId, ["expectedVersion"] = run!.ServerVersion, ["expectedMemoryVersion"] = context.MemoryVersion, ["selectedChangeIds"] = ids })),
                cancellation), NotSent);
        }

        /// <summary>Sends the outbox's pending request again exactly as stored (route, body, requestId, server version), whatever the UI
        /// shows now, and resolves it only by that attempt's validated reply.</summary>
        public Task<PracticeMutationResult> RetryPendingAsync(CancellationToken cancellation = default) =>
            Exclusive(async () =>
            {
                PendingPracticeCommand? pending;
                try { pending = outbox.Load(); }
                catch (PracticeOutboxException error) { NoteOutboxFailure(error.Code); return NotSent(error.Code); }
                NotePending(pending);
                if (pending == null) return NotSent(Codes.NothingPending);
                if (PracticeCommandOutbox.IsRecruitmentRoute(pending.Route)) return NotSent(Codes.PendingUnresolved);
                return await AttemptAsync(pending, State.Snapshot, cancellation).ConfigureAwait(false);
            }, NotSent);

        /// <summary>Pure POST /dev/practice/runs/{runId}/battle-query {expectedVersion, uid, mode, target?, picks} at the context's server
        /// version: Core's forecast and pick sheet for exactly this request. Allowed while a mutation is pending; never staged, never applied.</summary>
        public Task<PracticeBattleQueryResult> BattleQueryAsync(PracticeUiContext context, string uid, string? mode = null, string? target = null,
            IReadOnlyList<string>? picks = null, CancellationToken cancellation = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (uid == null) throw new ArgumentNullException(nameof(uid));
            string effective = mode ?? "now";
            var ordered = new List<string>();
            if (picks != null) foreach (string pick in picks) ordered.Add(pick);
            if (!Modes.Contains(effective) || ordered.Contains(null!)) return Task.FromResult(new PracticeBattleQueryResult(Codes.InvalidCommand, null, context, null));
            return Exclusive(() => QueryAsync(context, uid, effective, target, ordered, cancellation), code => new PracticeBattleQueryResult(code, null, context, null));
        }

        /// <summary>Pure POST /dev/practice/runs/{runId}/memory-preview {selectedChangeIds} on the context's save screen: Core's full
        /// resulting party decks, their memory costs, the capacities and Core's verdict for exactly this ordered selection (empty = keep
        /// the saved decks). The IDs are copied once, at the call. The route takes no version, so the owner handshake must still show
        /// exactly the context (owner, run, server and memory version; else REFRESH_REQUIRED, nothing posted) and the reply must carry
        /// the context's run and server version (else RESPONSE_MISMATCH and a due refresh). Allowed while a mutation is pending; never
        /// staged, applied or presented, and it authorizes nothing: CommitMemoryAsync is judged again by the server.</summary>
        public Task<PracticeMemoryPreviewResult> PreviewMemoryAsync(PracticeUiContext context, IReadOnlyList<string> selectedChangeIds, CancellationToken cancellation = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (selectedChangeIds == null) throw new ArgumentNullException(nameof(selectedChangeIds));
            var ordered = new List<string>(selectedChangeIds.Count);
            foreach (string id in selectedChangeIds) ordered.Add(id);
            if (ordered.Contains(null!)) return Task.FromResult(new PracticeMemoryPreviewResult(Codes.InvalidCommand, null, context, null));
            var selected = System.Array.AsReadOnly(ordered.ToArray());
            return Exclusive(() => PreviewAsync(context, selected, cancellation), code => new PracticeMemoryPreviewResult(code, null, context, null));
        }

        /// <summary>Cancels a running exchange (it ends CANCELED; its request, if any, stays pending) and releases the HTTP client. The
        /// outbox record is never touched: an uncertain request stays pending for an exact retry by a later session.</summary>
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
            }
            lifetime.Cancel();
            client.Dispose();
        }

        // ---- operations ----

        async Task<T> Exclusive<T>(Func<Task<T>> operation, Func<string, T> refused)
        {
            lock (sync) if (disposed) return refused(Codes.Disposed);
            if (!gate.Wait(0)) return refused(Codes.Busy);
            try
            {
                SetInFlight(true);
                return await operation().ConfigureAwait(false);
            }
            finally
            {
                SetInFlight(false);
                gate.Release();
            }
        }

        async Task<PracticeMutationResult> NewMutationAsync(PracticeUiContext context, bool runRoute, Func<string, PracticeRunView?, (string Route, string Body)> build,
            CancellationToken cancellation)
        {
            var current = State;
            var snapshot = current.Snapshot;
            if (snapshot == null) return NotSent(Codes.NoSnapshot);
            if (current.NeedsRefresh) return NotSent(Codes.RefreshRequired);
            if (!context.Equals(snapshot.Context)) return NotSent(Codes.ContextStale);
            if (runRoute && snapshot.Run == null) return NotSent(Codes.NoRun);
            PendingPracticeCommand? existing;
            try { existing = outbox.Load(); }
            catch (PracticeOutboxException error) { NoteOutboxFailure(error.Code); return NotSent(error.Code); }
            NotePending(existing);
            if (existing != null) return new PracticeMutationResult(PracticeMutationStatus.NotSent, Codes.PendingUnresolved, null, existing, null, null, null, null, null);
            // A cancelled or disposed call stages nothing: only a request the player is sending now becomes pending.
            if (lifetimeToken.IsCancellationRequested) return NotSent(Codes.Disposed);
            if (cancellation.IsCancellationRequested) return NotSent(Codes.Canceled);
            // The owner handshake comes before any requestId or record: a request is staged only when this credential may send it now,
            // and only while the server still shows exactly the context the player acted on.
            string? credential = CaptureCredential();
            if (credential == null) return NotSent(Codes.CredentialUnavailable);
            var handshake = await ReadAccountAsync(credential, cancellation).ConfigureAwait(false);
            var account = handshake.Value;
            if (account == null) return NotSentWith(handshake.Code, handshake.Status);
            if (!Shows(account, context))
            {
                SetNeedsRefresh();
                return NotSentWith(Codes.RefreshRequired, 200);
            }
            if (lifetimeToken.IsCancellationRequested) return NotSent(Codes.Disposed);
            if (cancellation.IsCancellationRequested) return NotSent(Codes.Canceled);
            var (route, body) = build(Guid.NewGuid().ToString("D"), snapshot.Run);
            PendingPracticeCommand staged;
            try { staged = outbox.Stage(route, body); }
            catch (PracticeOutboxException error)
            {
                // Nothing was returned to send; Load tells whether the request nevertheless became pending (then only a retry sends it).
                ReloadPending();
                return NotSent(error.Code);
            }
            NotePending(staged);
            return await PostAsync(staged, account, credential, snapshot, cancellation).ConfigureAwait(false);
        }

        // A retry of exactly this pending request: its own credential capture and owner handshake, then the POST. A preflight failure
        // leaves it pending. shown is the snapshot the UI showed before it (for event freshness only).
        async Task<PracticeMutationResult> AttemptAsync(PendingPracticeCommand pending, PracticeSnapshot? shown, CancellationToken cancellation)
        {
            string? credential = CaptureCredential();
            if (credential == null) return Unresolved(pending, Codes.CredentialUnavailable, null);
            var handshake = await ReadAccountAsync(credential, cancellation).ConfigureAwait(false);
            if (handshake.Value == null) return Unresolved(pending, handshake.Code, handshake.Status);
            return await PostAsync(pending, handshake.Value, credential, shown, cancellation).ConfigureAwait(false);
        }

        // One POST of exactly this pending request with the credential whose immediately preceding handshake returned account.
        async Task<PracticeMutationResult> PostAsync(PendingPracticeCommand pending, PracticeAccountView account, string credential, PracticeSnapshot? shown,
            CancellationToken cancellation)
        {
            var exchange = await ExchangeAsync(HttpMethod.Post, pending.Route, pending.BodyUtf8(), credential, cancellation).ConfigureAwait(false);
            if (exchange.Failure != null) return Unresolved(pending, exchange.Failure, null);
            if (exchange.Status == 200)
            {
                PracticeReceipt receipt;
                try { receipt = PracticeReceipt.Parse(pending, exchange.Body); }
                catch (PracticeProtocolException error) { return Unresolved(pending, error.Code, 200); }
                string? outboxCode = Resolve(pending, true);
                var after = await RefreshWithAsync(credential, cancellation).ConfigureAwait(false);
                return new PracticeMutationResult(PracticeMutationStatus.Committed, Codes.Ok, 200, pending, receipt, after.Value, outboxCode,
                    after.Value == null ? after.Code : null, FreshEvents(receipt, shown, after.Value));
            }
            string? code = ServerCode(exchange.Body);
            if (code == null) return Unresolved(pending, PracticeProtocolException.Invalid, exchange.Status);
            if (!KnownNotCommitted(pending, account, exchange.Status, code)) return Unresolved(pending, code, exchange.Status);
            string? clearFailure = Resolve(pending, false);
            if (clearFailure != null)
                return new PracticeMutationResult(PracticeMutationStatus.Pending, code, exchange.Status, pending, null, null, clearFailure, null, null);
            var refreshed = await RefreshWithAsync(credential, cancellation).ConfigureAwait(false);
            return new PracticeMutationResult(PracticeMutationStatus.NotCommitted, code, exchange.Status, pending, null, refreshed.Value, null,
                refreshed.Value == null ? refreshed.Code : null, null);
        }

        // The outbox clear of a validated resolution. From here the shown snapshot is due a re-read (the server moved on, or refused it as
        // stale). A clear failure leaves the request pending (Load shows it) and is returned.
        string? Resolve(PendingPracticeCommand pending, bool committed)
        {
            SetNeedsRefresh();
            string? failure = null;
            try
            {
                if (committed) outbox.Confirm(pending);
                else outbox.ConfirmNotCommitted(pending);
            }
            catch (PracticeOutboxException error) { failure = error.Code; }
            ReloadPending();
            return failure;
        }

        /// <summary>The only refusals that clear a pending request (P1-PRACTICE-TRANSPORT-REFUSAL-FIX): a versioned /commands or
        /// /memory-commit request answered 409 STALE_VERSION or 422 (a Core rule / COMMAND_REJECTED refusal). Program.Execute raises both
        /// after PracticeRunStore.Mutate's receipt lookup and before any write. In addition, the same credential's handshake immediately
        /// before this POST must have named the request's run as the current run at a server journal version &gt;= its expectedVersion:
        /// journal versions only grow, so such a request can never execute later (STALE_VERSION is an inequality, and a future-version
        /// request could still become current). Everything else keeps it pending: every new-run refusal (a delayed earlier copy can still
        /// execute once the active run is DONE), every 400 (parsed before the receipt lookup), every 404 and 507 (507 can come from loading
        /// before the lookup), 401/403, 409 IDEMPOTENCY_CONFLICT and the store binding refusals, every 500/503 and anything unknown.</summary>
        static bool KnownNotCommitted(PendingPracticeCommand pending, PracticeAccountView handshake, int status, string code)
        {
            if (!(pending.ExpectedVersion is int expected) || pending.Route == PracticeCommandOutbox.CreateRunRoute) return false;
            if (!(status == 409 && code == "STALE_VERSION" || status == 422)) return false;
            var run = handshake.Run;
            return run != null && pending.Route.Substring(RunsPrefix.Length, 35) == run.RunId && run.ServerVersion >= expected;
        }

        static JArray? FreshEvents(PracticeReceipt receipt, PracticeSnapshot? shown, PracticeSnapshot? applied)
        {
            var before = shown?.Run; var after = applied?.Run;
            if (before == null || after == null || before.RunId != receipt.RunId || after.RunId != receipt.RunId) return null;
            if ((long)before.ServerVersion + 1 != receipt.ServerVersion || after.ServerVersion != receipt.ServerVersion) return null;
            return receipt.Reply.EventsJArray();
        }

        async Task<PracticeBattleQueryResult> QueryAsync(PracticeUiContext context, string uid, string mode, string? target, List<string> picks, CancellationToken cancellation)
        {
            PracticeBattleQueryResult Fail(string code, int? status) => new PracticeBattleQueryResult(code, status, context, null);
            var current = State;
            var snapshot = current.Snapshot;
            if (snapshot == null) return Fail(Codes.NoSnapshot, null);
            if (current.NeedsRefresh) return Fail(Codes.RefreshRequired, null);
            if (!context.Equals(snapshot.Context)) return Fail(Codes.ContextStale, null);
            var run = snapshot.Run;
            if (run == null) return Fail(Codes.NoRun, null);
            if (run.BattleVersion == null) return Fail(Codes.NoBattle, null);
            var query = new JObject { ["expectedVersion"] = run.ServerVersion, ["uid"] = uid, ["mode"] = mode };
            if (target != null) query["target"] = target;
            var ordered = new JArray();
            foreach (string pick in picks) ordered.Add(pick);
            query["picks"] = ordered;
            byte[] body;
            try { body = PracticeCommandOutbox.StrictUtf8.GetBytes(Json(query)); }
            catch (EncoderFallbackException) { return Fail(Codes.InvalidCommand, null); }
            if (body.Length > PracticeCommandOutbox.MaxBodyBytes) return Fail(Codes.InvalidCommand, null);
            string? credential = CaptureCredential();
            if (credential == null) return Fail(Codes.CredentialUnavailable, null);
            var handshake = await ReadAccountAsync(credential, cancellation).ConfigureAwait(false);
            if (handshake.Value == null) return Fail(handshake.Code, handshake.Status);
            var exchange = await ExchangeAsync(HttpMethod.Post, RunsPrefix + run.RunId + BattleQuerySuffix, body, credential, cancellation).ConfigureAwait(false);
            if (exchange.Failure != null) return Fail(exchange.Failure, null);
            if (exchange.Status != 200)
            {
                string code = ServerCode(exchange.Body) ?? PracticeProtocolException.Invalid;
                // The shown state is behind the server, or its run was replaced: it must be re-read before anyone acts on it.
                if (exchange.Status == 409 && code == "STALE_VERSION" || exchange.Status == 404 && code == "RUN_NOT_FOUND") SetNeedsRefresh();
                return Fail(code, exchange.Status);
            }
            try { return new PracticeBattleQueryResult(Codes.Ok, 200, context, PracticeBattleQueryView.Parse(exchange.Body, context, uid, mode, target, picks)); }
            catch (PracticeProtocolException error) { return Fail(error.Code, 200); }
        }

        async Task<PracticeMemoryPreviewResult> PreviewAsync(PracticeUiContext context, IReadOnlyList<string> selected, CancellationToken cancellation)
        {
            PracticeMemoryPreviewResult Fail(string code, int? status) => new PracticeMemoryPreviewResult(code, status, context, null);
            var current = State;
            var snapshot = current.Snapshot;
            if (snapshot == null) return Fail(Codes.NoSnapshot, null);
            if (current.NeedsRefresh) return Fail(Codes.RefreshRequired, null);
            if (!context.Equals(snapshot.Context)) return Fail(Codes.ContextStale, null);
            var run = snapshot.Run;
            if (run == null) return Fail(Codes.NoRun, null);
            if (run.PendingType != "save") return Fail(Codes.NoSave, null);
            var ids = new JArray();
            foreach (string id in selected) ids.Add(id);
            byte[] body;
            try { body = PracticeCommandOutbox.StrictUtf8.GetBytes(Json(new JObject { ["selectedChangeIds"] = ids })); }
            catch (EncoderFallbackException) { return Fail(Codes.InvalidCommand, null); }
            if (body.Length > PracticeCommandOutbox.MaxBodyBytes) return Fail(Codes.InvalidCommand, null);
            string? credential = CaptureCredential();
            if (credential == null) return Fail(Codes.CredentialUnavailable, null);
            var handshake = await ReadAccountAsync(credential, cancellation).ConfigureAwait(false);
            var account = handshake.Value;
            if (account == null) return Fail(handshake.Code, handshake.Status);
            // Only a server that still shows exactly this context is asked. The reply carries no memory version: an equal server version
            // below proves it unchanged since this handshake, as only a journal entry of the current run (the save) changes it.
            if (!Shows(account, context))
            {
                SetNeedsRefresh();
                return Fail(Codes.RefreshRequired, 200);
            }
            var exchange = await ExchangeAsync(HttpMethod.Post, RunsPrefix + run.RunId + MemoryPreviewSuffix, body, credential, cancellation).ConfigureAwait(false);
            if (exchange.Failure != null) return Fail(exchange.Failure, null);
            if (exchange.Status != 200)
            {
                string code = ServerCode(exchange.Body) ?? PracticeProtocolException.Invalid;
                // The run was replaced, or the server's run at this context is not on the shown save screen: re-read before anyone acts.
                if (exchange.Status == 404 && code == "RUN_NOT_FOUND" || exchange.Status == 422 && code == "WRONG_PHASE") SetNeedsRefresh();
                return Fail(code, exchange.Status);
            }
            try { return new PracticeMemoryPreviewResult(Codes.Ok, 200, context, PracticeMemoryPreviewView.Parse(exchange.Body, context, selected)); }
            catch (PracticeProtocolException error)
            {
                // Another run or server version: the server moved on after the handshake, so the shown save screen is behind it.
                if (error.Code == PracticeProtocolException.Mismatch) SetNeedsRefresh();
                return Fail(error.Code, 200);
            }
        }

        // The owner handshake still shows exactly the acting context: its saved memory version and its current run at that server version.
        static bool Shows(PracticeAccountView account, PracticeUiContext context) =>
            account.MemoryVersion == context.MemoryVersion && account.Run?.RunId == context.RunId && account.Run?.ServerVersion == context.ServerVersion;

        // ---- reads and snapshots ----

        readonly struct Read<T> where T : class
        {
            public Read(T? value, string code, int? status) { Value = value; Code = code; Status = status; }
            public T? Value { get; }
            public string Code { get; }
            public int? Status { get; }
        }

        async Task<Read<PracticeAccountView>> ReadAccountAsync(string credential, CancellationToken cancellation)
        {
            var exchange = await ExchangeAsync(HttpMethod.Get, AccountRoute, null, credential, cancellation).ConfigureAwait(false);
            if (exchange.Failure != null) return new Read<PracticeAccountView>(null, exchange.Failure, null);
            if (exchange.Status != 200) return new Read<PracticeAccountView>(null, ServerCode(exchange.Body) ?? PracticeProtocolException.Invalid, exchange.Status);
            try { return new Read<PracticeAccountView>(PracticeAccountView.Parse(exchange.Body, account), Codes.Ok, 200); }
            catch (PracticeProtocolException error) { return new Read<PracticeAccountView>(null, error.Code, 200); }
        }

        // The account (owner first), then its current run at exactly the account's server and memory version; nothing is applied here.
        async Task<Read<PracticeSnapshot>> ReadSnapshotAsync(string credential, CancellationToken cancellation)
        {
            var read = await ReadAccountAsync(credential, cancellation).ConfigureAwait(false);
            var view = read.Value;
            if (view == null) return new Read<PracticeSnapshot>(null, read.Code, read.Status);
            if (view.Run == null) return new Read<PracticeSnapshot>(new PracticeSnapshot(view, null), Codes.Ok, 200);
            var exchange = await ExchangeAsync(HttpMethod.Get, RunsPrefix + view.Run.RunId, null, credential, cancellation).ConfigureAwait(false);
            if (exchange.Failure != null) return new Read<PracticeSnapshot>(null, exchange.Failure, null);
            if (exchange.Status != 200)
            {
                string code = ServerCode(exchange.Body) ?? PracticeProtocolException.Invalid;
                return new Read<PracticeSnapshot>(null, exchange.Status == 404 && code == "RUN_NOT_FOUND" ? Codes.RefreshRaced : code, exchange.Status);
            }
            PracticeRunView run;
            try { run = PracticeRunView.Parse(exchange.Body, view.Run.RunId); }
            catch (PracticeProtocolException error) { return new Read<PracticeSnapshot>(null, error.Code, 200); }
            if (run.ServerVersion != view.Run.ServerVersion || run.Phase != view.Run.Phase || run.AccountMemoryVersion != view.MemoryVersion)
                return new Read<PracticeSnapshot>(null, Codes.RefreshRaced, 200);
            return new Read<PracticeSnapshot>(new PracticeSnapshot(view, run), Codes.Ok, 200);
        }

        // A fresh read applied monotonically; Value is set only when it was applied. A failed read keeps the last good snapshot but marks
        // it NeedsRefresh: what the server holds now is unknown, so no mutation may start from it.
        async Task<Read<PracticeSnapshot>> RefreshWithAsync(string credential, CancellationToken cancellation)
        {
            var read = await ReadSnapshotAsync(credential, cancellation).ConfigureAwait(false);
            var next = read.Value;
            if (next == null)
            {
                SetNeedsRefresh();
                return read;
            }
            lock (sync)
            {
                var shown = state.Snapshot;
                if (shown != null && RollsBack(shown, next))
                {
                    state = new PracticeSessionState(shown, state.Pending, state.OutboxCode, true, state.InFlight);
                    return new Read<PracticeSnapshot>(null, Codes.RollbackRefused, 200);
                }
                state = new PracticeSessionState(next, state.Pending, state.OutboxCode, false, state.InFlight);
            }
            return read;
        }

        // Both are this owner's (every read passed the handshake). The same run never goes to an older server version; the saved memory
        // version and the finished-run count never go back; the current run is only replaced by archiving it (a new run: finishedRuns + 1).
        static bool RollsBack(PracticeSnapshot shown, PracticeSnapshot next)
        {
            if (next.Account.MemoryVersion < shown.Account.MemoryVersion || next.Account.FinishedRuns < shown.Account.FinishedRuns) return true;
            if (shown.Run == null) return false;
            if (next.Run != null && next.Run.RunId == shown.Run.RunId) return next.Run.ServerVersion < shown.Run.ServerVersion;
            return next.Account.FinishedRuns <= shown.Account.FinishedRuns;
        }

        // ---- HTTP ----

        sealed class Exchange
        {
            public Exchange(int status, byte[] body) { Status = status; Body = body; }
            public Exchange(string failure) { Failure = failure; Body = System.Array.Empty<byte>(); }
            /// <summary>A transport or reply-framing failure: there is no usable reply.</summary>
            public string? Failure { get; }
            public int Status { get; }
            public byte[] Body { get; }
        }

        // One request with this operation's credential under one deadline (caller cancellation and Dispose linked). The reply body must be
        // application/json with a Content-Length within the cap; it is read into exactly that many bytes, and the deadline aborts a stalled
        // read by disposing the response.
        async Task<Exchange> ExchangeAsync(HttpMethod method, string route, byte[]? body, string credential, CancellationToken cancellation)
        {
            using (var deadline = new CancellationTokenSource(timeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token, lifetimeToken))
            {
                var token = linked.Token;
                bool reading = false;
                try
                {
                    using (var request = new HttpRequestMessage(method, new Uri(endpoint + route, UriKind.Absolute)))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        request.Headers.ExpectContinue = false;
                        if (body != null)
                        {
                            request.Content = new ByteArrayContent(body);
                            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                        }
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                        using (token.Register(Abort, response))
                        {
                            reading = true;
                            var content = response.Content;
                            if (content == null || !IsJson(content.Headers.ContentType)) return new Exchange(PracticeProtocolException.Invalid);
                            long? length = content.Headers.ContentLength;
                            if (length == null || length < 0) return new Exchange(PracticeProtocolException.Invalid);
                            if (length > PracticePublicJson.MaxResponseBytes) return new Exchange(Codes.ResponseTooLarge);
                            var bytes = new byte[(int)length.Value];
                            using (var stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
                            {
                                int total = 0;
                                while (total < bytes.Length)
                                {
                                    int read = await stream.ReadAsync(bytes, total, bytes.Length - total, token).ConfigureAwait(false);
                                    if (read == 0) return new Exchange(Codes.ResponseTruncated);
                                    total += read;
                                }
                                if (await stream.ReadAsync(new byte[1], 0, 1, token).ConfigureAwait(false) != 0) return new Exchange(PracticeProtocolException.Invalid);
                            }
                            return new Exchange((int)response.StatusCode, bytes);
                        }
                    }
                }
                catch (Exception error) when (IsTransportFailure(error))
                {
                    if (token.IsCancellationRequested)
                        return new Exchange(deadline.IsCancellationRequested && !cancellation.IsCancellationRequested && !lifetimeToken.IsCancellationRequested
                            ? Codes.Timeout : Codes.Canceled);
                    // HttpClient's backstop timeout cancels without our token.
                    if (error is OperationCanceledException) return new Exchange(Codes.Timeout);
                    return new Exchange(reading ? Codes.ResponseTruncated : Codes.NetworkFailed);
                }
            }
        }

        static void Abort(object? response) => ((HttpResponseMessage)response!).Dispose();

        // The outcome of a request whose exchange failed is unknown, whatever the exception (Mono and .NET differ in which they throw);
        // it maps to a fixed code and its text is never kept. ObjectDisposedException is an InvalidOperationException.
        static bool IsTransportFailure(Exception error) => error is HttpRequestException || error is IOException || error is WebException
            || error is SocketException || error is OperationCanceledException || error is InvalidOperationException;

        static bool IsJson(MediaTypeHeaderValue? type) => type != null && string.Equals(type.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            && (type.CharSet == null || string.Equals(type.CharSet, "utf-8", StringComparison.OrdinalIgnoreCase));

        /// <summary>The code of a server error reply {code, message} (exactly those two strings; Reply.Error), when it is a safe
        /// upper-case code of 1..64 [A-Z0-9_]; null for anything else.</summary>
        static string? ServerCode(byte[] body)
        {
            JObject error;
            try { error = PracticePublicJson.ParseObject(body); }
            catch (PracticeProtocolException) { return null; }
            if (error.Count != 2 || !(error["message"] is JValue message && message.Type == JTokenType.String)
                || !(error["code"] is JValue code && code.Type == JTokenType.String)) return null;
            string text = (string)code.Value!;
            return PracticePublicJson.IsCode(text) ? text : null;
        }

        // ---- state ----

        string? CaptureCredential()
        {
            string? value;
            // The callback is the caller's code: its failure becomes CREDENTIAL_UNAVAILABLE and nothing of it is kept.
            try { value = credentials(); }
            catch (Exception) { return null; }
            if (value == null || value.Length < MinCredential || value.Length > MaxCredential) return null;
            foreach (char c in value) if (c < '!' || c > '~') return null;
            return value;
        }

        void ReloadPending()
        {
            try { NotePending(outbox.Load()); }
            catch (PracticeOutboxException error) { NoteOutboxFailure(error.Code); }
        }

        void NotePending(PendingPracticeCommand? pending)
        {
            lock (sync) state = new PracticeSessionState(state.Snapshot, pending, null, state.NeedsRefresh, state.InFlight);
        }

        // The pending state is unknown; the last known request (if any) is kept so it is never shown as resolved.
        void NoteOutboxFailure(string code)
        {
            lock (sync) state = new PracticeSessionState(state.Snapshot, state.Pending, code, state.NeedsRefresh, state.InFlight);
        }

        void SetNeedsRefresh()
        {
            lock (sync) state = new PracticeSessionState(state.Snapshot, state.Pending, state.OutboxCode, true, state.InFlight);
        }

        void SetInFlight(bool inFlight)
        {
            lock (sync) state = new PracticeSessionState(state.Snapshot, state.Pending, state.OutboxCode, state.NeedsRefresh, inFlight);
        }

        static string Json(JObject body) => body.ToString(Formatting.None);
        static PracticeMutationResult NotSent(string code) => NotSentWith(code, null);
        static PracticeMutationResult NotSentWith(string code, int? status) =>
            new PracticeMutationResult(PracticeMutationStatus.NotSent, code, status, null, null, null, null, null, null);
        static PracticeMutationResult Unresolved(PendingPracticeCommand pending, string code, int? status) =>
            new PracticeMutationResult(PracticeMutationStatus.Pending, code, status, pending, null, null, null, null, null);
    }
}
