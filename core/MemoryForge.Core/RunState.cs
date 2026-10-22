using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoryForge.Core
{
    public sealed class RunNode
    {
        public string Id = "", Type = "battle";
        public int Floor, Index;
        public List<string>? Next;
        internal RunNode Copy() => new RunNode { Id = Id, Type = Type, Floor = Floor, Index = Index, Next = Next == null ? null : new List<string>(Next) };
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = MemoryJson.Object("id", Id, "f", Floor, "i", Index, "type", Type);
            if (Next != null) graph.Add("next", new List<string>(Next)); return graph;
        }
    }
    public sealed class RunMap
    {
        public int? Zone;
        public List<List<RunNode>> Floors = new List<List<RunNode>>();
        public RunNode Boss = new RunNode();
        internal RunMap Copy() => new RunMap { Zone = Zone, Floors = Floors.Select(row => row.Select(n => n.Copy()).ToList()).ToList(), Boss = Boss.Copy() };
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = MemoryJson.Object("floors", Floors.Select(row => row.Select(n => n.ToJsonGraph()).ToList()).ToList(), "boss", Boss.ToJsonGraph());
            if (Zone.HasValue) graph.Add("zone", Zone.Value); return graph;
        }
    }
    public sealed class RunStatistics
    {
        public int Battles, Elites, Kills;
        internal RunStatistics Copy() => (RunStatistics)MemberwiseClone();
        internal Dictionary<string, object?> ToJsonGraph() => MemoryJson.Object("battles", Battles, "elites", Elites, "kills", Kills);
    }
    public sealed class RunState
    {
        public string Schema = "mf-run-0.7", Length = "short", Phase = "MAP";
        public long Seed;
        public uint[] Rng = new uint[4];
        public RunDifficulty Difficulty = new RunDifficulty();
        public int? StageLevel, Zone;
        public List<int> Zones = new List<int>();
        public int ZoneIndex, Floor, Hp, MaxHp, HpReference, Coins = 60, SequenceCard;
        public string? Position;
        public object? Bosses;
        public RunMap? Map;
        public List<RunPartyEntry> Party = new List<RunPartyEntry>();
        public RunPartyEntry? Support;
        public List<RunCardSpec> Deck = new List<RunCardSpec>();
        public List<string> Relics = new List<string>(), PartsFound = new List<string>(), Log = new List<string>();
        public object? Pending, Result;
        public BattleState? Battle;
        public RunStatistics Statistics = new RunStatistics();
        public Dictionary<string, object?> RunFlags = MemoryJson.Object("envelopeUsed", false, "refusals", 0, "nextApBonus", 0);
        public Dictionary<string, int> Offered = new Dictionary<string, int>(StringComparer.Ordinal);
        public object? StartMemory;
        public string? MemoryPolicy, RunId;
        public long? MemoryVersion;
        public MemorySnapshot? MemoryBaseline, MemoryBefore, PlayBefore;
        // R1a: source startBattle adds battleType; v2 captureMemory adds memoryAfter. Both stay absent until then.
        public string? BattleType;
        public MemorySnapshot? MemoryAfter;
        // R1c: source commitMemory adds savedMemory (the committed memory graph) on success only; absent before.
        public object? SavedMemory;

        internal RunState Copy()
        {
            var copy = (RunState)MemberwiseClone(); copy.Rng = (uint[])Rng.Clone(); copy.Difficulty = Difficulty.Copy();
            copy.Zones = new List<int>(Zones); copy.Bosses = MemoryJson.Copy(Bosses); copy.Map = Map?.Copy();
            copy.Party = Party.Select(p => p.Copy()).ToList(); copy.Deck = Deck.Select(c => c.Copy()).ToList();
            copy.Relics = new List<string>(Relics); copy.PartsFound = new List<string>(PartsFound); copy.Log = new List<string>(Log);
            copy.Pending = MemoryJson.Copy(Pending); copy.Result = MemoryJson.Copy(Result); copy.Battle = Battle?.Copy(); copy.Statistics = Statistics.Copy();
            copy.RunFlags = RunValues.Fields(RunFlags); copy.Offered = new Dictionary<string, int>(Offered, StringComparer.Ordinal); copy.StartMemory = MemoryJson.Copy(StartMemory);
            copy.MemoryBaseline = MemoryBaseline?.Copy(); copy.MemoryBefore = MemoryBefore?.Copy(); copy.PlayBefore = PlayBefore?.Copy(); copy.MemoryAfter = MemoryAfter?.Copy();
            copy.SavedMemory = MemoryJson.Copy(SavedMemory); copy.Support = Support?.Copy(); return copy;
        }
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = MemoryJson.Object("schema", Schema, "seed", Seed, "rng", (uint[])Rng.Clone(), "length", Length,
                "difficulty", Difficulty.ToJsonGraph(), "stageLevel", StageLevel, "zones", new List<int>(Zones), "zoneIdx", ZoneIndex, "bosses", MemoryJson.Copy(Bosses),
                "floor", Floor, "pos", Position, "map", Map?.ToJsonGraph(), "party", Party.Select(p => p.ToJsonGraph()).ToList(), "hp", Hp, "maxHp", MaxHp,
                "coins", Coins, "deck", Deck.Select(c => c.ToJsonGraph()).ToList(), "relics", new List<string>(Relics), "partsFound", new List<string>(PartsFound),
                "pending", MemoryJson.Copy(Pending), "battle", Battle?.ToJsonGraph(), "phase", Phase, "log", new List<string>(Log), "stats", Statistics.ToJsonGraph(),
                "runFlags", RunValues.Fields(RunFlags), "offered", Offered.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal),
                "result", MemoryJson.Copy(Result), "startMemory", MemoryJson.Copy(StartMemory), "seqCard", SequenceCard, "hpRef", HpReference);
            if (Zone.HasValue) graph.Add("zone", Zone.Value);
            if (MemoryPolicy != null) graph.Add("memoryPolicy", MemoryPolicy);
            if (RunId != null) graph.Add("runId", RunId);
            if (MemoryVersion.HasValue) graph.Add("memoryVersion", MemoryVersion.Value);
            if (MemoryBaseline != null) graph.Add("memoryBaseline", MemoryBaseline.ToJsonGraph());
            if (MemoryBefore != null) graph.Add("memoryBefore", MemoryBefore.ToJsonGraph());
            if (PlayBefore != null) graph.Add("playBefore", PlayBefore.ToJsonGraph());
            if (BattleType != null) graph.Add("battleType", BattleType);
            if (MemoryAfter != null) graph.Add("memoryAfter", MemoryAfter.ToJsonGraph());
            if (Support != null) graph.Add("support", Support.ToJsonGraph());
            if (SavedMemory != null) graph.Add("savedMemory", MemoryJson.Copy(SavedMemory)); return graph;
        }
    }
}
