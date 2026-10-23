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
    public sealed class EncounterRefreshResult
    {
        internal EncounterRefreshResult(string code, int? status, PracticeEncounterSnapshot? snapshot) { Code = code; HttpStatus = status; Snapshot = snapshot; }
        public string Code { get; }
        public int? HttpStatus { get; }
        public PracticeEncounterSnapshot? Snapshot { get; }
    }

    public sealed class GrowthMutationResult
    {
        readonly JArray? events;
        internal GrowthMutationResult(PracticeMutationStatus status, string code, int? httpStatus = null, PendingPracticeCommand? request = null,
            PracticeGrowthReceipt? receipt = null, PracticeEncounterSnapshot? snapshot = null, PracticeProgressionSnapshot? progression = null,
            string? outboxCode = null, string? refreshCode = null, JArray? events = null)
        { Status = status; Code = code; HttpStatus = httpStatus; Request = request; Receipt = receipt; Snapshot = snapshot; Progression = progression; OutboxCode = outboxCode; RefreshCode = refreshCode; this.events = events; }
        public PracticeMutationStatus Status { get; }
        public string Code { get; }
        public int? HttpStatus { get; }
        public PendingPracticeCommand? Request { get; }
        public PracticeGrowthReceipt? Receipt { get; }
        public PracticeEncounterSnapshot? Snapshot { get; }
        public PracticeProgressionSnapshot? Progression { get; }
        public string? OutboxCode { get; }
        public string? RefreshCode { get; }
        public JArray? PresentableEvents() => events == null ? null : (JArray)events.DeepClone();
    }

    public sealed class EncounterBattleQueryResult
    {
        internal EncounterBattleQueryResult(string code, int? status, PracticeEncounterContext context, PracticeEncounterBattleQueryView? view) { Code = code; HttpStatus = status; Context = context; View = view; }
        public string Code { get; }
        public int? HttpStatus { get; }
        public PracticeEncounterContext Context { get; }
        public PracticeEncounterBattleQueryView? View { get; }
    }

    public sealed class CharacterLevelPreviewResult
    {
        internal CharacterLevelPreviewResult(string code, int? status, PracticeCharacterLevelPreview? view) { Code = code; HttpStatus = status; View = view; }
        public string Code { get; }
        public int? HttpStatus { get; }
        public PracticeCharacterLevelPreview? View { get; }
    }

    public sealed partial class PracticeServerSession
    {
        PracticeEncounterSnapshot? encounter;
        bool encounterNeedsRefresh = true;
        public PracticeEncounterSnapshot? Encounter { get { lock (sync) return encounter; } }
        public bool EncounterNeedsRefresh { get { lock (sync) return encounterNeedsRefresh; } }
        public bool CanMutateEncounter
        {
            get
            {
                lock (sync)
                {
                    var active = progression?.ActiveEncounter;
                    return !disposed && !progressionNeedsRefresh && encounter != null && !encounterNeedsRefresh && encounter.Phase == "PLAYER" && active != null
                        && active.EncounterId == encounter.EncounterId && active.Version == encounter.Version
                        && state.Pending == null && state.OutboxCode == null && !state.InFlight;
                }
            }
        }
        public bool IsCurrent(EncounterBattleQueryResult result) => result != null && Encounter?.Context.Equals(result.Context) == true && !EncounterNeedsRefresh;

        public Task<EncounterRefreshResult> RefreshEncounterAsync(string encounterId, CancellationToken cancellation = default)
        {
            if (!PracticeCommandOutbox.IsEncounterId(encounterId)) return Task.FromResult(new EncounterRefreshResult(Codes.InvalidCommand, null, null));
            return Exclusive(async () =>
            {
                ReloadPending(); string? credential = CaptureCredential();
                if (credential == null) { MarkEncounterStale(); return new EncounterRefreshResult(Codes.CredentialUnavailable, null, null); }
                var profile = await RefreshProgressionWithAsync(credential, cancellation).ConfigureAwait(false);
                if (profile.Value == null) { MarkEncounterStale(); return new EncounterRefreshResult(profile.Code, profile.Status, null); }
                var read = await RefreshEncounterWithAsync(credential, encounterId, profile.Value, cancellation).ConfigureAwait(false);
                return new EncounterRefreshResult(read.Code, read.Status, read.Value);
            }, code => new EncounterRefreshResult(code, null, null));
        }

        public Task<GrowthMutationResult> StartEncounterAsync(long expectedRevision, string dungeonId, int tier, IReadOnlyList<string> party, CancellationToken cancellation = default)
        {
            var ordered = party?.ToArray();
            if (!PublicId(dungeonId) || tier < 1 || tier > 6 || ordered == null || ordered.Length != 3 || ordered.Any(id => !PublicId(id)) || ordered.Distinct(StringComparer.Ordinal).Count() != 3)
                return Task.FromResult(GrowthNotSent(Codes.InvalidCommand));
            return NewGrowthRevisionAsync(expectedRevision, PracticeCommandOutbox.StartEncounterRoute, new JObject { ["dungeonId"] = dungeonId, ["tier"] = tier, ["party"] = new JArray(ordered) }, cancellation);
        }

        public Task<GrowthMutationResult> SweepDungeonAsync(long expectedRevision, string dungeonId, int tier, int times, CancellationToken cancellation = default)
        {
            if (!PublicId(dungeonId) || tier < 1 || tier > 6 || times < 1 || times > 12) return Task.FromResult(GrowthNotSent(Codes.InvalidCommand));
            return NewGrowthRevisionAsync(expectedRevision, PracticeCommandOutbox.DungeonSweepRoute, new JObject { ["dungeonId"] = dungeonId, ["tier"] = tier, ["times"] = times }, cancellation);
        }

        public Task<GrowthMutationResult> LevelUpCharacterAsync(long expectedRevision, string charId, int targetLevel, CancellationToken cancellation = default)
        {
            if (!PublicId(charId) || targetLevel < 1 || targetLevel > 60) return Task.FromResult(GrowthNotSent(Codes.InvalidCommand));
            return NewGrowthRevisionAsync(expectedRevision, PracticeCommandOutbox.CharacterLevelUpRoute(charId), new JObject { ["targetLevel"] = targetLevel }, cancellation);
        }

        Task<GrowthMutationResult> NewGrowthRevisionAsync(long expectedRevision, string route, JObject intent, CancellationToken cancellation)
        {
            if (expectedRevision < 0 || expectedRevision == long.MaxValue) return Task.FromResult(GrowthNotSent(Codes.InvalidCommand));
            return Exclusive(async () =>
            {
                var blocked = GrowthPendingBlock(); if (blocked != null) return blocked;
                var shown = Progression;
                if (shown == null) return GrowthNotSent(Codes.NoSnapshot);
                if (shown.Revision != expectedRevision) return GrowthNotSent(Codes.ContextStale);
                if (ProgressionNeedsRefresh) return GrowthNotSent(Codes.RefreshRequired);
                string? credential = CaptureCredential(); if (credential == null) return GrowthNotSent(Codes.CredentialUnavailable);
                var profile = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
                if (profile.Value == null) { MarkProgressionStale(); return GrowthNotSent(profile.Code, profile.Status); }
                if (profile.Value.Revision != expectedRevision) { MarkProgressionStale(); return GrowthNotSent(Codes.RefreshRequired, 200); }
                if (lifetimeToken.IsCancellationRequested) return GrowthNotSent(Codes.Disposed);
                if (cancellation.IsCancellationRequested) return GrowthNotSent(Codes.Canceled);
                var body = new JObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["expectedRevision"] = expectedRevision };
                foreach (var property in intent.Properties()) body.Add(property.Name, property.Value.DeepClone());
                PendingPracticeCommand pending;
                try { pending = outbox.Stage(route, Json(body)); } catch (PracticeOutboxException error) { ReloadPending(); return GrowthNotSent(error.Code); }
                NotePending(pending);
                return await PostGrowthAsync(pending, profile.Value, null, credential, Encounter, cancellation).ConfigureAwait(false);
            }, code => GrowthNotSent(code));
        }

        public Task<GrowthMutationResult> EncounterCommandAsync(PracticeEncounterContext context, string kind, JObject? args = null, CancellationToken cancellation = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var copied = args == null ? new JObject() : (JObject)args.DeepClone();
            if (context.Version < 0 || context.Version >= int.MaxValue || !PracticeCommandOutbox.ValidEncounterArgs(kind, copied)) return Task.FromResult(GrowthNotSent(Codes.InvalidCommand));
            return Exclusive(async () =>
            {
                var blocked = GrowthPendingBlock(); if (blocked != null) return blocked;
                var shown = Encounter;
                if (shown == null) return GrowthNotSent(Codes.NoSnapshot);
                if (EncounterNeedsRefresh) return GrowthNotSent(Codes.RefreshRequired);
                if (!shown.Context.Equals(context)) return GrowthNotSent(Codes.ContextStale);
                string? credential = CaptureCredential(); if (credential == null) return GrowthNotSent(Codes.CredentialUnavailable);
                var profile = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
                if (profile.Value == null) return GrowthNotSent(profile.Code, profile.Status);
                var active = profile.Value.ActiveEncounter;
                if (active == null || active.EncounterId != context.EncounterId || active.Version != context.Version) { MarkEncounterStale(); return GrowthNotSent(Codes.RefreshRequired, 200); }
                var before = await ReadEncounterAsync(credential, context.EncounterId, cancellation).ConfigureAwait(false);
                if (before.Value == null) { MarkEncounterStale(); return GrowthNotSent(before.Code, before.Status); }
                if (!before.Value.Context.Equals(context)) { MarkEncounterStale(); return GrowthNotSent(Codes.RefreshRequired, 200); }
                if (lifetimeToken.IsCancellationRequested) return GrowthNotSent(Codes.Disposed);
                if (cancellation.IsCancellationRequested) return GrowthNotSent(Codes.Canceled);
                var body = new JObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["expectedVersion"] = context.Version, ["kind"] = kind, ["args"] = copied };
                PendingPracticeCommand pending;
                try { pending = outbox.Stage(PracticeCommandOutbox.EncounterCommandRoute(context.EncounterId), Json(body)); } catch (PracticeOutboxException error) { ReloadPending(); return GrowthNotSent(error.Code); }
                NotePending(pending);
                return await PostGrowthAsync(pending, profile.Value, before.Value, credential, shown, cancellation).ConfigureAwait(false);
            }, code => GrowthNotSent(code));
        }

        public Task<GrowthMutationResult> RetryGrowthPendingAsync(CancellationToken cancellation = default) => Exclusive(async () =>
        {
            ReloadPending(); var current = State; var pending = current.Pending;
            if (current.OutboxCode != null) return GrowthNotSent(current.OutboxCode);
            if (pending == null) return GrowthNotSent(Codes.NothingPending);
            if (!PracticeCommandOutbox.IsGrowthRoute(pending.Route)) return GrowthNotSent(Codes.PendingUnresolved);
            string? credential = CaptureCredential(); if (credential == null) return GrowthUnresolved(pending, Codes.CredentialUnavailable);
            var profile = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
            if (profile.Value == null) return GrowthUnresolved(pending, profile.Code, profile.Status);
            PracticeEncounterSnapshot? before = null;
            if (PracticeCommandOutbox.IsEncounterCommandRoute(pending.Route))
            {
                var read = await ReadEncounterAsync(credential, pending.Route.Substring(PracticeCommandOutbox.StartEncounterRoute.Length + 1, 35), cancellation).ConfigureAwait(false);
                if (read.Value == null) return GrowthUnresolved(pending, read.Code, read.Status);
                before = read.Value;
            }
            return await PostGrowthAsync(pending, profile.Value, before, credential, Encounter, cancellation).ConfigureAwait(false);
        }, code => GrowthNotSent(code));

        public Task<CharacterLevelPreviewResult> PreviewCharacterLevelAsync(long expectedRevision, string charId, int targetLevel, CancellationToken cancellation = default)
        {
            if (expectedRevision < 0 || !PublicId(charId) || targetLevel < 1 || targetLevel > 60) return Task.FromResult(new CharacterLevelPreviewResult(Codes.InvalidCommand, null, null));
            return Exclusive(async () =>
            {
                string? credential = CaptureCredential(); if (credential == null) return new CharacterLevelPreviewResult(Codes.CredentialUnavailable, null, null);
                var profile = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
                if (profile.Value == null) return new CharacterLevelPreviewResult(profile.Code, profile.Status, null);
                if (profile.Value.Revision != expectedRevision) { MarkProgressionStale(); return new CharacterLevelPreviewResult(Codes.RefreshRequired, 200, null); }
                var body = new JObject { ["expectedRevision"] = expectedRevision, ["targetLevel"] = targetLevel };
                var exchange = await ExchangeAsync(HttpMethod.Post, PracticeCommandOutbox.CharacterLevelPreviewRoute(charId), PracticeCommandOutbox.StrictUtf8.GetBytes(Json(body)), credential, cancellation).ConfigureAwait(false);
                if (exchange.Failure != null) return new CharacterLevelPreviewResult(exchange.Failure, null, null);
                if (exchange.Status != 200) return new CharacterLevelPreviewResult(ServerCode(exchange.Body) ?? PracticeProtocolException.Invalid, exchange.Status, null);
                try { return new CharacterLevelPreviewResult(Codes.Ok, 200, PracticeCharacterLevelPreview.Parse(exchange.Body, expectedRevision, charId, targetLevel)); }
                catch (PracticeProtocolException error) { return new CharacterLevelPreviewResult(error.Code, 200, null); }
            }, code => new CharacterLevelPreviewResult(code, null, null));
        }

        public Task<EncounterBattleQueryResult> EncounterBattleQueryAsync(PracticeEncounterContext context, string uid, string mode = "now", string? target = null,
            IReadOnlyList<string>? picks = null, CancellationToken cancellation = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var copied = picks == null ? new string[0] : picks.ToArray();
            var args = new JObject { ["uid"] = uid, ["mode"] = mode, ["picks"] = new JArray(copied) }; if (target != null) args["target"] = target;
            if (context.Version < 0 || context.Version > int.MaxValue || !PracticeCommandOutbox.ValidEncounterArgs("play", args)) return Task.FromResult(new EncounterBattleQueryResult(Codes.InvalidCommand, null, context, null));
            return Exclusive(async () =>
            {
                var shown = Encounter;
                if (shown == null) return new EncounterBattleQueryResult(Codes.NoSnapshot, null, context, null);
                if (EncounterNeedsRefresh || !shown.Context.Equals(context)) return new EncounterBattleQueryResult(Codes.ContextStale, null, context, null);
                string? credential = CaptureCredential(); if (credential == null) return new EncounterBattleQueryResult(Codes.CredentialUnavailable, null, context, null);
                var profile = await ReadProgressionAsync(credential, cancellation).ConfigureAwait(false);
                if (profile.Value == null) return new EncounterBattleQueryResult(profile.Code, profile.Status, context, null);
                var active = profile.Value.ActiveEncounter;
                if (active == null || active.EncounterId != context.EncounterId || active.Version != context.Version) { MarkEncounterStale(); return new EncounterBattleQueryResult(Codes.RefreshRequired, 200, context, null); }
                var body = (JObject)args.DeepClone(); body["expectedVersion"] = context.Version;
                var exchange = await ExchangeAsync(HttpMethod.Post, PracticeCommandOutbox.EncounterBattleQueryRoute(context.EncounterId), PracticeCommandOutbox.StrictUtf8.GetBytes(Json(body)), credential, cancellation).ConfigureAwait(false);
                if (exchange.Failure != null) return new EncounterBattleQueryResult(exchange.Failure, null, context, null);
                if (exchange.Status != 200)
                {
                    string code = ServerCode(exchange.Body) ?? PracticeProtocolException.Invalid;
                    if (exchange.Status == 409 && code == "STALE_VERSION" || exchange.Status == 404 && code == "ENCOUNTER_NOT_FOUND") MarkEncounterStale();
                    return new EncounterBattleQueryResult(code, exchange.Status, context, null);
                }
                try { return new EncounterBattleQueryResult(Codes.Ok, 200, context, PracticeEncounterBattleQueryView.Parse(exchange.Body, context, uid, mode, target, copied)); }
                catch (PracticeProtocolException error) { return new EncounterBattleQueryResult(error.Code, 200, context, null); }
            }, code => new EncounterBattleQueryResult(code, null, context, null));
        }

        GrowthMutationResult? GrowthPendingBlock()
        {
            ReloadPending(); var current = State;
            return current.OutboxCode != null ? GrowthNotSent(current.OutboxCode) : current.Pending != null ? new GrowthMutationResult(PracticeMutationStatus.NotSent, Codes.PendingUnresolved, request: current.Pending) : null;
        }

        async Task<GrowthMutationResult> PostGrowthAsync(PendingPracticeCommand pending, PracticeProgressionSnapshot profile, PracticeEncounterSnapshot? before,
            string credential, PracticeEncounterSnapshot? shown, CancellationToken cancellation)
        {
            MarkProgressionStale(); MarkEncounterStale();
            var exchange = await ExchangeAsync(HttpMethod.Post, pending.Route, pending.BodyUtf8(), credential, cancellation).ConfigureAwait(false);
            if (exchange.Failure != null) return GrowthUnresolved(pending, exchange.Failure);
            if (exchange.Status == 200)
            {
                PracticeGrowthReceipt receipt;
                try
                {
                    receipt = PracticeGrowthReceipt.Parse(pending, exchange.Body);
                    if (before != null && receipt.Encounter != null && (before.Kind != receipt.Encounter.Kind || before.BossId != receipt.Encounter.BossId
                        || before.Difficulty != receipt.Encounter.Difficulty || before.Practice != receipt.Encounter.Practice)) throw PracticePublicJson.Mismatch("encounter kind");
                    if (before != null && before.Support != null && before.Support.CharId != receipt.Encounter?.Support?.CharId)
                        throw PracticePublicJson.Mismatch("frozen encounter support");
                }
                catch (PracticeProtocolException error) { return GrowthUnresolved(pending, error.Code, 200); }
                string? clear = Resolve(pending, true);
                return await RefreshGrowthResolutionAsync(pending, PracticeMutationStatus.Committed, Codes.Ok, 200, receipt, clear, credential, shown, cancellation).ConfigureAwait(false);
            }
            string? code = ServerCode(exchange.Body);
            if (code == null) return GrowthUnresolved(pending, PracticeProtocolException.Invalid, exchange.Status);
            if (!GrowthKnownNotCommitted(pending, profile, before, exchange.Status, code)) return GrowthUnresolved(pending, code, exchange.Status);
            string? clearFailure = Resolve(pending, false);
            if (clearFailure != null) return new GrowthMutationResult(PracticeMutationStatus.Pending, code, exchange.Status, pending, outboxCode: clearFailure);
            return await RefreshGrowthResolutionAsync(pending, PracticeMutationStatus.NotCommitted, code, exchange.Status, null, null, credential, shown, cancellation).ConfigureAwait(false);
        }

        async Task<GrowthMutationResult> RefreshGrowthResolutionAsync(PendingPracticeCommand pending, PracticeMutationStatus status, string code, int httpStatus,
            PracticeGrowthReceipt? receipt, string? outboxCode, string credential, PracticeEncounterSnapshot? shown, CancellationToken cancellation)
        {
            var profile = await RefreshProgressionWithAsync(credential, cancellation, receipt?.Revision ?? 0).ConfigureAwait(false);
            if (profile.Value == null) return new GrowthMutationResult(status, code, httpStatus, pending, receipt, outboxCode: outboxCode, refreshCode: profile.Code);
            string? id = profile.Value.ActiveEncounter?.EncounterId ?? receipt?.Encounter?.EncounterId;
            PracticeEncounterSnapshot? snapshot = null; string? refreshCode = null;
            if (id != null)
            {
                var fresh = await RefreshEncounterWithAsync(credential, id, profile.Value, cancellation).ConfigureAwait(false); snapshot = fresh.Value;
                if (snapshot == null) refreshCode = fresh.Code;
            }
            else { lock (sync) { encounter = null; encounterNeedsRefresh = false; } }
            var prior = shown; var applied = snapshot; var historical = receipt?.Encounter;
            JArray? events = prior != null && applied != null && historical != null && prior.EncounterId == historical.EncounterId && applied.EncounterId == historical.EncounterId
                && prior.Version + 1 == historical.Version && applied.Version == historical.Version ? historical.EventsJArray() : null;
            return new GrowthMutationResult(status, code, httpStatus, pending, receipt, snapshot, profile.Value, outboxCode, refreshCode, events);
        }

        static bool GrowthKnownNotCommitted(PendingPracticeCommand pending, PracticeProgressionSnapshot profile, PracticeEncounterSnapshot? before, int status, string code)
        {
            if (PracticeCommandOutbox.IsEncounterCommandRoute(pending.Route))
                return (!SupportJson.IsSupportCommand(pending) || status != 422 || SupportJson.KnownCommandRefusal(code)) && pending.ExpectedVersion is int expected && before != null && pending.Route == PracticeCommandOutbox.EncounterCommandRoute(before.EncounterId) && before.Version >= expected
                    && (status == 409 && (code == "STALE_VERSION" || code == "ENCOUNTER_FINISHED") || status == 422);
            if (!(pending.ExpectedRevision is long revision) || profile.Revision < revision) return false;
            if ((pending.Route == PracticeCommandOutbox.StartEncounterRoute || pending.Route == PracticeCommandOutbox.WeeklyBossStartRoute) && profile.PolicyVersion == SupportJson.Policy && status == 422 && code == "SUPPORT_IN_PARTY") return true;
            if (pending.Route == PracticeCommandOutbox.WeeklyBossStartRoute)
                return status == 409 && (code == "STALE_REVISION" || code == "RUN_ACTIVE") || status == 422 &&
                    new[] { "TUTORIAL_REQUIRED", "PARTY_SIZE", "PARTY_DUPLICATE", "NOT_OWNED", "NO_ELIGIBLE_WEAPON", "OVER_CAPACITY", "LOCKED", "UNKNOWN_CONTENT", "UNKNOWN_TIER" }.Contains(code, StringComparer.Ordinal);
            if (pending.Route == PracticeCommandOutbox.StartEncounterRoute && SupportJson.EquipmentPolicy(profile.PolicyVersion) && status == 422 && code == "NO_ELIGIBLE_WEAPON") return true;
            return status == 409 && (code == "STALE_REVISION" || code == "RUN_ACTIVE") || status == 422 &&
                new[] { "TUTORIAL_REQUIRED", "NOT_OWNED", "PARTY_SIZE", "PARTY_DUPLICATE", "OVER_CAPACITY", "LOCKED", "UNKNOWN_CONTENT", "UNKNOWN_TIER", "NOT_ENOUGH_STAMINA",
                    "SWEEP_LOCKED", "INVALID_SWEEP", "LEVEL_CAP", "INVALID_TARGET_LEVEL", "INSUFFICIENT_RESOURCES", "RESOURCE_OVERFLOW" }.Contains(code, StringComparer.Ordinal);
        }

        async Task<Read<PracticeEncounterSnapshot>> ReadEncounterAsync(string credential, string id, CancellationToken cancellation)
        {
            var exchange = await ExchangeAsync(HttpMethod.Get, PracticeCommandOutbox.EncounterRoute(id), null, credential, cancellation).ConfigureAwait(false);
            if (exchange.Failure != null) return new Read<PracticeEncounterSnapshot>(null, exchange.Failure, null);
            if (exchange.Status != 200) return new Read<PracticeEncounterSnapshot>(null, ServerCode(exchange.Body) ?? PracticeProtocolException.Invalid, exchange.Status);
            try { return new Read<PracticeEncounterSnapshot>(PracticeEncounterSnapshot.Parse(exchange.Body, account, id), Codes.Ok, 200); }
            catch (PracticeProtocolException error) { return new Read<PracticeEncounterSnapshot>(null, error.Code, 200); }
        }

        async Task<Read<PracticeEncounterSnapshot>> RefreshEncounterWithAsync(string credential, string id, PracticeProgressionSnapshot profile, CancellationToken cancellation)
        {
            var read = await ReadEncounterAsync(credential, id, cancellation).ConfigureAwait(false);
            lock (sync)
            {
                var next = read.Value; var active = profile.ActiveEncounter;
                if (next == null) { encounterNeedsRefresh = true; return read; }
                if (next.Revision < profile.Revision || encounter != null && (next.Revision < encounter.Revision || next.EncounterId == encounter.EncounterId && next.Version < encounter.Version))
                { encounterNeedsRefresh = true; return new Read<PracticeEncounterSnapshot>(null, Codes.RollbackRefused, 200); }
                if (active != null && active.EncounterId == id && (active.Version != next.Version || active.Phase != next.Phase || active.Kind != next.Kind
                    || active.BossId != next.BossId || active.Difficulty != next.Difficulty || active.Practice != next.Practice)
                    || active?.EncounterId != id && !GrowthJson.Terminal(next.Phase))
                { encounterNeedsRefresh = true; return new Read<PracticeEncounterSnapshot>(null, Codes.RefreshRaced, 200); }
                encounter = next; encounterNeedsRefresh = false;
                if (next.Revision > profile.Revision) progressionNeedsRefresh = true;
            }
            return read;
        }

        static bool PublicId(string? id) => id != null && PracticeCommandOutbox.IsPublicId(id);
        void MarkEncounterStale() { lock (sync) encounterNeedsRefresh = true; }
        static GrowthMutationResult GrowthNotSent(string code, int? status = null) => new GrowthMutationResult(PracticeMutationStatus.NotSent, code, status);
        static GrowthMutationResult GrowthUnresolved(PendingPracticeCommand pending, string code, int? status = null) => new GrowthMutationResult(PracticeMutationStatus.Pending, code, status, pending);
    }
}
