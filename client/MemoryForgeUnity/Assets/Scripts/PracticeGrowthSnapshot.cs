#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json.Linq;
using P = MemoryForge.Client.PracticePublicJson;

namespace MemoryForge.Client
{
    static class GrowthJson
    {
        internal static readonly string[] ResourceKeys = { "credits", "ink", "oil", "seal", "alloy", "book", "polish", "core", "refine_core" };
        internal static IReadOnlyDictionary<string, long> Resources(JObject json)
        {
            ProgressionJson.Fields(json, ResourceKeys);
            return new ReadOnlyDictionary<string, long>(ResourceKeys.ToDictionary(key => key, key => RecruitmentJson.Long(json, key), StringComparer.Ordinal));
        }
        internal static string EncounterId(JObject json, string name)
        {
            string id = P.Text(json, name);
            return PracticeCommandOutbox.IsEncounterId(id) ? id : throw P.Invalid(name);
        }
        internal static bool Terminal(string phase) => phase == "VICTORY" || phase == "DEFEAT";
    }

    public sealed class GrowthFocus
    {
        internal GrowthFocus(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "value", "cap", "overflowCap", "regenSeconds", "nextRegenAtMs", "serverNowMs" });
            Value = ProgressionJson.Number(json, "value", 0, 999); Cap = ProgressionJson.Number(json, "cap", 240, 240);
            OverflowCap = ProgressionJson.Number(json, "overflowCap", 999, 999); RegenSeconds = ProgressionJson.Number(json, "regenSeconds", 360, 360);
            NextRegenAtMs = P.Member(json, "nextRegenAtMs").Type == JTokenType.Null ? (long?)null : RecruitmentJson.Long(json, "nextRegenAtMs");
            ServerNowMs = RecruitmentJson.Long(json, "serverNowMs");
            if ((Value >= Cap) != (NextRegenAtMs == null)) throw P.Invalid("focus regeneration");
        }
        public int Value { get; }
        public int Cap { get; }
        public int OverflowCap { get; }
        public int RegenSeconds { get; }
        public long? NextRegenAtMs { get; }
        public long ServerNowMs { get; }
    }

    public sealed class GrowthGrant
    {
        internal GrowthGrant(JObject json, bool equipment = false)
        {
            ProgressionJson.Fields(json, new[] { "resources", "accountXp" });
            Resources = equipment ? EquipmentJson.Resources(P.Object(json, "resources")) : GrowthJson.Resources(P.Object(json, "resources")); AccountXp = RecruitmentJson.Long(json, "accountXp");
        }
        public IReadOnlyDictionary<string, long> Resources { get; }
        public long AccountXp { get; }
    }

    public sealed class GrowthDungeonTier
    {
        internal GrowthDungeonTier(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "tier", "stage", "percent", "reward", "bestStars", "sweepAvailable" });
            Tier = ProgressionJson.Number(json, "tier", 1, 6); Stage = ProgressionJson.Number(json, "stage", 10, 60);
            Percent = ProgressionJson.Number(json, "percent", 0, 100); Reward = new GrowthGrant(P.Object(json, "reward"));
            BestStars = ProgressionJson.Number(json, "bestStars", 0, 3); SweepAvailable = RecruitmentJson.Bool(json, "sweepAvailable");
        }
        public int Tier { get; }
        public int Stage { get; }
        public int Percent { get; }
        public GrowthGrant Reward { get; }
        public int BestStars { get; }
        public bool SweepAvailable { get; }
    }

    public sealed class GrowthDungeon
    {
        internal GrowthDungeon(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "id", "unlockLevel", "cost", "available", "tiers" });
            Id = RecruitmentJson.Id(json, "id"); UnlockLevel = ProgressionJson.Number(json, "unlockLevel", 1, 60);
            Cost = ProgressionJson.Number(json, "cost", 20, 20); Available = RecruitmentJson.Bool(json, "available");
            Tiers = RecruitmentJson.List(json, "tiers", tier => new GrowthDungeonTier(tier));
            if (Tiers.Select(tier => tier.Tier).Distinct().Count() != Tiers.Count) throw P.Invalid("duplicate dungeon tier");
        }
        public string Id { get; }
        public int UnlockLevel { get; }
        public int Cost { get; }
        public bool Available { get; }
        public IReadOnlyList<GrowthDungeonTier> Tiers { get; }
    }

    public sealed class GrowthActiveEncounter
    {
        internal GrowthActiveEncounter(JObject json)
        {
            bool weekly = json.Property("kind") != null;
            ProgressionJson.Fields(json, weekly ? new[] { "encounterId", "kind", "bossId", "difficulty", "practice", "phase", "version" } : new[] { "encounterId", "dungeonId", "tier", "phase", "version" });
            Kind = weekly ? RecruitmentJson.Choice(json, "kind", "weeklyBoss") : "dungeon";
            EncounterId = GrowthJson.EncounterId(json, "encounterId");
            if (weekly) { BossId = RecruitmentJson.Choice(json, "bossId", "b_bell", "b_notary", "b_garden", "b_archivist"); Difficulty = ProgressionJson.Number(json, "difficulty", 1, 5); Practice = RecruitmentJson.Bool(json, "practice"); }
            else { DungeonId = RecruitmentJson.Id(json, "dungeonId"); Tier = ProgressionJson.Number(json, "tier", 1, 6); }
            Phase = RecruitmentJson.Choice(json, "phase", "PLAYER", "VICTORY", "DEFEAT"); Version = P.Count(json, "version");
        }
        public string EncounterId { get; }
        public string Kind { get; } public string? BossId { get; } public int? Difficulty { get; } public bool? Practice { get; }
        public string DungeonId { get; } = "";
        public int Tier { get; }
        public string Phase { get; }
        public long Version { get; }
    }

    public sealed class GrowthStats
    {
        internal GrowthStats(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "power4", "power", "hp", "gearBps", "skillLv", "ultLv", "reference" });
            Power4 = P.Count(json, "power4"); Power = P.Count(json, "power"); Hp = ProgressionJson.Number(json, "hp", 1); GearBps = P.Count(json, "gearBps");
            SkillLv = ProgressionJson.Number(json, "skillLv", 1, 10); UltLv = ProgressionJson.Number(json, "ultLv", 1, 10); Reference = RecruitmentJson.Bool(json, "reference");
        }
        public int Power4 { get; }
        public int Power { get; }
        public int Hp { get; }
        public int GearBps { get; }
        public int SkillLv { get; }
        public int UltLv { get; }
        public bool Reference { get; }
    }

    public sealed class GrowthLevelState
    {
        internal GrowthLevelState(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "level", "xp", "cap", "stats" });
            Level = ProgressionJson.Number(json, "level", 1, 60); Xp = RecruitmentJson.Long(json, "xp");
            Cap = ProgressionJson.Number(json, "cap", 20, 60); Stats = new GrowthStats(P.Object(json, "stats"));
            if (Level > Cap || Cap % 10 != 0) throw P.Invalid("level cap");
        }
        public int Level { get; }
        public long Xp { get; }
        public int Cap { get; }
        public GrowthStats Stats { get; }
    }

    public sealed class GrowthSettlement
    {
        internal GrowthSettlement(JObject json)
        {
            bool weekly = json.Property("kind") != null;
            ProgressionJson.Fields(json, weekly ? new[] { "outcome", "stars", "cost", "grant", "kind", "rewarded", "reason", "weekKey", "claimed", "remaining" } : new[] { "outcome", "stars", "cost", "grant" });
            Kind = weekly ? RecruitmentJson.Choice(json, "kind", "weeklyBoss") : "dungeon";
            Outcome = RecruitmentJson.Choice(json, "outcome", "VICTORY", "DEFEAT"); Stars = ProgressionJson.Number(json, "stars", 0, 3);
            var cost = P.Object(json, "cost"); ProgressionJson.Fields(cost, new[] { "focus" }); FocusCost = P.Count(cost, "focus");
            Grant = new GrowthGrant(P.Object(json, "grant"), weekly);
            if (weekly)
            {
                Rewarded = RecruitmentJson.Bool(json, "rewarded"); Reason = P.NullableText(json, "reason"); WeekKey = EquipmentJson.Week(json);
                EquipmentJson.Claims(json, out int claimed, out int remaining); Claimed = claimed; Remaining = remaining;
                if (Stars != 0 || FocusCost != 0 || Rewarded != (Reason == null) || Reason != null && !new[] { "DEFEAT", "PRACTICE", "WEEKLY_LIMIT" }.Contains(Reason)
                    || Outcome == "DEFEAT" && (Rewarded != false || Reason != "DEFEAT") || Outcome == "VICTORY" && Reason == "DEFEAT"
                    || Rewarded == false && (Grant.AccountXp != 0 || Grant.Resources.Values.Any(value => value != 0))
                    || Rewarded == true && (Outcome != "VICTORY" || Claimed < 1 || Grant.AccountXp != 100 || Grant.Resources.Any(row => row.Value != (row.Key == "core" ? 2 : row.Key == "credits" ? 20000 : row.Key == "boss_token" ? 40 : 0)))
                    || Reason == "WEEKLY_LIMIT" && Claimed != 3) throw P.Invalid("weekly settlement");
                return;
            }
            if (Outcome == "VICTORY" && FocusCost != 20 || Outcome == "DEFEAT" && (FocusCost != 0 || Stars != 0 || Grant.AccountXp != 0 || Grant.Resources.Values.Any(value => value != 0)))
                throw P.Invalid("settlement outcome");
        }
        public string Kind { get; } public bool? Rewarded { get; } public string? Reason { get; } public string? WeekKey { get; } public int? Claimed { get; } public int? Remaining { get; }
        public string Outcome { get; }
        public int Stars { get; }
        public int FocusCost { get; }
        public GrowthGrant Grant { get; }
    }

    public sealed class PracticeEncounterContext : IEquatable<PracticeEncounterContext>
    {
        internal PracticeEncounterContext(string owner, string encounterId, long version, int turn, int battleVersion, string phase)
        { Owner = owner; EncounterId = encounterId; Version = version; Turn = turn; BattleVersion = battleVersion; BattlePhase = phase; }
        public string Owner { get; }
        public string EncounterId { get; }
        public long Version { get; }
        public int Turn { get; }
        public int BattleVersion { get; }
        public string BattlePhase { get; }
        public bool Equals(PracticeEncounterContext? other) => other != null && Owner == other.Owner && EncounterId == other.EncounterId && Version == other.Version
            && Turn == other.Turn && BattleVersion == other.BattleVersion && BattlePhase == other.BattlePhase;
        public override bool Equals(object? other) => Equals(other as PracticeEncounterContext);
        public override int GetHashCode() => Owner.GetHashCode() ^ EncounterId.GetHashCode() ^ Version.GetHashCode() ^ Turn ^ BattleVersion;
    }

    /// <summary>A public standalone Battle, never a synthetic Run. Journal Version and Core BattleVersion have separate meanings.</summary>
    public sealed class PracticeEncounterSnapshot
    {
        readonly JObject json;
        PracticeEncounterSnapshot(JObject json, string owner, string? expectedId)
        {
            bool weekly = json.Property("kind") != null;
            ProgressionJson.Fields(json, weekly ? new[] { "encounterId", "version", "phase", "revision", "state", "kind", "bossId", "difficulty", "practice" } : new[] { "encounterId", "version", "phase", "revision", "state" }, "events", "settlement");
            Kind = weekly ? RecruitmentJson.Choice(json, "kind", "weeklyBoss") : "dungeon";
            if (weekly) { BossId = RecruitmentJson.Choice(json, "bossId", "b_bell", "b_notary", "b_garden", "b_archivist"); Difficulty = ProgressionJson.Number(json, "difficulty", 1, 5); Practice = RecruitmentJson.Bool(json, "practice"); }
            EncounterId = GrowthJson.EncounterId(json, "encounterId"); if (expectedId != null && expectedId != EncounterId) throw P.Mismatch("encounterId");
            Version = P.Count(json, "version"); Revision = RecruitmentJson.Long(json, "revision"); Phase = RecruitmentJson.Choice(json, "phase", "PLAYER", "VICTORY", "DEFEAT");
            var state = P.Object(json, "state");
            Support = SupportJson.Summary(state);
            foreach (string hidden in P.HiddenBattleMembers) if (state.ContainsKey(hidden)) throw P.Private();
            ProgressionJson.Fields(state, new[] { "v", "phase", "turn", "hp", "maxHp", "block", "counter", "ap", "apNext", "apDrainNext", "silenceUntil", "weak", "debts",
                "echo", "echoMax", "echoActive", "freeNext", "played", "restraint", "bonus", "growth", "buffs", "powers", "piles", "hand", "reservations", "slots", "gaugeMax", "actors", "enemies", "result" }, "support");
            if (P.Text(state, "phase") != Phase) throw P.Mismatch("encounter phase");
            var piles = P.Object(state, "piles"); ProgressionJson.Fields(piles, P.PileCounts);
            foreach (string pile in P.PileCounts) P.Count(piles, pile);
            Context = new PracticeEncounterContext(owner, EncounterId, Version, P.Count(state, "turn"), P.Count(state, "v"), Phase);
            foreach (string array in new[] { "debts", "buffs", "powers", "hand", "reservations", "actors", "enemies" })
                foreach (var item in P.Array(state, array)) if (!(item is JObject)) throw P.Invalid(array);
            foreach (string number in new[] { "hp", "maxHp", "block", "counter", "ap", "apNext", "apDrainNext", "silenceUntil", "weak", "echo", "echoMax", "freeNext", "played", "gaugeMax" }) P.Count(state, number);
            RecruitmentJson.Bool(state, "echoActive"); P.Object(state, "restraint"); P.Object(state, "bonus"); P.Object(state, "growth"); P.Object(state, "slots"); P.NullableObject(state, "result");
            if (json.ContainsKey("events")) SupportJson.Events(P.Array(json, "events"));
            Settlement = json.Property("settlement") == null || json["settlement"]!.Type == JTokenType.Null ? null : new GrowthSettlement(P.Object(json, "settlement"));
            if (GrowthJson.Terminal(Phase) && Settlement == null || Settlement != null && (Settlement.Outcome != Phase || Settlement.Kind != Kind)
                || Settlement != null && weekly && (Practice == true && (Settlement.Rewarded != false || Phase == "VICTORY" && Settlement.Reason != "PRACTICE")
                    || Practice == false && Settlement.Reason == "PRACTICE")) throw P.Invalid("terminal settlement");
            this.json = (JObject)json.DeepClone();
        }
        public string Kind { get; } public string? BossId { get; } public int? Difficulty { get; } public bool? Practice { get; }
        public string EncounterId { get; }
        public long Version { get; }
        public long Revision { get; }
        public string Phase { get; }
        public PracticeEncounterContext Context { get; }
        public PracticeSupportSummary? Support { get; }
        public GrowthSettlement? Settlement { get; }
        public JObject StateJObject() => (JObject)json["state"]!.DeepClone();
        public JObject ToJObject() => (JObject)json.DeepClone();
        public JArray? EventsJArray() => json["events"] is JArray events ? (JArray)events.DeepClone() : null;
        public static PracticeEncounterSnapshot Parse(byte[] utf8, string owner, string? expectedId = null) => new PracticeEncounterSnapshot(P.ParseGrowthObject(utf8, GrowthPublicKind.Encounter), owner, expectedId);
    }

    public sealed class PracticeCharacterLevelPreview
    {
        readonly JObject json;
        PracticeCharacterLevelPreview(JObject json, long expectedRevision, string charId, int targetLevel)
        {
            ProgressionJson.Fields(json, new[] { "revision", "charId", "before", "after", "cost", "canCommit" }, "code");
            Revision = RecruitmentJson.Long(json, "revision"); CharId = RecruitmentJson.Id(json, "charId");
            if (Revision != expectedRevision || CharId != charId) throw P.Mismatch("level preview context");
            Before = new GrowthLevelState(P.Object(json, "before")); After = new GrowthLevelState(P.Object(json, "after"));
            if (After.Level != targetLevel) throw P.Mismatch("level preview target");
            var cost = P.Object(json, "cost"); ProgressionJson.Fields(cost, new[] { "ink", "credits" }); CostInk = RecruitmentJson.Long(cost, "ink"); CostCredits = RecruitmentJson.Long(cost, "credits");
            CanCommit = RecruitmentJson.Bool(json, "canCommit"); Code = json.Property("code") == null || json["code"]!.Type == JTokenType.Null ? null : P.Text(json, "code");
            if (Code != null && !P.IsCode(Code) || CanCommit && Code != null) throw P.Invalid("preview code");
            this.json = (JObject)json.DeepClone();
        }
        public long Revision { get; }
        public string CharId { get; }
        public GrowthLevelState Before { get; }
        public GrowthLevelState After { get; }
        public long CostInk { get; }
        public long CostCredits { get; }
        public bool CanCommit { get; }
        public string? Code { get; }
        public JObject ToJObject() => (JObject)json.DeepClone();
        public static PracticeCharacterLevelPreview Parse(byte[] utf8, long revision, string charId, int targetLevel) => new PracticeCharacterLevelPreview(P.ParseGrowthObject(utf8, GrowthPublicKind.Preview), revision, charId, targetLevel);
    }

    public sealed class PracticeEncounterBattleQueryView
    {
        readonly JObject json;
        PracticeEncounterBattleQueryView(JObject json, PracticeEncounterContext context, string uid, string mode, string? target, IReadOnlyList<string> picks)
        {
            ProgressionJson.Fields(json, new[] { "encounterId", "version", "phase", "context", "source", "slots", "forecast", "choices" });
            if (GrowthJson.EncounterId(json, "encounterId") != context.EncounterId || RecruitmentJson.Long(json, "version") != context.Version || P.Text(json, "phase") != context.BattlePhase) throw P.Mismatch("encounter query");
            var at = P.Object(json, "context"); ProgressionJson.Fields(at, new[] { "turn", "battleVersion", "battlePhase" });
            if (P.Count(at, "turn") != context.Turn || P.Count(at, "battleVersion") != context.BattleVersion || P.Text(at, "battlePhase") != context.BattlePhase) throw P.Mismatch("encounter query context");
            var source = P.Object(json, "source"); P.Text(source, "id"); P.NullableText(source, "name"); P.NullableText(source, "text");
            ProgressionJson.Fields(source, new[] { "uid", "id", "name", "text", "mode", "target", "picks" });
            var echoed = P.Array(source, "picks");
            bool same = P.Text(source, "uid") == uid && P.Text(source, "mode") == mode && P.NullableText(source, "target") == target && echoed.Count == picks.Count;
            for (int i = 0; same && i < picks.Count; i++) same = echoed[i].Type == JTokenType.String && echoed[i].Value<string>() == picks[i];
            if (!same) throw P.Mismatch("encounter query source");
            P.Object(json, "slots"); var forecast = P.Object(json, "forecast"); ForecastError = P.NullableText(forecast, "error");
            ForecastHidden = forecast["hidden"] is JValue hidden && hidden.Type == JTokenType.Boolean && hidden.Value<bool>();
            EnemyHpLosses = PracticeBattleQueryView.EnemyHpLossesOf(forecast, ForecastError);
            var choices = P.Object(json, "choices"); ChoicesError = P.NullableText(choices, "error"); ChoicesBlocked = P.NullableText(choices, "blocked");
            P.Array(choices, "requests"); P.Array(choices, "candidates"); Context = context; this.json = (JObject)json.DeepClone();
        }
        public PracticeEncounterContext Context { get; }
        public string? ForecastError { get; }
        public bool ForecastHidden { get; }
        public IReadOnlyList<(string Uid, int HpLoss)> EnemyHpLosses { get; }
        public string? ChoicesError { get; }
        public string? ChoicesBlocked { get; }
        public JObject ToJObject() => (JObject)json.DeepClone();
        public static PracticeEncounterBattleQueryView Parse(byte[] utf8, PracticeEncounterContext context, string uid, string mode, string? target, IReadOnlyList<string> picks) =>
            new PracticeEncounterBattleQueryView(P.ParseGrowthObject(utf8, GrowthPublicKind.Query), context, uid, mode, target, picks);
    }

    /// <summary>The validated receipt of one exact shared-outbox request. Its Battle/profile is historical evidence, not current state.</summary>
    public sealed class PracticeGrowthReceipt
    {
        PracticeGrowthReceipt(PendingPracticeCommand request, long revision) { Route = request.Route; RequestId = request.RequestId; Revision = revision; }
        public string Route { get; }
        public string RequestId { get; }
        public long Revision { get; }
        public PracticeEncounterSnapshot? Encounter { get; private set; }
        public PracticeProgressionSnapshot? Profile { get; private set; }
        public string? Kind { get; private set; }
        public string? CharId { get; private set; }
        public GrowthLevelState? Before { get; private set; }
        public GrowthLevelState? After { get; private set; }
        public GrowthGrant? Grant { get; private set; }
        public int FocusCost { get; private set; }
        public long CostInk { get; private set; }
        public long CostCredits { get; private set; }

        public static PracticeGrowthReceipt Parse(PendingPracticeCommand pending, byte[] utf8)
        {
            if (pending == null) throw new ArgumentNullException(nameof(pending));
            if (!PracticeCommandOutbox.IsGrowthRoute(pending.Route)) throw P.Mismatch("growth receipt route");
            bool weekly = pending.Route == PracticeCommandOutbox.WeeklyBossStartRoute;
            bool start = weekly || pending.Route == PracticeCommandOutbox.StartEncounterRoute;
            if (start || PracticeCommandOutbox.IsEncounterCommandRoute(pending.Route))
            {
                string? id = start ? null : pending.Route.Substring(PracticeCommandOutbox.StartEncounterRoute.Length + 1, 35);
                var encounter = PracticeEncounterSnapshot.Parse(utf8, pending.Account, id);
                long expected = start ? 0 : (long)(pending.ExpectedVersion ?? throw P.Invalid("expectedVersion")) + 1;
                if (weekly)
                {
                    var intent = JObject.Parse(pending.Body);
                    if (encounter.Kind != "weeklyBoss" || encounter.BossId != P.Text(intent, "bossId") || encounter.Difficulty != P.Count(intent, "difficulty") || encounter.Practice != RecruitmentJson.Bool(intent, "practice")) throw P.Mismatch("weekly receipt intent");
                }
                else if (start && encounter.Kind != "dungeon") throw P.Mismatch("dungeon receipt kind");
                if (encounter.Version != expected || !start && encounter.EventsJArray() == null) throw P.Mismatch("encounter receipt version/events");
                if (start && (!(pending.ExpectedRevision is long revision) || revision == long.MaxValue || encounter.Revision != revision + 1)) throw P.Mismatch("encounter start revision");
                SupportJson.CommandReceipt(pending, encounter.EventsJArray(), encounter.Support);
                return new PracticeGrowthReceipt(pending, encounter.Revision) { Encounter = encounter };
            }
            var json = P.ParseGrowthObject(utf8, GrowthPublicKind.Action);
            long receiptRevision = RecruitmentJson.Long(json, "revision");
            if (!(pending.ExpectedRevision is long requestRevision) || requestRevision == long.MaxValue || receiptRevision != requestRevision + 1
                || P.Text(json, "requestId") != pending.RequestId) throw P.Mismatch("growth receipt identity");
            var result = new PracticeGrowthReceipt(pending, receiptRevision);
            var body = JObject.Parse(pending.Body);
            if (pending.Route == PracticeCommandOutbox.DungeonSweepRoute)
            {
                ProgressionJson.Fields(json, new[] { "requestId", "revision", "kind", "dungeonId", "tier", "times", "cost", "grant", "profile" });
                result.Kind = RecruitmentJson.Choice(json, "kind", "dungeonSweep");
                if (RecruitmentJson.Id(json, "dungeonId") != P.Text(body, "dungeonId") || P.Count(json, "tier") != P.Count(body, "tier") || P.Count(json, "times") != P.Count(body, "times")) throw P.Mismatch("sweep intent");
                var cost = P.Object(json, "cost"); ProgressionJson.Fields(cost, new[] { "focus" }); result.FocusCost = P.Count(cost, "focus");
                if (result.FocusCost != P.Count(body, "times") * 20) throw P.Invalid("sweep cost");
                result.Grant = new GrowthGrant(P.Object(json, "grant"));
            }
            else
            {
                ProgressionJson.Fields(json, new[] { "requestId", "revision", "kind", "charId", "before", "after", "cost", "profile" });
                result.Kind = RecruitmentJson.Choice(json, "kind", "characterLevelUp"); result.CharId = RecruitmentJson.Id(json, "charId");
                if (result.CharId != PracticeCommandOutbox.CharacterRouteId(pending.Route, "/level-up")) throw P.Mismatch("level character");
                result.Before = new GrowthLevelState(P.Object(json, "before")); result.After = new GrowthLevelState(P.Object(json, "after"));
                if (result.After.Level != P.Count(body, "targetLevel") || result.After.Level <= result.Before.Level || result.After.Xp != 0) throw P.Mismatch("level target");
                var cost = P.Object(json, "cost"); ProgressionJson.Fields(cost, new[] { "ink", "credits" });
                result.CostInk = RecruitmentJson.Long(cost, "ink"); result.CostCredits = RecruitmentJson.Long(cost, "credits");
                if (result.CostInk == 0 || result.CostInk % 2 != 0 || result.CostCredits != result.CostInk / 2) throw P.Invalid("level cost");
            }
            result.Profile = PracticeProgressionSnapshot.Parse(P.Object(json, "profile"), pending.Account);
            if (result.Profile.Revision != receiptRevision || result.Profile.Resources == null) throw P.Mismatch("growth profile revision");
            return result;
        }
    }
}
