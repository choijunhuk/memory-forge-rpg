using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    sealed class EncounterJournal
    {
        public string RequestId = "", Kind = "", Identity = "", Sha256 = "";
        public int ExpectedVersion; public long Revision, AtMs;
        public Dictionary<string, object?> Args = GraphJson.Object();
        public Dictionary<string, object?> ToGraph() => GraphJson.Object("requestId", RequestId, "kind", Kind, "identity", Identity, "sha256", Sha256,
            "expectedVersion", ExpectedVersion, "revision", Revision, "atMs", AtMs, "args", Args);
        public static EncounterJournal Parse(JsonElement j)
        {
            BattleDataLoader.Fields(j, "requestId", "kind", "identity", "sha256", "expectedVersion", "revision", "atMs", "args");
            return new EncounterJournal { RequestId = Envelopes.Text(j, "requestId"), Kind = Envelopes.Text(j, "kind"), Identity = Envelopes.Hex(j, "identity"), Sha256 = Envelopes.Hex(j, "sha256"),
                ExpectedVersion = RecruitmentJson.Small(j, "expectedVersion"), Revision = RecruitmentJson.Amount(j, "revision"), AtMs = RecruitmentJson.Amount(j, "atMs"), Args = (Dictionary<string, object?>)MemoryV2DataLoader.Graph(j.GetProperty("args"))! };
        }
    }
    sealed class EncounterRecord
    {
        public string Id = "", RequestId = "", Identity = "", Dungeon = "", PolicyVersion = GrowthPolicy.Version, PolicyHash = GrowthPolicy.Hash, CoreModule = "", ContentSha256 = "", CreatedSha256 = "";
        public string Kind = "dungeon", BossId = ""; public int Difficulty; public bool Practice;
        public int Tier; public long Revision, AtMs, MemoryVersion; public uint Seed;
        public List<Dictionary<string, object?>> Party = new List<Dictionary<string, object?>>();
        public List<string> Weapons = new List<string>(); public object? Memory;
        public FrozenSupport? Support;
        public Dictionary<string, object?> AccountAtStart = GraphJson.Object(), Stage = GraphJson.Object();
        public List<EncounterJournal> Journal = new List<EncounterJournal>();
        public Dictionary<string, object?> ToGraph()
        {
            var graph = GraphJson.Object("id", Id, "requestId", RequestId, "identity", Identity, "dungeon", Dungeon, "tier", Tier, "policyVersion", PolicyVersion,
            "policyHash", PolicyHash, "coreModule", CoreModule, "contentSha256", ContentSha256, "createdSha256", CreatedSha256, "revision", Revision, "atMs", AtMs,
            "memoryVersion", MemoryVersion, "seed", Seed, "party", Party.Cast<object?>().ToList(), "weapons", Weapons.Cast<object?>().ToList(), "memory", Memory,
            "accountAtStart", AccountAtStart, "stage", Stage, "journal", Journal.Select(x => (object?)x.ToGraph()).ToList());
            if (Kind == "weeklyBoss") { graph.Add("kind", Kind); graph.Add("bossId", BossId); graph.Add("difficulty", Difficulty); graph.Add("practice", Practice); }
            if(Support!=null)graph.Add("support",Support.ToGraph());
            return graph;
        }
        public static EncounterRecord Parse(JsonElement j)
        {
            BattleDataLoader.Fields(j, "id", "requestId", "identity", "dungeon", "tier", "policyVersion", "policyHash", "coreModule", "contentSha256", "createdSha256", "revision", "atMs", "memoryVersion", "seed", "party", "weapons", "memory", "accountAtStart", "stage", "journal", "kind", "bossId", "difficulty", "practice","support");
            var r = new EncounterRecord { Id = Envelopes.Text(j, "id"), RequestId = Envelopes.Text(j, "requestId"), Identity = Envelopes.Hex(j, "identity"), Dungeon = Envelopes.Text(j, "dungeon"), Tier = RecruitmentJson.Small(j, "tier"),
                PolicyVersion = Envelopes.Text(j, "policyVersion"), PolicyHash = Envelopes.Hex(j, "policyHash"), CoreModule = Envelopes.Text(j, "coreModule"), ContentSha256 = Envelopes.Hex(j, "contentSha256"),
                CreatedSha256 = Envelopes.Hex(j, "createdSha256"), Revision = RecruitmentJson.Amount(j, "revision"), AtMs = RecruitmentJson.Amount(j, "atMs"), MemoryVersion = RecruitmentJson.Amount(j, "memoryVersion"), Seed = j.GetProperty("seed").GetUInt32(),
                Memory = MemoryV2DataLoader.Graph(j.GetProperty("memory")), AccountAtStart = (Dictionary<string, object?>)MemoryV2DataLoader.Graph(j.GetProperty("accountAtStart"))!, Stage = (Dictionary<string, object?>)MemoryV2DataLoader.Graph(j.GetProperty("stage"))! };
            GrowthPolicy.Require(r.PolicyVersion, r.PolicyHash);
            if (j.TryGetProperty("kind", out var kind))
            {
                if (kind.GetString() != "weeklyBoss" || r.PolicyVersion is not (EquipmentPolicy.Version or SupportPolicy.Version) || r.Dungeon.Length != 0 || r.Tier != 0) throw new InvalidDataException("weekly encounter variant");
                r.Kind = "weeklyBoss"; r.BossId = Envelopes.Text(j, "bossId"); r.Difficulty = RecruitmentJson.Small(j, "difficulty"); r.Practice = RecruitmentJson.Flag(j, "practice");
            }
            else if (new[]{"bossId","difficulty","practice"}.Any(k=>j.TryGetProperty(k,out _))) throw new InvalidDataException("dungeon encounter shape");
            if(j.TryGetProperty("support",out var support)){if(r.PolicyVersion!=SupportPolicy.Version)throw new InvalidDataException("support encounter policy");r.Support=FrozenSupport.Parse(support);}
            if (!GrowthCommands.IsId(r.Id) || !Contracts.IsRequestId(r.RequestId)) throw new InvalidDataException("encounter id");
            foreach (var p in j.GetProperty("party").EnumerateArray()) r.Party.Add((Dictionary<string, object?>)MemoryV2DataLoader.Graph(p)!);
            foreach (var w in j.GetProperty("weapons").EnumerateArray()) r.Weapons.Add(w.GetString() ?? throw new InvalidDataException("encounter weapon"));
            foreach (var x in j.GetProperty("journal").EnumerateArray()) r.Journal.Add(EncounterJournal.Parse(x)); return r;
        }
        public Battle Start(PracticeContent content) => Kind == "weeklyBoss" ? ContentModes.WeeklyBoss.Start(EquipmentPolicy.Modes(content), ContentModesDataLoader.Account(AccountAtStart)!, new WeeklyBossStartOptions
        { BossId = BossId, Difficulty = Difficulty, Practice = Practice, Party = Party.Select(p => (RunPartyEntry?)RunPartyEntry.FromJsonGraph(GraphJson.Copy(p))).ToList(), Memory = GraphJson.Copy(Memory), Seed = Seed,Support=Support?.ToCore() }) : ContentModes.Dungeon.Start(GrowthPolicy.Modes(content), ContentModesDataLoader.Account(AccountAtStart)!, new DungeonStartOptions
        { DungeonId = Dungeon, Tier = Tier, Party = Party.Select(p => (RunPartyEntry?)RunPartyEntry.FromJsonGraph(GraphJson.Copy(p))).ToList(), Memory = GraphJson.Copy(Memory), Seed = Seed,Support=Support?.ToCore() });
    }
    sealed class GrowthFact
    {
        public string Kind = "", RequestId = "", EncounterId = "", PolicyVersion = GrowthPolicy.Version, PolicyHash = GrowthPolicy.Hash;
        public string TerminalStateSha256 = ""; public string? WeekKey;
        public long Revision, AtMs, MemoryVersion; public string? RunId; public int RunVersion, JournalIndex;
        public Dictionary<string, object?> Request = GraphJson.Object();
        public Dictionary<string, object?> ToGraph()
        {
            var graph = GraphJson.Object("kind", Kind, "requestId", RequestId, "encounterId", EncounterId, "policyVersion", PolicyVersion, "policyHash", PolicyHash,
            "revision", Revision, "atMs", AtMs, "memoryVersion", MemoryVersion, "runId", RunId, "runVersion", RunVersion, "journalIndex", JournalIndex, "terminalStateSha256", TerminalStateSha256, "request", Request);
            if (Kind == "weeklyBossVictory" || Kind == "weeklyBossDefeat") graph.Add("weekKey", WeekKey);
            return graph;
        }
        public static GrowthFact Parse(JsonElement j)
        {
            BattleDataLoader.Fields(j, "kind", "requestId", "encounterId", "policyVersion", "policyHash", "revision", "atMs", "memoryVersion", "runId", "runVersion", "journalIndex", "terminalStateSha256", "request", "weekKey");
            var f = new GrowthFact { Kind = Envelopes.Text(j, "kind"), RequestId = Envelopes.Text(j, "requestId"), EncounterId = Envelopes.Text(j, "encounterId"), PolicyVersion = Envelopes.Text(j, "policyVersion"), PolicyHash = Envelopes.Hex(j, "policyHash"),
                Revision = RecruitmentJson.Amount(j, "revision"), AtMs = RecruitmentJson.Amount(j, "atMs"), MemoryVersion = RecruitmentJson.Amount(j, "memoryVersion"),
                RunId = j.GetProperty("runId").ValueKind == JsonValueKind.Null ? null : Envelopes.Text(j, "runId"), RunVersion = RecruitmentJson.Small(j, "runVersion"), JournalIndex = RecruitmentJson.Small(j, "journalIndex"), TerminalStateSha256 = Envelopes.Text(j, "terminalStateSha256"), Request = (Dictionary<string, object?>)MemoryV2DataLoader.Graph(j.GetProperty("request"))! };
            if (f.Kind == "weeklyBossVictory" || f.Kind == "weeklyBossDefeat") f.WeekKey = Envelopes.Text(j, "weekKey");
            else if (j.TryGetProperty("weekKey",out _)) throw new InvalidDataException("nonweekly fact shape");
            GrowthPolicy.Require(f.PolicyVersion, f.PolicyHash); if (!Contracts.IsRequestId(f.RequestId)) throw new InvalidDataException("growth request id"); return f;
        }
    }
    sealed class GrowthRecord
    {
        public string? Active;
        public readonly List<GrowthFact> Ledger = new List<GrowthFact>();
        public readonly List<EncounterRecord> Encounters = new List<EncounterRecord>();
        public Dictionary<string, object?> ToGraph() => GraphJson.Object("active", Active, "ledger", Ledger.Select(f => (object?)f.ToGraph()).ToList(), "encounters", Encounters.Select(r => (object?)r.ToGraph()).ToList());
        public static GrowthRecord Parse(JsonElement j)
        {
            BattleDataLoader.Fields(j, "active", "ledger", "encounters"); var r = new GrowthRecord { Active = j.GetProperty("active").ValueKind == JsonValueKind.Null ? null : Envelopes.Text(j, "active") };
            foreach (var f in j.GetProperty("ledger").EnumerateArray()) r.Ledger.Add(GrowthFact.Parse(f));
            foreach (var e in j.GetProperty("encounters").EnumerateArray()) r.Encounters.Add(EncounterRecord.Parse(e)); return r;
        }
    }
}
