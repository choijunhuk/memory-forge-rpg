// Recruitment shares the run session's credential capture, HTTP framing/deadlines, exclusive operation gate and durable outbox.
#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Codes = MemoryForge.Client.PracticeSessionCodes;

namespace MemoryForge.Client
{
    public sealed class RecruitmentRefreshResult
    {
        internal RecruitmentRefreshResult(string code, int? httpStatus, RecruitmentSnapshot? snapshot) { Code = code; HttpStatus = httpStatus; Snapshot = snapshot; }
        public string Code { get; }
        public int? HttpStatus { get; }
        public RecruitmentSnapshot? Snapshot { get; }
    }

    public sealed class RecruitmentMutationResult
    {
        internal RecruitmentMutationResult(PracticeMutationStatus status, string code, int? httpStatus = null, PendingPracticeCommand? request = null,
            RecruitmentReceipt? receipt = null, RecruitmentSnapshot? snapshot = null, string? outboxCode = null, string? refreshCode = null)
        {
            Status = status; Code = code; HttpStatus = httpStatus; Request = request; Receipt = receipt; Snapshot = snapshot; OutboxCode = outboxCode; RefreshCode = refreshCode;
        }
        public PracticeMutationStatus Status { get; }
        public string Code { get; }
        public int? HttpStatus { get; }
        public PendingPracticeCommand? Request { get; }
        public RecruitmentReceipt? Receipt { get; }
        public RecruitmentSnapshot? Snapshot { get; }
        public string? OutboxCode { get; }
        public string? RefreshCode { get; }
    }

    public sealed partial class PracticeServerSession
    {
        public const string RecruitmentRoute = "/dev/practice/recruitment";
        RecruitmentSnapshot? recruitment;
        bool recruitmentNeedsRefresh = true;
        public RecruitmentSnapshot? Recruitment { get { lock (sync) return recruitment; } }
        public bool RecruitmentNeedsRefresh { get { lock (sync) return recruitmentNeedsRefresh; } }

        public Task<RecruitmentRefreshResult> RefreshRecruitmentAsync(CancellationToken cancellation = default) =>
            Exclusive(async () =>
            {
                ReloadPending();
                string? credential = CaptureCredential();
                if (credential == null) { MarkRecruitmentStale(); return new RecruitmentRefreshResult(Codes.CredentialUnavailable, null, null); }
                var read = await RefreshRecruitmentWithAsync(credential, cancellation).ConfigureAwait(false);
                return new RecruitmentRefreshResult(read.Code, read.Status, read.Value);
            }, code => new RecruitmentRefreshResult(code, null, null));

        public Task<RecruitmentMutationResult> PullRecruitmentAsync(long expectedRevision, string bannerId, int count, CancellationToken cancellation = default)
        {
            if (bannerId == null || !PracticeCommandOutbox.IsPublicId(bannerId) || count != 1 && count != 10) return Task.FromResult(RecruitmentNotSent(Codes.InvalidCommand));
            return NewRecruitmentMutation(expectedRevision, PracticeCommandOutbox.RecruitmentPullRoute, new JObject { ["bannerId"] = bannerId, ["count"] = count }, cancellation);
        }
        public Task<RecruitmentMutationResult> ClaimRecruitmentLaunchAsync(long expectedRevision, CancellationToken cancellation = default) =>
            NewRecruitmentMutation(expectedRevision, PracticeCommandOutbox.RecruitmentLaunchRoute, new JObject(), cancellation);
        public Task<RecruitmentMutationResult> ClaimRecruitmentSelectorAsync(long expectedRevision, string charId, CancellationToken cancellation = default)
        {
            if (charId == null || !PracticeCommandOutbox.IsPublicId(charId)) return Task.FromResult(RecruitmentNotSent(Codes.InvalidCommand));
            return NewRecruitmentMutation(expectedRevision, PracticeCommandOutbox.RecruitmentSelectorRoute, new JObject { ["charId"] = charId }, cancellation);
        }

        Task<RecruitmentMutationResult> NewRecruitmentMutation(long expectedRevision, string route, JObject args, CancellationToken cancellation) =>
            Exclusive(async () =>
            {
                ReloadPending();
                var current = State;
                if (current.OutboxCode != null) return RecruitmentNotSent(current.OutboxCode);
                if (current.Pending != null) return new RecruitmentMutationResult(PracticeMutationStatus.NotSent, Codes.PendingUnresolved, request: current.Pending);
                var shown = Recruitment;
                if (shown == null) return RecruitmentNotSent(Codes.NoSnapshot);
                if (expectedRevision < 0) return RecruitmentNotSent(Codes.InvalidCommand);
                if (shown.Revision != expectedRevision) return RecruitmentNotSent(Codes.ContextStale);
                if (RecruitmentNeedsRefresh) return RecruitmentNotSent(Codes.RefreshRequired);
                if (lifetimeToken.IsCancellationRequested) return RecruitmentNotSent(Codes.Disposed);
                if (cancellation.IsCancellationRequested) return RecruitmentNotSent(Codes.Canceled);
                string? credential = CaptureCredential();
                if (credential == null) return RecruitmentNotSent(Codes.CredentialUnavailable);
                var handshake = await ReadRecruitmentAsync(credential, cancellation).ConfigureAwait(false);
                if (handshake.Value == null) { MarkRecruitmentStale(); return RecruitmentNotSent(handshake.Code, handshake.Status); }
                if (handshake.Value.Revision != expectedRevision)
                {
                    MarkRecruitmentStale();
                    return RecruitmentNotSent(Codes.RefreshRequired, 200);
                }
                if (lifetimeToken.IsCancellationRequested) return RecruitmentNotSent(Codes.Disposed);
                if (cancellation.IsCancellationRequested) return RecruitmentNotSent(Codes.Canceled);
                var body = new JObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["expectedRevision"] = expectedRevision };
                foreach (var arg in args.Properties()) body.Add(arg.Name, arg.Value.DeepClone());
                PendingPracticeCommand pending;
                try { pending = outbox.Stage(route, Json(body)); }
                catch (PracticeOutboxException error) { ReloadPending(); return RecruitmentNotSent(error.Code); }
                NotePending(pending);
                return await PostRecruitmentAsync(pending, handshake.Value, credential, cancellation).ConfigureAwait(false);
            }, code => RecruitmentNotSent(code));

        public Task<RecruitmentMutationResult> RetryRecruitmentPendingAsync(CancellationToken cancellation = default) =>
            Exclusive(async () =>
            {
                ReloadPending(); var current = State; var pending = current.Pending;
                if (current.OutboxCode != null) return RecruitmentNotSent(current.OutboxCode);
                if (pending == null) return RecruitmentNotSent(Codes.NothingPending);
                if (!PracticeCommandOutbox.IsRecruitmentRoute(pending.Route)) return RecruitmentNotSent(Codes.PendingUnresolved);
                string? credential = CaptureCredential();
                if (credential == null) return RecruitmentUnresolved(pending, Codes.CredentialUnavailable);
                var handshake = await ReadRecruitmentAsync(credential, cancellation).ConfigureAwait(false);
                if (handshake.Value == null) return RecruitmentUnresolved(pending, handshake.Code, handshake.Status);
                return await PostRecruitmentAsync(pending, handshake.Value, credential, cancellation).ConfigureAwait(false);
            }, code => RecruitmentNotSent(code));

        async Task<RecruitmentMutationResult> PostRecruitmentAsync(PendingPracticeCommand pending, RecruitmentSnapshot handshake, string credential, CancellationToken cancellation)
        {
            MarkRecruitmentStale();
            var exchange = await ExchangeAsync(HttpMethod.Post, pending.Route, pending.BodyUtf8(), credential, cancellation).ConfigureAwait(false);
            if (exchange.Failure != null) return RecruitmentUnresolved(pending, exchange.Failure);
            if (exchange.Status == 200)
            {
                RecruitmentReceipt receipt;
                try { receipt = RecruitmentReceipt.Parse(pending, exchange.Body); }
                catch (PracticeProtocolException error) { return RecruitmentUnresolved(pending, error.Code, 200); }
                string? clear = Resolve(pending, true);
                var fresh = await RefreshRecruitmentWithAsync(credential, cancellation, receipt.Revision).ConfigureAwait(false);
                return new RecruitmentMutationResult(PracticeMutationStatus.Committed, Codes.Ok, 200, pending, receipt, fresh.Value, clear,
                    fresh.Value == null ? fresh.Code : null);
            }
            string? code = ServerCode(exchange.Body);
            if (code == null) return RecruitmentUnresolved(pending, PracticeProtocolException.Invalid, exchange.Status);
            // Receipt lookup precedes these refusals. Monotonic revision proof excludes a delayed request executing later.
            bool refused = pending.ExpectedRevision is long revision && handshake.Revision >= revision
                && (exchange.Status == 409 && code == "STALE_REVISION" || exchange.Status == 422);
            if (!refused) return RecruitmentUnresolved(pending, code, exchange.Status);
            string? clearFailure = Resolve(pending, false);
            if (clearFailure != null) return new RecruitmentMutationResult(PracticeMutationStatus.Pending, code, exchange.Status, pending, outboxCode: clearFailure);
            var refreshed = await RefreshRecruitmentWithAsync(credential, cancellation).ConfigureAwait(false);
            return new RecruitmentMutationResult(PracticeMutationStatus.NotCommitted, code, exchange.Status, pending, snapshot: refreshed.Value,
                refreshCode: refreshed.Value == null ? refreshed.Code : null);
        }

        async Task<Read<RecruitmentSnapshot>> ReadRecruitmentAsync(string credential, CancellationToken cancellation)
        {
            var exchange = await ExchangeAsync(HttpMethod.Get, RecruitmentRoute, null, credential, cancellation).ConfigureAwait(false);
            if (exchange.Failure != null) return new Read<RecruitmentSnapshot>(null, exchange.Failure, null);
            if (exchange.Status != 200) return new Read<RecruitmentSnapshot>(null, ServerCode(exchange.Body) ?? PracticeProtocolException.Invalid, exchange.Status);
            try { return new Read<RecruitmentSnapshot>(RecruitmentSnapshot.Parse(exchange.Body, account), Codes.Ok, 200); }
            catch (PracticeProtocolException error) { return new Read<RecruitmentSnapshot>(null, error.Code, 200); }
        }

        async Task<Read<RecruitmentSnapshot>> RefreshRecruitmentWithAsync(string credential, CancellationToken cancellation, long minimumRevision = 0)
        {
            var read = await ReadRecruitmentAsync(credential, cancellation).ConfigureAwait(false);
            lock (sync)
            {
                var next = read.Value;
                if (next == null) { recruitmentNeedsRefresh = true; return read; }
                if (next.Revision < minimumRevision || recruitment != null && next.Revision < recruitment.Revision)
                {
                    recruitmentNeedsRefresh = true;
                    return new Read<RecruitmentSnapshot>(null, Codes.RollbackRefused, 200);
                }
                recruitment = next; recruitmentNeedsRefresh = false;
            }
            return read;
        }

        void MarkRecruitmentStale() { lock (sync) recruitmentNeedsRefresh = true; }
        static RecruitmentMutationResult RecruitmentNotSent(string code, int? status = null) => new RecruitmentMutationResult(PracticeMutationStatus.NotSent, code, status);
        static RecruitmentMutationResult RecruitmentUnresolved(PendingPracticeCommand pending, string code, int? status = null) =>
            new RecruitmentMutationResult(PracticeMutationStatus.Pending, code, status, pending);
    }
}
