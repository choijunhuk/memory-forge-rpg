// RECRUIT-SERVER-20261021 §12.1–§12.4/§17.2: actual recruitment for the authenticated accounts of the LOCAL DEVELOPMENT practice host
// (/dev/practice/recruitment*). It is not the production API of server/openapi.yaml, a payment, a shop or a production account system.
// The rules are Core's Gacha state machine on an unseeded GachaState with the server's CSPRNG (RandomNumberGenerator.GetInt32) injected
// through the bounded-source overloads, and every (bound, value) it returns is recorded as the pull's private draw tape (the §17.2 roll
// audit, never public); no seeded RNG is used here. Each mutation body carries expectedRevision: after Mutate's receipt lookup (a committed
// retry always wins) the callback refuses any other envelope revision with 409 STALE_REVISION before any rule, banner or Core work, and a
// success commits exactly expectedRevision + 1. Core's results, the debit (tickets first, then free before paid crystals, checked
// arithmetic), duplicate rewards, pity, ownership, the append-only ledger entry and the receipt are committed in that one envelope write
// before the reply, so skipping or crashing an animation cannot reroll or lose a result; a refusal or failure commits nothing. The stored
// record is never trusted on its own: every decode and every candidate replays the chronological ledger through Core from an empty
// unseeded state and a zero wallet (the approved launch grant, Core Pull over the recorded tape, Core ClaimSelector) and must reproduce
// the stored results, debits, balances, copies and pity exactly; each entry's stored receipt must be byte for byte the reply projected
// from the replayed prefix at that entry (the same projection Commit replies with), and a receipt that is not a run reply must have its
// ledger entry (also when the member is absent, null or emptied). That is internal consistency, not authentication (no MAC): an envelope
// that never recruited, or from which a whole consistent history was removed, is indistinguishable. Standard and beginner banners are
// active; a limited/weapon banner needs a rotation epoch this unit does not have and is BANNER_INACTIVE. The launch claim grants exactly the bound content's
// progression.seasonIncome.oneTime.launch, once per account: no beginner tickets, prologue characters or fixture-party ownership. Core
// currently lets a beginner ten-pull pay missing tickets with crystals (costPerPull each); that is kept unchanged and disclosed, not
// altered here. Public views carry no RNG, draw tape, seed, ledger debit, receipt, identity or credential.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    /// <summary>The bound content's recruitment data, read from the very bytes ContentSha256 hashes: the linked GachaDataLoader's
    /// GachaConfig, the active banners (standard and beginner, in content order) and the approved launch reward. Anything outside these
    /// checks refuses startup.</summary>
    sealed class RecruitmentContent
    {
        public static readonly string[] TicketIds = { "limitedTicket", "weaponTicket", "standardTicket" };
        public static readonly string[] PityKinds = { "character", "weapon", "standard", "beginner" };
        public const string LaunchTicket = "standardTicket";
        /// <summary>Copies per ID kept far below Int32 overflow; a pull adds at most ten.</summary>
        public const int MaxCopies = 1_000_000;
        public GachaConfig Config { get; }
        public IReadOnlyList<GachaBanner> Active { get; }
        public long LaunchCrystals { get; }
        public int LaunchTickets { get; }

        RecruitmentContent(GachaConfig config, List<GachaBanner> active, long launchCrystals, int launchTickets)
        {
            Config = config; Active = active; LaunchCrystals = launchCrystals; LaunchTickets = launchTickets;
        }

        public static RecruitmentContent Load(JsonElement root)
        {
            var cfg = GachaDataLoader.Load(root.GetProperty("gacha"));
            var launch = root.GetProperty("progression").GetProperty("seasonIncome").GetProperty("oneTime").GetProperty("launch");
            BattleDataLoader.Fields(launch, "crystals", LaunchTicket);
            long crystals = launch.GetProperty("crystals").GetInt64();
            int tickets = launch.GetProperty(LaunchTicket).GetInt32();
            Require(crystals >= 1 && crystals <= CanonicalState.MaxSafeInteger && tickets >= 0, "launch reward");
            Require(cfg.CostPerPull >= 1 && cfg.ShardSsr >= 0 && cfg.ShardSsrOverflow >= 0 && cfg.ShardSr >= 0 && cfg.ShardSrOverflow >= 0 && cfg.AfterglowR >= 0, "prices and duplicates");
            foreach (string kind in PityKinds)
                Require(cfg.Curves.TryGetValue(kind, out var c) && c.SsrBase >= 1 && c.SrBase >= 0 && c.SsrBase + c.SrBase <= Gacha.Bps && c.SoftStart >= 1 && c.SoftStep >= 0
                    && c.Hard >= 1 && c.SrGuarantee >= 1, "curve " + kind);
            Require(cfg.Curves["standard"].SelectorAt >= 1 && cfg.Curves["beginner"].MaxPulls >= 10 && cfg.Curves["beginner"].TenPullTickets >= 1, "selector and beginner limits");
            Require(cfg.StandardSsrChars.Count > 0 && cfg.SrChars.Count + cfg.SrWeapons.Count > 0 && cfg.RItems.Count > 0, "pools");
            Require(cfg.Banners.Select(b => b.Id).Distinct(StringComparer.Ordinal).Count() == cfg.Banners.Count, "banner IDs");
            var active = cfg.Banners.Where(b => b.Kind == "standard" || b.Kind == "beginner").ToList();
            Require(active.Count > 0 && active.All(b => Contracts.IsReference(b.Id) && b.Name.Length > 0 && TicketIds.Contains(b.Ticket)), "active banners");
            return new RecruitmentContent(cfg, active, crystals, tickets);
        }

        /// <summary>An active banner, or 422: UNKNOWN_BANNER for an ID the content does not have, BANNER_INACTIVE for a limited/weapon one.</summary>
        public GachaBanner ActiveBanner(string id) => Active.FirstOrDefault(b => b.Id == id)
            ?? throw new ApiError(422, Config.Banners.Any(b => b.Id == id) ? "BANNER_INACTIVE" : "UNKNOWN_BANNER");

        static void Require(bool condition, string what) { if (!condition) throw new InvalidDataException("recruitment content: " + what); }
    }

    /// <summary>One committed recruitment request in the private ledger, with the envelope revision it was committed at (its
    /// expectedRevision): launch (the grant), pull (banner, count, the ticket/free/paid debit, Core's results and the private CSPRNG draw
    /// tape) or selector (the choice and its result). Append-only and chronological; history is never truncated.</summary>
    sealed class LedgerEntry
    {
        public const string Launch = "launch", Pull = "pull", Selector = "selector";
        public string RequestId = "", Kind = "", BannerId = "", Ticket = "", CharId = "";
        public long Revision;
        public int Count, TicketsUsed, GrantTickets;
        public long FreeCrystals, PaidCrystals, GrantCrystals;
        public List<PullResult> Results = new List<PullResult>();
        public List<(int Bound, int Value)> Draws = new List<(int Bound, int Value)>();

        /// <summary>The canonical request identity this entry was committed under (revision and count included); its receipt carries it.</summary>
        public string Identity() => Kind == Launch ? Contracts.LaunchIdentity(Revision)
            : Kind == Pull ? Contracts.PullIdentity(Revision, BannerId, Count) : Contracts.SelectorIdentity(Revision, CharId);

        public Dictionary<string, object?> ToGraph()
        {
            switch (Kind)
            {
                case Launch:
                    return GraphJson.Object("requestId", RequestId, "kind", Kind, "expectedRevision", Revision,
                        "grant", GraphJson.Object("freeCrystals", GrantCrystals, RecruitmentContent.LaunchTicket, GrantTickets));
                case Pull:
                    return GraphJson.Object("requestId", RequestId, "kind", Kind, "expectedRevision", Revision, "bannerId", BannerId, "count", Count,
                        "debit", GraphJson.Object("ticket", Ticket, "tickets", TicketsUsed, "freeCrystals", FreeCrystals, "paidCrystals", PaidCrystals),
                        "results", RecruitmentJson.Results(Results), "draws", Draws.Select(d => (object?)new List<object?> { d.Bound, d.Value }).ToList());
                default:
                    return GraphJson.Object("requestId", RequestId, "kind", Kind, "expectedRevision", Revision, "charId", CharId, "results", RecruitmentJson.Results(Results));
            }
        }

        public static LedgerEntry Parse(JsonElement json)
        {
            var entry = new LedgerEntry { Kind = Envelopes.Text(json, "kind") };
            switch (entry.Kind)
            {
                case Launch:
                    BattleDataLoader.Fields(json, "requestId", "kind", "expectedRevision", "grant");
                    var grant = json.GetProperty("grant");
                    BattleDataLoader.Fields(grant, "freeCrystals", RecruitmentContent.LaunchTicket);
                    entry.GrantCrystals = RecruitmentJson.Amount(grant, "freeCrystals");
                    entry.GrantTickets = RecruitmentJson.Small(grant, RecruitmentContent.LaunchTicket);
                    break;
                case Pull:
                    BattleDataLoader.Fields(json, "requestId", "kind", "expectedRevision", "bannerId", "count", "debit", "results", "draws");
                    var debit = json.GetProperty("debit");
                    BattleDataLoader.Fields(debit, "ticket", "tickets", "freeCrystals", "paidCrystals");
                    entry.BannerId = Envelopes.Text(json, "bannerId");
                    entry.Count = RecruitmentJson.Small(json, "count");
                    entry.Ticket = Envelopes.Text(debit, "ticket");
                    entry.TicketsUsed = RecruitmentJson.Small(debit, "tickets");
                    entry.FreeCrystals = RecruitmentJson.Amount(debit, "freeCrystals");
                    entry.PaidCrystals = RecruitmentJson.Amount(debit, "paidCrystals");
                    entry.Results = RecruitmentJson.ResultsOf(json.GetProperty("results"));
                    entry.Draws = RecruitmentJson.DrawsOf(json.GetProperty("draws"));
                    break;
                case Selector:
                    BattleDataLoader.Fields(json, "requestId", "kind", "expectedRevision", "charId", "results");
                    entry.CharId = Envelopes.Text(json, "charId");
                    entry.Results = RecruitmentJson.ResultsOf(json.GetProperty("results"));
                    break;
                default: throw new InvalidDataException("ledger kind");
            }
            entry.RequestId = Envelopes.Text(json, "requestId");
            entry.Revision = RecruitmentJson.Amount(json, "expectedRevision");
            if (!Contracts.IsRequestId(entry.RequestId)) throw new InvalidDataException("ledger requestId");
            return entry;
        }
    }

    /// <summary>The account's recruitment state in the private envelope ("recruitment"): free and paid crystals as separate balances, the
    /// three ticket kinds, shards, afterglow, owned copies, Core's four pity states, the launch claim and the ledger. An envelope without
    /// the member has never recruited (empty wallet and collection, zero pity, launch unclaimed); a present but malformed member is
    /// STORE_CORRUPT and is never repaired, reset or migrated.</summary>
    sealed class RecruitmentRecord
    {
        public long FreeCrystals, PaidCrystals, Shards, Afterglow;
        public bool LaunchClaimed;
        public readonly Dictionary<string, int> Tickets = RecruitmentContent.TicketIds.ToDictionary(id => id, id => 0, StringComparer.Ordinal);
        public readonly Dictionary<string, int> OwnedChars = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> OwnedWeapons = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, PityState> Pity = RecruitmentContent.PityKinds.ToDictionary(kind => kind, kind => new PityState(), StringComparer.Ordinal);
        public readonly List<LedgerEntry> Ledger = new List<LedgerEntry>();

        /// <summary>An unseeded Core state (only the injected-source overloads can draw for it) holding the total crystals; the free/paid
        /// split stays here.</summary>
        public GachaState ToState()
        {
            var g = new GachaState { Crystals = checked(FreeCrystals + PaidCrystals), Shards = Shards, Afterglow = Afterglow };
            foreach (string id in RecruitmentContent.TicketIds) g.Tickets[id] = Tickets[id];
            foreach (string kind in RecruitmentContent.PityKinds) CopyPity(Pity[kind], g.Pity[kind]);
            foreach (var pair in OwnedChars) g.OwnedChars.Add(pair.Key, pair.Value);
            foreach (var pair in OwnedWeapons) g.OwnedWeapons.Add(pair.Key, pair.Value);
            return g;
        }

        /// <summary>Everything Core changed except crystals, whose debit the caller splits into free and paid.</summary>
        public void FromState(GachaState g)
        {
            foreach (string id in RecruitmentContent.TicketIds) Tickets[id] = g.Tickets[id];
            Shards = g.Shards; Afterglow = g.Afterglow;
            foreach (string kind in RecruitmentContent.PityKinds) CopyPity(g.Pity[kind], Pity[kind]);
            OwnedChars.Clear(); foreach (var pair in g.OwnedChars) OwnedChars.Add(pair.Key, pair.Value);
            OwnedWeapons.Clear(); foreach (var pair in g.OwnedWeapons) OwnedWeapons.Add(pair.Key, pair.Value);
        }
        static void CopyPity(PityState from, PityState to)
        {
            to.N = from.N; to.SrN = from.SrN; to.Guar = from.Guar; to.SrGuar = from.SrGuar; to.Total = from.Total; to.Done = from.Done; to.GotSsr = from.GotSsr; to.SelectorUsed = from.SelectorUsed;
        }

        public Dictionary<string, object?> Wallet() => GraphJson.Object("freeCrystals", FreeCrystals, "paidCrystals", PaidCrystals,
            "tickets", GraphJson.Object("limitedTicket", Tickets["limitedTicket"], "weaponTicket", Tickets["weaponTicket"], "standardTicket", Tickets["standardTicket"]),
            "shards", Shards, "afterglow", Afterglow);
        public Dictionary<string, object?> PityGraph()
        {
            var view = GraphJson.Object();
            foreach (string kind in RecruitmentContent.PityKinds) view.Add(kind, RecruitmentJson.Pity(Pity[kind]));
            return view;
        }

        public Dictionary<string, object?> ToGraph() => GraphJson.Object("wallet", Wallet(), "ownedChars", RecruitmentJson.Counts(OwnedChars), "ownedWeapons", RecruitmentJson.Counts(OwnedWeapons),
            "pity", PityGraph(), "launchClaimed", LaunchClaimed, "ledger", Ledger.Select(e => (object?)e.ToGraph()).ToList());

        /// <summary>The structural parse of the stored member (exact allowlists, JS-safe non-negative integers, booleans); the content and
        /// receipt invariants are CheckEnvelope and Validate, which run after the envelope's binding was accepted.</summary>
        public static RecruitmentRecord Parse(JsonElement json)
        {
            BattleDataLoader.Fields(json, "wallet", "ownedChars", "ownedWeapons", "pity", "launchClaimed", "ledger");
            var record = new RecruitmentRecord();
            var wallet = json.GetProperty("wallet");
            BattleDataLoader.Fields(wallet, "freeCrystals", "paidCrystals", "tickets", "shards", "afterglow");
            record.FreeCrystals = RecruitmentJson.Amount(wallet, "freeCrystals");
            record.PaidCrystals = RecruitmentJson.Amount(wallet, "paidCrystals");
            record.Shards = RecruitmentJson.Amount(wallet, "shards");
            record.Afterglow = RecruitmentJson.Amount(wallet, "afterglow");
            var tickets = wallet.GetProperty("tickets");
            BattleDataLoader.Fields(tickets, RecruitmentContent.TicketIds);
            foreach (string id in RecruitmentContent.TicketIds) record.Tickets[id] = RecruitmentJson.Small(tickets, id);
            RecruitmentJson.Copies(json.GetProperty("ownedChars"), record.OwnedChars);
            RecruitmentJson.Copies(json.GetProperty("ownedWeapons"), record.OwnedWeapons);
            var pity = json.GetProperty("pity");
            BattleDataLoader.Fields(pity, RecruitmentContent.PityKinds);
            foreach (string kind in RecruitmentContent.PityKinds) record.Pity[kind] = RecruitmentJson.PityOf(pity.GetProperty(kind));
            record.LaunchClaimed = RecruitmentJson.Flag(json, "launchClaimed");
            var ledger = json.GetProperty("ledger");
            if (ledger.ValueKind != JsonValueKind.Array) throw new InvalidDataException("ledger");
            foreach (var entry in ledger.EnumerateArray()) record.Ledger.Add(LedgerEntry.Parse(entry));
            return record;
        }

        /// <summary>Reconciles the record with its ledger through Core (decode: else STORE_CORRUPT; every candidate before it is committed).
        /// The chronological ledger is replayed from an empty unseeded GachaState and a zero free/paid wallet: unique request IDs at strictly
        /// increasing revisions; a launch entry grants exactly the approved reward, once; a pull entry runs Core Gacha.Pull on its active
        /// banner over its recorded draw tape (each bound must match, Core range-checks each value, the tape must be used up) and must
        /// reproduce its ordered results with their duplicate grants and its ticket and free-before-paid crystal debit; a selector entry runs
        /// Core ClaimSelector and must reproduce its result. The stored balances, tickets, shards, afterglow, copies, pity and launch flag must
        /// then equal the replay exactly. No seeded RNG and no new randomness is involved; the business rules are Core's, not repeated here.
        /// During the same single replay a running prefix record (the record as it stood right after each entry; nothing is replayed again)
        /// projects that entry's exact public reply through RecruitmentCommands.ReplyText, the one projection Commit replies with, and the
        /// entry's stored receipt must hold exactly those bytes with status 200, the entry's canonical identity and a revision below the
        /// committing revision; an extra, missing, changed or private member anywhere in a receipt is refused. Only the pending candidate
        /// entry (the last one, whose receipt Mutate adds after the callback) has no receipt yet; its projected reply is returned.</summary>
        public string? Validate(RecruitmentContent content, string accountId, IReadOnlyDictionary<string, StoredReceipt> receipts, long revision, string? pending)
        {
            var cfg = content.Config;
            var g = new GachaState();
            var prefix = new RecruitmentRecord();
            long free = 0, paid = 0, last = -1;
            bool launched = false;
            string? pendingReply = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Ledger.Count; i++)
            {
                var entry = Ledger[i];
                Require(seen.Add(entry.RequestId) && entry.Revision > last && entry.Revision < revision, "ledger order");
                last = entry.Revision;
                switch (entry.Kind)
                {
                    case LedgerEntry.Launch:
                        Require(!launched && entry.GrantCrystals == content.LaunchCrystals && entry.GrantTickets == content.LaunchTickets, "launch entry");
                        launched = true;
                        free = checked(free + entry.GrantCrystals);
                        g.Crystals = checked(g.Crystals + entry.GrantCrystals);
                        g.Tickets[RecruitmentContent.LaunchTicket] = checked(g.Tickets[RecruitmentContent.LaunchTicket] + entry.GrantTickets);
                        break;
                    case LedgerEntry.Pull:
                    {
                        var banner = content.Active.FirstOrDefault(b => b.Id == entry.BannerId) ?? throw new InvalidDataException("recruitment record: pull banner");
                        Require(entry.Ticket == banner.Ticket, "pull ticket");
                        long crystals = g.Crystals;
                        int tickets = g.Tickets[banner.Ticket], at = 0;
                        List<PullResult> results;
                        try
                        {
                            results = Gacha.Pull(cfg, g, banner.Id, entry.Count, n =>
                            {
                                if (at >= entry.Draws.Count || entry.Draws[at].Bound != n) throw new InvalidDataException("recruitment record: draw tape");
                                return entry.Draws[at++].Value;
                            });
                        }
                        catch (RuleException) { throw new InvalidDataException("recruitment record: a recorded pull Core refuses"); }
                        Require(at == entry.Draws.Count && SameResults(results, entry.Results), "pull results");
                        long spent = checked(crystals - g.Crystals), fromFree = Math.Min(free, spent);
                        Require(checked(tickets - g.Tickets[banner.Ticket]) == entry.TicketsUsed && fromFree == entry.FreeCrystals && checked(spent - fromFree) == entry.PaidCrystals, "pull debit");
                        free = checked(free - fromFree);
                        paid = checked(paid - (spent - fromFree));
                        break;
                    }
                    default:
                    {
                        PullResult granted;
                        try { granted = Gacha.ClaimSelector(cfg, g, entry.CharId); }
                        catch (RuleException) { throw new InvalidDataException("recruitment record: a recorded selector claim Core refuses"); }
                        Require(SameResults(new List<PullResult> { granted }, entry.Results), "selector result");
                        break;
                    }
                }
                prefix.FreeCrystals = free; prefix.PaidCrystals = paid; prefix.LaunchClaimed = launched;
                prefix.FromState(g);
                prefix.Ledger.Add(entry);
                string reply = RecruitmentCommands.ReplyText(content, accountId, entry, prefix);
                if (entry.RequestId == pending)
                {
                    Require(i == Ledger.Count - 1 && !receipts.ContainsKey(entry.RequestId), "pending entry");
                    pendingReply = reply;
                }
                else Require(receipts.TryGetValue(entry.RequestId, out var receipt) && receipt.Status == 200 && receipt.Identity == entry.Identity() && receipt.Body == reply,
                    "ledger receipt");
            }
            Require(pending == null || pendingReply != null, "pending entry");
            Require(free >= 0 && paid >= 0 && checked(free + paid) == g.Crystals, "crystal ledgers");
            Require(FreeCrystals == free && PaidCrystals == paid && Shards == g.Shards && Afterglow == g.Afterglow && LaunchClaimed == launched
                && RecruitmentContent.TicketIds.All(id => Tickets[id] == g.Tickets[id]) && SameCounts(OwnedChars, g.OwnedChars) && SameCounts(OwnedWeapons, g.OwnedWeapons)
                && RecruitmentContent.PityKinds.All(kind => SamePity(Pity[kind], g.Pity[kind])), "the stored state does not reconcile with the ledger");
            return pendingReply;
        }

        /// <summary>The envelope-wide recruitment relation, at decode after the binding: every receipt that is not a run reply must belong to a
        /// ledger entry, so recruitment receipts with an absent, null or emptied member, or an orphan recruitment receipt, are corrupt; a
        /// present record must reconcile through Core with the exact bytes of every entry's receipt (Validate). An envelope without the member
        /// and without recruitment receipts (an account that never recruited) is accepted. This proves internal consistency only; it is not
        /// an authentication of the owner's whole history (there is no MAC).</summary>
        public static void CheckEnvelope(RecruitmentContent content, AccountEnvelope envelope)
        {
            var ledger = new HashSet<string>(envelope.Recruitment?.Ledger.Select(e => e.RequestId) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            foreach (var pair in envelope.Receipts)
                Require(ledger.Contains(pair.Key) || IsRunReply(pair.Value.Body), "a recruitment receipt without its ledger entry");
            envelope.Recruitment?.Validate(content, envelope.AccountId, envelope.Receipts, envelope.Revision, null);
        }

        // The replies of Mutate's run callbacks (Create and Execute in Program.cs): one JSON object of only these members with a string runId.
        // Anything else stored as a receipt must be a ledger-backed recruitment reply.
        static readonly HashSet<string> RunReplyMembers = new HashSet<string>(StringComparer.Ordinal) { "runId", "version", "phase", "memoryVersion", "events", "result", "state" };
        static bool IsRunReply(string body)
        {
            try
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(body), new JsonReaderOptions { MaxDepth = 256 });
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
                bool runId = false;
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    string name = reader.GetString() ?? "";
                    if (!RunReplyMembers.Contains(name) || !reader.Read()) return false;
                    if (name == "runId") runId = reader.TokenType == JsonTokenType.String;
                    reader.Skip();
                }
                return runId && reader.TokenType == JsonTokenType.EndObject && !reader.Read();
            }
            catch (JsonException) { return false; }
        }

        static bool SameResults(List<PullResult> core, List<PullResult> stored) => core.Count == stored.Count && core.Zip(stored).All(p => p.First.Rarity == p.Second.Rarity
            && p.First.Kind == p.Second.Kind && p.First.Id == p.Second.Id && p.First.Featured == p.Second.Featured && p.First.Lost == p.Second.Lost && p.First.PityAt == p.Second.PityAt
            && p.First.IsNew == p.Second.IsNew && p.First.Resonance == p.Second.Resonance && p.First.Refine == p.Second.Refine && p.First.Shards == p.Second.Shards
            && p.First.Afterglow == p.Second.Afterglow);
        static bool SameCounts(Dictionary<string, int> stored, Dictionary<string, int> core) => stored.Count == core.Count && stored.All(p => core.TryGetValue(p.Key, out int n) && n == p.Value);
        static bool SamePity(PityState a, PityState b) => a.N == b.N && a.SrN == b.SrN && a.Guar == b.Guar && a.SrGuar == b.SrGuar && a.Total == b.Total && a.Done == b.Done
            && a.GotSsr == b.GotSsr && a.SelectorUsed == b.SelectorUsed;
        static void Require(bool condition, string what) { if (!condition) throw new InvalidDataException("recruitment record: " + what); }
    }

    /// <summary>Graph and strict parse helpers of the recruitment member and views.</summary>
    static class RecruitmentJson
    {
        static readonly string[] ResultMembers = { "rarity", "kind", "id", "featured", "lost", "pityAt", "isNew", "resonance", "refine", "shards", "afterglow" };
        static readonly string[] PityMembers = { "n", "srN", "guar", "srGuar", "total", "done", "gotSsr", "selectorUsed" };

        /// <summary>The public PullResult fields, in this order; the same members are stored in the ledger.</summary>
        public static Dictionary<string, object?> Result(PullResult r) => GraphJson.Object("rarity", r.Rarity, "kind", r.Kind, "id", r.Id, "featured", r.Featured,
            "lost", r.Lost, "pityAt", r.PityAt, "isNew", r.IsNew, "resonance", r.Resonance, "refine", r.Refine, "shards", r.Shards, "afterglow", r.Afterglow);
        public static List<object?> Results(IEnumerable<PullResult> results) => results.Select(r => (object?)Result(r)).ToList();
        public static Dictionary<string, object?> Pity(PityState p) => GraphJson.Object("n", p.N, "srN", p.SrN, "guar", p.Guar, "srGuar", p.SrGuar, "total", p.Total,
            "done", p.Done, "gotSsr", p.GotSsr, "selectorUsed", p.SelectorUsed);
        /// <summary>{id: copies} in ordinal ID order, so a mutation reply and a later read serialize the same state identically.</summary>
        public static Dictionary<string, object?> Counts(IReadOnlyDictionary<string, int> counts)
        {
            var view = GraphJson.Object();
            foreach (var pair in counts.OrderBy(p => p.Key, StringComparer.Ordinal)) view.Add(pair.Key, pair.Value);
            return view;
        }
        public static List<object?> Texts(IEnumerable<string> values) => values.Select(v => (object?)v).ToList();

        public static List<PullResult> ResultsOf(JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Array) throw new InvalidDataException("results");
            return json.EnumerateArray().Select(ResultOf).ToList();
        }
        /// <summary>The private draw tape [[bound, value], ...]: bound ≥ 1, value ≥ 0; Core range-checks value &lt; bound when it is replayed.</summary>
        public static List<(int Bound, int Value)> DrawsOf(JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Array) throw new InvalidDataException("draws");
            var draws = new List<(int Bound, int Value)>();
            foreach (var pair in json.EnumerateArray())
            {
                if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2 || !Integer(pair[0], out int bound) || bound < 1 || !Integer(pair[1], out int value))
                    throw new InvalidDataException("draws");
                draws.Add((bound, value));
            }
            return draws;
        }
        static bool Integer(JsonElement json, out int value)
        {
            value = 0;
            return json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out value) && value >= 0;
        }
        static PullResult ResultOf(JsonElement json)
        {
            BattleDataLoader.Fields(json, ResultMembers);
            var r = new PullResult
            {
                Rarity = Envelopes.Text(json, "rarity"), Kind = Envelopes.Text(json, "kind"), Id = Envelopes.Text(json, "id"), Featured = Flag(json, "featured"), Lost = Flag(json, "lost"),
                PityAt = Small(json, "pityAt"), IsNew = Flag(json, "isNew"), Resonance = Small(json, "resonance"), Refine = Small(json, "refine"), Shards = Small(json, "shards"),
                Afterglow = Small(json, "afterglow")
            };
            if (!(r.Rarity == "SSR" || r.Rarity == "SR" || r.Rarity == "R") || !(r.Kind == "char" || r.Kind == "weapon" || r.Kind == "item") || r.Resonance > 6 || r.Refine > 5)
                throw new InvalidDataException("result");
            return r;
        }
        public static PityState PityOf(JsonElement json)
        {
            BattleDataLoader.Fields(json, PityMembers);
            return new PityState
            {
                N = Small(json, "n"), SrN = Small(json, "srN"), Guar = Flag(json, "guar"), SrGuar = Flag(json, "srGuar"), Total = Small(json, "total"), Done = Flag(json, "done"),
                GotSsr = Flag(json, "gotSsr"), SelectorUsed = Flag(json, "selectorUsed")
            };
        }
        public static void Copies(JsonElement json, Dictionary<string, int> into)
        {
            if (json.ValueKind != JsonValueKind.Object) throw new InvalidDataException("copies");
            foreach (var property in json.EnumerateObject())
            {
                var value = property.Value;
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long copies) || copies < 1 || copies > RecruitmentContent.MaxCopies) throw new InvalidDataException("copies");
                into.Add(BattleDataLoader.Name(property), (int)copies); // a repeated ID throws
            }
        }
        /// <summary>A JS-safe non-negative integer member.</summary>
        public static long Amount(JsonElement json, string name)
        {
            var value = json.GetProperty(name);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long n) || n < 0 || n > CanonicalState.MaxSafeInteger) throw new InvalidDataException(name);
            return n;
        }
        public static int Small(JsonElement json, string name)
        {
            long n = Amount(json, name);
            return n <= int.MaxValue ? (int)n : throw new InvalidDataException(name);
        }
        public static bool Flag(JsonElement json, string name)
        {
            var value = json.GetProperty(name);
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            throw new InvalidDataException(name);
        }
    }

    /// <summary>The recruitment read and the three mutations. Each mutation runs as a PracticeRunStore.Mutate callback on the freshly
    /// decoded envelope (a committed receipt is returned before this code runs; afterwards receipt + revision + one durable write); it
    /// never touches the run, the archive or the memory store.</summary>
    static class RecruitmentCommands
    {
        /// <summary>The public snapshot (also the "snapshot" of every mutation reply): the committed envelope revision, wallet, owned
        /// copies, the four pity states, the launch claim, the active banners with their published curve and pools, and the pull history
        /// newest first (only pull entries, each with its 1 or 10 results). Launch and selector claims are not pulls; the private ledger
        /// keeps every entry in commit order.</summary>
        public static Dictionary<string, object?> Snapshot(RecruitmentContent content, string accountId, long revision, RecruitmentRecord? stored)
        {
            var record = stored ?? new RecruitmentRecord();
            var history = new List<object?>();
            for (int i = record.Ledger.Count - 1; i >= 0; i--)
            {
                var e = record.Ledger[i];
                if (e.Kind == LedgerEntry.Pull) history.Add(GraphJson.Object("requestId", e.RequestId, "bannerId", e.BannerId, "results", RecruitmentJson.Results(e.Results)));
            }
            return GraphJson.Object("accountId", accountId, "revision", revision, "wallet", record.Wallet(),
                "ownedChars", RecruitmentJson.Counts(record.OwnedChars), "ownedWeapons", RecruitmentJson.Counts(record.OwnedWeapons), "pity", record.PityGraph(),
                "launchClaimed", record.LaunchClaimed, "banners", content.Active.Select(b => (object?)Banner(content.Config, b)).ToList(), "history", history);
        }

        // tenPullTickets mirrors Core's Pull: the beginner ten-pull's TenPullTickets, otherwise one ticket per pull. Without tickets Core charges
        // costPerPull crystals per missing ticket, on the beginner banner too (the disclosed fallback). maxPulls (beginner cap) and selectorAt
        // (standard selector total) are Core's curve values, 0 where the curve has none.
        static Dictionary<string, object?> Banner(GachaConfig cfg, GachaBanner b)
        {
            var c = cfg.Curves[Gacha.KindOf(b)];
            return GraphJson.Object("id", b.Id, "name", b.Name, "kind", b.Kind, "ticket", b.Ticket, "costPerPull", cfg.CostPerPull,
                "tenPullTickets", b.Kind == "beginner" ? cfg.Curves["beginner"].TenPullTickets : 10,
                "curve", GraphJson.Object("ssrBase", c.SsrBase, "softStart", c.SoftStart, "softStep", c.SoftStep, "hard", c.Hard, "srBase", c.SrBase, "srGuarantee", c.SrGuarantee,
                    "maxPulls", c.MaxPulls, "selectorAt", c.SelectorAt),
                "ssrPool", RecruitmentJson.Texts(cfg.StandardSsrChars), "srChars", RecruitmentJson.Texts(cfg.SrChars), "srWeapons", RecruitmentJson.Texts(cfg.SrWeapons),
                "rItems", RecruitmentJson.Texts(cfg.RItems));
        }

        // First check of every mutation callback, before any rule, banner or Core work: the request was made against this committed revision.
        static void RequireRevision(AccountEnvelope envelope, long expected)
        {
            if (envelope.Revision != expected) throw new ApiError(409, "STALE_REVISION");
        }

        /// <summary>1 or 10 pulls (Core decides BAD_COUNT, BEGINNER_TEN_ONLY, BEGINNER_LIMIT and CRYSTALS_LOW before any draw) with the CSPRNG;
        /// each bounded draw is recorded as the entry's private tape. Tickets first (Core), then the crystal debit is split free before paid.
        /// Core's SpentCrystals counts ticket equivalents and is not used as a crystal delta.</summary>
        public static Reply Pull(RecruitmentContent content, AccountEnvelope envelope, PullRequest request)
        {
            RequireRevision(envelope, request.ExpectedRevision);
            var banner = content.ActiveBanner(request.BannerId);
            var record = envelope.Recruitment ?? new RecruitmentRecord();
            var g = record.ToState();
            long crystals = g.Crystals;
            int tickets = g.Tickets[banner.Ticket];
            var draws = new List<(int Bound, int Value)>();
            List<PullResult> results;
            try
            {
                results = Gacha.Pull(content.Config, g, banner.Id, request.Count, bound =>
                {
                    int value = RandomNumberGenerator.GetInt32(bound);
                    draws.Add((bound, value));
                    return value;
                });
            }
            catch (RuleException error) { throw new ApiError(422, error.Code); }
            long spent = checked(crystals - g.Crystals);
            int used = checked(tickets - g.Tickets[banner.Ticket]);
            long free = Math.Min(record.FreeCrystals, spent), paid = checked(spent - free);
            if (spent < 0 || used < 0 || paid > record.PaidCrystals || results.Count != request.Count) throw new InvalidOperationException("an inconsistent recruitment debit");
            record.FreeCrystals = checked(record.FreeCrystals - free);
            record.PaidCrystals = checked(record.PaidCrystals - paid);
            record.FromState(g);
            record.Ledger.Add(new LedgerEntry
            {
                RequestId = request.RequestId, Kind = LedgerEntry.Pull, Revision = request.ExpectedRevision, BannerId = banner.Id, Count = request.Count, Ticket = banner.Ticket,
                TicketsUsed = used, FreeCrystals = free, PaidCrystals = paid, Results = results, Draws = draws
            });
            return Commit(content, envelope, record, request.RequestId);
        }

        /// <summary>The once-per-account launch reward of the bound content: free crystals and standard tickets, nothing else. A second
        /// claim at the current revision is 422 LAUNCH_ALREADY_CLAIMED.</summary>
        public static Reply ClaimLaunch(RecruitmentContent content, AccountEnvelope envelope, LaunchClaimRequest request)
        {
            RequireRevision(envelope, request.ExpectedRevision);
            var record = envelope.Recruitment ?? new RecruitmentRecord();
            if (record.LaunchClaimed) throw new ApiError(422, "LAUNCH_ALREADY_CLAIMED");
            record.FreeCrystals = checked(record.FreeCrystals + content.LaunchCrystals);
            record.Tickets[RecruitmentContent.LaunchTicket] = checked(record.Tickets[RecruitmentContent.LaunchTicket] + content.LaunchTickets);
            record.LaunchClaimed = true;
            record.Ledger.Add(new LedgerEntry
            {
                RequestId = request.RequestId, Kind = LedgerEntry.Launch, Revision = request.ExpectedRevision, GrantCrystals = content.LaunchCrystals, GrantTickets = content.LaunchTickets
            });
            return Commit(content, envelope, record, request.RequestId);
        }

        /// <summary>Core's standard selector: SELECTOR_USED, SELECTOR_NOT_READY (standard + beginner total below selectorAt) and
        /// SELECTOR_BAD_CHOICE (not a standard SSR character) refuse; otherwise one copy of the choice with the duplicate table.</summary>
        public static Reply ClaimSelector(RecruitmentContent content, AccountEnvelope envelope, SelectorRequest request)
        {
            RequireRevision(envelope, request.ExpectedRevision);
            var record = envelope.Recruitment ?? new RecruitmentRecord();
            var g = record.ToState();
            PullResult granted;
            try { granted = Gacha.ClaimSelector(content.Config, g, request.CharId); }
            catch (RuleException error) { throw new ApiError(422, error.Code); }
            if (g.Crystals != checked(record.FreeCrystals + record.PaidCrystals)) throw new InvalidOperationException("the selector changed crystals");
            record.FromState(g);
            record.Ledger.Add(new LedgerEntry
            {
                RequestId = request.RequestId, Kind = LedgerEntry.Selector, Revision = request.ExpectedRevision, CharId = request.CharId, Results = new List<PullResult> { granted }
            });
            return Commit(content, envelope, record, request.RequestId);
        }

        /// <summary>The exact reply bytes of one committed ledger entry: {requestId, accountId, revision = its expectedRevision + 1, kind, its
        /// ordered results, and the snapshot of the record as it stood right after it (that prefix's history only)}. Commit replies with it
        /// and every later decode requires the entry's stored receipt to hold exactly these bytes.</summary>
        public static string ReplyText(RecruitmentContent content, string accountId, LedgerEntry entry, RecruitmentRecord prefix)
        {
            long revision = checked(entry.Revision + 1);
            return GraphJson.Text(GraphJson.Object("requestId", entry.RequestId, "accountId", accountId, "revision", revision, "kind", entry.Kind,
                "results", RecruitmentJson.Results(entry.Results), "snapshot", Snapshot(content, accountId, revision, prefix)), false);
        }

        // The candidate must reconcile through Core exactly like a later decode (every earlier receipt included), so nothing is committed that
        // the next read would refuse; the reply is the projection of its own new entry, which becomes that entry's receipt.
        static Reply Commit(RecruitmentContent content, AccountEnvelope envelope, RecruitmentRecord record, string requestId)
        {
            string reply;
            try { reply = record.Validate(content, envelope.AccountId, envelope.Receipts, PracticeRunStore.NextRevision(envelope), requestId) ?? throw new InvalidDataException("no pending entry"); }
            catch (InvalidDataException) { throw new InvalidOperationException("a recruitment candidate failed the ledger reconciliation"); }
            envelope.Recruitment = record;
            return new Reply(200, reply);
        }
    }
}
