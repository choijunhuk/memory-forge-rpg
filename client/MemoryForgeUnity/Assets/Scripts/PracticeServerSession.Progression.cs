#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Codes = MemoryForge.Client.PracticeSessionCodes;

namespace MemoryForge.Client
{
    public sealed class ProgressionRefreshResult
    {
        internal ProgressionRefreshResult(string code, int? httpStatus, PracticeProgressionSnapshot? snapshot) { Code = code; HttpStatus = httpStatus; Snapshot = snapshot; }
        public string Code { get; }
        public int? HttpStatus { get; }
        public PracticeProgressionSnapshot? Snapshot { get; }
    }

    public sealed class ProgressionMutationResult
    {
        internal ProgressionMutationResult(PracticeMutationStatus status, string code, int? httpStatus = null, PendingPracticeCommand? request = null,
            PracticeReceipt? receipt = null, PracticeAbandonReceipt? abandonReceipt = null, PracticeSnapshot? snapshot = null,
            PracticeProgressionSnapshot? progression = null, string? outboxCode = null, string? refreshCode = null)
        {
            Status = status; Code = code; HttpStatus = httpStatus; Request = request; Receipt = receipt; AbandonReceipt = abandonReceipt;
            Snapshot = snapshot; Progression = progression; OutboxCode = outboxCode; RefreshCode = refreshCode;
        }
        public PracticeMutationStatus Status { get; }
        public string Code { get; }
        public int? HttpStatus { get; }
        public PendingPracticeCommand? Request { get; }
        public PracticeReceipt? Receipt { get; }
        public PracticeAbandonReceipt? AbandonReceipt { get; }
        public PracticeSnapshot? Snapshot { get; }
        public PracticeProgressionSnapshot? Progression { get; }
        public string? OutboxCode { get; }
        public string? RefreshCode { get; }
    }

    public sealed partial class PracticeServerSession
    {
        public const string ProgressionRoute = "/dev/practice/progression";
        PracticeProgressionSnapshot? progression;
        bool progressionNeedsRefresh = true;
        public PracticeProgressionSnapshot? Progression { get { lock (sync) return progression; } }
        public bool ProgressionNeedsRefresh { get { lock (sync) return progressionNeedsRefresh; } }

        public Task<ProgressionRefreshResult> RefreshProgressionAsync(CancellationToken cancellation = default) =>
            Exclusive(async () =>
            {
                ReloadPending();
                string? credential = CaptureCredential();
                if (credential == null) { MarkProgressionStale(); return new ProgressionRefreshResult(Codes.CredentialUnavailable, null, null); }
                var read = await RefreshProgressionWithAsync(credential, cancellation).ConfigureAwait(false);
                return new ProgressionRefreshResult(read.Code, read.Status, read.Value);
            }, code => new ProgressionRefreshResult(code, null, null));

        /// <summary>One explicit tutorial or owned story departure. Party order is copied before asynchronous preflight; no stats or length are sent.</summary>
        public Task<ProgressionMutationResult> LaunchExpeditionAsync(long expectedRevision, string mode, int? chapter = null,
            IReadOnlyList<string>? party = null, bool useMemory = true, CancellationToken cancellation = default)
        {
            var ordered = party?.ToArray();
            if (expectedRevision < 0 || expectedRevision == long.MaxValue || mode != "tutorial" && mode != "story"
                || mode == "tutorial" && (chapter != null || ordered != null)
                || mode == "story" && (chapter != 1 || ordered == null || ordered.Length != 3
                    || ordered.Any(id => id == null || !PracticeCommandOutbox.IsPublicId(id)) || ordered.Distinct(StringComparer.Ordinal).Count() != 3))
                return Task.FromResult(ProgressionNotSent(Codes.InvalidCommand));
            var intent = new JObject { ["mode"] = mode };
            if (mode == "story") { intent["chapter"] = chapter!.Value; intent["party"] = new JArray(ordered!); }
            intent["useMemory"] = useMemory;
            return Exclusive(async () =>
            {
                var blocked = ProgressionPendingBlock(); if (blocked != null) return blocked;
                var shown = Progression;
                if (shown == null) return ProgressionNotSent(Codes.NoSnapshot);
                if (shown.Revision != expectedRevision) return ProgressionNotSent(Codes.ContextStale);
                if (ProgressionNeedsRefresh) return ProgressionNotSent(Codes.RefreshRequired);
                if (lifetimeToken.IsCancellationRequested) return ProgressionNotSent(Codes.Disposed);
                if (cancellation.IsCancellationRequested) return ProgressionNotSent(Codes.Canceled);
                string? credential = CaptureCredential(); if (credential == null) return ProgressionNotSent(Codes.CredentialUnavailable);
                var handshake = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
                if (handshake.Value == null) { MarkProgressionStale(); return ProgressionNotSent(handshake.Code, handshake.Status); }
                if (handshake.Value.Revision != expectedRevision) { MarkProgressionStale(); return ProgressionNotSent(Codes.RefreshRequired, 200); }
                var body = new JObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["expectedRevision"] = expectedRevision };
                foreach (var property in intent.Properties()) body.Add(property.Name, property.Value.DeepClone());
                if (lifetimeToken.IsCancellationRequested) return ProgressionNotSent(Codes.Disposed);
                if (cancellation.IsCancellationRequested) return ProgressionNotSent(Codes.Canceled);
                PendingPracticeCommand pending;
                try { pending = outbox.Stage(PracticeCommandOutbox.ExpeditionRoute, Json(body)); }
                catch (PracticeOutboxException error) { ReloadPending(); return ProgressionNotSent(error.Code); }
                NotePending(pending);
                return await PostProgressionAsync(pending, null, handshake.Value, credential, cancellation).ConfigureAwait(false);
            }, code => ProgressionNotSent(code));
        }

        public Task<ProgressionMutationResult> AbandonRunAsync(PracticeUiContext context, CancellationToken cancellation = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return Exclusive(async () =>
            {
                var blocked = ProgressionPendingBlock(); if (blocked != null) return blocked;
                var current = State; var shown = current.Snapshot;
                if (shown == null) return ProgressionNotSent(Codes.NoSnapshot);
                if (current.NeedsRefresh) return ProgressionNotSent(Codes.RefreshRequired);
                if (!context.Equals(shown.Context)) return ProgressionNotSent(Codes.ContextStale);
                if (shown.Run == null) return ProgressionNotSent(Codes.NoRun);
                if (lifetimeToken.IsCancellationRequested) return ProgressionNotSent(Codes.Disposed);
                if (cancellation.IsCancellationRequested) return ProgressionNotSent(Codes.Canceled);
                string? credential = CaptureCredential(); if (credential == null) return ProgressionNotSent(Codes.CredentialUnavailable);
                var handshake = await ReadAccountAsync(credential, cancellation).ConfigureAwait(false);
                if (handshake.Value == null) return ProgressionNotSent(handshake.Code, handshake.Status);
                if (!Shows(handshake.Value, context)) { SetNeedsRefresh(); return ProgressionNotSent(Codes.RefreshRequired, 200); }
                if (lifetimeToken.IsCancellationRequested) return ProgressionNotSent(Codes.Disposed);
                if (cancellation.IsCancellationRequested) return ProgressionNotSent(Codes.Canceled);
                var body = new JObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["expectedVersion"] = shown.Run.ServerVersion };
                PendingPracticeCommand pending;
                try { pending = outbox.Stage(PracticeCommandOutbox.AbandonRoute(shown.Run.RunId), Json(body)); }
                catch (PracticeOutboxException error) { ReloadPending(); return ProgressionNotSent(error.Code); }
                NotePending(pending);
                return await PostProgressionAsync(pending, handshake.Value, null, credential, cancellation).ConfigureAwait(false);
            }, code => ProgressionNotSent(code));
        }

        /// <summary>Explicitly re-sends only an expedition/abandon record, byte for byte; another route remains pending for its own retry.</summary>
        public Task<ProgressionMutationResult> RetryProgressionPendingAsync(CancellationToken cancellation = default) =>
            Exclusive(async () =>
            {
                ReloadPending(); var current = State; var pending = current.Pending;
                if (current.OutboxCode != null) return ProgressionNotSent(current.OutboxCode);
                if (pending == null) return ProgressionNotSent(Codes.NothingPending);
                if (!PracticeCommandOutbox.IsProgressionRoute(pending.Route)) return ProgressionNotSent(Codes.PendingUnresolved);
                string? credential = CaptureCredential(); if (credential == null) return ProgressionUnresolved(pending, Codes.CredentialUnavailable);
                if (pending.Route == PracticeCommandOutbox.ExpeditionRoute)
                {
                    var handshake = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
                    if (handshake.Value == null) return ProgressionUnresolved(pending, handshake.Code, handshake.Status);
                    return await PostProgressionAsync(pending, null, handshake.Value, credential, cancellation).ConfigureAwait(false);
                }
                var account = await ReadAccountAsync(credential, cancellation).ConfigureAwait(false);
                if (account.Value == null) return ProgressionUnresolved(pending, account.Code, account.Status);
                return await PostProgressionAsync(pending, account.Value, null, credential, cancellation).ConfigureAwait(false);
            }, code => ProgressionNotSent(code));

        ProgressionMutationResult? ProgressionPendingBlock()
        {
            ReloadPending(); var state = State;
            return state.OutboxCode != null ? ProgressionNotSent(state.OutboxCode)
                : state.Pending != null ? new ProgressionMutationResult(PracticeMutationStatus.NotSent, Codes.PendingUnresolved, request: state.Pending) : null;
        }

        async Task<ProgressionMutationResult> PostProgressionAsync(PendingPracticeCommand pending, PracticeAccountView? account,
            PracticeProgressionSnapshot? profile, string credential, CancellationToken cancellation)
        {
            MarkProgressionStale();
            var exchange = await ExchangeAsync(HttpMethod.Post, pending.Route, pending.BodyUtf8(), credential, cancellation).ConfigureAwait(false);
            if (exchange.Failure != null) return ProgressionUnresolved(pending, exchange.Failure);
            if (exchange.Status == 200)
            {
                PracticeReceipt? receipt = null; PracticeAbandonReceipt? abandoned = null;
                try
                {
                    if (pending.Route == PracticeCommandOutbox.ExpeditionRoute) receipt = PracticeReceipt.Parse(pending, exchange.Body);
                    else
                    {
                        abandoned = PracticeAbandonReceipt.Parse(pending, exchange.Body);
                        if (account == null || abandoned.MemoryVersion > account.MemoryVersion) throw PracticePublicJson.Mismatch("abandon memory version");
                    }
                }
                catch (PracticeProtocolException error) { return ProgressionUnresolved(pending, error.Code, 200); }
                string? clear = Resolve(pending, true);
                return await RefreshProgressionResolutionAsync(pending, PracticeMutationStatus.Committed, Codes.Ok, 200, receipt, abandoned, clear, credential, cancellation).ConfigureAwait(false);
            }
            string? code = ServerCode(exchange.Body);
            if (code == null) return ProgressionUnresolved(pending, PracticeProtocolException.Invalid, exchange.Status);
            if (!ProgressionKnownNotCommitted(pending, account, profile, exchange.Status, code)) return ProgressionUnresolved(pending, code, exchange.Status);
            string? clearFailure = Resolve(pending, false);
            if (clearFailure != null) return new ProgressionMutationResult(PracticeMutationStatus.Pending, code, exchange.Status, pending, outboxCode: clearFailure);
            return await RefreshProgressionResolutionAsync(pending, PracticeMutationStatus.NotCommitted, code, exchange.Status, null, null, null, credential, cancellation).ConfigureAwait(false);
        }

        async Task<ProgressionMutationResult> RefreshProgressionResolutionAsync(PendingPracticeCommand pending, PracticeMutationStatus status, string code,
            int httpStatus, PracticeReceipt? receipt, PracticeAbandonReceipt? abandoned, string? outboxCode, string credential, CancellationToken cancellation)
        {
            // The account's current pointer alone chooses the run. A cached old receipt or state-less abandon acknowledgement never installs one.
            var fresh = await RefreshWithAsync(credential, cancellation).ConfigureAwait(false);
            long minimum = status == PracticeMutationStatus.Committed && pending.ExpectedRevision is long revision && revision < long.MaxValue ? revision + 1 : 0;
            var profile = await RefreshProgressionWithAsync(credential, cancellation, minimum).ConfigureAwait(false);
            return new ProgressionMutationResult(status, code, httpStatus, pending, receipt, abandoned, fresh.Value, profile.Value, outboxCode,
                fresh.Value == null ? fresh.Code : profile.Value == null ? profile.Code : null);
        }

        static bool ProgressionKnownNotCommitted(PendingPracticeCommand pending, PracticeAccountView? account, PracticeProgressionSnapshot? profile, int status, string code)
        {
            if (pending.Route == PracticeCommandOutbox.ExpeditionRoute)
            {
                if (!(pending.ExpectedRevision is long expected) || profile == null || profile.Revision < expected) return false;
                if (profile.PolicyVersion == SupportJson.Policy && status == 422 && code == "SUPPORT_IN_PARTY") return true;
                if (SupportJson.EquipmentPolicy(profile.PolicyVersion) && status == 422 && code == "NO_ELIGIBLE_WEAPON") return true;
                return status == 409 && (code == "STALE_REVISION" || code == "RUN_ACTIVE") || status == 422 &&
                    (code == "TUTORIAL_REQUIRED" || code == "TUTORIAL_DONE" || code == "CHAPTER_LOCKED" || code == "NOT_OWNED"
                    || code == "PARTY_SIZE" || code == "PARTY_DUPLICATE" || code == "OVER_CAPACITY");
            }
            if (status != 409 || account == null) return false;
            if (code == "RUN_NOT_ACTIVE") return true; // Closed run IDs are immutable and can never become current again.
            return code == "STALE_VERSION" && pending.ExpectedVersion is int version && account.Run != null
                && pending.Route == PracticeCommandOutbox.AbandonRoute(account.Run.RunId) && account.Run.ServerVersion >= version;
        }

        async Task<Read<PracticeProgressionSnapshot>> ReadProgressionAsync(string credential, CancellationToken cancellation)
        {
            var exchange = await ExchangeAsync(HttpMethod.Get, ProgressionRoute, null, credential, cancellation).ConfigureAwait(false);
            if (exchange.Failure != null) return new Read<PracticeProgressionSnapshot>(null, exchange.Failure, null);
            if (exchange.Status != 200) return new Read<PracticeProgressionSnapshot>(null, ServerCode(exchange.Body) ?? PracticeProtocolException.Invalid, exchange.Status);
            try { return new Read<PracticeProgressionSnapshot>(PracticeProgressionSnapshot.Parse(exchange.Body, this.account), Codes.Ok, 200); }
            catch (PracticeProtocolException error) { return new Read<PracticeProgressionSnapshot>(null, error.Code, 200); }
        }

        async Task<Read<PracticeProgressionSnapshot>> RefreshProgressionWithAsync(string credential, CancellationToken cancellation, long minimumRevision = 0)
        {
            var read = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
            lock (sync)
            {
                var next = read.Value;
                if (next == null) { progressionNeedsRefresh = true; return read; }
                if (next.Revision < minimumRevision || progression != null && next.Revision < progression.Revision)
                {
                    progressionNeedsRefresh = true; return new Read<PracticeProgressionSnapshot>(null, Codes.RollbackRefused, 200);
                }
                progression = next; progressionNeedsRefresh = false;
            }
            return read;
        }

        void MarkProgressionStale() { lock (sync) progressionNeedsRefresh = true; }
        static ProgressionMutationResult ProgressionNotSent(string code, int? httpStatus = null) => new ProgressionMutationResult(PracticeMutationStatus.NotSent, code, httpStatus);
        static ProgressionMutationResult ProgressionUnresolved(PendingPracticeCommand pending, string code, int? httpStatus = null) => new ProgressionMutationResult(PracticeMutationStatus.Pending, code, httpStatus, pending);
    }
}
