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
    public sealed class SupportQueryResult
    {
        internal SupportQueryResult(string code, int? status, PracticeUiContext? run, PracticeEncounterContext? encounter, PracticeSupportQueryView? view = null)
        { Code = code; HttpStatus = status; RunContext = run; EncounterContext = encounter; View = view; }
        public string Code { get; } public int? HttpStatus { get; } public PracticeUiContext? RunContext { get; } public PracticeEncounterContext? EncounterContext { get; } public PracticeSupportQueryView? View { get; }
    }
    public sealed class SupportSelectionMutationResult
    {
        internal SupportSelectionMutationResult(PracticeMutationStatus status, string code, int? httpStatus = null, PendingPracticeCommand? request = null,
            PracticeSupportSelectionReceipt? receipt = null, PracticeProgressionSnapshot? progression = null, string? outboxCode = null, string? refreshCode = null)
        { Status = status; Code = code; HttpStatus = httpStatus; Request = request; Receipt = receipt; Progression = progression; OutboxCode = outboxCode; RefreshCode = refreshCode; }
        public PracticeMutationStatus Status { get; } public string Code { get; } public int? HttpStatus { get; } public PendingPracticeCommand? Request { get; }
        public PracticeSupportSelectionReceipt? Receipt { get; } public PracticeProgressionSnapshot? Progression { get; } public string? OutboxCode { get; } public string? RefreshCode { get; }
    }
    public sealed partial class PracticeServerSession
    {
        public bool IsCurrent(SupportQueryResult result)
        {
            if (result?.View == null || result.Code != Codes.Ok) return false;
            lock (sync)
            {
                if (disposed) return false;
                if (result.RunContext != null) return !state.NeedsRefresh && state.Snapshot?.Context.Equals(result.RunContext) == true && state.Snapshot.Run?.Support?.CharId == result.View.Source.CharId && state.Snapshot.Run.Support.Used == result.View.Source.Used;
                return !encounterNeedsRefresh && !progressionNeedsRefresh && result.EncounterContext != null && encounter?.Context.Equals(result.EncounterContext) == true && encounter.Support?.CharId == result.View.Source.CharId && encounter.Support.Used == result.View.Source.Used;
            }
        }
        public Task<SupportQueryResult> SupportQueryAsync(PracticeUiContext context, string caller, string? target = null, IReadOnlyList<string>? picks = null, CancellationToken cancellation = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var copied = picks == null ? new string[0] : picks.ToArray(); var intent = SupportJson.Args(caller, target, copied);
            SupportQueryResult Fail(string code, int? status = null) => new SupportQueryResult(code, status, context, null);
            if (!SupportJson.ValidArgs(intent)) return Task.FromResult(Fail(Codes.InvalidCommand));
            return Exclusive(async () =>
            {
                var current = State; var shown = current.Snapshot; var run = shown?.Run;
                if (shown == null) return Fail(Codes.NoSnapshot);
                if (current.NeedsRefresh) return Fail(Codes.RefreshRequired);
                if (!shown.Context.Equals(context)) return Fail(Codes.ContextStale);
                if (run == null) return Fail(Codes.NoRun);
                if (run.Phase != "BATTLE" || run.BattlePhase != "PLAYER") return Fail(Codes.NoBattle);
                if (run.Support == null) return Fail("SUPPORT_NOT_SELECTED");
                string? credential = CaptureCredential(); if (credential == null) return Fail(Codes.CredentialUnavailable);
                var accountRead = await ReadAccountAsync(credential, cancellation).ConfigureAwait(false);
                if (accountRead.Value == null) { SetNeedsRefresh(); return Fail(accountRead.Code, accountRead.Status); }
                if (!Shows(accountRead.Value, context)) { SetNeedsRefresh(); return Fail(Codes.RefreshRequired, 200); }
                var body = (JObject)intent.DeepClone(); body.Add("expectedVersion", run.ServerVersion);
                var reply = await ExchangeAsync(HttpMethod.Post, PracticeCommandOutbox.RunSupportQueryRoute(run.RunId), PracticeCommandOutbox.StrictUtf8.GetBytes(Json(body)), credential, cancellation).ConfigureAwait(false);
                if (reply.Failure != null) return Fail(reply.Failure);
                if (reply.Status != 200)
                {
                    string code = ServerCode(reply.Body) ?? PracticeProtocolException.Invalid;
                    if (reply.Status == 409 && code == "STALE_VERSION" || reply.Status == 404 && code == "RUN_NOT_FOUND" || reply.Status == 422 && code == "WRONG_PHASE") SetNeedsRefresh();
                    return Fail(code, reply.Status);
                }
                try
                {
                    var view = PracticeSupportQueryView.Parse(reply.Body, context, caller, target, copied, run.Support.CharId);
                    if (view.Source.Used != run.Support.Used) throw PracticePublicJson.Mismatch("support used flag");
                    return new SupportQueryResult(Codes.Ok, 200, context, null, view);
                }
                catch (PracticeProtocolException error) { if (error.Code == PracticeProtocolException.Mismatch) SetNeedsRefresh(); return Fail(error.Code, 200); }
            }, code => Fail(code));
        }
        public Task<SupportQueryResult> EncounterSupportQueryAsync(PracticeEncounterContext context, string caller, string? target = null, IReadOnlyList<string>? picks = null, CancellationToken cancellation = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var copied = picks == null ? new string[0] : picks.ToArray(); var intent = SupportJson.Args(caller, target, copied);
            SupportQueryResult Fail(string code, int? status = null) => new SupportQueryResult(code, status, null, context);
            if (!SupportJson.ValidArgs(intent)) return Task.FromResult(Fail(Codes.InvalidCommand));
            return Exclusive(async () =>
            {
                var shown = Encounter;
                if (shown == null) return Fail(Codes.NoSnapshot);
                if (EncounterNeedsRefresh || ProgressionNeedsRefresh) return Fail(Codes.RefreshRequired);
                if (!shown.Context.Equals(context)) return Fail(Codes.ContextStale);
                if (shown.Phase != "PLAYER") return Fail(Codes.NoBattle);
                if (shown.Support == null) return Fail("SUPPORT_NOT_SELECTED");
                string? credential = CaptureCredential(); if (credential == null) return Fail(Codes.CredentialUnavailable);
                var profile = await RefreshProgressionWithAsync(credential, cancellation).ConfigureAwait(false);
                if (profile.Value == null) return Fail(profile.Code, profile.Status);
                var active = profile.Value.ActiveEncounter;
                if (active == null || active.EncounterId != context.EncounterId || active.Version != context.Version) { MarkEncounterStale(); return Fail(Codes.RefreshRequired, 200); }
                var body = (JObject)intent.DeepClone(); body.Add("expectedVersion", context.Version);
                var reply = await ExchangeAsync(HttpMethod.Post, PracticeCommandOutbox.EncounterSupportQueryRoute(context.EncounterId), PracticeCommandOutbox.StrictUtf8.GetBytes(Json(body)), credential, cancellation).ConfigureAwait(false);
                if (reply.Failure != null) return Fail(reply.Failure);
                if (reply.Status != 200)
                {
                    string code = ServerCode(reply.Body) ?? PracticeProtocolException.Invalid;
                    if (reply.Status == 409 && (code == "STALE_VERSION" || code == "ENCOUNTER_FINISHED") || reply.Status == 404 && code == "ENCOUNTER_NOT_FOUND") MarkEncounterStale();
                    return Fail(code, reply.Status);
                }
                try
                {
                    var view = PracticeSupportQueryView.Parse(reply.Body, context, caller, target, copied, shown.Support.CharId);
                    if (view.Source.Used != shown.Support.Used) throw PracticePublicJson.Mismatch("support used flag");
                    return new SupportQueryResult(Codes.Ok, 200, null, context, view);
                }
                catch (PracticeProtocolException error) { if (error.Code == PracticeProtocolException.Mismatch) MarkEncounterStale(); return Fail(error.Code, 200); }
            }, code => Fail(code));
        }
        public Task<SupportSelectionMutationResult> SelectSupportAsync(long expectedRevision, string? charId, CancellationToken cancellation = default)
        {
            if (expectedRevision < 0 || expectedRevision == long.MaxValue || charId != null && !PublicId(charId)) return Task.FromResult(SupportNotSent(Codes.InvalidCommand));
            return Exclusive(async () =>
            {
                ReloadPending(); var current = State;
                if (current.OutboxCode != null) return SupportNotSent(current.OutboxCode);
                if (current.Pending != null) return SupportNotSent(Codes.PendingUnresolved);
                var shown = Progression;
                if (shown == null) return SupportNotSent(Codes.NoSnapshot);
                if (shown.Revision != expectedRevision) return SupportNotSent(Codes.ContextStale);
                if (ProgressionNeedsRefresh) return SupportNotSent(Codes.RefreshRequired);
                string? credential = CaptureCredential(); if (credential == null) return SupportNotSent(Codes.CredentialUnavailable);
                var preflight = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
                if (preflight.Value == null) { MarkProgressionStale(); return SupportNotSent(preflight.Code, preflight.Status); }
                if (preflight.Value.Revision != expectedRevision) { MarkProgressionStale(); return SupportNotSent(Codes.RefreshRequired, 200); }
                if (lifetimeToken.IsCancellationRequested) return SupportNotSent(Codes.Disposed);
                if (cancellation.IsCancellationRequested) return SupportNotSent(Codes.Canceled);
                var body = new JObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["expectedRevision"] = expectedRevision, ["charId"] = charId == null ? (JToken)JValue.CreateNull() : charId };
                PendingPracticeCommand pending;
                try { pending = outbox.Stage(PracticeCommandOutbox.SupportSelectionRoute, Json(body)); }
                catch (PracticeOutboxException error) { ReloadPending(); return SupportNotSent(error.Code); }
                NotePending(pending); return await PostSupportSelectionAsync(pending, preflight.Value, credential, cancellation).ConfigureAwait(false);
            }, code => SupportNotSent(code));
        }
        public Task<SupportSelectionMutationResult> RetrySupportPendingAsync(CancellationToken cancellation = default) => Exclusive(async () =>
        {
            ReloadPending(); var current = State; var pending = current.Pending;
            if (current.OutboxCode != null) return SupportNotSent(current.OutboxCode);
            if (pending == null) return SupportNotSent(Codes.NothingPending);
            if (pending.Route != PracticeCommandOutbox.SupportSelectionRoute) return SupportNotSent(Codes.PendingUnresolved);
            string? credential = CaptureCredential(); if (credential == null) return SupportUnresolved(pending, Codes.CredentialUnavailable);
            var preflight = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
            if (preflight.Value == null) return SupportUnresolved(pending, preflight.Code, preflight.Status);
            return await PostSupportSelectionAsync(pending, preflight.Value, credential, cancellation).ConfigureAwait(false);
        }, code => SupportNotSent(code));
        async Task<SupportSelectionMutationResult> PostSupportSelectionAsync(PendingPracticeCommand pending, PracticeProgressionSnapshot profile, string credential, CancellationToken cancellation)
        {
            MarkProgressionStale(); var reply = await ExchangeAsync(HttpMethod.Post, pending.Route, pending.BodyUtf8(), credential, cancellation).ConfigureAwait(false);
            if (reply.Failure != null) return SupportUnresolved(pending, reply.Failure);
            PracticeSupportSelectionReceipt? receipt = null; PracticeMutationStatus status; string code;
            if (reply.Status == 200)
            {
                try { receipt = PracticeSupportSelectionReceipt.Parse(pending, reply.Body); }
                catch (PracticeProtocolException error) { return SupportUnresolved(pending, error.Code, 200); }
                status = PracticeMutationStatus.Committed; code = Codes.Ok;
            }
            else
            {
                code = ServerCode(reply.Body) ?? PracticeProtocolException.Invalid;
                if (!(pending.ExpectedRevision is long revision) || profile.Revision < revision || !(reply.Status == 409 && code == "STALE_REVISION"
                    || reply.Status == 422 && new[] { "ALREADY_SELECTED", "NOT_OWNED", "TUTORIAL_REQUIRED", "NO_ELIGIBLE_WEAPON" }.Contains(code, StringComparer.Ordinal))) return SupportUnresolved(pending, code, reply.Status);
                status = PracticeMutationStatus.NotCommitted;
            }
            string? clear = Resolve(pending, status == PracticeMutationStatus.Committed);
            if (clear != null && status == PracticeMutationStatus.NotCommitted) return new SupportSelectionMutationResult(PracticeMutationStatus.Pending, code, reply.Status, pending, outboxCode: clear);
            var fresh = await RefreshProgressionWithAsync(credential, cancellation, receipt?.Revision ?? 0).ConfigureAwait(false);
            return new SupportSelectionMutationResult(status, code, reply.Status, pending, receipt, fresh.Value, clear, fresh.Value == null ? fresh.Code : null);
        }
        static SupportSelectionMutationResult SupportNotSent(string code, int? status = null) => new SupportSelectionMutationResult(PracticeMutationStatus.NotSent, code, status);
        static SupportSelectionMutationResult SupportUnresolved(PendingPracticeCommand pending, string code, int? status = null) => new SupportSelectionMutationResult(PracticeMutationStatus.Pending, code, status, pending);
    }
}
