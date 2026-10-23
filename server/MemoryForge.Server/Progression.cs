// S1: immutable server-owned launch inputs and append-only progression facts. No Core rules or legacy content are changed.
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
    sealed record ExpeditionRequest(string RequestId, long ExpectedRevision, string Mode, int? Chapter, List<string>? Party, bool UseMemory)
    {
        public Dictionary<string, object?> ToGraph() => GraphJson.Object("requestId", RequestId, "expectedRevision", ExpectedRevision,
            "mode", Mode, "chapter", Chapter, "party", Party?.Cast<object?>().ToList(), "useMemory", UseMemory);
        public string Identity => GraphJson.Digest(GraphJson.Object("op", "expedition", "expectedRevision", ExpectedRevision,
            "mode", Mode, "chapter", Chapter, "party", Party?.Cast<object?>().ToList(), "useMemory", UseMemory));
    }
    sealed record AbandonRequest(string RequestId, int ExpectedVersion, string Identity);

    static partial class Contracts
    {
        public static ExpeditionRequest ParseExpedition(byte[] bytes)
        {
            using var document = Document(bytes);
            var m = Members(document.RootElement, "requestId", "expectedRevision", "mode", "chapter", "party", "useMemory");
            string id = RequestId(m), mode = Reference(m, "mode"); long revision = Revision(m, "expectedRevision");
            if (mode != "tutorial" && mode != "story") throw new ApiError(400, "INVALID_REQUEST");
            if (!m.TryGetValue("useMemory", out var um) || (um.ValueKind != JsonValueKind.True && um.ValueKind != JsonValueKind.False)) throw new ApiError(400, "INVALID_REQUEST");
            int? chapter = m.ContainsKey("chapter") ? Version(m, "chapter") : null;
            List<string>? party = null;
            if (m.TryGetValue("party", out var raw))
            {
                if (raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() > 32) throw new ApiError(400, "INVALID_REQUEST");
                party = raw.EnumerateArray().Select(x => StringValue(x, "INVALID_REQUEST")).ToList();
            }
            if (mode == "tutorial" && (chapter.HasValue && chapter != 0 || party != null)) throw new ApiError(400, "INVALID_REQUEST");
            if (mode == "story" && !chapter.HasValue) throw new ApiError(400, "INVALID_REQUEST");
            return new ExpeditionRequest(id, revision, mode, chapter, party, um.GetBoolean());
        }
        public static AbandonRequest ParseAbandon(byte[] bytes, string runId)
        {
            using var document = Document(bytes); var m = Members(document.RootElement, "requestId", "expectedVersion");
            string id = RequestId(m); int version = Version(m, "expectedVersion");
            return new AbandonRequest(id, version, AbandonIdentity(runId, version));
        }
        public static string AbandonIdentity(string runId, int version) => GraphJson.Digest(GraphJson.Object("op", "abandon", "runId", runId, "expectedVersion", version));
    }

    sealed class ExpeditionLaunch
    {
        public string Mode = "", PolicyVersion = AccountPolicy.Version, PolicyHash = "", CoreModule = "", ContentSha256 = "";
        public int Chapter, StageLevel; public long Revision; public bool UseMemory;
        public List<Dictionary<string, object?>> Party = new List<Dictionary<string, object?>>();
        public List<string> WeaponInstances = new List<string>();
        public FrozenSupport? Support;
        // Phase2: the registered launch-only balance preset (ExpeditionBalancePolicy.cs). Null is a legacy launch with the original 100/100.
        public LaunchBalancePolicy? BalancePolicy;
        public Dictionary<string, object?> ToGraph()
        {
            var difficulty = BalancePolicy == null ? GraphJson.Object("hp", 100, "dmg", 100) : ExpeditionBalancePolicy.DifficultyGraph(BalancePolicy);
            var graph = GraphJson.Object("mode", Mode, "chapter", Chapter, "policyVersion", PolicyVersion, "revision", Revision,
            "coreModule", CoreModule, "contentSha256", ContentSha256, "stageLevel", StageLevel, "zones", new List<object?> { 1 }, "bosses", new List<object?> { "b_bell" },
            "difficulty", difficulty, "length", "short", "useMemory", UseMemory,
            "party", Party.Cast<object?>().ToList(), "weaponInstances", WeaponInstances.Cast<object?>().ToList());
            if (PolicyVersion != AccountPolicy.Version) graph.Add("policyHash", PolicyHash);
            if(Support!=null)graph.Add("support",Support.ToGraph());
            // Absent: exactly the legacy member set and order. Present: one more trailing member.
            if (BalancePolicy != null) graph.Add("balancePolicy", BalancePolicy.ToGraph());
            return graph;
        }
        public static ExpeditionLaunch Parse(JsonElement j)
        {
            BattleDataLoader.Fields(j, "mode", "chapter", "policyVersion", "revision", "coreModule", "contentSha256", "stageLevel", "zones", "bosses", "difficulty", "length", "useMemory", "party", "weaponInstances", "policyHash","support", "balancePolicy");
            var x = new ExpeditionLaunch { Mode = Envelopes.Text(j, "mode"), Chapter = RecruitmentJson.Small(j, "chapter"), PolicyVersion = Envelopes.Text(j, "policyVersion"),
                Revision = RecruitmentJson.Amount(j, "revision"), CoreModule = Envelopes.Text(j, "coreModule"), ContentSha256 = Envelopes.Text(j, "contentSha256"),
                StageLevel = RecruitmentJson.Small(j, "stageLevel"), UseMemory = RecruitmentJson.Flag(j, "useMemory") };
            AccountPolicy.Require(x.PolicyVersion);
            if (x.PolicyVersion != AccountPolicy.Version) { x.PolicyHash = Envelopes.Hex(j, "policyHash"); GrowthPolicy.Require(x.PolicyVersion, x.PolicyHash); }
            else if (j.TryGetProperty("policyHash", out _)) throw new InvalidDataException("v1 launch shape");
            foreach (var p in j.GetProperty("party").EnumerateArray()) x.Party.Add((Dictionary<string, object?>)MemoryV2DataLoader.Graph(p)!);
            foreach (var p in j.GetProperty("weaponInstances").EnumerateArray()) x.WeaponInstances.Add(p.GetString() ?? throw new InvalidDataException("instance"));
            if(j.TryGetProperty("support",out var support)){if(x.PolicyVersion!=SupportPolicy.Version||x.Mode=="tutorial")throw new InvalidDataException("support launch policy");x.Support=FrozenSupport.Parse(support);}
            // A present member (null included) must name exactly the registered preset on a v4 story launch of its bound content. Its
            // difficulty is then fixed by the canonical comparison below: any other stored multiplier, or the preset's difficulty without
            // the member, is noncanonical.
            if (j.TryGetProperty("balancePolicy", out var balance))
            {
                x.BalancePolicy = LaunchBalancePolicy.Parse(balance);
                ExpeditionBalancePolicy.RequireLaunch(x);
            }
            if (GraphJson.Digest(MemoryV2DataLoader.Graph(j)) != GraphJson.Digest(x.ToGraph())) throw new InvalidDataException("noncanonical launch");
            return x;
        }
        public RunSetup Setup(RunRecord r) => new RunSetup { Seed = r.Seed, RunId = r.RunId, MemoryVersion = r.MemoryVersion, Memory = GraphJson.Copy(r.StartMemory),
            MemoryPolicy = "v2", UseMemory = UseMemory ? null : false, Length = "short", Zones = new List<int> { 1 }, Bosses = new List<object?> { "b_bell" },
            StageLevel = StageLevel, Difficulty = BalancePolicy == null ? new RunDifficulty { Hp = 100, Damage = 100 } : ExpeditionBalancePolicy.Difficulty(BalancePolicy),
            Party = Party.Select(p => RunPartyEntry.FromJsonGraph(GraphJson.Copy(p))).ToList(),Support=Support?.ToCore() };
    }
    sealed class ExpeditionEnd
    {
        public string RequestId = ""; public int AtVersion; public long Revision, MemoryVersion;
        public Dictionary<string, object?> ToGraph() => GraphJson.Object("kind", "abandon", "requestId", RequestId, "atVersion", AtVersion, "revision", Revision, "memoryVersion", MemoryVersion);
        public static ExpeditionEnd Parse(JsonElement j)
        {
            BattleDataLoader.Fields(j, "kind", "requestId", "atVersion", "revision", "memoryVersion");
            if (Envelopes.Text(j, "kind") != "abandon") throw new InvalidDataException("end kind");
            var e = new ExpeditionEnd { RequestId = Envelopes.Text(j, "requestId"), AtVersion = RecruitmentJson.Small(j, "atVersion"), Revision = RecruitmentJson.Amount(j, "revision"), MemoryVersion = RecruitmentJson.Amount(j, "memoryVersion") };
            if (!Contracts.IsRequestId(e.RequestId)) throw new InvalidDataException("end request"); return e;
        }
        public Reply Reply(string runId) => Server.Reply.Json(200, GraphJson.Object("runId", runId, "version", checked(AtVersion + 1), "memoryVersion", MemoryVersion));
    }
    sealed class ProgressionFact
    {
        public string Kind = "", RequestId = "", RunId = "", PolicyVersion = AccountPolicy.Version, PolicyHash = "", StateSha256 = "";
        public long Revision; public int Chapter, JournalIndex; public ExpeditionRequest? Request;
        // Phase2: the launch fact's own independent pin of its Run's preset (ExpeditionBalancePolicy.cs). Null on every legacy fact and grant.
        public LaunchBalancePolicy? BalancePolicy;
        public Dictionary<string, object?> ToGraph()
        {
            var g = GraphJson.Object("kind", Kind, "requestId", RequestId, "runId", RunId, "policyVersion", PolicyVersion, "revision", Revision);
            if (PolicyVersion != AccountPolicy.Version) g.Add("policyHash", PolicyHash);
            if (Kind == "launch") g.Add("request", Request!.ToGraph());
            else { g.Add("chapter", Chapter); g.Add("journalIndex", JournalIndex); g.Add("stateSha256", StateSha256); g.Add("grant", AccountPolicy.Grant(Chapter)); }
            // Absent: exactly the legacy member set and order. Present: one more trailing member.
            if (BalancePolicy != null) g.Add("balancePolicy", BalancePolicy.ToGraph());
            return g;
        }
        public static ProgressionFact Parse(JsonElement j)
        {
            var f = new ProgressionFact { Kind = Envelopes.Text(j, "kind"), RequestId = Envelopes.Text(j, "requestId"), RunId = Envelopes.Text(j, "runId"), PolicyVersion = Envelopes.Text(j, "policyVersion"), Revision = RecruitmentJson.Amount(j, "revision") };
            AccountPolicy.Require(f.PolicyVersion);
            if (f.PolicyVersion != AccountPolicy.Version) { f.PolicyHash = Envelopes.Hex(j, "policyHash"); GrowthPolicy.Require(f.PolicyVersion, f.PolicyHash); }
            else if (j.TryGetProperty("policyHash", out _)) throw new InvalidDataException("v1 progression shape");
            if (!Contracts.IsRequestId(f.RequestId) || !PracticeRunStore.IsRunId(f.RunId)) throw new InvalidDataException("progression identity");
            if (f.Kind == "launch")
            {
                BattleDataLoader.Fields(j, "kind", "requestId", "runId", "policyVersion", "revision", "request", "policyHash", "balancePolicy");
                // Stored requests contain explicit nullable optional fields; normalize them through the public parser without accepting public nulls.
                var q = j.GetProperty("request"); BattleDataLoader.Fields(q, "requestId", "expectedRevision", "mode", "chapter", "party", "useMemory");
                var g = (Dictionary<string, object?>)MemoryV2DataLoader.Graph(q)!;
                foreach (string key in new[] { "chapter", "party" }) if (g[key] == null) g.Remove(key);
                f.Request = Contracts.ParseExpedition(GraphJson.Bytes(g, false));
                // A present member (null included) must name exactly the registered preset on a v4 story launch fact.
                if (j.TryGetProperty("balancePolicy", out var balance))
                {
                    f.BalancePolicy = LaunchBalancePolicy.Parse(balance);
                    ExpeditionBalancePolicy.RequireFact(f);
                }
            }
            else if (f.Kind == "grant")
            {
                BattleDataLoader.Fields(j, "kind", "requestId", "runId", "policyVersion", "revision", "chapter", "journalIndex", "stateSha256", "grant", "policyHash");
                f.Chapter = RecruitmentJson.Small(j, "chapter"); AccountPolicy.Stage(f.Chapter);
                f.JournalIndex = RecruitmentJson.Small(j, "journalIndex"); f.StateSha256 = Envelopes.Hex(j, "stateSha256");
                if (GraphJson.Digest(MemoryV2DataLoader.Graph(j.GetProperty("grant"))) != GraphJson.Digest(AccountPolicy.Grant(f.Chapter))) throw new InvalidDataException("grant values");
            }
            else throw new InvalidDataException("progression fact kind");
            return f;
        }
    }
    sealed class ProgressionRecord
    {
        public readonly List<ProgressionFact> Ledger = new List<ProgressionFact>();
        public Dictionary<string, object?> ToGraph() => GraphJson.Object("ledger", Ledger.Select(x => (object?)x.ToGraph()).ToList());
        public static ProgressionRecord Parse(JsonElement j)
        {
            BattleDataLoader.Fields(j, "ledger"); var r = new ProgressionRecord();
            foreach (var x in j.GetProperty("ledger").EnumerateArray()) r.Ledger.Add(ProgressionFact.Parse(x)); return r;
        }
        public bool Cleared(int chapter, long before = long.MaxValue) => Ledger.Any(f => f.Kind == "grant" && f.Chapter == chapter && f.Revision < before);
        public (int Level, int Xp) Account(long before = long.MaxValue) => AccountPolicy.Account(Ledger.Where(f => f.Kind == "grant" && f.Revision < before).Sum(f => AccountPolicy.Value(f.Chapter, "accountXp")));
    }

    static class ProgressionCommands
    {
        internal static void Require(bool ok) { if (!ok) throw new InvalidDataException("progression reconciliation"); }
        public static void EnsureLegacyAllowed(AccountEnvelope e) { if (e.Progression != null) throw new ApiError(409, "LEGACY_PRACTICE_RETIRED"); }
        internal static Dictionary<string, int> Copies(AccountEnvelope e, bool weapon, long before)
        {
            var c = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in e.Recruitment?.Ledger.Where(f => f.Revision < before) ?? Enumerable.Empty<LedgerEntry>())
                foreach (var r in f.Results.Where(r => r.Kind == (weapon ? "weapon" : "char"))) c[r.Id] = checked(c.GetValueOrDefault(r.Id) + 1);
            return c;
        }
        internal static List<(string Instance, string Weapon, string? Equipped)> Weapons(PracticeContent content, AccountEnvelope e, long before)
        {
            var result = new List<(string, string, string?)>();
            if (e.Progression?.Cleared(0, before) == true) result.AddRange(AccountPolicy.Basics.Select(w => ("basic:" + w, w, AccountPolicy.Equipped(w))));
            foreach (var w in Copies(e, true, before).OrderBy(p => p.Key, StringComparer.Ordinal)) result.Add(("recruit:" + w.Key, w.Key, null));
            return result;
        }
        internal static ExpeditionLaunch Build(PracticeContent content, AccountEnvelope e, ExpeditionRequest q)
        {
            int chapter = q.Mode == "tutorial" ? 0 : q.Chapter ?? -1;
            int stage = AccountPolicy.Stage(chapter);
            if (q.Mode == "story" && chapter != 1) throw new ApiError(422, "CHAPTER_LOCKED");
            var progress = e.Progression;
            if (q.Mode == "tutorial") { if (progress?.Cleared(0, q.ExpectedRevision + 1) == true) throw new ApiError(422, "TUTORIAL_DONE"); }
            else
            {
                if (progress?.Cleared(0, q.ExpectedRevision + 1) != true) throw new ApiError(422, "TUTORIAL_REQUIRED");
                if (progress.Account(q.ExpectedRevision + 1).Level < AccountPolicy.Unlock(chapter)) throw new ApiError(422, "CHAPTER_LOCKED");
                if (q.Party == null || q.Party.Count != 3) throw new ApiError(422, "PARTY_SIZE");
                if (q.Party.Distinct(StringComparer.Ordinal).Count() != 3) throw new ApiError(422, "PARTY_DUPLICATE");
            }
            var launch = new ExpeditionLaunch { Mode = q.Mode, Chapter = chapter, StageLevel = stage, Revision = q.ExpectedRevision,
                CoreModule = content.CoreModule, ContentSha256 = content.ContentSha256, UseMemory = q.UseMemory };
            var owned = Copies(e, false, q.ExpectedRevision + 1); var weapons = Weapons(content, e, q.ExpectedRevision + 1); var used = new HashSet<string>();
            foreach (string id in q.Mode == "tutorial" ? (IEnumerable<string>)AccountPolicy.Starters : q.Party!)
            {
                if (q.Mode == "story" && !owned.ContainsKey(id)) throw new ApiError(422, "NOT_OWNED");
                string wid = Run.DefaultWeapon(content.Data, id), instance = "temporary:" + wid;
                if (q.Mode == "story")
                {
                    string type = content.Data.Characters.Single(c => c.Id == id).WeaponType;
                    var matching = weapons.Where(w => !used.Contains(w.Instance) && (w.Equipped == id || w.Equipped == null)
                        && content.Data.Memory.Battle.Weapons.Any(d => d.Id == w.Weapon && d.Type == type)).OrderBy(w => w.Equipped == id ? 0 : 1).ThenBy(w => w.Instance, StringComparer.Ordinal).ToList();
                    // The character IS owned here; only the matching available weapon is missing (S1 review P2).
                    if (matching.Count == 0) throw new ApiError(422, "NO_ELIGIBLE_WEAPON");
                    (instance, wid, _) = matching[0]; used.Add(instance);
                }
                int refine = q.Mode == "tutorial" || wid.StartsWith("w_basic_", StringComparison.Ordinal) ? 1 : Math.Min(5, Copies(e, true, q.ExpectedRevision + 1).GetValueOrDefault(wid));
                launch.Party.Add(GraphJson.Object("charId", id, "level", AccountPolicy.Build("level"), "asc", AccountPolicy.Build("asc"), "res", q.Mode == "tutorial" ? AccountPolicy.Build("res") : Math.Min(6, owned[id] - 1),
                    "skillLv", AccountPolicy.Build("skillLv"), "ultLv", AccountPolicy.Build("ultLv"), "armor", null, "accessory", null, "weaponId", wid, "weaponLevel", AccountPolicy.Build("weaponLevel"), "refine", refine));
                launch.WeaponInstances.Add(instance);
            }
            return launch;
        }
        public static Reply Launch(PracticeContent content, PracticeAccount account, AccountEnvelope e, Run? current, ExpeditionRequest q)
        {
            if (e.Revision != q.ExpectedRevision) throw new ApiError(409, "STALE_REVISION");
            if (e.Run != null && (current == null || current.State.Phase != "DONE")) throw new ApiError(409, "RUN_ACTIVE");
            GrowthCommands.NoEncounter(e);
            var launch = q.Mode == "tutorial" ? Build(content,e,q) : GrowthCommands.BuildLaunchV4(content,e,q);
            // Phase2: only a fresh story launch records the preset, after the unchanged v4 builder. The public request cannot choose it.
            if (q.Mode == ExpeditionBalancePolicy.Mode) ExpeditionBalancePolicy.Stamp(content, launch);
            var r = new RunRecord { RunId = "pr-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), Seed = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)),
                MemoryVersion = AccountEnvelope.MemoryVersion(e.Memory), StartMemory = GraphJson.Copy(e.Memory["memory"]), UseMemory = q.UseMemory, Launch = launch };
            Run run; try { run = r.CreateRun(account, content); } catch (RuleException ex) { throw new ApiError(422, ex.Code); }
            if (e.Run != null) e.Archive.Add(e.Run);
            e.Run = r; e.Progression ??= new ProgressionRecord();
            e.Progression.Ledger.Add(new ProgressionFact { Kind = "launch", RequestId = q.RequestId, RunId = r.RunId, Revision = q.ExpectedRevision, Request = q, PolicyVersion = launch.PolicyVersion, PolicyHash = launch.PolicyHash,
                BalancePolicy = ExpeditionBalancePolicy.Copy(launch.BalancePolicy) });
            r.CreatedSha256 = PracticeRunStore.StateDigest(run);
            return CreationReply(content, r, run);
        }
        static Reply CreationReply(PracticeContent content, RunRecord r, Run run)
        {
            var view = PublicView.Run(run, r.RunId, 0, content.Rules);
            return Reply.Json(200, GraphJson.Object("runId", r.RunId, "version", 0, "phase", view["phase"], "memoryVersion", r.MemoryVersion, "state", view));
        }
        public static Reply Abandon(AccountEnvelope e, Run? live, string runId, AbandonRequest q)
        {
            var r = e.Run;
            if (r == null || r.RunId != runId) throw new ApiError(404, "RUN_NOT_FOUND");
            if (r.Journal.Count != q.ExpectedVersion) throw new ApiError(409, "STALE_VERSION");
            if (live == null || live.State.Phase == "DONE") throw new ApiError(409, "RUN_NOT_ACTIVE");
            r.End = new ExpeditionEnd { RequestId = q.RequestId, AtVersion = q.ExpectedVersion, Revision = e.Revision, MemoryVersion = AccountEnvelope.MemoryVersion(e.Memory) };
            e.Archive.Add(r); e.Run = null; return r.End.Reply(runId);
        }
        // Call only AFTER Execute adds the successful live memory-commit journal entry. Replay never calls this.
        public static void AfterMemoryCommit(AccountEnvelope e, Run run, RunRecord record, string requestId, PracticeContent content)
        {
            var launch = record.Launch;
            if (launch == null || run.State.Phase != "DONE" || !IsClear(run) || e.Progression == null || e.Progression.Cleared(launch.Chapter)) return;
            var entry = record.Journal.Last();
            if (entry.Kind != Contracts.MemoryCommit || !Equals(entry.Args.GetValueOrDefault("requestId"), requestId)) throw new InvalidDataException("live grant link");
            var fact = new ProgressionFact { Kind = "grant", RequestId = requestId, Revision = e.Revision, RunId = record.RunId, Chapter = launch.Chapter,
                JournalIndex = record.Journal.Count, StateSha256 = entry.StateSha256, PolicyVersion = launch.PolicyVersion, PolicyHash = launch.PolicyHash };
            e.Progression.Ledger.Add(fact); RecruitmentGrants.Apply(content.Recruitment, e, fact);
        }
        static bool IsClear(Run run) => run.State.Result is IDictionary<string, object?> r && Equals((r.TryGetValue("outcome", out var value) ? value : null), "clear");
        internal static Reply CommandReply(PracticeContent content, RunRecord r, Run run, CommandOutcome outcome, long memoryVersion, string kind)
        {
            int version = r.Journal.Count; var view = PublicView.Run(run, r.RunId, version, content.Rules);
            var graph = GraphJson.Object("runId", r.RunId, "version", version, "phase", view["phase"], "memoryVersion", memoryVersion);
            if (outcome.Events != null) graph.Add("events", PublicView.Events(outcome.Events));
            if (kind == "forgeOffer") graph.Add("result", PublicView.Offer(outcome.Result));
            if (kind == Contracts.MemoryCommit) graph.Add("result", PublicView.Commit(outcome.Result));
            graph.Add("state", view); return Reply.Json(200, graph);
        }
        public static void Validate(PracticeContent content, PracticeAccount account, AccountEnvelope e)
        {
            var records = e.Archive.Concat(e.Run == null ? Enumerable.Empty<RunRecord>() : new[] { e.Run }).ToList();
            Require(records.Select(r => r.RunId).Distinct(StringComparer.Ordinal).Count() == records.Count);
            var receiptsByIdentity = e.Receipts.ToLookup(pair => pair.Value.Identity, StringComparer.Ordinal);
            var facts = e.Progression?.Ledger ?? new List<ProgressionFact>(); var seen = new HashSet<string>(); long last = -1;
            foreach (var f in facts)
            {
                AccountPolicy.Require(f.PolicyVersion); Require(seen.Add(f.RequestId) && f.Revision > last && f.Revision < e.Revision); last = f.Revision;
                var r = records.SingleOrDefault(r => r.RunId == f.RunId); Require(r?.Launch != null);
                Require(f.PolicyVersion == r!.Launch!.PolicyVersion && f.PolicyHash == r.Launch.PolicyHash);
                if (f.Kind == "launch")
                {
                    var q = f.Request!; Require(q.RequestId == f.RequestId && q.ExpectedRevision == f.Revision && r!.Launch!.Revision == f.Revision);
                    var expected = f.PolicyVersion == AccountPolicy.Version ? Build(content, e, q) :f.PolicyVersion==SupportPolicy.Version?GrowthCommands.BuildLaunchV4(content,e,q): f.PolicyVersion == EquipmentPolicy.Version ? GrowthCommands.BuildLaunchV3(content, e, q) : GrowthCommands.BuildLaunch(content, e, q);
                    if(f.PolicyVersion==SupportPolicy.Version)Require(q.Mode=="story");else if (f.PolicyVersion == EquipmentPolicy.Version) Require(q.Mode=="story"&&!GrowthCommands.SupportActive(e,f.Revision)); else Require(!GrowthCommands.Active(e, f.Revision));
                    // The historical builder stays the oracle and the expected preset comes from the launch FACT, not from the Run record:
                    // a record whose preset was removed (or added) without the fact differs in the full graph below. The created digest,
                    // creation receipt and journal checks still prove every other launch input.
                    if (f.BalancePolicy != null)
                    {
                        Require(f.PolicyVersion == SupportPolicy.Version && q.Mode == ExpeditionBalancePolicy.Mode);
                        ExpeditionBalancePolicy.Apply(content, expected, f.BalancePolicy);
                    }
                    Require(GraphJson.Digest(expected.ToGraph()) == GraphJson.Digest(r.Launch.ToGraph()));
                    Require(e.Receipts.TryGetValue(f.RequestId, out var receipt) && receipt.Status == 200 && receipt.Identity == q.Identity
                        && receipt.Body == CreationReply(content, r, r.CreateRun(account, content)).Body);
                }
                else
                {
                    Require(f.Kind == "grant" && f.Chapter == r!.Launch!.Chapter && f.JournalIndex > 0 && f.JournalIndex <= r.Journal.Count);
                    var j = r!.Journal[f.JournalIndex - 1]; Require(j.Kind == Contracts.MemoryCommit && Equals(j.Args.GetValueOrDefault("requestId"), f.RequestId) && j.StateSha256 == f.StateSha256);
                    Require(facts.Count(x => x.Kind == "grant" && x.Chapter == f.Chapter) == 1);
                    Require(e.Recruitment?.Ledger.Count(x => x.Kind == LedgerEntry.Grant && x.RequestId == f.RequestId && x.Revision == f.Revision && x.Chapter == f.Chapter && x.PolicyVersion == f.PolicyVersion) == 1);
                }
            }
            if (e.Progression != null) Require(facts.Count > 0 && facts[0].Kind == "launch" && facts[0].Request!.Mode == "tutorial");
            var matched = new HashSet<string>(facts.Where(f => f.Kind == "launch").Select(f => f.RequestId), StringComparer.Ordinal);
            foreach (var r in records)
            {
                if (r.Launch != null)
                {
                    Require(facts.Count(f => f.Kind == "launch" && f.RunId == r.RunId) == 1 && r.Launch.UseMemory == r.UseMemory
                        && r.Launch.CoreModule == content.CoreModule && r.Launch.ContentSha256 == content.ContentSha256);
                    var run = r.CreateRun(account, content); Require(PracticeRunStore.StateDigest(run) == r.CreatedSha256);
                    var memory = MemoryV2.Migrate(content.Data.Memory, new Dictionary<string, object?>(), new Dictionary<string, object?>()).ToJsonGraph();
                    memory["version"] = r.MemoryVersion; memory["memory"] = GraphJson.Copy(r.StartMemory);
                    var prefix = new RunRecord { RunId = r.RunId };
                    foreach (var j in r.Journal)
                    {
                        var outcome = RunCommands.Execute(run, r.RunId, j.Kind, Contracts.CheckArgs(j.Kind, j.Args), memory);
                        if (outcome.NextMemory != null) memory = outcome.NextMemory;
                        Require(PracticeRunStore.StateDigest(run) == j.StateSha256); prefix.Journal.Add(j);
                        string identity = GraphJson.Digest(j.Kind == Contracts.MemoryCommit
                            ? GraphJson.Object("op", Contracts.MemoryCommit, "runId", r.RunId, "expectedVersion", prefix.Journal.Count - 1, "args", j.Args)
                            : GraphJson.Object("op", "command", "runId", r.RunId, "expectedVersion", prefix.Journal.Count - 1, "kind", j.Kind, "args", j.Args));
                        var receipts = receiptsByIdentity[identity].ToList(); Require(receipts.Count == 1);
                        var receipt = receipts[0]; Require(receipt.Value.Status == 200 && receipt.Value.Body == CommandReply(content, prefix, run, outcome, AccountEnvelope.MemoryVersion(memory), j.Kind).Body); matched.Add(receipt.Key);
                        if (j.Kind == Contracts.MemoryCommit)
                        {
                            var grant = facts.SingleOrDefault(f => f.Kind == "grant" && f.RunId == r.RunId && f.JournalIndex == prefix.Journal.Count);
                            bool previous = facts.Any(f => f.Kind == "grant" && f.Chapter == r.Launch.Chapter && f.Revision < r.Launch.Revision);
                            Require((grant != null) == (IsClear(run) && !previous));
                            if (grant != null) Require(grant.RequestId == receipt.Key);
                        }
                    }
                    if (ReferenceEquals(r, records.Last()))
                    {
                        Require(AccountEnvelope.MemoryVersion(e.Memory) == AccountEnvelope.MemoryVersion(memory)
                            && GraphJson.Digest(e.Memory["memory"]) == GraphJson.Digest(memory["memory"]));
                    }
                    if (r.End != null) Require(run.State.Phase != "DONE");
                }
                if (r.End != null)
                {
                    var end = r.End; Require(e.Run != r && end.AtVersion == r.Journal.Count && end.Revision < e.Revision && end.MemoryVersion == r.MemoryVersion && e.Receipts.TryGetValue(end.RequestId, out _));
                    var receipt = e.Receipts[end.RequestId]; Require(receipt.Status == 200 && receipt.Identity == Contracts.AbandonIdentity(r.RunId, end.AtVersion) && receipt.Body == end.Reply(r.RunId).Body); matched.Add(end.RequestId);
                }
            }
            foreach (var pair in e.Receipts)
            {
                Require(pair.Value.Status == 200);
                using var json = JsonDocument.Parse(pair.Value.Body);
                if (!json.RootElement.TryGetProperty("runId", out var id)) continue;
                var record = records.SingleOrDefault(r => r.RunId == id.GetString()); Require(record != null);
                if (record!.Launch != null || record.End?.RequestId == pair.Key) Require(matched.Contains(pair.Key));
            }
        }
        public static Dictionary<string, object?> Profile(PracticeContent content, string accountId, AccountEnvelope? e, Run? live) => GrowthCommands.Profile(content, accountId, e, live);
        internal static Dictionary<string, object?> BaseProfile(PracticeContent content, string accountId, AccountEnvelope? e, Run? live, long before = long.MaxValue)
        {
            var p = e?.Progression; var account = GrowthCommands.Account(e, before);
            var owned = e == null ? new Dictionary<string, int>() : Copies(e, false, before); var weapons = e == null ? new List<(string Instance, string Weapon, string? Equipped)>() : Weapons(content, e, before);
            var current = e?.Run; object? active = current == null ? null : GraphJson.Object("runId", current.RunId, "mode", current.Launch?.Mode ?? "legacy", "chapter", current.Launch?.Chapter,
                "phase", live?.State.Phase, "version", (long)current.Journal.Count);
            return GraphJson.Object("accountId", accountId, "revision", e?.Revision ?? 0, "policyVersion", AccountPolicy.Version, "initialized", p != null,
                "account", GraphJson.Object("level", account.Item1, "xp", account.Item2, "next", 20 * account.Item1 + 80),
                "tutorial", GraphJson.Object("completed", p?.Cleared(0, before) == true, "activeRunId", current?.Launch?.Mode == "tutorial" ? current.RunId : null),
                "chapters", new[] { 0, 1 }.Select(ch => (object?)GraphJson.Object("id", ch, "stageLevel", AccountPolicy.Stage(ch), "zones", new List<object?> { 1 }, "bosses", new List<object?> { "b_bell" },
                    "unlockLevel", AccountPolicy.Unlock(ch), "firstClear", p?.Cleared(ch, before) == true, "available", ch == 0 ? p?.Cleared(0, before) != true : p?.Cleared(0, before) == true && account.Item1 >= 3)).ToList(),
                "characters", owned.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => (object?)GraphJson.Object("charId", c.Key, "copies", c.Value, "res", Math.Min(6, c.Value - 1), "level", GrowthCommands.CharacterLevel(e!, c.Key, before), "xp", 0, "asc", 0,
                    "skillLv", 1, "ultLv", 1, "weaponInstanceId", weapons.FirstOrDefault(w => w.Equipped == c.Key).Instance, "armor", null, "accessory", null)).ToList(),
                "weapons", weapons.Select(w => (object?)GraphJson.Object("instanceId", w.Instance, "weaponId", w.Weapon, "level", 1, "xp", 0, "asc", 0,
                    "refine", w.Instance.StartsWith("basic:", StringComparison.Ordinal) ? 1 : Math.Min(5, Copies(e!, true, before)[w.Weapon]), "equippedBy", w.Equipped)).ToList(),
                "lastParty", p?.Ledger.LastOrDefault(f => f.Kind == "launch" && f.Revision < before && f.Request!.Mode == "story")?.Request!.Party?.Cast<object?>().ToList() ?? new List<object?>(), "activeSession", active);
        }
    }

    // Adapter for the already-authenticated Program dispatcher. It does not create its own listener or credential path.
    static class ProgressionRoutes
    {
        public static Task<Reply> Read(PracticeHost h, PracticeAccount a) => h.Store.Read(a, (e, run) => Reply.Json(200, ProgressionCommands.Profile(h.Content, a.Id, e, run)));
        public static Task<Reply> Launch(PracticeHost h, PracticeAccount a, byte[] body)
        {
            var q = Contracts.ParseExpedition(body); return h.Store.Mutate(a, q.RequestId, q.Identity, (e, run) => ProgressionCommands.Launch(h.Content, a, e, run, q));
        }
        public static Task<Reply> Abandon(PracticeHost h, PracticeAccount a, string id, byte[] body)
        {
            var q = Contracts.ParseAbandon(body, id); return h.Store.Mutate(a, q.RequestId, q.Identity, (e, run) => ProgressionCommands.Abandon(e, run, id, q));
        }
    }
}
