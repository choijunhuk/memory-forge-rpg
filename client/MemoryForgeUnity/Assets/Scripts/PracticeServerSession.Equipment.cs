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
    public sealed class EquipmentPreviewResult
    {
        internal EquipmentPreviewResult(string code, int? status = null, PracticeEquipmentPreview? view = null) { Code = code; HttpStatus = status; View = view; }
        public string Code { get; } public int? HttpStatus { get; } public PracticeEquipmentPreview? View { get; }
    }
    public sealed class EquipmentMutationResult
    {
        internal EquipmentMutationResult(PracticeMutationStatus status, string code, int? httpStatus = null, PendingPracticeCommand? request = null,
            PracticeEquipmentReceipt? receipt = null, PracticeProgressionSnapshot? progression = null, string? outboxCode = null, string? refreshCode = null)
        { Status = status; Code = code; HttpStatus = httpStatus; Request = request; Receipt = receipt; Progression = progression; OutboxCode = outboxCode; RefreshCode = refreshCode; }
        public PracticeMutationStatus Status { get; } public string Code { get; } public int? HttpStatus { get; } public PendingPracticeCommand? Request { get; }
        public PracticeEquipmentReceipt? Receipt { get; } public PracticeProgressionSnapshot? Progression { get; } public string? OutboxCode { get; } public string? RefreshCode { get; }
    }
    public sealed partial class PracticeServerSession
    {
        static bool EquipmentIntent(string kind, string? target, JObject intent)
        {
            if (!EquipmentJson.Intent(kind, intent)) return false;
            return kind == "bossExchange" ? target == null : PublicId(target);
        }
        public Task<EquipmentPreviewResult> PreviewEquipmentAsync(long expectedRevision, string kind, string? target, JObject? intent = null, CancellationToken cancellation = default)
        {
            var args = intent == null ? new JObject() : (JObject)intent.DeepClone();
            if (expectedRevision < 0 || expectedRevision == long.MaxValue || !EquipmentIntent(kind, target, args)) return Task.FromResult(new EquipmentPreviewResult(Codes.InvalidCommand));
            return Exclusive(async () =>
            {
                string? credential = CaptureCredential(); if (credential == null) return new EquipmentPreviewResult(Codes.CredentialUnavailable);
                var profile = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
                if (profile.Value == null) return new EquipmentPreviewResult(profile.Code, profile.Status);
                if (profile.Value.Revision != expectedRevision) { MarkProgressionStale(); return new EquipmentPreviewResult(Codes.RefreshRequired, 200); }
                var body = (JObject)args.DeepClone(); body.Add("expectedRevision", expectedRevision);
                var exchange = await ExchangeAsync(HttpMethod.Post, EquipmentJson.Route(kind, target, true), PracticeCommandOutbox.StrictUtf8.GetBytes(Json(body)), credential, cancellation).ConfigureAwait(false);
                if (exchange.Failure != null) return new EquipmentPreviewResult(exchange.Failure);
                if (exchange.Status != 200) return new EquipmentPreviewResult(ServerCode(exchange.Body) ?? PracticeProtocolException.Invalid, exchange.Status);
                try { return new EquipmentPreviewResult(Codes.Ok, 200, PracticeEquipmentPreview.Parse(exchange.Body, expectedRevision, kind, target, args)); }
                catch (PracticeProtocolException error) { return new EquipmentPreviewResult(error.Code, 200); }
            }, code => new EquipmentPreviewResult(code));
        }
        public Task<EquipmentMutationResult> CommitEquipmentAsync(long expectedRevision, string kind, string? target, JObject? intent = null, CancellationToken cancellation = default)
        {
            var args = intent == null ? new JObject() : (JObject)intent.DeepClone();
            if (expectedRevision < 0 || expectedRevision == long.MaxValue || !EquipmentIntent(kind, target, args)) return Task.FromResult(EquipmentNotSent(Codes.InvalidCommand));
            return Exclusive(async () =>
            {
                ReloadPending(); var current = State;
                if (current.OutboxCode != null) return EquipmentNotSent(current.OutboxCode);
                if (current.Pending != null) return EquipmentNotSent(Codes.PendingUnresolved);
                var shown = Progression;
                if (shown == null) return EquipmentNotSent(Codes.NoSnapshot);
                if (shown.Revision != expectedRevision) return EquipmentNotSent(Codes.ContextStale);
                if (ProgressionNeedsRefresh) return EquipmentNotSent(Codes.RefreshRequired);
                string? credential = CaptureCredential(); if (credential == null) return EquipmentNotSent(Codes.CredentialUnavailable);
                var profile = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
                if (profile.Value == null) { MarkProgressionStale(); return EquipmentNotSent(profile.Code, profile.Status); }
                if (profile.Value.Revision != expectedRevision) { MarkProgressionStale(); return EquipmentNotSent(Codes.RefreshRequired, 200); }
                if (lifetimeToken.IsCancellationRequested) return EquipmentNotSent(Codes.Disposed);
                if (cancellation.IsCancellationRequested) return EquipmentNotSent(Codes.Canceled);
                var body = new JObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["expectedRevision"] = expectedRevision };
                foreach (var p in args.Properties()) body.Add(p.Name, p.Value.DeepClone());
                PendingPracticeCommand pending;
                try { pending = outbox.Stage(EquipmentJson.Route(kind, target), Json(body)); }
                catch (PracticeOutboxException error) { ReloadPending(); return EquipmentNotSent(error.Code); }
                NotePending(pending);
                return await PostEquipmentAsync(pending, profile.Value, credential, cancellation).ConfigureAwait(false);
            }, code => EquipmentNotSent(code));
        }
        public Task<EquipmentMutationResult> RetryEquipmentPendingAsync(CancellationToken cancellation = default) => Exclusive(async () =>
        {
            ReloadPending(); var current = State; var pending = current.Pending;
            if (current.OutboxCode != null) return EquipmentNotSent(current.OutboxCode);
            if (pending == null) return EquipmentNotSent(Codes.NothingPending);
            if (!PracticeCommandOutbox.IsEquipmentRoute(pending.Route)) return EquipmentNotSent(Codes.PendingUnresolved);
            string? credential = CaptureCredential(); if (credential == null) return EquipmentUnresolved(pending, Codes.CredentialUnavailable);
            var profile = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
            if (profile.Value == null) return EquipmentUnresolved(pending, profile.Code, profile.Status);
            return await PostEquipmentAsync(pending, profile.Value, credential, cancellation).ConfigureAwait(false);
        }, code => EquipmentNotSent(code));

        async Task<EquipmentMutationResult> PostEquipmentAsync(PendingPracticeCommand pending, PracticeProgressionSnapshot profile, string credential, CancellationToken cancellation)
        {
            MarkProgressionStale();
            var exchange = await ExchangeAsync(HttpMethod.Post, pending.Route, pending.BodyUtf8(), credential, cancellation).ConfigureAwait(false);
            if (exchange.Failure != null) return EquipmentUnresolved(pending, exchange.Failure);
            PracticeEquipmentReceipt? receipt = null; PracticeMutationStatus status; string code;
            if (exchange.Status == 200)
            {
                try { receipt = PracticeEquipmentReceipt.Parse(pending, exchange.Body); }
                catch (PracticeProtocolException error) { return EquipmentUnresolved(pending, error.Code, 200); }
                status = PracticeMutationStatus.Committed; code = Codes.Ok;
            }
            else
            {
                string? serverCode = ServerCode(exchange.Body);
                if (serverCode == null) return EquipmentUnresolved(pending, PracticeProtocolException.Invalid, exchange.Status);
                if (!EquipmentKnownNotCommitted(pending, profile, exchange.Status, serverCode)) return EquipmentUnresolved(pending, serverCode, exchange.Status);
                status = PracticeMutationStatus.NotCommitted; code = serverCode;
            }
            string? clear = Resolve(pending, status == PracticeMutationStatus.Committed);
            if (clear != null && status == PracticeMutationStatus.NotCommitted) return new EquipmentMutationResult(PracticeMutationStatus.Pending, code, exchange.Status, pending, outboxCode: clear);
            var fresh = await RefreshProgressionWithAsync(credential, cancellation, receipt?.Revision ?? 0).ConfigureAwait(false);
            return new EquipmentMutationResult(status, code, exchange.Status, pending, receipt, fresh.Value, clear, fresh.Value == null ? fresh.Code : null);
        }
        static bool EquipmentKnownNotCommitted(PendingPracticeCommand pending, PracticeProgressionSnapshot profile, int status, string code)
        {
            string? kind = EquipmentJson.Kind(pending.Route, out _);
            if (kind == null || !(pending.ExpectedRevision is long revision) || profile.Revision < revision) return false;
            if (status == 409) return code == "STALE_REVISION";
            if (status != 422) return false;
            if (code == "TUTORIAL_REQUIRED" || code == "INSUFFICIENT_RESOURCES" || code == "RESOURCE_OVERFLOW") return true;
            if (kind == "bossExchange") return code == "INVALID_EXCHANGE" || code == "EXCHANGE_LIMIT";
            if (kind.StartsWith("weapon", StringComparison.Ordinal))
            {
                if (code == "WEAPON_NOT_OWNED") return true;
                if (kind == "weaponEquip") return code == "NOT_OWNED" || code == "WEAPON_TYPE_MISMATCH" || code == "ALREADY_EQUIPPED";
                if (kind == "weaponLevelUp") return code == "LEVEL_CAP" || code == "INVALID_TARGET_LEVEL";
                if (kind == "weaponAscend") return code == "ASCENSION_CAP" || code == "ASCENSION_LEVEL_REQUIRED";
                return code == "REFINE_CAP" || code == "AUTO_REFINED";
            }
            if (code == "NOT_OWNED") return true;
            if (kind == "characterAscend") return code == "ASCENSION_CAP" || code == "ASCENSION_LEVEL_REQUIRED" || code == "ACCOUNT_LEVEL_REQUIRED";
            if (kind == "skillUp") return code == "SKILL_CAP" || code == "INVALID_SKILL_LINE";
            if (code == "INVALID_GEAR_SLOT") return true;
            return kind == "gearCraft" ? code == "ACCOUNT_LEVEL_REQUIRED" || code == "GEAR_ALREADY_CRAFTED" : code == "GEAR_NOT_CRAFTED" || code == "GEAR_CAP";
        }
        static EquipmentMutationResult EquipmentNotSent(string code, int? status = null) => new EquipmentMutationResult(PracticeMutationStatus.NotSent, code, status);
        static EquipmentMutationResult EquipmentUnresolved(PendingPracticeCommand pending, string code, int? status = null) => new EquipmentMutationResult(PracticeMutationStatus.Pending, code, status, pending);

        public Task<GrowthMutationResult> StartWeeklyBossAsync(long expectedRevision, string bossId, int difficulty, bool practice, IReadOnlyList<string> party, CancellationToken cancellation = default)
        {
            var ordered = party?.ToArray();
            if (!new[] { "b_bell", "b_notary", "b_garden", "b_archivist" }.Contains(bossId, StringComparer.Ordinal) || difficulty < 1 || difficulty > 5
                || ordered == null || ordered.Length != 3 || ordered.Any(id => !PublicId(id)) || ordered.Distinct(StringComparer.Ordinal).Count() != 3) return Task.FromResult(GrowthNotSent(Codes.InvalidCommand));
            return NewGrowthRevisionAsync(expectedRevision, PracticeCommandOutbox.WeeklyBossStartRoute,
                new JObject { ["bossId"] = bossId, ["difficulty"] = difficulty, ["practice"] = practice, ["party"] = new JArray(ordered) }, cancellation);
        }
    }
}
