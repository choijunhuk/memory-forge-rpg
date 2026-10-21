// Public development-account recruitment data. Exact schemas keep request IDs/revisions public only in their documented locations.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using P = MemoryForge.Client.PracticePublicJson;

namespace MemoryForge.Client
{
    static class RecruitmentJson
    {
        internal static void Fields(JObject json, params string[] names)
        {
            if (json.Count != names.Length || names.Any(name => json.Property(name, StringComparison.Ordinal) == null)) throw P.Invalid("recruitment members");
        }
        internal static long Long(JObject json, string name) => P.Member(json, name) is JValue value && value.Type == JTokenType.Integer
            && value.Value is long number && number >= 0 ? number : throw P.Invalid(name);
        internal static bool Bool(JObject json, string name) => P.Member(json, name) is JValue value && value.Type == JTokenType.Boolean ? (bool)value.Value! : throw P.Invalid(name);
        internal static string Id(JObject json, string name)
        {
            string id = P.Text(json, name);
            return PracticeCommandOutbox.IsPublicId(id) ? id : throw P.Invalid(name);
        }
        internal static string Choice(JObject json, string name, params string[] choices)
        {
            string value = P.Text(json, name);
            return choices.Contains(value, StringComparer.Ordinal) ? value : throw P.Invalid(name);
        }
        internal static IReadOnlyDictionary<string, int> Counts(JObject json, bool allowZero)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in json.Properties())
            {
                int count = P.AsCount(item.Value) ?? throw P.Invalid("recruitment count");
                if (!PracticeCommandOutbox.IsPublicId(item.Name) || !allowZero && count == 0) throw P.Invalid("recruitment count");
                result.Add(item.Name, count);
            }
            return new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(result);
        }
        internal static IReadOnlyList<string> Ids(JObject json, string name, bool displayItems = false)
        {
            var result = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in P.Array(json, name))
            {
                if (!(token is JValue value && value.Type == JTokenType.String && value.Value is string id && (displayItems ? id.Length > 0 && id.Length <= 256 : PracticeCommandOutbox.IsPublicId(id))) || !seen.Add(id)) throw P.Invalid(name);
                result.Add(id);
            }
            return result.AsReadOnly();
        }
        internal static IReadOnlyList<T> List<T>(JObject json, string name, Func<JObject, T> parse)
        {
            var result = new List<T>();
            foreach (var token in P.Array(json, name)) result.Add(parse(token as JObject ?? throw P.Invalid(name)));
            return result.AsReadOnly();
        }
    }

    public sealed class RecruitmentCurve
    {
        internal RecruitmentCurve(JObject json)
        {
            var fields = new[] { "ssrBase", "softStart", "softStep", "hard", "srBase", "srGuarantee" };
            RecruitmentJson.Fields(json, fields.Concat(new[] { "maxPulls", "selectorAt" }.Where(name => json.Property(name, StringComparison.Ordinal) != null)).ToArray());
            SsrBase = P.Count(json, "ssrBase"); SoftStart = P.Count(json, "softStart"); SoftStep = P.Count(json, "softStep");
            Hard = P.Count(json, "hard"); SrBase = P.Count(json, "srBase"); SrGuarantee = P.Count(json, "srGuarantee");
            MaxPulls = json["maxPulls"] == null ? (int?)null : P.Count(json, "maxPulls");
            SelectorAt = json["selectorAt"] == null ? (int?)null : P.Count(json, "selectorAt");
            if (SsrBase > 10000 || SrBase > 10000 || SoftStep > 10000 || Hard == 0 || Hard > 1000 || SoftStart == 0 || SoftStart > 1000
                || SrGuarantee == 0 || SrGuarantee > 1000 || MaxPulls > 1000 || SelectorAt > 100000) throw P.Invalid("curve");
        }
        public int SsrBase { get; }
        public int SoftStart { get; }
        public int SoftStep { get; }
        public int Hard { get; }
        public int SrBase { get; }
        public int SrGuarantee { get; }
        public int? MaxPulls { get; }
        public int? SelectorAt { get; }
    }

    public sealed class RecruitmentBanner
    {
        internal RecruitmentBanner(JObject json)
        {
            RecruitmentJson.Fields(json, "id", "name", "kind", "ticket", "costPerPull", "tenPullTickets", "curve", "ssrPool", "srChars", "srWeapons", "rItems");
            Id = RecruitmentJson.Id(json, "id"); Name = P.Text(json, "name");
            if (Name.Length == 0 || Name.Length > 256) throw P.Invalid("banner name");
            Kind = RecruitmentJson.Choice(json, "kind", "standard", "beginner");
            Ticket = RecruitmentJson.Choice(json, "ticket", "limitedTicket", "weaponTicket", "standardTicket");
            CostPerPull = P.Count(json, "costPerPull"); TenPullTickets = P.Count(json, "tenPullTickets"); Curve = new RecruitmentCurve(P.Object(json, "curve"));
            if (Kind == "beginner" && Curve.MaxPulls == 0) throw P.Invalid("beginner cap");
            SsrPool = RecruitmentJson.Ids(json, "ssrPool"); SrChars = RecruitmentJson.Ids(json, "srChars"); SrWeapons = RecruitmentJson.Ids(json, "srWeapons"); RItems = RecruitmentJson.Ids(json, "rItems", true);
        }
        public string Id { get; }
        public string Name { get; }
        public string Kind { get; }
        public string Ticket { get; }
        public int CostPerPull { get; }
        public int TenPullTickets { get; }
        public RecruitmentCurve Curve { get; }
        public IReadOnlyList<string> SsrPool { get; }
        public IReadOnlyList<string> SrChars { get; }
        public IReadOnlyList<string> SrWeapons { get; }
        public IReadOnlyList<string> RItems { get; }
    }

    public sealed class RecruitmentPity
    {
        internal RecruitmentPity(JObject json)
        {
            RecruitmentJson.Fields(json, "n", "srN", "guar", "srGuar", "total", "done", "gotSsr", "selectorUsed");
            N = P.Count(json, "n"); SrN = P.Count(json, "srN"); Total = P.Count(json, "total");
            Guar = RecruitmentJson.Bool(json, "guar"); SrGuar = RecruitmentJson.Bool(json, "srGuar"); Done = RecruitmentJson.Bool(json, "done");
            GotSsr = RecruitmentJson.Bool(json, "gotSsr"); SelectorUsed = RecruitmentJson.Bool(json, "selectorUsed");
        }
        public int N { get; }
        public int SrN { get; }
        public int Total { get; }
        public bool Guar { get; }
        public bool SrGuar { get; }
        public bool Done { get; }
        public bool GotSsr { get; }
        public bool SelectorUsed { get; }
    }

    public sealed class RecruitmentPull
    {
        internal RecruitmentPull(JObject json)
        {
            RecruitmentJson.Fields(json, "rarity", "kind", "id", "featured", "lost", "pityAt", "isNew", "resonance", "refine", "shards", "afterglow");
            Rarity = RecruitmentJson.Choice(json, "rarity", "SSR", "SR", "R"); Kind = RecruitmentJson.Choice(json, "kind", "char", "weapon", "item"); Id = P.Text(json, "id");
            if (Id.Length == 0 || Id.Length > 256 || Kind != "item" && !PracticeCommandOutbox.IsPublicId(Id)) throw P.Invalid("result id");
            Featured = RecruitmentJson.Bool(json, "featured"); Lost = RecruitmentJson.Bool(json, "lost"); IsNew = RecruitmentJson.Bool(json, "isNew");
            PityAt = P.Count(json, "pityAt"); Resonance = P.Count(json, "resonance"); Refine = P.Count(json, "refine"); Shards = P.Count(json, "shards"); Afterglow = P.Count(json, "afterglow");
        }
        public string Rarity { get; }
        public string Kind { get; }
        public string Id { get; }
        public bool Featured { get; }
        public bool Lost { get; }
        public bool IsNew { get; }
        public int PityAt { get; }
        public int Resonance { get; }
        public int Refine { get; }
        public int Shards { get; }
        public int Afterglow { get; }
    }

    public sealed class RecruitmentHistoryEntry
    {
        internal RecruitmentHistoryEntry(JObject json)
        {
            RecruitmentJson.Fields(json, "requestId", "bannerId", "results");
            RequestId = RecruitmentJson.Id(json, "requestId"); BannerId = RecruitmentJson.Id(json, "bannerId");
            Results = RecruitmentJson.List(json, "results", item => new RecruitmentPull(item));
            if (Results.Count != 1 && Results.Count != 10) throw P.Invalid("history count");
        }
        public string RequestId { get; }
        public string BannerId { get; }
        public IReadOnlyList<RecruitmentPull> Results { get; }
    }

    public sealed class RecruitmentSnapshot
    {
        readonly JObject json;
        RecruitmentSnapshot(JObject json, string owner)
        {
            RecruitmentJson.Fields(json, "accountId", "revision", "wallet", "ownedChars", "ownedWeapons", "pity", "launchClaimed", "banners", "history");
            AccountId = P.Text(json, "accountId"); if (AccountId != owner) throw P.Mismatch("recruitment owner");
            Revision = RecruitmentJson.Long(json, "revision");
            var wallet = P.Object(json, "wallet"); RecruitmentJson.Fields(wallet, "freeCrystals", "paidCrystals", "tickets", "shards", "afterglow");
            FreeCrystals = RecruitmentJson.Long(wallet, "freeCrystals"); PaidCrystals = RecruitmentJson.Long(wallet, "paidCrystals");
            if (FreeCrystals > long.MaxValue - PaidCrystals) throw P.Invalid("wallet crystal total");
            Shards = RecruitmentJson.Long(wallet, "shards"); Afterglow = RecruitmentJson.Long(wallet, "afterglow");
            var tickets = P.Object(wallet, "tickets"); RecruitmentJson.Fields(tickets, "limitedTicket", "weaponTicket", "standardTicket");
            Tickets = RecruitmentJson.Counts(tickets, true); OwnedChars = RecruitmentJson.Counts(P.Object(json, "ownedChars"), false); OwnedWeapons = RecruitmentJson.Counts(P.Object(json, "ownedWeapons"), false);
            var pity = P.Object(json, "pity"); RecruitmentJson.Fields(pity, "character", "weapon", "standard", "beginner");
            var pities = new Dictionary<string, RecruitmentPity>(StringComparer.Ordinal);
            foreach (var entry in pity.Properties()) pities.Add(entry.Name, new RecruitmentPity(entry.Value as JObject ?? throw P.Invalid("pity")));
            Pity = new System.Collections.ObjectModel.ReadOnlyDictionary<string, RecruitmentPity>(pities);
            LaunchClaimed = RecruitmentJson.Bool(json, "launchClaimed"); Banners = RecruitmentJson.List(json, "banners", item => new RecruitmentBanner(item));
            if (Banners.Select(b => b.Id).Distinct(StringComparer.Ordinal).Count() != Banners.Count) throw P.Invalid("duplicate banner");
            History = RecruitmentJson.List(json, "history", item => new RecruitmentHistoryEntry(item));
            if (History.Select(h => h.RequestId).Distinct(StringComparer.Ordinal).Count() != History.Count) throw P.Invalid("duplicate history");
            this.json = (JObject)json.DeepClone();
        }
        public string AccountId { get; }
        public long Revision { get; }
        public long FreeCrystals { get; }
        public long PaidCrystals { get; }
        public long Shards { get; }
        public long Afterglow { get; }
        public IReadOnlyDictionary<string, int> Tickets { get; }
        public IReadOnlyDictionary<string, int> OwnedChars { get; }
        public IReadOnlyDictionary<string, int> OwnedWeapons { get; }
        public IReadOnlyDictionary<string, RecruitmentPity> Pity { get; }
        public bool LaunchClaimed { get; }
        public IReadOnlyList<RecruitmentBanner> Banners { get; }
        public IReadOnlyList<RecruitmentHistoryEntry> History { get; }
        public JObject ToJObject() => (JObject)json.DeepClone();
        public static RecruitmentSnapshot Parse(byte[] utf8, string owner) => new RecruitmentSnapshot(P.ParseRecruitmentObject(utf8), owner);
        internal static RecruitmentSnapshot Parse(JObject json, string owner) => new RecruitmentSnapshot(json, owner);
    }

    public sealed class RecruitmentReceipt
    {
        RecruitmentReceipt(string requestId, string accountId, long revision, string kind, IReadOnlyList<RecruitmentPull> results, RecruitmentSnapshot snapshot)
        {
            RequestId = requestId; AccountId = accountId; Revision = revision; Kind = kind; Results = results; Snapshot = snapshot;
        }
        public string RequestId { get; }
        public string AccountId { get; }
        public long Revision { get; }
        public string Kind { get; }
        public IReadOnlyList<RecruitmentPull> Results { get; }
        public RecruitmentSnapshot Snapshot { get; }

        public static RecruitmentReceipt Parse(PendingPracticeCommand request, byte[] utf8)
        {
            if (!PracticeCommandOutbox.IsRecruitmentRoute(request.Route) || !request.ExpectedRevision.HasValue) throw P.Mismatch("recruitment route");
            var json = P.ParseRecruitmentObject(utf8); RecruitmentJson.Fields(json, "requestId", "accountId", "revision", "kind", "results", "snapshot");
            string requestId = RecruitmentJson.Id(json, "requestId"), accountId = P.Text(json, "accountId"), kind = P.Text(json, "kind");
            long revision = RecruitmentJson.Long(json, "revision");
            string expectedKind = request.Route == PracticeCommandOutbox.RecruitmentPullRoute ? "pull" : request.Route == PracticeCommandOutbox.RecruitmentLaunchRoute ? "launch" : "selector";
            var body = P.ParseRecruitmentObject(request.BodyUtf8());
            int count = expectedKind == "pull" ? P.Count(body, "count") : expectedKind == "selector" ? 1 : 0;
            var results = RecruitmentJson.List(json, "results", item => new RecruitmentPull(item));
            var snapshot = RecruitmentSnapshot.Parse(P.Object(json, "snapshot"), request.Account);
            if (requestId != request.RequestId || accountId != request.Account || kind != expectedKind || results.Count != count
                || request.ExpectedRevision.Value == long.MaxValue || revision != request.ExpectedRevision.Value + 1 || snapshot.Revision != revision) throw P.Mismatch("recruitment receipt");
            if (kind == "selector" && (results[0].Kind != "char" || results[0].Id != P.Text(body, "charId"))) throw P.Mismatch("selector result");
            return new RecruitmentReceipt(requestId, accountId, revision, kind, results, snapshot);
        }
    }
}
