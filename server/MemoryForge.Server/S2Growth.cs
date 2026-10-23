using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    sealed class GrowthTrace
    {
        public GrowthState State = new GrowthState();
        public Dictionary<string, Battle> Battles = new Dictionary<string, Battle>(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, object?>> Settlements = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
    }
    static partial class GrowthCommands
    {
        internal static Func<long> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public static bool IsId(string id) => id.Length == 35 && id.StartsWith("ec-", StringComparison.Ordinal) && id.Skip(3).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        static void Require(bool condition) { if (!condition) throw new InvalidDataException("growth reconciliation"); }
        static long Number(object? x) => x is long n ? n : x is int i ? i : throw new InvalidDataException("growth integer");
        static int Int(Dictionary<string, object?> g, string key) => checked((int)Number(g[key]));
        static string Text(Dictionary<string, object?> g, string key) => g[key] as string ?? throw new InvalidDataException("growth string");
        public static IEnumerable<string> ReceiptIds(AccountEnvelope e) => e.Growth?.Ledger.Where(f => f.Kind != "growthInitialize").Select(f => f.RequestId) ?? Enumerable.Empty<string>();
        public static int CharacterLevel(AccountEnvelope e, string cid, long before = long.MaxValue) => e.Growth?.Ledger.LastOrDefault(f => f.Kind == "characterLevelUp" && f.Revision < before && Equals(f.Request.GetValueOrDefault("charId"), cid)) is GrowthFact f ? Int(f.Request, "targetLevel") : 1;
        public static (int Level, int Xp) Account(AccountEnvelope? e, long before = long.MaxValue)
        {
            long total = e?.Progression?.Ledger.Where(f => f.Kind == "grant" && f.Revision < before).Sum(f => (long)AccountPolicy.Value(f.Chapter, "accountXp")) ?? 0;
            foreach (var f in e?.Growth?.Ledger.Where(f => f.Revision < before) ?? Enumerable.Empty<GrowthFact>())
                if (f.Kind == "encounterVictory") total = checked(total + 20); else if (f.Kind == "dungeonSweep") total = checked(total + 20L * Int(f.Request, "times"));
            total = checked(total + WeeklyAccountXp(e,before));
            if (total > int.MaxValue) throw new ApiError(422, "RESOURCE_OVERFLOW"); return AccountPolicy.Account((int)total);
        }
        public static void NoEncounter(AccountEnvelope e) { if (e.Growth?.Active != null) throw new ApiError(409, "RUN_ACTIVE"); }
        static void Tutorial(AccountEnvelope e, long before) { if (e.Progression?.Cleared(0, before) != true) throw new ApiError(422, "TUTORIAL_REQUIRED"); }
        static List<string> Party(Dictionary<string, object?> q) => ((IList)q["party"]!).Cast<string>().ToList();
        static ExpeditionLaunch Build(PracticeContent c, AccountEnvelope e, List<string> party, long revision, string version)
        {
            var request = new ExpeditionRequest("growth-build", revision, "story", 1, party, true);
            return version==SupportPolicy.Version?BuildLaunchV4(c,e,request):version == EquipmentPolicy.Version ? BuildLaunchV3(c, e, request) : BuildLaunch(c, e, request);
        }
        internal static ExpeditionLaunch BuildLaunch(PracticeContent c, AccountEnvelope e, ExpeditionRequest request)
        {
            var launch = ProgressionCommands.Build(c, e, request);
            launch.PolicyVersion = GrowthPolicy.Version; launch.PolicyHash = GrowthPolicy.Hash;
            foreach (var p in launch.Party) p["level"] = CharacterLevel(e, Text(p, "charId"), request.ExpectedRevision + 1);
            return launch;
        }
        static ContentAccount ContentAccount(AccountEnvelope e, GrowthState s, long before)
        {
            long total = checked((e.Progression?.Ledger.Where(f => f.Kind == "grant" && f.Revision < before).Sum(f => (long)AccountPolicy.Value(f.Chapter, "accountXp")) ?? 0) + s.ExtraAccountXp);
            if (total > int.MaxValue) throw new ApiError(422, "RESOURCE_OVERFLOW");
            return new ContentAccount { Level = AccountPolicy.Account((int)total).Level, Stamina = s.Focus,
                Owned = ProgressionCommands.Copies(e, false, before).Keys.OrderBy(x => x, StringComparer.Ordinal).ToList(), Dungeons = new Dictionary<string, int>(s.Stars, StringComparer.Ordinal) };
        }
        static object MemoryAt(AccountEnvelope e, long version)
        {
            var result = GraphJson.Object(); long reached = 0;
            foreach (var value in (IList)e.Memory["history"]!)
            {
                var h = (IDictionary<string, object?>)value!; long v = Number(h["version"]);
                if (v > version) break;
                foreach (var p in (IDictionary<string, object?>)h["memory"]!) result[p.Key] = GraphJson.Copy(p.Value);
                reached = v;
            }
            Require(reached == version); return result;
        }
        static void Capacity(PracticeContent c, List<Dictionary<string, object?>> party, object? memory)
        {
            var inspection = MemoryV2.Inspect(c.Data.Memory, memory);
            foreach (var p in party)
            {
                string cid = Text(p, "charId"); var snapshot = inspection.Snapshot;
                var rows = snapshot.CharacterIds.Contains(cid) ? snapshot.Cards(cid) : MemoryV2.Baseline(c.Data.Memory, cid);
                long used = MemoryV2.Cost(c.Data.Memory, cid, rows.Select(row => (object?)row.ToJsonGraph()).ToList());
                if (used > MemoryV2.CapacityOf(c.Data.Memory, new MemoryCapacityEntry { CharId = cid, Res = Int(p, "res"), Asc = Int(p, "asc") })) throw new ApiError(422, "OVER_CAPACITY");
            }
        }
        static Dictionary<string, object?> Stage(ContentModesData modes, string dungeon, int tier)
        {
            var d = modes.Dungeon.Dungeons.SingleOrDefault(d => d.Id == dungeon) ?? throw new ApiError(422, "UNKNOWN_CONTENT");
            var t = modes.Dungeon.Tiers.SingleOrDefault(t => t.Tier == tier) ?? throw new ApiError(422, "UNKNOWN_TIER");
            return GraphJson.Object("tier", tier, "stage", t.Stage, "hpBps", t.HpBps, "dmgBps", t.DmgBps, "lineup", modes.Dungeon.Encounters![d.Id][tier.ToString(System.Globalization.CultureInfo.InvariantCulture)].Select(w => (object?)w.Cast<object?>().ToList()).ToList());
        }
        static GrowthState Effective(GrowthState s, long at) { var copy = s.Copy(); copy.At(at); return copy; }
        public static Dictionary<string, object?> Grant(Dictionary<string, int>? grant)
        {
            var resources = GrowthPolicy.Resources.ToDictionary(x => x, _ => (object?)0L, StringComparer.Ordinal); long xp = 0;
            foreach (var p in grant ?? new Dictionary<string, int>()) if (p.Key == "accountXp") xp = p.Value; else { Require(resources.ContainsKey(p.Key) && p.Value >= 0); resources[p.Key] = (long)p.Value; }
            return GraphJson.Object("resources", resources, "accountXp", xp);
        }
        static Dictionary<string, object?> TierGrant(ContentModesData modes, string id, int tier)
        { var grant = ContentModes.Dungeon.Reward(modes, id, tier); grant["accountXp"] = 20; return Grant(grant); }
        static Dictionary<string, object?> Settle(PracticeContent c, AccountEnvelope e, GrowthState s, EncounterRecord r, Battle battle, long at, long before)
        {
            var b = battle.State; Require(b.Phase == "VICTORY" || b.Phase == "DEFEAT"); s.At(at);
            if(r.Kind=="weeklyBoss") return WeeklySettle(c,e,s,r,battle,at,before);
            Dictionary<string, int>? grant = null; int stars = 0, cost = 0;
            if (b.Phase == "VICTORY")
            {
                var settled = ContentModes.Dungeon.Settle(GrowthPolicy.Modes(c), ContentAccount(e, s, before), b);
                grant = settled.Grant!; stars = settled.Stars!.Value; cost = 20; s.Spend(cost, at); s.Grant(grant);
                s.Stars[r.Dungeon + ":" + r.Tier] = settled.Account.Dungeons[r.Dungeon + ":" + r.Tier];
            }
            s.Active = null;
            return GraphJson.Object("outcome", b.Phase, "stars", stars, "cost", GraphJson.Object("focus", cost), "grant", Grant(grant));
        }
        static Dictionary<string, object?> StatsView(PracticeContent c, string cid, int level)
        {
            string weapon = Run.DefaultWeapon(c.Data, cid); string rarity = c.Data.Memory.Battle.Weapons.Single(w => w.Id == weapon).Rarity;
            var d = Stats.Derive(c.Data.Stats, new GrowthEntry { CharId = cid, Level = level, WeaponRarity = rarity, WeaponLevel = 1 });
            return GraphJson.Object("power4", d.Power4, "power", d.Power, "hp", d.Hp, "gearBps", d.GearBps, "skillLv", d.SkillLv, "ultLv", d.UltLv, "reference", d.Reference);
        }
        // v2 history always has asc0 (cap20), so its receipt rows are byte-identical; v3 ascension raises the cap.
        static Dictionary<string, object?> LevelRow(PracticeContent c, string cid, int level, int asc) => GraphJson.Object("level", level, "xp", 0, "cap", EquipmentPolicy.Caps[asc], "stats", StatsView(c, cid, level));
        static (Dictionary<string, object?> Before, Dictionary<string, object?> After, long Ink, long Credits) LevelPlan(PracticeContent c, AccountEnvelope e, GrowthState s, GrowthRequest q)
        {
            Tutorial(e, q.ExpectedRevision + 1); string cid = Text(q.Args, "charId");
            if (!ProgressionCommands.Copies(e, false, q.ExpectedRevision + 1).ContainsKey(cid)) throw new ApiError(422, "NOT_OWNED");
            int current = s.Levels.GetValueOrDefault(cid, 1), target = Int(q.Args, "targetLevel"), asc = s.S3.Asc.GetValueOrDefault(cid);
            var cost = GrowthMath.Cost(c.Data.Stats, current, 0, target, asc);
            return Active(e,q.ExpectedRevision+1)
                ? (EquippedLevelRow(c,e,s,cid,current,asc,q.ExpectedRevision+1),EquippedLevelRow(c,e,s,cid,target,asc,q.ExpectedRevision+1),cost.Ink,cost.Credits)
                : (LevelRow(c, cid, current, asc), LevelRow(c, cid, target, asc), cost.Ink, cost.Credits);
        }
        static GrowthRequest StoredRequest(GrowthFact f)
        {
            if(f.Kind=="supportSelection")return Contracts.ParseSupportSelection(GraphJson.Bytes(GraphJson.Object("requestId",f.RequestId,"expectedRevision",f.Revision).Concat(f.Request).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal),false));
            if (Contracts.EquipmentActions.ContainsKey(f.Kind)) return Contracts.StoredEquipment(f.Kind, f.RequestId, f.Revision, f.Request);
            var raw = (Dictionary<string, object?>)GraphJson.Copy(f.Request)!; string? character = null;
            if (f.Kind == "characterLevelUp") { character = Text(raw, "charId"); raw.Remove("charId"); }
            raw.Add("requestId", f.RequestId); raw.Add("expectedRevision", f.Revision);
            byte[] bytes = GraphJson.Bytes(raw, false);
            return f.Kind switch { "encounterStart" => Contracts.ParseEncounter(bytes), "weeklyBossStart" => Contracts.ParseWeeklyBoss(bytes), "dungeonSweep" => Contracts.ParseSweep(bytes), "characterLevelUp" => Contracts.ParseLevel(bytes, character!, false), _ => throw new InvalidDataException("growth operation") };
        }
        static CommandRequest StoredCommand(GrowthFact f)
        {
            using var document = JsonDocument.Parse(GraphJson.Bytes(f.Request, false)); BattleDataLoader.Fields(document.RootElement, "expectedVersion", "kind", "args");
            return Contracts.ParseEncounterCommand(GraphJson.Bytes(GraphJson.Object("requestId", f.RequestId, "expectedVersion", f.Request["expectedVersion"], "kind", f.Request["kind"], "args", f.Request["args"]), false), f.EncounterId);
        }
        static Run? ContextRun(PracticeContent c, PracticeAccount account, AccountEnvelope e, GrowthFact f, out RunRecord? record)
        {
            record = f.RunId == null ? null : e.Archive.Concat(e.Run == null ? Enumerable.Empty<RunRecord>() : new[] { e.Run }).SingleOrDefault(r => r.RunId == f.RunId);
            if (f.RunId == null) { Require(f.RunVersion == 0); return null; }
            Require(record != null && f.RunVersion <= record.Journal.Count && (record.Launch == null || record.Launch.Revision <= f.Revision));
            var run = record!.CreateRun(account, c);
            for (int i = 0; i < f.RunVersion; i++) { var j = record.Journal[i]; RunCommands.Execute(run, record.RunId, j.Kind, j.Args, e.Memory); }
            record = new RunRecord { RunId = record.RunId, Launch = record.Launch, Journal = record.Journal.Take(f.RunVersion).ToList() };
            return run;
        }
        static Dictionary<string, object?> ProfileAt(PracticeContent c, PracticeAccount account, AccountEnvelope e, GrowthTrace trace, GrowthFact f)
        {
            var run = ContextRun(c, account, e, f, out var record);
            var prefix = new AccountEnvelope { AccountId = e.AccountId, Revision = checked(f.Revision + 1), Recruitment = e.Recruitment, Progression = e.Progression, Growth = e.Growth, Run = record, Memory = e.Memory };
            return Profile(c, account.Id, prefix, run, f.AtMs, trace, f.Revision + 1, f.PolicyVersion);
        }
        static Dictionary<string, object?> Apply(PracticeContent c, PracticeAccount account, AccountEnvelope e, GrowthTrace trace, GrowthFact f)
        {
            GrowthPolicy.Require(f.PolicyVersion, f.PolicyHash); var s = trace.State;
            if (f.Kind == "growthInitialize")
            { Require(!s.Initialized && f.Request.Count == 0 && f.EncounterId.Length == 0 && f.TerminalStateSha256.Length == 0); s.Initialized = true; s.Anchor = f.AtMs; return GraphJson.Object(); }
            Require(s.Initialized);s.At(f.AtMs);if(f.Kind=="supportSelection")return ApplySupportSelection(c,account,e,trace,f);Tutorial(e, f.Revision + 1);
            if (f.Kind == "weeklyBossStart") return ApplyWeeklyStart(c,account,e,trace,f);
            if (f.Kind == "encounterStart")
            {
                Require(e.Growth!.Encounters.Single(x=>x.Id==f.EncounterId).Kind=="dungeon");
                Require(f.JournalIndex == 0);
                var q = StoredRequest(f); var r = e.Growth!.Encounters.Single(x => x.Id == f.EncounterId); Require(s.Active == null && r.RequestId == f.RequestId && r.Identity == q.Identity && r.Revision == f.Revision && r.AtMs == f.AtMs);
                Require(r.CoreModule == c.CoreModule && r.ContentSha256 == c.ContentSha256 && r.MemoryVersion == f.MemoryVersion && r.Dungeon == Text(q.Args, "dungeonId") && r.Tier == Int(q.Args, "tier"));
                Require(r.PolicyVersion == f.PolicyVersion && r.PolicyHash == f.PolicyHash);
                var launch = Build(c, e, Party(q.Args), f.Revision, f.PolicyVersion);
                Require(GraphJson.Digest(launch.Party.Cast<object?>().ToList()) == GraphJson.Digest(r.Party.Cast<object?>().ToList()) && launch.WeaponInstances.SequenceEqual(r.Weapons));
                Require(GraphJson.Digest(launch.Support?.ToGraph())==GraphJson.Digest(r.Support?.ToGraph()));
                Require(GraphJson.Digest(MemoryAt(e, r.MemoryVersion)) == GraphJson.Digest(r.Memory) && GraphJson.Digest(ContentAccount(e, s, f.Revision + 1).ToJsonGraph()) == GraphJson.Digest(r.AccountAtStart));
                Require(GraphJson.Digest(Stage(GrowthPolicy.Modes(c), r.Dungeon, r.Tier)) == GraphJson.Digest(r.Stage)); Capacity(c, r.Party, r.Memory);
                var battle = r.Start(c); Require(GraphJson.Digest(battle.State.ToJsonGraph()) == r.CreatedSha256);
                trace.Battles.Add(r.Id, battle); s.Active = r.Id;
                return EncounterReply(c, r, battle, 0, f.Revision + 1, null, null);
            }
            if (CommandFact(f.Kind))
            {
                var q = StoredCommand(f); var r = e.Growth!.Encounters.Single(x => x.Id == f.EncounterId);
                Require(s.Active == r.Id && trace.Battles.TryGetValue(r.Id, out _) && f.JournalIndex == q.ExpectedVersion + 1 && f.JournalIndex <= r.Journal.Count);
                var battle = trace.Battles[r.Id]; var j = r.Journal[f.JournalIndex - 1];
                Require(j.RequestId == f.RequestId && j.Identity == q.Identity && j.ExpectedVersion == q.ExpectedVersion && j.Kind == q.Kind && j.Revision == f.Revision && j.AtMs == f.AtMs && GraphJson.Digest(j.Args) == GraphJson.Digest(q.Args));
                var response = battle.Command(new BattleRequest(q.RequestId, battle.State.V, q.Kind, BattleArgs(q.Args)));
                Require(GraphJson.Digest(battle.State.ToJsonGraph()) == j.Sha256);
                string kind = EndKind(r,response.Phase); Require(f.Kind == kind);
                Dictionary<string, object?>? settlement = null;
                if (kind != "encounterCommand") { Require(f.TerminalStateSha256 == j.Sha256); settlement = Settle(c, e, s, r, battle, f.AtMs, f.Revision + 1); if(r.Kind=="weeklyBoss") Require(f.WeekKey == (string)settlement["weekKey"]!); trace.Settlements.Add(r.Id, settlement); }
                return EncounterReply(c, r, battle, f.JournalIndex, f.Revision + 1, response.Events, settlement);
            }
            Require(f.JournalIndex == 0 && f.EncounterId.Length == 0);
            var request = StoredRequest(f);
            if (Contracts.EquipmentActions.ContainsKey(f.Kind)) return ApplyEquipment(c, account, e, trace, f, request);
            if (f.Kind == "dungeonSweep")
            {
                Require(s.Active == null); var settled = ContentModes.Dungeon.Sweep(GrowthPolicy.Modes(c), ContentAccount(e, s, f.Revision + 1), Text(request.Args, "dungeonId"), Int(request.Args, "tier"), Int(request.Args, "times"));
                int cost = checked(20 * Int(request.Args, "times")); s.Spend(cost, f.AtMs); s.Grant(settled.Grant!);
                return GraphJson.Object("requestId", f.RequestId, "revision", f.Revision + 1, "kind", "dungeonSweep", "dungeonId", request.Args["dungeonId"], "tier", request.Args["tier"], "times", request.Args["times"],
                    "cost", GraphJson.Object("focus", cost), "grant", Grant(settled.Grant), "profile", ProfileAt(c, account, e, trace, f));
            }
            if (f.Kind == "characterLevelUp")
            {
                var plan = LevelPlan(c, e, s, request);
                if (s.Resources["ink"] < plan.Ink || s.Resources["credits"] < plan.Credits) throw new ApiError(422, "INSUFFICIENT_RESOURCES");
                s.Resources["ink"] -= plan.Ink; s.Resources["credits"] -= plan.Credits; s.Levels[Text(request.Args, "charId")] = Int(request.Args, "targetLevel");
                return GraphJson.Object("requestId", f.RequestId, "revision", f.Revision + 1, "kind", "characterLevelUp", "charId", request.Args["charId"], "before", plan.Before, "after", plan.After,
                    "cost", GraphJson.Object("ink", plan.Ink, "credits", plan.Credits), "profile", ProfileAt(c, account, e, trace, f));
            }
            throw new InvalidDataException("growth fact kind");
        }
        public static GrowthTrace Validate(PracticeContent c, PracticeAccount account, AccountEnvelope e)
        {
            var trace = new GrowthTrace(); if (e.Growth == null) return trace;
            var ids = new HashSet<string>(StringComparer.Ordinal); long revision = -1; GrowthFact? init = null;int policy=0;
            Require(e.Growth.Ledger.Count > 1 && e.Growth.Ledger[0].Kind == "growthInitialize" && e.Growth.Encounters.Select(r => r.Id).Distinct().Count() == e.Growth.Encounters.Count);
            foreach (var f in e.Growth.Ledger)
            {
                Require(f.Revision < e.Revision && f.AtMs >= 0);
                if (f.Kind == "growthInitialize") { Require(init == null && revision == -1); init = f; Apply(c, account, e, trace, f); continue; }
                Require(f.Revision > revision && ids.Add(f.RequestId)); revision = f.Revision;
                // v3 pin is monotonic: no v2 fact after the first v3 fact, and S3 kinds exist only as v3.
                int nextPolicy=f.PolicyVersion==SupportPolicy.Version?4:f.PolicyVersion==EquipmentPolicy.Version?3:2;
                Require(nextPolicy>=policy&&(nextPolicy>=3||!Contracts.EquipmentActions.ContainsKey(f.Kind)));policy=nextPolicy;
                if(f.PolicyVersion!=SupportPolicy.Version)Require(!SupportActive(e,f.Revision));
                if (ids.Count == 1) Require(init!.RequestId == f.RequestId && init.AtMs == f.AtMs && init.Revision == f.Revision && init.MemoryVersion == f.MemoryVersion && init.RunId == f.RunId && init.RunVersion == f.RunVersion);
                if (!TerminalFact(f.Kind)) Require(f.TerminalStateSha256.Length == 0);
                var reply = Apply(c, account, e, trace, f);
                string identity = CommandFact(f.Kind) ? StoredCommand(f).Identity : StoredRequest(f).Identity;
                Require(e.Receipts.TryGetValue(f.RequestId, out var receipt) && receipt.Status == 200 && receipt.Identity == identity && receipt.Body == Reply.Json(200, reply).Body);
            }
            Require(trace.State.Active == e.Growth.Active);
            foreach (var r in e.Growth.Encounters)
            { Require(trace.Battles.ContainsKey(r.Id) && e.Growth.Ledger.Count(f => (f.Kind == "encounterStart" || f.Kind == "weeklyBossStart") && f.EncounterId == r.Id) == 1 && e.Growth.Ledger.Count(f => f.EncounterId == r.Id && f.Kind != "encounterStart" && f.Kind != "weeklyBossStart") == r.Journal.Count); }
            long expectedXp = checked(e.Growth.Ledger.Sum(f=>f.Kind=="encounterVictory"?20L:f.Kind=="dungeonSweep"?20L*Int(f.Request,"times"):0L)+WeeklyAccountXp(e,long.MaxValue));
            Require(trace.State.ExtraAccountXp==expectedXp);
            e.GrowthCache = trace; return trace;
        }
        static GrowthTrace Trace(PracticeContent c, PracticeAccount a, AccountEnvelope e) => e.GrowthCache ?? Validate(c, a, e);
        static GrowthFact Fact(AccountEnvelope e, string kind, string requestId, long now)
        {
            var pin = Pin(e, kind);
            return new GrowthFact { Kind = kind, RequestId = requestId, Revision = e.Revision, AtMs = now, MemoryVersion = AccountEnvelope.MemoryVersion(e.Memory), RunId = e.Run?.RunId, RunVersion = e.Run?.Journal.Count ?? 0,
                PolicyVersion = pin.Version, PolicyHash = pin.Hash };
        }
        static GrowthTrace Begin(PracticeContent c, PracticeAccount a, AccountEnvelope e, string requestId, long now)
        {
            Tutorial(e, e.Revision + 1); var trace = Trace(c, a, e);
            if (e.Growth == null)
            {
                e.Growth = new GrowthRecord(); var init = Fact(e, "growthInitialize", requestId, now); e.Growth.Ledger.Add(init); Apply(c, a, e, trace, init);
            }
            return trace;
        }
        static Reply Append(PracticeContent c, PracticeAccount a, AccountEnvelope e, GrowthTrace trace, GrowthFact fact)
        {
            e.Growth!.Ledger.Add(fact); var reply = Apply(c, a, e, trace, fact); e.Growth.Active = trace.State.Active; e.GrowthCache = trace;
            return Reply.Json(200, reply);
        }
        static void NoRun(AccountEnvelope e, Run? run)
        { if (e.Run != null && (run == null || run.State.Phase != "DONE")) throw new ApiError(409, "RUN_ACTIVE"); NoEncounter(e); }
        public static Reply Start(PracticeContent c, PracticeAccount a, AccountEnvelope e, Run? run, GrowthRequest q)
        {
            if (e.Revision != q.ExpectedRevision) throw new ApiError(409, "STALE_REVISION"); NoRun(e, run);
            long now = Clock(); var trace = Begin(c, a, e, q.RequestId, now); var state = Effective(trace.State, now); var pin = Pin(e, "encounterStart"); var launch = Build(c, e, Party(q.Args), e.Revision, pin.Version);
            var r = new EncounterRecord { Id = "ec-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), RequestId = q.RequestId, Identity = q.Identity, PolicyVersion = pin.Version, PolicyHash = pin.Hash,
                Revision = e.Revision, AtMs = now, Dungeon = Text(q.Args, "dungeonId"), Tier = Int(q.Args, "tier"), CoreModule = c.CoreModule, ContentSha256 = c.ContentSha256,
                MemoryVersion = AccountEnvelope.MemoryVersion(e.Memory), Memory = GraphJson.Copy(e.Memory["memory"]), Party = launch.Party, Weapons = launch.WeaponInstances,
                Seed = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)), AccountAtStart = ContentAccount(e, state, e.Revision + 1).ToJsonGraph(), Stage = Stage(GrowthPolicy.Modes(c), Text(q.Args, "dungeonId"), Int(q.Args, "tier")),Support=launch.Support };
            Capacity(c, r.Party, r.Memory); r.CreatedSha256 = GraphJson.Digest(r.Start(c).State.ToJsonGraph()); e.Growth!.Encounters.Add(r);
            var f = Fact(e, "encounterStart", q.RequestId, now); f.Request = q.Args; f.EncounterId = r.Id;
            return Append(c, a, e, trace, f);
        }
        static EncounterRecord Record(AccountEnvelope e, string id) => e.Growth?.Encounters.SingleOrDefault(r => r.Id == id) ?? throw new ApiError(404, "ENCOUNTER_NOT_FOUND");
        static Battle Rebuild(PracticeContent c, EncounterRecord r)
        {
            var b = r.Start(c); Require(GraphJson.Digest(b.State.ToJsonGraph()) == r.CreatedSha256);
            foreach (var j in r.Journal) { b.Command(new BattleRequest(j.RequestId, b.State.V, j.Kind, BattleArgs(j.Args))); Require(GraphJson.Digest(b.State.ToJsonGraph()) == j.Sha256); }
            return b;
        }
        public static Reply Command(PracticeContent c, PracticeAccount a, AccountEnvelope e, string id, CommandRequest q)
        {
            var r = Record(e, id); var trace = Trace(c, a, e); if (trace.State.Active != id) throw new ApiError(409, "ENCOUNTER_FINISHED");
            if (r.Journal.Count != q.ExpectedVersion) throw new ApiError(409, "STALE_VERSION");
            var probe = Rebuild(c, r); var response = probe.Command(new BattleRequest(q.RequestId, probe.State.V, q.Kind, BattleArgs(q.Args)));
            long now = Clock(); r.Journal.Add(new EncounterJournal { RequestId = q.RequestId, Kind = q.Kind, Identity = q.Identity, ExpectedVersion = q.ExpectedVersion,
                Revision = e.Revision, AtMs = now, Args = q.Args, Sha256 = GraphJson.Digest(probe.State.ToJsonGraph()) });
            var f = Fact(e, EndKind(r,response.Phase), q.RequestId, now);
            f.EncounterId = id; f.JournalIndex = r.Journal.Count; if (f.Kind != "encounterCommand") f.TerminalStateSha256 = r.Journal.Last().Sha256; if(r.Kind=="weeklyBoss" && TerminalFact(f.Kind)) f.WeekKey=EquipmentPolicy.Week(EquipmentPolicy.Modes(c),now).Key; f.Request = GraphJson.Object("expectedVersion", q.ExpectedVersion, "kind", q.Kind, "args", q.Args);
            return Append(c, a, e, trace, f);
        }
        public static Reply Mutate(PracticeContent c, PracticeAccount a, AccountEnvelope e, Run? run, GrowthRequest q)
        {
            if (e.Revision != q.ExpectedRevision) throw new ApiError(409, "STALE_REVISION"); if (q.Kind == "dungeonSweep") NoRun(e, run);
            long now = Clock(); var trace = Begin(c, a, e, q.RequestId, now); var f = Fact(e, q.Kind, q.RequestId, now); f.Request = q.Args;
            return Append(c, a, e, trace, f);
        }
        public static Reply Read(PracticeContent c, PracticeAccount a, AccountEnvelope? e, string id)
        {
            if (e == null) throw new ApiError(404, "ENCOUNTER_NOT_FOUND"); var r = Record(e, id); var trace = Trace(c, a, e); var b = trace.Battles[id];
            return Reply.Json(200, EncounterReply(c, r, b, r.Journal.Count, e.Revision, null, trace.Settlements.GetValueOrDefault(id)));
        }
        static Dictionary<string, object?> EncounterReply(PracticeContent c, EncounterRecord r, Battle b, int version, long revision, IEnumerable<BattleEvent>? events, object? settlement)
        {
            string digest = GraphJson.Digest(b.State.ToJsonGraph());
            var g = GraphJson.Object("encounterId", r.Id, "version", version, "phase", b.State.Phase, "revision", revision, "state", PublicView.Battle(b, b.State, c.Rules));
            Require(digest == GraphJson.Digest(b.State.ToJsonGraph()));
            WeeklyMetadata(r,g);
            if (events != null) g.Add("events", PublicView.Events(events)); if (settlement != null) g.Add("settlement", settlement); return g;
        }
        public static BattleArgument BattleArgs(Dictionary<string, object?> args) => BattleArgument.Object(args.Where(p => p.Value != null).Select(p => new KeyValuePair<string, BattleArgument>(p.Key,
            p.Value is IList list ? BattleArgument.Array(list.Cast<object?>().Select(x => BattleArgument.String((string)x!))) : BattleArgument.String((string)p.Value!))));
        public static Reply Query(PracticeContent c, PracticeAccount a, AccountEnvelope? e, string id, BattleQueryRequest q)
        {
            if (e == null) throw new ApiError(404, "ENCOUNTER_NOT_FOUND"); var r = Record(e, id); if (r.Journal.Count != q.ExpectedVersion) throw new ApiError(409, "STALE_VERSION");
            var b = Trace(c, a, e).Battles[id]; if (b.State.Phase != "PLAYER") throw new ApiError(409, "ENCOUNTER_FINISHED");
            if (!b.State.Zones.Hand.Contains(q.Uid)) throw new ApiError(422, "NOT_IN_HAND");
            string before = GraphJson.Digest(b.State.ToJsonGraph()); var choices = b.HandChoices(q.Uid, q.Mode, q.Picks); var forecast = b.Forecast(q.Uid, q.Mode, q.Target, q.Picks);
            var view = PublicView.BattleQuery(b, id, q.ExpectedVersion, q, choices, forecast, c.Data.Memory.Battle); Require(before == GraphJson.Digest(b.State.ToJsonGraph())); return Reply.Json(200, view);
        }
        public static Reply Preview(PracticeContent c, AccountEnvelope? e, GrowthRequest q)
        {
            if (e == null) throw new ApiError(422, "TUTORIAL_REQUIRED");
            if (e.Revision != q.ExpectedRevision) throw new ApiError(409, "STALE_REVISION");
            var state = e.GrowthCache?.State ?? new GrowthState(); var plan = LevelPlan(c, e, state, q); bool enough = state.Resources["ink"] >= plan.Ink && state.Resources["credits"] >= plan.Credits;
            var g = GraphJson.Object("revision", e.Revision, "charId", q.Args["charId"], "before", plan.Before, "after", plan.After, "cost", GraphJson.Object("ink", plan.Ink, "credits", plan.Credits), "canCommit", enough);
            if (!enough) g.Add("code", "INSUFFICIENT_RESOURCES"); return Reply.Json(200, g);
        }
        // version: the stored fact's pinned policy for receipts; null = current GET, which is the v3 projection.
        public static Dictionary<string, object?> Profile(PracticeContent c, string accountId, AccountEnvelope? e, Run? live, long? now = null, GrowthTrace? prefix = null, long before = long.MaxValue, string? version = null)
        {
            long at = now ?? Clock(); var trace = prefix ?? e?.GrowthCache ?? new GrowthTrace(); var state = trace.State;
            var view = ProgressionCommands.BaseProfile(c, accountId, e, live, before); view["resources"] = state.Resources.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal);
            view["resourceUnits"] = GraphJson.Object("ink", "XP", "oil", "XP"); view["focus"] = state.FocusView(at); var modes = GrowthPolicy.Modes(c); int al = Account(e, before).Level;
            view["dungeons"] = modes.Dungeon.Dungeons.Select(d => (object?)GraphJson.Object("id", d.Id, "unlockLevel", d.Unlock, "cost", d.Cost,
                "available", e?.Progression?.Cleared(0, before) == true && al >= d.Unlock, "tiers", modes.Dungeon.Tiers.Select(t => (object?)GraphJson.Object("tier", t.Tier, "stage", t.Stage, "percent", t.Pct,
                    "reward", TierGrant(modes, d.Id, t.Tier), "bestStars", state.Stars.GetValueOrDefault(d.Id + ":" + t.Tier), "sweepAvailable", state.Stars.GetValueOrDefault(d.Id + ":" + t.Tier) >= 3)).ToList())).ToList();
            view["activeEncounter"] = state.Active is string id && e?.Growth?.Encounters.SingleOrDefault(r => r.Id == id) is EncounterRecord rec && trace.Battles.TryGetValue(id, out var b)
                ? GraphJson.Object("encounterId", id, "dungeonId", rec.Dungeon, "tier", rec.Tier, "phase", b.State.Phase, "version", before == long.MaxValue ? rec.Journal.Count : e.Growth.Ledger.Count(f => f.EncounterId == id && f.Kind != "encounterStart" && f.Kind != "weeklyBossStart" && f.Revision < before)) : null;
            if(state.Active is string active && e?.Growth?.Encounters.SingleOrDefault(r=>r.Id==active) is EncounterRecord weekly && weekly.Kind=="weeklyBoss")
            {
                var activeView=(Dictionary<string,object?>)view["activeEncounter"]!;activeView.Remove("dungeonId");activeView.Remove("tier");WeeklyMetadata(weekly,activeView);
            }
            if (version == null || version == EquipmentPolicy.Version||version==SupportPolicy.Version) Enrich(c, e, state, view, at, before);
            if(version==null||version==SupportPolicy.Version)SupportProfile(e,view,before);
            return view;
        }
    }
    static class GrowthRoutes
    {
        static async Task<Reply> Execute(Func<Task<Reply>> action)
        {
            try { return await action(); }
            catch (RuleException error) { throw new ApiError(422, error.Code); }
            catch (MemoryBoundaryException) { throw new ApiError(422, "COMMAND_REJECTED"); }
            catch (OverflowException) { throw new ApiError(422, "RESOURCE_OVERFLOW"); }
        }
        public static Task<Reply> Start(PracticeHost h, PracticeAccount a, byte[] body) { var q = Contracts.ParseEncounter(body); return Execute(() => h.Store.Mutate(a, q.RequestId, q.Identity, (e, r) => GrowthCommands.Start(h.Content, a, e, r, q))); }
        public static Task<Reply> Read(PracticeHost h, PracticeAccount a, string id) => Execute(() => h.Store.Read(a, (e, _) => GrowthCommands.Read(h.Content, a, e, id)));
        public static Task<Reply> Command(PracticeHost h, PracticeAccount a, string id, byte[] body) { var q = Contracts.ParseEncounterCommand(body, id); return Execute(() => h.Store.Mutate(a, q.RequestId, q.Identity, (e, _) => GrowthCommands.Command(h.Content, a, e, id, q))); }
        public static Task<Reply> Query(PracticeHost h, PracticeAccount a, string id, byte[] body) { var q = Contracts.ParseBattleQuery(body); return Execute(() => h.Store.Read(a, (e, _) => GrowthCommands.Query(h.Content, a, e, id, q))); }
        public static Task<Reply> SupportQuery(PracticeHost h,PracticeAccount a,string id,byte[] body){var q=Contracts.ParseSupportQuery(body);return Execute(()=>h.Store.Read(a,(e,_)=>GrowthCommands.SupportQuery(h.Content,a,e,id,q)));}
        public static Task<Reply> Sweep(PracticeHost h, PracticeAccount a, byte[] body) { var q = Contracts.ParseSweep(body); return Execute(() => h.Store.Mutate(a, q.RequestId, q.Identity, (e, r) => GrowthCommands.Mutate(h.Content, a, e, r, q))); }
        public static Task<Reply> Level(PracticeHost h, PracticeAccount a, string id, byte[] body, bool preview) { var q = Contracts.ParseLevel(body, id, preview); return Execute(() => preview ? h.Store.Read(a, (e, _) => GrowthCommands.Preview(h.Content, e, q)) : h.Store.Mutate(a, q.RequestId, q.Identity, (e, r) => GrowthCommands.Mutate(h.Content, a, e, r, q))); }
    }
}
