#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using P = MemoryForge.Client.PracticePublicJson;

namespace MemoryForge.Client
{
    static class SupportJson
    {
        internal const string Policy = "account-progression-20261021-v4";
        internal static bool EquipmentPolicy(string policy) => policy == MemoryForge.Client.EquipmentJson.Policy || policy == Policy;
        internal static JObject Args(string caller, string? target, IReadOnlyList<string>? picks)
        {
            var args = new JObject { ["caller"] = caller };
            if (target != null) args.Add("target", target);
            if (picks != null) args.Add("picks", new JArray(picks));
            return args;
        }
        internal static bool ValidArgs(JObject args)
        {
            if (args.Property("caller") == null || args.Properties().Any(p => p.Name != "caller" && p.Name != "target" && p.Name != "picks")) return false;
            foreach (string name in new[] { "caller", "target" }) if (args.Property(name) != null && (args[name]?.Type != JTokenType.String || !(args[name]!.Value<string>() is string id) || !PracticeCommandOutbox.IsPublicId(id))) return false;
            return args.Property("picks") == null || args["picks"] is JArray picks && picks.Count <= 64 && picks.All(p => p.Type == JTokenType.String && p.Value<string>() is string id && PracticeCommandOutbox.IsPublicId(id));
        }
        internal static bool KnownCommandRefusal(string code) => new[] { "SUPPORT_NOT_SELECTED", "SUPPORT_ALREADY_USED", "SUPPORT_IN_PARTY", "UNKNOWN_ACTOR", "ULT_NOT_READY", "BAD_TARGET",
            "WRONG_PHASE", "PICK_INVALID", "PICK_GROWTH_CAP", "PULL_DESTINATION_FULL", "BATTLE_NUMBER_OUT_OF_RANGE", "OVER_CAPACITY", "NOT_OWNED", "NO_ELIGIBLE_WEAPON", "WEAPON_NOT_OWNED", "WEAPON_TYPE_MISMATCH" }.Contains(code, StringComparer.Ordinal);
        internal static bool IsSupportCommand(PendingPracticeCommand pending) => pending.ExpectedVersion != null && JObject.Parse(pending.Body)["kind"] is JValue value && value.Type == JTokenType.String && value.Value<string>() == "support";
        internal static PracticeSupportSummary? Summary(JObject battle) => battle.Property("support") == null ? null : new PracticeSupportSummary(P.Object(battle, "support"));
        internal static void Events(JArray events)
        {
            foreach (var row in events)
            {
                if (!(row is JObject item)) throw P.Invalid("events");
                if (item["e"]?.Type != JTokenType.String || item["e"]!.Value<string>() != "support") continue;
                ProgressionJson.Fields(item, new[] { "e", "actor", "caller" }); RecruitmentJson.Id(item, "actor"); RecruitmentJson.Id(item, "caller");
            }
        }
        internal static void CommandReceipt(PendingPracticeCommand request, JArray? events, PracticeSupportSummary? support)
        {
            if (!IsSupportCommand(request)) return;
            if (events == null) throw P.Invalid("support receipt events");
            var emitted = events.OfType<JObject>().Where(e => e["e"]?.Type == JTokenType.String && e["e"]!.Value<string>() == "support").ToArray();
            string caller = P.Text(P.Object(JObject.Parse(request.Body), "args"), "caller");
            if (emitted.Length != 1 || P.Text(emitted[0], "caller") != caller || support != null && (P.Text(emitted[0], "actor") != support.CharId || !support.Used)) throw P.Mismatch("support receipt attribution/use");
        }
        internal static void QueryParts(JObject json, out string? error, out bool hidden, out IReadOnlyList<(string Uid, int HpLoss)> losses, out string? choicesError, out string? blocked, out int index, out int requestCount)
        {
            var slots = P.Object(json, "slots"); ProgressionJson.Fields(slots, new[] { "next", "after", "sealedUntil" }); P.Count(slots, "sealedUntil");
            foreach (string name in new[] { "next", "after" })
            {
                var slot = P.Object(slots, name); ProgressionJson.Fields(slot, new[] { "base", "temp", "max", "cap", "used", "sealed", "due" });
                foreach (string number in new[] { "base", "temp", "max", "cap", "used", "due" }) P.Count(slot, number); RecruitmentJson.Bool(slot, "sealed");
            }
            var forecast = P.Object(json, "forecast");
            ProgressionJson.Fields(forecast, new[] { "error", "mode", "cost", "due", "hidden", "enemyHpLoss", "enemyDamage", "newlyBroken", "blockDelta", "hpDelta", "defeat", "enemyHpLosses" });
            error = P.NullableText(forecast, "error"); if (error != null && !P.IsCode(error)) throw P.Invalid("forecast error");
            hidden = RecruitmentJson.Bool(forecast, "hidden"); if (P.Text(forecast, "mode") != "now" || P.Member(forecast, "due").Type != JTokenType.Null) throw P.Invalid("support immediate forecast");
            int cost = P.Count(forecast, "cost"); if (error == null ? cost != 100 : cost != 0 && cost != 100) throw P.Invalid("support forecast gauge");
            foreach (string name in new[] { "enemyHpLoss", "enemyDamage", "newlyBroken", "blockDelta", "hpDelta", "defeat" })
            {
                var value = P.Member(forecast, name);
                if (hidden) { if (value.Type != JTokenType.Null) throw P.Invalid("hidden support forecast"); }
                else if (name == "defeat") RecruitmentJson.Bool(forecast, name);
                else if (name == "blockDelta" || name == "hpDelta") { if (!(value is JValue number) || number.Type != JTokenType.Integer || !(number.Value is long n) || n < int.MinValue || n > int.MaxValue) throw P.Invalid(name); }
                else P.Count(forecast, name);
            }
            losses = PracticeBattleQueryView.EnemyHpLossesOf(forecast, error);
            var choices = P.Object(json, "choices"); ProgressionJson.Fields(choices, new[] { "error", "requests", "accepted", "index", "left", "blocked", "candidates" });
            choicesError = P.NullableText(choices, "error"); blocked = P.NullableText(choices, "blocked");
            if (choicesError != null && !P.IsCode(choicesError) || blocked != null && !P.IsCode(blocked)) throw P.Invalid("choices error");
            index = P.Count(choices, "index"); P.Count(choices, "accepted"); P.Count(choices, "left"); var requests = P.Array(choices, "requests"); requestCount = requests.Count;
            if (index > requestCount) throw P.Invalid("choice index");
            foreach (var request in requests)
            {
                if (!(request is JObject item)) throw P.Invalid("choice request");
                ProgressionJson.Fields(item, new[] { "label", "from", "count", "growthOnly", "op", "part", "leading" });
                P.Text(item, "label"); P.Text(item, "from"); P.Count(item, "count"); RecruitmentJson.Bool(item, "growthOnly"); P.Text(item, "op"); P.NullableText(item, "part"); RecruitmentJson.Bool(item, "leading");
            }
            foreach (var candidate in P.Array(choices, "candidates"))
            {
                if (!(candidate is JObject item)) throw P.Invalid("choice candidate");
                ProgressionJson.Fields(item, new[] { "uid", "id", "zone", "plus", "mods", "temp", "growth" });
                RecruitmentJson.Id(item, "uid"); RecruitmentJson.Id(item, "id"); P.NullableText(item, "zone"); RecruitmentJson.Bool(item, "plus");
                var mods = P.Object(item, "mods"); ProgressionJson.Fields(mods, new[] { "c", "e" }); P.NullableText(mods, "c"); P.NullableText(mods, "e");
                foreach (var token in P.Array(item, "temp")) if (token.Type != JTokenType.String) throw P.Invalid("candidate temp"); P.Count(item, "growth");
            }
        }
    }
    public sealed class PracticeSupportSummary
    {
        internal PracticeSupportSummary(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "charId", "used", "gaugeCost" }); CharId = RecruitmentJson.Id(json, "charId"); Used = RecruitmentJson.Bool(json, "used"); GaugeCost = ProgressionJson.Number(json, "gaugeCost", 100, 100);
        }
        public string CharId { get; } public bool Used { get; } public int GaugeCost { get; }
    }
    public sealed class PracticeSupportSource
    {
        internal PracticeSupportSource(JObject json, string caller, string? target, IReadOnlyList<string> picks)
        {
            ProgressionJson.Fields(json, new[] { "kind", "caller", "charId", "name", "ultName", "text", "attr", "gaugeCost", "used", "ready", "code", "needsTarget", "target", "picks" });
            Kind = RecruitmentJson.Choice(json, "kind", "support"); Caller = RecruitmentJson.Id(json, "caller"); CharId = RecruitmentJson.Id(json, "charId");
            Name = P.Text(json, "name"); UltName = P.Text(json, "ultName"); Text = P.Text(json, "text"); Attr = RecruitmentJson.Id(json, "attr");
            GaugeCost = ProgressionJson.Number(json, "gaugeCost", 100, 100); Used = RecruitmentJson.Bool(json, "used"); Ready = RecruitmentJson.Bool(json, "ready"); Code = P.NullableText(json, "code");
            NeedsTarget = P.NullableText(json, "needsTarget"); Target = P.NullableText(json, "target"); Picks = ProgressionJson.IdList(json, "picks");
            if (NeedsTarget != null && NeedsTarget != "one" && NeedsTarget != "fixed" || Ready && Used || Ready != (Code == null) || Code != null && !P.IsCode(Code)) throw P.Invalid("support readiness");
            if (Caller != caller || Target != target || !Picks.SequenceEqual(picks, StringComparer.Ordinal)) throw P.Mismatch("support source request");
        }
        public string Kind { get; } public string Caller { get; } public string CharId { get; } public string Name { get; } public string UltName { get; } public string Text { get; } public string Attr { get; }
        public int GaugeCost { get; } public bool Used { get; } public bool Ready { get; } public string? Code { get; } public string? NeedsTarget { get; } public string? Target { get; } public IReadOnlyList<string> Picks { get; }
    }
    /// <summary>A pure, detached support answer bound to one actual Run OR Encounter context and one caller/target/pick request.</summary>
    public sealed class PracticeSupportQueryView
    {
        readonly JObject json;
        PracticeSupportQueryView(JObject json, PracticeUiContext? run, PracticeEncounterContext? encounter, string caller, string? target, IReadOnlyList<string> picks, string? supportId)
        {
            bool isRun = run != null;
            ProgressionJson.Fields(json, new[] { isRun ? "runId" : "encounterId", "version", "phase", "context", "source", "slots", "forecast", "choices" });
            var at = P.Object(json, "context");
            ProgressionJson.Fields(at, isRun ? new[] { "node", "turn", "battleVersion", "battlePhase" } : new[] { "turn", "battleVersion", "battlePhase" });
            Phase = P.Text(json, "phase"); BattlePhase = P.Text(at, "battlePhase");
            if (isRun)
            {
                if (P.RunId(json, "runId") != run!.RunId || P.Count(json, "version") != run.ServerVersion || Phase != "BATTLE" || P.NullableText(at, "node") != run.Node
                    || P.Count(at, "turn") != run.Turn || P.Count(at, "battleVersion") != run.BattleVersion || BattlePhase != "PLAYER") throw P.Mismatch("run support context");
            }
            else if (encounter == null || GrowthJson.EncounterId(json, "encounterId") != encounter.EncounterId || P.Count(json, "version") != encounter.Version || Phase != encounter.BattlePhase
                || P.Count(at, "turn") != encounter.Turn || P.Count(at, "battleVersion") != encounter.BattleVersion || BattlePhase != encounter.BattlePhase) throw P.Mismatch("encounter support context");
            Source = new PracticeSupportSource(P.Object(json, "source"), caller, target, picks);
            if (supportId != null && Source.CharId != supportId) throw P.Mismatch("frozen support source");
            SupportJson.QueryParts(json, out string? forecastError, out bool hidden, out var losses, out string? choicesError, out string? blocked, out int index, out int requestCount);
            ForecastError = forecastError; ForecastHidden = hidden; EnemyHpLosses = losses; ChoicesError = choicesError; ChoicesBlocked = blocked; ChoicesIndex = index; ChoicesCount = requestCount;
            RunContext = run; EncounterContext = encounter; this.json = (JObject)json.DeepClone();
        }
        public PracticeUiContext? RunContext { get; } public PracticeEncounterContext? EncounterContext { get; } public string Phase { get; } public string BattlePhase { get; } public PracticeSupportSource Source { get; }
        public string? ForecastError { get; } public bool ForecastHidden { get; } public IReadOnlyList<(string Uid, int HpLoss)> EnemyHpLosses { get; }
        public string? ChoicesError { get; } public string? ChoicesBlocked { get; } public int ChoicesIndex { get; } public int ChoicesCount { get; }
        public bool CanAct => BattlePhase == "PLAYER" && Source.Ready && !Source.Used && ForecastError == null && ChoicesError == null && ChoicesBlocked == null && ChoicesIndex == ChoicesCount;
        public JObject ToJObject() => (JObject)json.DeepClone();
        public static PracticeSupportQueryView Parse(byte[] utf8, PracticeUiContext context, string caller, string? target, IReadOnlyList<string> picks, string? supportId = null) =>
            new PracticeSupportQueryView(P.ParseGrowthObject(utf8, GrowthPublicKind.SupportQuery), context, null, caller, target, picks, supportId);
        public static PracticeSupportQueryView Parse(byte[] utf8, PracticeEncounterContext context, string caller, string? target, IReadOnlyList<string> picks, string? supportId = null) =>
            new PracticeSupportQueryView(P.ParseGrowthObject(utf8, GrowthPublicKind.SupportQuery), null, context, caller, target, picks, supportId);
    }
    public sealed class PracticeSupportSelectionReceipt
    {
        PracticeSupportSelectionReceipt(PendingPracticeCommand request, long revision, string? before, string? after, PracticeProgressionSnapshot profile) { RequestId = request.RequestId; Route = request.Route; Revision = revision; BeforeCharId = before; AfterCharId = after; Profile = profile; }
        public string RequestId { get; } public string Route { get; } public long Revision { get; } public string? BeforeCharId { get; } public string? AfterCharId { get; } public PracticeProgressionSnapshot Profile { get; }
        public static PracticeSupportSelectionReceipt Parse(PendingPracticeCommand pending, byte[] utf8)
        {
            if (pending.Route != PracticeCommandOutbox.SupportSelectionRoute || !(pending.ExpectedRevision is long expected) || expected == long.MaxValue) throw P.Mismatch("support selection request");
            var json = P.ParseGrowthObject(utf8, GrowthPublicKind.Action); ProgressionJson.Fields(json, new[] { "requestId", "revision", "kind", "target", "before", "after", "cost", "profile" });
            long revision = RecruitmentJson.Long(json, "revision"); if (revision != expected + 1 || P.Text(json, "requestId") != pending.RequestId || P.Text(json, "kind") != "supportSelection") throw P.Mismatch("support selection receipt");
            var intent = JObject.Parse(pending.Body); string? selected = P.NullableText(intent, "charId");
            foreach (string field in new[] { "target", "before", "after" }) ProgressionJson.Fields(P.Object(json, field), new[] { "charId" });
            string? before = ProgressionJson.OptionalId(P.Object(json, "before"), "charId"), after = ProgressionJson.OptionalId(P.Object(json, "after"), "charId");
            if (P.NullableText(P.Object(json, "target"), "charId") != selected || after != selected || before == after) throw P.Mismatch("support selection target");
            ProgressionJson.Fields(P.Object(json, "cost"), new string[0]);
            var profile = PracticeProgressionSnapshot.Parse(P.Object(json, "profile"), pending.Account);
            if (profile.Revision != revision || !profile.HasSupportSelection || profile.SupportSelection != after) throw P.Mismatch("support selection profile");
            return new PracticeSupportSelectionReceipt(pending, revision, before, after, profile);
        }
    }
}
